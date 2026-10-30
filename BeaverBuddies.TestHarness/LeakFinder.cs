using BeaverBuddies.Performance;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BeaverBuddies.Util
{
    /**
     * Profiling only (BB_HITCH_MS): after each load, says whether a game
     * loaded before is still reachable, and by which chain of fields from a
     * static one. A game kept alive keeps its whole object graph, and every
     * garbage collection marks it all again.
     */
    public static class LeakFinder
    {
        // Each scene's singletons, as they update every frame
        [HarmonyPatch(typeof(Timberborn.SingletonSystem.SingletonLifecycleService), nameof(Timberborn.SingletonSystem.SingletonLifecycleService.UpdateSingletons))]
        static class UpdatingPatcher
        {
            static bool Prepare() => HitchWatch.Profiling;

            // Before HitchWatch's, which updates them itself
            [HarmonyPriority(Priority.First)]
            static void Prefix(Timberborn.SingletonSystem.SingletonLifecycleService __instance) => Updating(__instance);
        }

        // A Co-op game loaded: the leak finder's look, and the heap after it
        [HarmonyPatch(typeof(ReplayService), nameof(ReplayService.PostLoad))]
        static class LoadedPatcher
        {
            static bool Prepare() => HitchWatch.Profiling;

            static void Postfix()
            {
                Loaded();
                Plugin.LogWarning($"[Pace] heap after loading, collected: {GC.GetTotalMemory(true) >> 20} MB, " +
                    $"Mono used {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() >> 20} MB of {UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() >> 20} MB");
            }
        }

        // Every scene's singleton lifecycle, and the frame it last updated
        private static readonly List<(WeakReference scene, int frame)> Scenes = new List<(WeakReference, int)>();

        private static float _checkAt = -1;

        public static void Updating(object lifecycle)
        {
            if (_checkAt > 0 && UnityEngine.Time.realtimeSinceStartup > _checkAt)
            {
                _checkAt = -1;
                Plugin.LogWarning($"[Leak] 30 s into the scene, heap collected: {GC.GetTotalMemory(true) >> 20} MB");
                for (int i = 1; i < Singletons.Count - 1; i++)
                {
                    int alive = Singletons[i].Count(w => w.Target != null);
                    Plugin.LogWarning($"[Leak] 30 s in, scene {i}: {alive} of {Singletons[i].Count} singletons still alive");
                    if (alive > 300) WalkToAny(i);
                }
            }
            int frame = UnityEngine.Time.frameCount;
            for (int i = 0; i < Scenes.Count; i++)
            {
                if (!ReferenceEquals(Scenes[i].scene.Target, lifecycle)) continue;
                Scenes[i] = (Scenes[i].scene, frame);
                return;
            }
            Scenes.Add((new WeakReference(lifecycle), frame));
            _checkAt = UnityEngine.Time.realtimeSinceStartup + 30;
            var repository = ((Timberborn.SingletonSystem.SingletonLifecycleService)lifecycle)._singletonRepository;
            Singletons.Add(repository.GetSingletons<object>().Select(o => new WeakReference(o)).ToList());
            Plugin.LogWarning($"[Leak] a scene starts, heap collected: {GC.GetTotalMemory(true) >> 20} MB");
            // The scenes before this one, but the lasting first one
            for (int i = 1; i < Singletons.Count - 1; i++)
            {
                var alive = Singletons[i].Select(w => w.Target).Where(t => t != null).ToList();
                Plugin.LogWarning($"[Leak] scene {i}: {alive.Count} of {Singletons[i].Count} singletons still alive: " +
                    string.Join(", ", alive.Select(a => a.GetType().Name).Take(40)));
                
            }
        }

        // Each scene's singletons, seen as it starts
        private static readonly List<List<WeakReference>> Singletons = new List<List<WeakReference>>();

        // A game is loaded: the scenes that no longer update should be gone
        public static void Loaded()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            int now = UnityEngine.Time.frameCount;
            var stale = Scenes.Where(s => s.frame < now - 2).Select(s => s.scene.Target).Where(t => t != null).ToList();
            Plugin.LogWarning($"[Leak] {stale.Count} of {Scenes.Count} scenes seen are gone from the game but still alive");
            if (Environment.GetEnvironmentVariable("BB_LEAK_SIZES") != null)
                new System.Threading.Thread(Sizes) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal }.Start();
            if (stale.Count > 0) Walk(stale[0]);
        }

        // Unity's live behaviours, read on the main thread for the walk
        private static List<(string, object)> StaticRoots = new List<(string, object)>();
        private static Dictionary<object, string> _names = new Dictionary<object, string>();
        private static UnityEngine.Object[] Behaviours = new UnityEngine.Object[0];

        private static string SafeName(UnityEngine.Object behaviour)
        {
            try { return behaviour is UnityEngine.Component c ? c.gameObject.name : behaviour.name; } catch { return "?"; }
        }

        // To whichever of that scene's singletons comes first
        private static int _targetScene = -1;
        private static void WalkToAny(int scene)
        {
            _targetScene = scene;
            Walk(Singletons[scene].Select(w => w.Target).First(t => t != null));
        }

        private static HashSet<object> _targets;

        private static bool IsTarget(object value, object target) =>
            ReferenceEquals(value, target) || (_targets != null && _targets.Contains(value));

        // Off the main thread: the walk can cover the whole heap and take minutes
        private static void Walk(object leaked)
        {
            if (Environment.GetEnvironmentVariable("BB_LEAK_WALK") == null) return;
            // Read here, on the main thread: off it, a static constructor that
            // calls Unity hangs, and thread statics would be the walk's own
            var roots = new List<(string, object)>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                bool all = Roots.Contains("*") || Roots.Any(r => assembly.GetName().Name.StartsWith(r));
                // Reading a field runs its type's static constructor, and some of Unity's crash run that way
                string asm = assembly.GetName().Name;
                bool unity = asm.StartsWith("Unity");
                Type[] types;
                try { types = assembly.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var type in types)
                {
                    if (type.ContainsGenericParameters) continue;
                    if (unity && type.TypeInitializer != null) continue;
                    if (type == typeof(LeakFinder) || type.DeclaringType == typeof(LeakFinder)) continue;
                    FieldInfo[] fields;
                    try { fields = type.GetFields(Static); } catch { continue; }
                    foreach (var field in fields)
                    {
                        if (field.FieldType.IsPrimitive || field.IsLiteral) continue;
                        bool threadStatic = field.IsDefined(typeof(ThreadStaticAttribute), false);
                        if (!all && !threadStatic) continue;
                        try { roots.Add(($"{(threadStatic ? "[ThreadStatic] " : "")}{type.FullName}.{field.Name}", field.GetValue(null))); } catch { }
                    }
                }
            }
            roots.Add(("SynchronizationContext.Current", System.Threading.SynchronizationContext.Current));
            StaticRoots = roots;
            Behaviours = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.MonoBehaviour>().Cast<UnityEngine.Object>()
                .Concat(UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.ScriptableObject>()).ToArray();
            var names = Behaviours.ToDictionary(b => (object)b, SafeName, new ByReference());
            _names = names;
            var target = new WeakReference(leaked);
            leaked = null;
            new System.Threading.Thread(() =>
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                string path;
                try { path = PathTo(target.Target); }
                catch (Exception e) { path = $"the walk failed: {e.Message}"; }
                finally { _targets = null; }
                Plugin.LogWarning($"[Leak] kept by ({clock.Elapsed.TotalSeconds:F0} s): {path ?? "no path from a static field found"}");
            }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal }.Start();
        }

        private class Node
        {
            public object Value;
            public Node Parent;
            public string Name;
        }

        private class ByReference : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
        }

        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Dictionary<Type, FieldInfo[]> FieldsOf = new Dictionary<Type, FieldInfo[]>();

        // Breadth first from every static field of the game's and the mods' code, so the path is a shortest one
        // BB_LEAK_ROOTS: the assemblies whose static fields the walk starts from
        private static readonly string[] Roots = (Environment.GetEnvironmentVariable("BB_LEAK_ROOTS") ?? "BeaverBuddies").Split(',');

        private static string PathTo(object target)
        {
            if (target == null) return "it was collected after all";
            _targets = _targetScene < 0 ? null
                : new HashSet<object>(Singletons[_targetScene].Select(w => w.Target).Where(t => t != null), new ByReference());
            var seen = new HashSet<object>(new ByReference());
            // Not through the game being played: it's most of the heap, and a leak doesn't need it
            foreach (var w in Singletons[Singletons.Count - 1]) if (w.Target is object current) seen.Add(current);
            var queue = new Queue<Node>();
            foreach (var (name, value) in StaticRoots) Enqueue(queue, seen, value, null, name);
            // And the lasting scene's singletons: Unity holds that scene, no static field does
            if (Singletons.Count > 0)
                foreach (var w in Singletons[0])
                    if (w.Target is object lasting) Enqueue(queue, seen, lasting, null, $"(lasting scene) {lasting.GetType().FullName}");
            foreach (var behaviour in Behaviours) Enqueue(queue, seen, behaviour, null, $"(live {behaviour.GetType().FullName} on {(_names.TryGetValue(behaviour, out var n) ? n : "?")})");
            int visited = 0;
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                visited++;
                if (IsTarget(node.Value, target)) return Describe(node);
                if (visited % 1000000 == 0) Plugin.LogWarning($"[Leak] walked {visited} objects, {queue.Count} queued");
                object value = node.Value;
                if (value is Array array)
                {
                    if (array.GetType().GetElementType().IsPrimitive) continue;
                    int i = 0;
                    foreach (var item in array) Enqueue(queue, seen, item, node, $"[{i++}]");
                    continue;
                }
                var type = value.GetType();
                if (value is Delegate d)
                {
                    Enqueue(queue, seen, d.Target, node, $"(delegate {d.Method.DeclaringType?.Name}.{d.Method.Name})");
                    foreach (var inner in d.GetInvocationList()) if (inner != d) Enqueue(queue, seen, inner, node, "(invocation)");
                    continue;
                }
                foreach (var field in Fields(type))
                {
                    object child;
                    try { child = field.GetValue(value); } catch { continue; }
                    Enqueue(queue, seen, child, node, $".{field.Name}");
                }
            }
            return null;
        }

        // Roughly how much each static field of the roots keeps reachable, not counting what one before it reached
        private static void Sizes()
        {
            var seen = new HashSet<object>(new ByReference());
            var sizes = new List<(string, long, long)>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!Roots.Any(r => assembly.GetName().Name.StartsWith(r))) continue;
                Type[] types;
                try { types = assembly.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var type in types)
                {
                    if (type.IsGenericTypeDefinition) continue;
                    FieldInfo[] fields;
                    try { fields = type.GetFields(Static); } catch { continue; }
                    foreach (var field in fields)
                    {
                        if (field.FieldType.IsPrimitive || field.IsLiteral) continue;
                        object value;
                        try { value = field.GetValue(null); } catch { continue; }
                        long objects = 0, bytes = 0;
                        var queue = new Queue<Node>();
                        Enqueue(queue, seen, value, null, "");
                        while (queue.Count > 0)
                        {
                            var v = queue.Dequeue().Value;
                            objects++;
                            if (v is Array array)
                            {
                                var element = array.GetType().GetElementType();
                                if (element.IsPrimitive) { bytes += array.LongLength * System.Runtime.InteropServices.Marshal.SizeOf(element); continue; }
                                bytes += array.LongLength * 8;
                                foreach (var item in array) Enqueue(queue, seen, item, null, "");
                                continue;
                            }
                            bytes += 16;
                            foreach (var f in Fields(v.GetType()))
                            {
                                bytes += 8;
                                object child;
                                try { child = f.GetValue(v); } catch { continue; }
                                Enqueue(queue, seen, child, null, "");
                            }
                        }
                        if (bytes > 1 << 20) sizes.Add(($"{type.FullName}.{field.Name}", objects, bytes));
                    }
                }
            }
            foreach (var (name, objects, bytes) in sizes.OrderByDescending(s => s.Item3).Take(15))
                Plugin.LogWarning($"[Leak] {name}: {bytes >> 20} MB in {objects} objects");
        }

        private static IEnumerable<FieldInfo> Fields(Type type)
        {
            if (FieldsOf.TryGetValue(type, out var fields)) return fields;
            var all = new List<FieldInfo>();
            for (var t = type; t != null; t = t.BaseType)
                all.AddRange(t.GetFields(Instance | BindingFlags.DeclaredOnly).Where(f => !f.FieldType.IsPrimitive && !f.FieldType.IsEnum && !f.FieldType.IsPointer));
            return FieldsOf[type] = all.ToArray();
        }

        private static void Enqueue(Queue<Node> queue, HashSet<object> seen, object value, Node parent, string name)
        {
            if (value == null || value is string || value is Pointer) return;
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || type.IsPointer) return;
            if (!seen.Add(value)) return;
            queue.Enqueue(new Node { Value = value, Parent = parent, Name = name });
        }

        private static string Describe(Node node)
        {
            var parts = new List<string>();
            for (var n = node; n != null; n = n.Parent) parts.Add($"{n.Name} ({n.Value.GetType().Name})");
            parts.Reverse();
            return string.Join(" -> ", parts);
        }
    }
}
