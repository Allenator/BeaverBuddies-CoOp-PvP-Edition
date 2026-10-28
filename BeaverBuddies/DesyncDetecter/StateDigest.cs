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
     * tiles differ. Each tick of a co-op game every machine keeps a record
     * of the entities that ticked and where the walkers were, for the last
     * few ticks, nothing allocated as it goes; with tracing, of the water map
     * too, and the host sends a hash of each bucket of entities and each
     * square of water along with its traces. The machine that finds a
     * difference lists what's in the buckets and squares that differ, the host
     * compares that with its own record of the same tick, and says in its log
     * which entity or tile it is. Without tracing, a guest whose hashes part
     * from the host's sends its buckets and walkers of that tick and the one
     * before (Snapshot), and the host names the walkers that differ
     * (AnalyzeSnapshot).
     */
    public static class StateDigest
    {
        public const int Buckets = 256;
        private const int KeepTicks = 24;
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

        // A tick's entities in the ring, its arrays kept from tick to tick
        private class TickRecord
        {
            public int Tick = int.MinValue;
            public Record[] Records = new Record[1024];
            public int Count;
            public readonly int[] Buckets = new int[StateDigest.Buckets];
            public readonly int[] Counts = new int[StateDigest.Buckets];
            public IEnumerable<Record> All => new ArraySegment<Record>(Records, 0, Count);
        }

        private class WaterRecord
        {
            public int Stride, MaxIndex, AreasX;
            public int[] Areas;
            public ReadOnlyWaterColumn[] Columns;
            public byte[] Counts;
        }

        private static readonly TickRecord[] ring = Enumerable.Range(0, KeepTicks).Select(_ => new TickRecord()).ToArray();
        private static readonly SortedDictionary<int, WaterRecord> waters = new();
        // What the entities are called, for the report: kept for the ones that go too
        private static readonly Dictionary<Guid, string> names = new();

        // The tick being played, and its record, filled as its buckets tick
        private static int tickNow = int.MinValue;
        private static int currentTick = int.MinValue;
        private static TickRecord current;
        private static int nextSlot;

        public static void Reset()
        {
            foreach (TickRecord r in ring)
            {
                r.Tick = int.MinValue;
                r.Count = 0;
            }
            waters.Clear();
            names.Clear();
            current = null;
            currentTick = int.MinValue;
            tickNow = int.MinValue;
        }

        // The ticks kept, oldest first
        private static IEnumerable<TickRecord> Ticks()
        {
            if (current != null) Finish();
            return ring.Where(r => r.Tick != int.MinValue).OrderBy(r => r.Tick);
        }

        private static bool TryGetTick(int tick, out TickRecord record)
        {
            if (tick == currentTick && current != null) Finish();
            foreach (TickRecord r in ring)
            {
                if (r.Tick == tick && r != current)
                {
                    record = r;
                    return true;
                }
            }
            record = null;
            return false;
        }

        // ---- Entities: called for each entity as the tick runs its bucket ----

        /** The tick about to be played, which the entities it ticks are kept under. */
        public static void StartTick(int tick) => tickNow = tick;

        // Tests measuring what the record costs turn it off (BB_DIGEST_OFF)
        private static readonly bool OffForTests = DevTools.TestHarness.Active && Environment.GetEnvironmentVariable("BB_DIGEST_OFF") == "1";

        public static void Add(Guid id, Timberborn.EntitySystem.EntityComponent entity, bool hasPosition, Vector3 position)
        {
            if (OffForTests) return;
            if (tickNow != currentTick)
            {
                if (current != null) Finish();
                current = ring[nextSlot];
                nextSlot = (nextSlot + 1) % KeepTicks;
                current.Tick = currentTick = tickNow;
                current.Count = 0;
            }
            // Summed already for a look at it before the tick was over: taken up again, and summed again at the end
            else if (current == null) current = ring[(nextSlot + KeepTicks - 1) % KeepTicks];
            if (current.Count == current.Records.Length) Array.Resize(ref current.Records, current.Records.Length * 2);
            current.Records[current.Count++] = new Record { Id = id, HasPosition = hasPosition, X = position.x, Y = position.y, Z = position.z };
            // The name read once, for the report
            if (!names.ContainsKey(id)) names[id] = entity.GameObject.name;
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

        // Guid's hash is plain arithmetic on its fields, the same on every machine, and allocates nothing
        private static int BucketOf(Guid id) => id.GetHashCode() & (Buckets - 1);

        // The tick being filled is over: its buckets summed, and it's kept as it is
        private static void Finish()
        {
            TickRecord r = current;
            current = null;
            if (r == null) return;
            Array.Clear(r.Buckets, 0, Buckets);
            Array.Clear(r.Counts, 0, Buckets);
            for (int i = 0; i < r.Count; i++)
            {
                int bucket = BucketOf(r.Records[i].Id);
                r.Buckets[bucket] = unchecked(r.Buckets[bucket] + Hash(r.Records[i]));
                r.Counts[bucket]++;
            }
        }

        // The buckets of a tick that's over, to send with its traces
        public static int[] EntityBucketsOf(int tick) => TryGetTick(tick, out TickRecord r) ? (int[])r.Buckets.Clone() : null;

        // ---- Without tracing: a guest's side of a tick its hashes parted at, and the host's reading of it ----

        /** This machine's buckets, their counts, and its walkers' positions ("tick|id|x|y|z") at that tick and the one before. */
        public static (int[] buckets, int[] counts, string[] walkers) Snapshot(int tick)
        {
            if (!TryGetTick(tick, out TickRecord record)) return (null, null, null);
            var walkers = new List<string>();
            foreach (int t in new[] { tick - 1, tick })
            {
                if (!TryGetTick(t, out TickRecord r)) continue;
                foreach (Record w in r.All)
                {
                    if (w.HasPosition) walkers.Add($"{t}|{w.Id}|{w.X:R}|{w.Y:R}|{w.Z:R}");
                }
            }
            return ((int[])record.Buckets.Clone(), (int[])record.Counts.Clone(), walkers.ToArray());
        }

        /**
         * On the host: what a guest's snapshot of a tick says differs from ours, without tracing. The walkers whose
         * positions differ, by name, and the entities in the buckets that differ otherwise (one that ticked on one
         * machine and not the other).
         */
        public static string AnalyzeSnapshot(int tick, string who, int[] theirBuckets, int[] theirCounts, string[] theirWalkers)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"========== What differs at tick {tick}, {who}'s walkers and entities against ours ==========");
            if (!TryGetTick(tick, out TickRecord mine))
            {
                sb.AppendLine($"  tick {tick} isn't kept here any more: the ticks kept are {string.Join(", ", Ticks().Select(r => r.Tick))}");
                sb.AppendLine("========== End of what differs ==========");
                return sb.ToString();
            }
            var buckets = Enumerable.Range(0, Buckets)
                .Where(b => theirBuckets != null && b < theirBuckets.Length && mine.Buckets[b] != theirBuckets[b]).ToList();
            sb.AppendLine($"  {buckets.Count} of {Buckets} buckets of entities differ" + (buckets.Count == 0 ? "" : $": {string.Join(" ", buckets.Take(40))}"));
            // The walkers, by position, at both ticks
            var theirs = new Dictionary<(int, Guid), string>();
            foreach (string line in theirWalkers ?? new string[0])
            {
                string[] p = line.Split('|');
                if (p.Length == 5 && int.TryParse(p[0], out int t) && Guid.TryParse(p[1], out Guid id)) theirs[(t, id)] = $"{p[2]} {p[3]} {p[4]}";
            }
            int shown = 0;
            var ours = new HashSet<(int, Guid)>();
            foreach (int t in new[] { tick - 1, tick })
            {
                if (!TryGetTick(t, out TickRecord r)) continue;
                foreach (Record w in r.All)
                {
                    if (!w.HasPosition) continue;
                    ours.Add((t, w.Id));
                    string here = $"{w.X:R} {w.Y:R} {w.Z:R}";
                    if (!theirs.TryGetValue((t, w.Id), out string there))
                    {
                        if (theirs.Count > 0 && shown++ < 40) sb.AppendLine($"  walker only here at tick {t}: {w.Id} {Describe(w.Id)} at {here}");
                    }
                    else if (there != here && shown++ < 40) sb.AppendLine($"  walker {w.Id} {Describe(w.Id)} at tick {t}: here {here}, there {there}");
                }
            }
            foreach (var pair in theirs)
            {
                if (!ours.Contains(pair.Key) && shown++ < 60) sb.AppendLine($"  walker only there at tick {pair.Key.Item1}: {pair.Key.Item2} {Describe(pair.Key.Item2)} at {pair.Value}");
            }
            if (shown == 0) sb.AppendLine("  the walkers agree at both ticks");
            // Which entities ticked differently: ours in the buckets whose counts differ, which are the candidates
            foreach (int b in buckets.Where(b => theirCounts != null && b < theirCounts.Length && mine.Counts[b] != theirCounts[b]).Take(8))
            {
                sb.AppendLine($"  bucket {b}: {mine.Counts[b]} entities here, {theirCounts[b]} there; here: " +
                    string.Join(", ", mine.All.Where(r => BucketOf(r.Id) == b).Take(30).Select(r => $"{r.Id.ToString().Substring(0, 8)} {Describe(r.Id)}")));
            }
            sb.AppendLine("========== End of what differs ==========");
            return sb.ToString();
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
            if (otherBuckets != null && TryGetTick(tick, out TickRecord mine))
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

        /** Where the entities in those buckets were over the ticks kept: "id|tick|x|y|z". */
        public static string[] PositionHistory(Difference diff)
        {
            var lines = new List<string>();
            var wanted = new HashSet<int>(diff.EntityBuckets);
            foreach (TickRecord tick in Ticks())
            {
                foreach (Record r in tick.All)
                {
                    if (r.HasPosition && wanted.Contains(BucketOf(r.Id))) lines.Add($"{r.Id}|{tick.Tick}|{r.X:R}|{r.Y:R}|{r.Z:R}");
                }
                if (lines.Count > 12000) break;
            }
            return lines.ToArray();
        }

        /** Our side of a difference: the entities in its buckets and the columns in its squares. */
        public static (string[] entities, string[] water) Detail(Difference diff)
        {
            var entities = new List<string>();
            if (TryGetTick(diff.Tick, out TickRecord mine))
            {
                var wanted = new HashSet<int>(diff.EntityBuckets);
                foreach (Record r in mine.All)
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

        // The first few entities that differ, tick by tick: where each was on both machines, and what each logged about it
        private static void AppendHistories(StringBuilder sb, int tick, int[] entityBuckets, string[] theirEntities, string[] theirHistory, string[] theirTraces)
        {
            if (entityBuckets == null || entityBuckets.Length == 0 || !TryGetTick(tick, out TickRecord mine)) return;
            var wanted = new HashSet<int>(entityBuckets);
            var theirs = new Dictionary<Guid, string>();
            foreach (string line in theirEntities ?? new string[0])
            {
                if (Guid.TryParse(line.Split('|')[0], out Guid id)) theirs[id] = line;
            }
            var differing = mine.All.Where(r => wanted.Contains(BucketOf(r.Id)) && theirs.TryGetValue(r.Id, out string l) && l != Line(r))
                .Select(r => r.Id).Take(3).ToList();
            foreach (Guid id in differing)
            {
                sb.AppendLine($"  ---- {id} {Describe(id)}, position by tick (here | there) ----");
                var there = new Dictionary<int, string>();
                foreach (string line in theirHistory ?? new string[0])
                {
                    string[] p = line.Split('|');
                    if (p.Length == 5 && p[0] == id.ToString() && int.TryParse(p[1], out int t)) there[t] = $"{p[2]} {p[3]} {p[4]}";
                }
                bool split = false;
                foreach (TickRecord tickRecord in Ticks())
                {
                    Record r = tickRecord.All.FirstOrDefault(x => x.Id == id);
                    if (!r.HasPosition) continue;
                    string here = $"{r.X:R} {r.Y:R} {r.Z:R}";
                    there.TryGetValue(tickRecord.Tick, out string other);
                    bool differs = other != null && other != here;
                    sb.AppendLine($"    tick {tickRecord.Tick}: {here} | {other ?? "-"}{(differs && !split ? "   <- first difference" : "")}");
                    if (differs) split = true;
                }
                string key = id.ToString();
                sb.AppendLine($"  what the traces say about it, here:");
                foreach (string line in DesyncDetecterService.TraceLinesAbout(key, 30)) sb.AppendLine("    " + line);
                sb.AppendLine($"  and there:");
                foreach (string line in (theirTraces ?? new string[0]).Where(l => l.Contains(key)).TakeLast(30)) sb.AppendLine("    " + line);
            }
        }

        private static string WaterLine(int x, int y, int z, ReadOnlyWaterColumn c) =>
            $"{x},{y},{z}|floor {c.Floor:R}|ceiling {c.Ceiling:R}|depth {c.WaterDepth:R}|contamination {c.Contamination:R}|overflow {c.Overflow:R}";

        /**
         * On the host: what differs between the other machine's lines and
         * ours for the same tick, entity by entity and tile by tile.
         */
        public static string Analyze(int tick, string who, int[] entityBuckets, string[] theirEntities, int[] waterAreas, string[] theirWater,
            string[] theirHistory = null, string[] theirTraces = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"========== What differs at tick {tick}, {who}'s state against ours ==========");
            if (entityBuckets != null && entityBuckets.Length > 0 && TryGetTick(tick, out TickRecord mine))
            {
                var wanted = new HashSet<int>(entityBuckets);
                var ours = mine.All.Where(r => wanted.Contains(BucketOf(r.Id))).ToDictionary(r => r.Id);
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
            AppendHistories(sb, tick, entityBuckets, theirEntities, theirHistory, theirTraces);
            sb.AppendLine("========== End of what differs ==========");
            return sb.ToString();
        }
    }
}
