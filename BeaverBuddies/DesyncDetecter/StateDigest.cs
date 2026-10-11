using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Timberborn.WaterSystem;
using UnityEngine;

namespace BeaverBuddies.DesyncDetecter
{
    /**
     * What a desync leaves out of the traces: which entities and which water
     * tiles differ. Each tick (with logging on) every machine keeps a record
     * of the entities that ticked and where the walkers were, and of the
     * water map, for the last few ticks, and the host sends a hash of each
     * bucket of entities and each square of water along with its traces. The
     * machine that finds a difference lists what's in the buckets and
     * squares that differ, the host compares that with its own record of the
     * same tick, and says in its log which entity or tile it is.
     */
    public static class StateDigest
    {
        public const int Buckets = 256;
        private const int KeepTicks = 16;
        private const int KeepWaterTicks = 8;
        // Water columns copied for the ring, up to this many a tick
        private const int MaxWaterColumns = 600000;
        private const int Area = 16;
        private const int MaxDetailLines = 6000;

        public struct Record
        {
            public Guid Id;
            public bool HasPosition;
            public float X, Y, Z;
        }

        private class TickRecord
        {
            public Record[] Records;
            public int[] Buckets;
        }

        private class WaterRecord
        {
            public int Stride, MaxIndex, AreasX;
            public int[] Areas;
            public ReadOnlyWaterColumn[] Columns;
            public byte[] Counts;
        }

        private static readonly SortedDictionary<int, TickRecord> ticks = new();
        private static readonly SortedDictionary<int, WaterRecord> waters = new();
        // What the entities are called, for the report: kept for the ones that go too
        private static readonly Dictionary<Guid, string> names = new();

        private static int currentTick = int.MinValue;
        private static List<Record> current = new();

        public static void Reset()
        {
            ticks.Clear();
            waters.Clear();
            names.Clear();
            current = new List<Record>();
            currentTick = int.MinValue;
        }

        // ---- Entities: called for each entity as the tick runs its bucket ----

        public static void Add(int tick, Guid id, string name, bool hasPosition, Vector3 position)
        {
            if (tick != currentTick) Finish();
            currentTick = tick;
            current.Add(new Record { Id = id, HasPosition = hasPosition, X = position.x, Y = position.y, Z = position.z });
            if (!names.ContainsKey(id)) names[id] = name;
        }

        private static int Hash(Record r)
        {
            int h = r.Id.GetHashCode();
            if (r.HasPosition)
            {
                h = (h * 31) + BitConverter.SingleToInt32Bits(r.X);
                h = (h * 31) + BitConverter.SingleToInt32Bits(r.Y);
                h = (h * 31) + BitConverter.SingleToInt32Bits(r.Z);
            }
            // Spread, since buckets add these up
            return unchecked((int)((uint)h * 2654435761u));
        }

        private static int BucketOf(Guid id) => id.ToByteArray()[0];

        private static void Finish()
        {
            if (currentTick == int.MinValue || current.Count == 0) return;
            var buckets = new int[Buckets];
            foreach (Record r in current) buckets[BucketOf(r.Id)] = unchecked(buckets[BucketOf(r.Id)] + Hash(r));
            ticks[currentTick] = new TickRecord { Records = current.ToArray(), Buckets = buckets };
            while (ticks.Count > KeepTicks) ticks.Remove(ticks.Keys.First());
            current = new List<Record>();
        }

        // The buckets of a tick that's over, to send with its traces
        public static int[] EntityBucketsOf(int tick)
        {
            if (tick == currentTick) Finish();
            return ticks.TryGetValue(tick, out TickRecord r) ? r.Buckets : null;
        }

        // ---- Water: after the map updates ----

        public static void RecordWater(int tick, ThreadSafeWaterMap map)
        {
            int stride = map._mapIndexService.Stride;
            int maxIndex = map._mapIndexService.MaxIndex;
            var columns = map._threadSafeWaterColumns;
            var counts = map._threadSafeColumnCounts;
            int areasX = (stride + Area - 1) / Area;
            int areasY = (maxIndex / stride + Area - 1) / Area;
            var areas = new int[areasX * areasY];
            for (int index = 0; index < maxIndex; index++)
            {
                int x = index % stride, y = index / stride;
                int area = (y / Area) * areasX + (x / Area);
                int h = areas[area];
                for (int z = 0; z < counts[index]; z++)
                {
                    int i = index + z * maxIndex;
                    if (i >= columns.Length) break;
                    var c = columns[i];
                    h = unchecked((h * 31) + BitConverter.SingleToInt32Bits(c.Floor));
                    h = unchecked((h * 31) + BitConverter.SingleToInt32Bits(c.Ceiling));
                    h = unchecked((h * 31) + BitConverter.SingleToInt32Bits(c.WaterDepth));
                    h = unchecked((h * 31) + BitConverter.SingleToInt32Bits(c.Contamination));
                    h = unchecked((h * 31) + BitConverter.SingleToInt32Bits(c.Overflow));
                }
                areas[area] = h;
            }
            var record = new WaterRecord { Stride = stride, MaxIndex = maxIndex, AreasX = areasX, Areas = areas };
            if (columns.Length <= MaxWaterColumns)
            {
                record.Columns = (ReadOnlyWaterColumn[])columns.Clone();
                record.Counts = (byte[])counts.Clone();
            }
            waters[tick] = record;
            while (waters.Count > KeepWaterTicks)
            {
                waters.Remove(waters.Keys.First());
            }
        }

        public static int[] WaterAreasOf(int tick) => waters.TryGetValue(tick, out WaterRecord r) ? r.Areas : null;

        // ---- Comparing ----

        public class Difference
        {
            public int Tick;
            public List<int> EntityBuckets = new();
            public List<int> WaterAreas = new();
            public bool Any => EntityBuckets.Count > 0 || WaterAreas.Count > 0;
        }

        /** The buckets and squares the other machine's hashes say differ from ours at that tick. */
        public static Difference Compare(int tick, int[] otherBuckets, int[] otherAreas)
        {
            var diff = new Difference { Tick = tick };
            // The tick that's just over is the one being collected still
            if (tick == currentTick) Finish();
            if (otherBuckets != null && ticks.TryGetValue(tick, out TickRecord mine))
            {
                for (int i = 0; i < Buckets && i < otherBuckets.Length; i++)
                {
                    if (mine.Buckets[i] != otherBuckets[i]) diff.EntityBuckets.Add(i);
                }
            }
            if (otherAreas != null && waters.TryGetValue(tick, out WaterRecord water) && water.Areas.Length == otherAreas.Length)
            {
                for (int i = 0; i < otherAreas.Length; i++)
                {
                    if (water.Areas[i] != otherAreas[i]) diff.WaterAreas.Add(i);
                }
            }
            return diff;
        }

        // ---- What goes in the report, and the host's reading of it ----

        private static string Describe(Guid id) => names.TryGetValue(id, out string name) ? name : "?";

        private static string Line(Record r) =>
            $"{r.Id}|{Describe(r.Id)}|{(r.HasPosition ? $"{r.X:R}|{r.Y:R}|{r.Z:R}" : "-|-|-")}";

        /** Our side of a difference: the entities in its buckets and the columns in its squares. */
        public static (string[] entities, string[] water) Detail(Difference diff)
        {
            var entities = new List<string>();
            if (ticks.TryGetValue(diff.Tick, out TickRecord mine))
            {
                var wanted = new HashSet<int>(diff.EntityBuckets);
                foreach (Record r in mine.Records)
                {
                    if (wanted.Contains(BucketOf(r.Id))) entities.Add(Line(r));
                    if (entities.Count >= MaxDetailLines) break;
                }
            }
            var water = new List<string>();
            if (waters.TryGetValue(diff.Tick, out WaterRecord w) && w.Columns != null)
            {
                foreach (int area in diff.WaterAreas)
                {
                    int ax = area % w.AreasX, ay = area / w.AreasX;
                    for (int y = ay * Area; y < ay * Area + Area; y++)
                    {
                        for (int x = ax * Area; x < ax * Area + Area && x < w.Stride; x++)
                        {
                            int index = y * w.Stride + x;
                            if (index >= w.MaxIndex) continue;
                            for (int z = 0; z < w.Counts[index]; z++)
                            {
                                int i = index + z * w.MaxIndex;
                                if (i >= w.Columns.Length) break;
                                water.Add(WaterLine(x, y, z, w.Columns[i]));
                            }
                        }
                    }
                    if (water.Count >= MaxDetailLines) break;
                }
            }
            return (entities.ToArray(), water.ToArray());
        }

        private static string WaterLine(int x, int y, int z, ReadOnlyWaterColumn c) =>
            $"{x},{y},{z}|floor {c.Floor:R}|ceiling {c.Ceiling:R}|depth {c.WaterDepth:R}|contamination {c.Contamination:R}|overflow {c.Overflow:R}";

        /**
         * On the host: what differs between the other machine's lines and
         * ours for the same tick, entity by entity and tile by tile.
         */
        public static string Analyze(int tick, string who, int[] entityBuckets, string[] theirEntities, int[] waterAreas, string[] theirWater)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"========== What differs at tick {tick}, {who}'s state against ours ==========");
            if (entityBuckets != null && entityBuckets.Length > 0 && ticks.TryGetValue(tick, out TickRecord mine))
            {
                var wanted = new HashSet<int>(entityBuckets);
                var ours = mine.Records.Where(r => wanted.Contains(BucketOf(r.Id))).ToDictionary(r => r.Id);
                var theirs = new Dictionary<Guid, string>();
                foreach (string line in theirEntities ?? new string[0])
                {
                    if (Guid.TryParse(line.Split('|')[0], out Guid id)) theirs[id] = line;
                }
                int shown = 0;
                foreach (var pair in ours.OrderBy(p => p.Key))
                {
                    if (!theirs.TryGetValue(pair.Key, out string line))
                    {
                        sb.AppendLine($"  only here: {Line(pair.Value)}");
                        shown++;
                    }
                    else if (line != Line(pair.Value))
                    {
                        sb.AppendLine($"  differs, here {Line(pair.Value)}, there {line}");
                        shown++;
                    }
                    if (shown >= 60) { sb.AppendLine("  ..."); break; }
                }
                foreach (var pair in theirs.OrderBy(p => p.Key))
                {
                    if (!ours.ContainsKey(pair.Key))
                    {
                        sb.AppendLine($"  only there: {pair.Value}");
                        if (++shown >= 120) { sb.AppendLine("  ..."); break; }
                    }
                }
                if (shown == 0) sb.AppendLine("  entities: the lists agree (the difference is in an entity's order, or came and went)");
            }
            if (waterAreas != null && waterAreas.Length > 0 && waters.TryGetValue(tick, out WaterRecord w) && w.Columns != null)
            {
                var theirTiles = (theirWater ?? new string[0]).ToDictionary(l => l.Split('|')[0], l => l, StringComparer.Ordinal);
                int shown = 0;
                foreach (int area in waterAreas)
                {
                    int ax = area % w.AreasX, ay = area / w.AreasX;
                    for (int y = ay * Area; y < ay * Area + Area && shown < 60; y++)
                    {
                        for (int x = ax * Area; x < ax * Area + Area && x < w.Stride && shown < 60; x++)
                        {
                            int index = y * w.Stride + x;
                            if (index >= w.MaxIndex) continue;
                            for (int z = 0; z < w.Counts[index]; z++)
                            {
                                int i = index + z * w.MaxIndex;
                                if (i >= w.Columns.Length) break;
                                string key = $"{x},{y},{z}";
                                string here = WaterLine(x, y, z, w.Columns[i]);
                                if (!theirTiles.TryGetValue(key, out string there)) continue;
                                if (there != here) { sb.AppendLine($"  water {key}: here {here.Substring(key.Length + 1)}; there {there.Substring(key.Length + 1)}"); shown++; }
                            }
                        }
                    }
                }
                if (shown == 0) sb.AppendLine("  water: the squares' columns agree (a column came and went)");
            }
            sb.AppendLine("========== End of what differs ==========");
            return sb.ToString();
        }
    }
}
