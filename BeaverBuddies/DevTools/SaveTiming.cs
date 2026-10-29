using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace BeaverBuddies.DevTools
{
    /**
     * For tests (BB_SAVE_TIMING=1): where a save's time goes, logged as its archive is written. The game's own "Saved game
     * in" leaves out finishing the tick and doesn't split the rest: collecting the world's state, which has to be done on the
     * main thread, from writing it as JSON, the other entries, compressing them (as the archive closes: it's opened to
     * update) and copying the result to the file, which a worker thread could do.
     */
    static class SaveTiming
    {
        public static bool Active => TestHarness.Active && Environment.GetEnvironmentVariable("BB_SAVE_TIMING") == "1";

        private static readonly Dictionary<string, double> _ms = new Dictionary<string, double>();
        private static bool _saving;

        private static double Since(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;

        private static void Add(string phase, long start)
        {
            if (!_saving) return;
            _ms[phase] = (_ms.TryGetValue(phase, out double ms) ? ms : 0) + Since(start);
        }

        private static double Get(string phase) => _ms.TryGetValue(phase, out double ms) ? ms : 0;

        private static void Begin()
        {
            if (_saving) return;
            _ms.Clear();
            _saving = true;
        }

        // The game's "Saved game in" that follows has the rest: copying the archive to the file. GameSaver.Save itself
        // can't be patched (Harmony can't rewrite its exception filter)
        private static void Report()
        {
            _saving = false;
            var entries = _ms.Where(p => p.Key.StartsWith("entry ")).ToList();
            double inEntries = entries.Sum(p => p.Value);
            string each = string.Join(", ", entries.Select(p => $"{p.Key.Substring(6)} {p.Value:F0}"));
            Plugin.Log($"[SaveTiming] finishing the tick {Get("tick"):F0} ms; the archive {Get("write"):F0} ms: collecting the world " +
                $"{Get("collect"):F0}, its JSON {Get("json"):F0}, the entries {inEntries:F0} ({each}), compressing {Get("write") - inEntries:F0}");
        }

        [HarmonyPatch]
        static class PhasePatcher
        {
            static bool Prepare() => Active;

            static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method("Timberborn.TickSystem.Ticker:FinishFullTick");
                yield return AccessTools.Method("Timberborn.WorldPersistence.SerializedWorldFactory:Create");
                yield return AccessTools.Method("Timberborn.WorldSerialization.WorldSerializer:WriteToSaveEntryStream");
                yield return AccessTools.Method("Timberborn.SaveSystem.SaveWriter:WriteToSaveStream");
            }

            // A save starts by finishing the tick, or writes without (SaveWithoutFinishingTick)
            static void Prefix(out long __state, MethodBase __originalMethod)
            {
                if (__originalMethod.Name == "FinishFullTick" || __originalMethod.Name == "WriteToSaveStream") Begin();
                __state = Stopwatch.GetTimestamp();
            }

            static void Postfix(long __state, MethodBase __originalMethod)
            {
                string phase = __originalMethod.Name switch
                {
                    "FinishFullTick" => "tick",
                    "Create" => "collect",
                    "WriteToSaveEntryStream" => "json",
                    _ => "write",
                };
                Add(phase, __state);
                if (phase == "write") Report();
            }
        }

        // Each entry of a save: the world, its metadata, its thumbnail, the game's version
        [HarmonyPatch]
        static class EntryPatcher
        {
            static bool Prepare() => Active;

            static IEnumerable<MethodBase> TargetMethods()
            {
                Type writer = AccessTools.TypeByName("Timberborn.SaveSystem.ISaveEntryWriter");
                foreach (string name in new[] { "Timberborn.WorldPersistence", "Timberborn.SaveMetadataSaving", "Timberborn.SaveThumbnailCapturing", "Timberborn.VersioningSerialization" })
                {
                    Assembly assembly;
                    try { assembly = Assembly.Load(name); }
                    catch (Exception) { continue; }
                    foreach (Type type in AccessTools.GetTypesFromAssembly(assembly).Where(t => !t.IsAbstract && writer.IsAssignableFrom(t)))
                    {
                        MethodBase method = AccessTools.Method(type, "WriteToSaveEntryStream");
                        if (method != null) yield return method;
                    }
                }
            }

            static void Prefix(out long __state) => __state = Stopwatch.GetTimestamp();

            static void Postfix(long __state, MethodBase __originalMethod) => Add("entry " + __originalMethod.DeclaringType.Name, __state);
        }
    }
}
