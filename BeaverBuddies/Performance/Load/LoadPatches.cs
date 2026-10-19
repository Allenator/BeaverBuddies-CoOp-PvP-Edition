// Ported from T3MP (More More More Performance!) by toropippi,
// https://github.com/toropippi/Timberborn-More-More-More-Performance-Mod
// Licensed under the Apache License 2.0 (see LICENSE-T3MP.txt).
// Changed for BeaverBuddies: namespace, settings and logging; the
// optimizations themselves are unchanged.

using System;
using System.Linq;
using Debug = UnityEngine.Debug;

namespace BeaverBuddies.Performance;

// Save-load optimizations. Every feature verifies the game modules it relies on
// (LoadCompatibility) and falls back to the native code path on any mismatch or
// foreign patch. Nothing here runs outside SingletonLifecycleService.LoadAll /
// EventBus.PostLoad scopes except the deferred empty GoodStack models, which are
// created by the native factory on first access.
internal static class LoadPatches
{
    private static bool _installed;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        try
        {
            var harmonyType = LoadPatchBridge.Find("HarmonyLib.Harmony");
            var harmonyMethodType = LoadPatchBridge.Find("HarmonyLib.HarmonyMethod");
            var patch = harmonyType.GetMethods().Single(m => m.Name == "Patch" && m.GetParameters().Length == 5);
            LoadEventRouter.Install(harmonyType, harmonyMethodType, patch);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[BeaverBuddies Perf] Load event routing unavailable: " + exception.GetBaseException().Message);
        }
        if (LoadSettings.EnablePreparedStatusIcons) PreparedStatusIcons.Install("beaverbuddies.perf.load.status-icons");
        PreparedEntityVisuals.Install("beaverbuddies.perf.load.entity-visuals");
        TransputLoadRouting.Install("beaverbuddies.perf.load.transput-routing", production: true);
        NavigationShapePlans.Install("beaverbuddies.perf.load.navigation-shapes", production: true);
        DeferredGoodStackModels.Install("beaverbuddies.perf.load.good-stack-models", production: true);
        LoadGcBudget.Install();
        ConstructionPlans.Install();
        ComponentConstructionPlans.Install();
        InitialNavigationLoad.Install();
        BlockLoadRouting.Install();
        EmptyFlowFieldLoad.Install();
        TerrainSurfaceLoad.Install();
        AtlasPixelsLoad.Install();
    }
}
