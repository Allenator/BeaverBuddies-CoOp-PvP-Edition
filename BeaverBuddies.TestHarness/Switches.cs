using BeaverBuddies.Connect;
using BeaverBuddies.DesyncDetecter;
using BeaverBuddies.IO;
using BeaverBuddies.Players;
using HarmonyLib;
using System;
using TimberNet;

namespace BeaverBuddies.DevTools
{
    /**
     * What a test copy is told otherwise than a player's game would be, as patches on BeaverBuddies. The two copies on
     * one Mac share its settings and preferences, so each is told its own here, by its name or by the runner's
     * switches (lib.sh's bb_open). HarnessStarter sets the rest of the switches BeaverBuddies reads (HarnessFlags), and
     * the tools the others turn on read their own (NavAudit, SaveTiming, HideCustomFiles, LossyCloseStream).
     */
    static class Switches
    {
        // Each copy its own player ID, from its name: the two share the preferences a player's is kept in
        [HarmonyPatch(typeof(PlayerIdentity), nameof(PlayerIdentity.LocalID), MethodType.Getter)]
        static class OwnPlayerID
        {
            private static string _id;

            static bool Prefix(ref string __result)
            {
                if (_id == null)
                {
                    using var md5 = System.Security.Cryptography.MD5.Create();
                    _id = new Guid(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(TestHarness.Instance))).ToString();
                }
                __result = _id;
                return false;
            }
        }

        // The second copy prefers red
        [HarmonyPatch(typeof(Settings), nameof(Settings.PreferredColorIndex), MethodType.Getter)]
        static class SecondPrefersRed
        {
            static void Postfix(ref int __result)
            {
                if (TestHarness.Instance != "host") __result = 1;
            }
        }

        // Strict about builds or not, whatever the shared setting says (BB_STRICT, 0 for not)
        [HarmonyPatch(typeof(Settings), nameof(Settings.StrictBuild), MethodType.Getter)]
        static class Strict
        {
            private static readonly string Told = Environment.GetEnvironmentVariable("BB_STRICT");

            static bool Prepare() => !string.IsNullOrEmpty(Told);

            static void Postfix(ref bool __result) => __result = Told != "0";
        }

        // Its own choice of what pauses everyone (BB_PAUSES, a PauseReductionLevel)
        [HarmonyPatch(typeof(Settings), nameof(Settings.OwnPauseReduction), MethodType.Getter)]
        static class Pauses
        {
            private static readonly int? Told = int.TryParse(Environment.GetEnvironmentVariable("BB_PAUSES"), out int told) ? told : null;

            static bool Prepare() => Told != null;

            static void Postfix(ref PauseReductionLevel __result) => __result = (PauseReductionLevel)Told.Value;
        }

        // Tests measuring what the record of each tick's entities costs turn it off (BB_DIGEST_OFF=1)
        [HarmonyPatch(typeof(StateDigest), nameof(StateDigest.Add))]
        static class DigestOff
        {
            static bool Prepare() => Environment.GetEnvironmentVariable("BB_DIGEST_OFF") == "1";

            static bool Prefix() => false;
        }

        // A join's save that takes a while (BB_SLOW_JOIN_SAVE, in ms), for the player joining to leave as it's made: the
        // host looks whether they're still there once it's saved (see ResyncService.SaveAndReload), and only for a join
        [HarmonyPatch(typeof(ServerEventIO), nameof(ServerEventIO.CancelJoinIfGone))]
        static class SlowJoinSave
        {
            private static readonly int? Slow = int.TryParse(Environment.GetEnvironmentVariable("BB_SLOW_JOIN_SAVE"), out int slow) ? slow : null;

            static bool Prepare() => Slow != null;

            static void Prefix()
            {
                Plugin.Log($"[Test] The join's save takes {Slow} ms longer");
                System.Threading.Thread.Sleep(Slow.Value);
            }
        }

        // The guest's connection over TCP can behave as a Steam session does as it closes, as the host's can (see
        // HarnessStarter and LossyCloseStream)
        [HarmonyPatch(typeof(ClientConnectionService), "TryToConnect", typeof(ISocketStream))]
        static class ClientStream
        {
            static void Prefix(ref ISocketStream socket)
            {
                if (socket is TCPClientWrapper) socket = LossyCloseStream.Wrap(socket);
            }
        }
    }
}
