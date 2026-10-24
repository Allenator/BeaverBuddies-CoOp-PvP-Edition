using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Timberborn.DecalSystem;
using Timberborn.DecalSystemUI;
using Timberborn.HttpApiSystem;
using Timberborn.Illumination;
using Timberborn.IlluminationUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace BeaverBuddies.Events
{
    /**
     * A player's own cosmetic choices that the game also changes by itself:
     * a banner's or tail painter's image, and a light's color and whether
     * it's customized. Their setters are called by game logic too (placing,
     * Refresh, indicators copying their input's color, the HTTP Lever), so
     * the setter can't be what's synced (D1). Instead each of the player's
     * controls arms this for the length of its call, and the setter it calls
     * first goes out as an event, with the value the game worked out. What
     * that call sets off in turn is the game's, and plays on every machine.
     */
    static class PlayerControl
    {
        [ThreadStatic] private static bool _armed;

        internal static void Arm() => _armed = true;

        internal static void Disarm() => _armed = false;

        // Whether this setter call is the player's: once per control's call
        internal static bool Take()
        {
            if (!_armed) return false;
            _armed = false;
            return true;
        }
    }

    [Serializable]
    class DecalPickedEvent : ReplayEvent
    {
        public string entityID;
        public string category;
        public string id;

        public override void Replay(IReplayContext context)
        {
            GetComponent<DecalSupplier>(context, entityID)?.SetActiveDecal(new Decal(id, category));
        }

        public override string ToActionString()
        {
            return $"Picking the {category} image {id} for {entityID}";
        }
    }

    [Serializable]
    class IlluminatorColorEvent : ReplayEvent
    {
        public string entityID;
        // Red, green, blue and alpha, exactly as the game worked them out, or null for no custom color
        public float[] color;

        public override void Replay(IReplayContext context)
        {
            GetComponent<CustomizableIlluminator>(context, entityID)?.SetCustomColor(ToColor(color));
        }

        public override string ToActionString()
        {
            return $"Setting the light color of {entityID} to {(color == null ? "none" : ColorUtility.ToHtmlStringRGBA(ToColor(color).Value))}";
        }

        internal static float[] FromColor(Color? color) => color is Color c ? new[] { c.r, c.g, c.b, c.a } : null;

        internal static Color? ToColor(float[] color) => color == null ? null : new Color(color[0], color[1], color[2], color[3]);
    }

    [Serializable]
    class IlluminatorCustomizedEvent : ReplayEvent
    {
        public string entityID;
        // What the player meant, not "the opposite of now", so two clicks at once agree (D2)
        public bool customized;

        public override void Replay(IReplayContext context)
        {
            GetComponent<CustomizableIlluminator>(context, entityID)?.SetIsCustomized(customized);
        }

        public override string ToActionString()
        {
            return $"{(customized ? "Customizing" : "Uncustomizing")} the light of {entityID}";
        }
    }

    [Serializable]
    class HttpLeverColorEvent : ReplayEvent
    {
        public string entityID;
        public float[] color;

        public override void Replay(IReplayContext context)
        {
            GetComponent<HttpLever>(context, entityID)?.SetColor(IlluminatorColorEvent.ToColor(color).Value);
        }

        public override string ToActionString()
        {
            return $"Setting the HTTP lever color of {entityID} to {ColorUtility.ToHtmlStringRGBA(IlluminatorColorEvent.ToColor(color).Value)}";
        }
    }

    // The picker's image buttons. The click's handler is made by the compiler, so it's found as the one of DecalButton's
    // methods that takes a click and picks an image
    [HarmonyPatch]
    static class DecalButtonClickPatcher
    {
        // Found once, as the patch is made: reading a method's body mid-game would draw on the game's random numbers
        // on this machine alone (Harmony makes GUIDs, which BeaverBuddies makes from them, see GuidPatcher)
        internal static readonly MethodInfo Handler = AccessTools.GetDeclaredMethods(typeof(DecalButton)).FirstOrDefault(m =>
            m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(ClickEvent)
            && PatchProcessor.ReadMethodBody(m).Any(i => i.Value is MethodInfo called && called.Name == nameof(DecalSupplier.SetActiveDecal)));

        static bool Prepare()
        {
            if (Handler != null) return true;
            Plugin.LogError("No click handler on DecalButton that picks an image: banner and tail picks won't be synced");
            return false;
        }

        static MethodBase TargetMethod() => Handler;

        static void Prefix() => PlayerControl.Arm();

        static void Finalizer() => PlayerControl.Disarm();
    }

    [HarmonyPatch(typeof(DecalSupplier), nameof(DecalSupplier.SetActiveDecal))]
    static class DecalPickPatcher
    {
        static bool Prefix(DecalSupplier __instance, Decal decal)
        {
            if (!PlayerControl.Take()) return true;
            return ReplayEvent.DoEntityPrefix(__instance, entityID => new DecalPickedEvent
            {
                entityID = entityID,
                category = decal.Category,
                id = decal.Id,
            });
        }
    }

    // A light's color field and preset buttons, and its Customize toggle
    [HarmonyPatch]
    static class LightControlsPatcher
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CustomizableIlluminatorFragment), nameof(CustomizableIlluminatorFragment.OnRgbValueChanged));
            yield return AccessTools.Method(typeof(CustomizableIlluminatorFragment), nameof(CustomizableIlluminatorFragment.SetCustomColorToPreset));
            yield return AccessTools.Method(typeof(CustomizeIlluminationFragment), nameof(CustomizeIlluminationFragment.OnClicked));
        }

        static void Prefix() => PlayerControl.Arm();

        static void Finalizer() => PlayerControl.Disarm();
    }

    [HarmonyPatch(typeof(CustomizableIlluminator), nameof(CustomizableIlluminator.SetCustomColor))]
    static class LightColorPatcher
    {
        static bool Prefix(CustomizableIlluminator __instance, Color? value)
        {
            if (!PlayerControl.Take()) return true;
            return ReplayEvent.DoEntityPrefix(__instance, entityID => new IlluminatorColorEvent
            {
                entityID = entityID,
                color = IlluminatorColorEvent.FromColor(value),
            });
        }
    }

    [HarmonyPatch(typeof(CustomizableIlluminator), nameof(CustomizableIlluminator.SetIsCustomized))]
    static class LightCustomizedPatcher
    {
        static bool Prefix(CustomizableIlluminator __instance, bool value)
        {
            if (!PlayerControl.Take()) return true;
            return ReplayEvent.DoEntityPrefix(__instance, entityID => new IlluminatorCustomizedEvent
            {
                entityID = entityID,
                customized = value,
            });
        }
    }

    // An HTTP Lever's color, from the HTTP API: its only caller is the API's command queue, so the setter is the
    // player's (D1). Its switching on and off goes through the synced Lever.SwitchState
    [HarmonyPatch(typeof(HttpLever), nameof(HttpLever.SetColor))]
    static class HttpLeverColorPatcher
    {
        static bool Prefix(HttpLever __instance, Color color)
        {
            return ReplayEvent.DoEntityPrefix(__instance, entityID => new HttpLeverColorEvent
            {
                entityID = entityID,
                color = IlluminatorColorEvent.FromColor(color),
            });
        }
    }
}
