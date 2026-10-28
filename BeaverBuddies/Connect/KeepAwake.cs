using BeaverBuddies.IO;
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace BeaverBuddies.Connect
{
    /**
     * Keeps the computer from going to sleep while in a co-op game, hosted or joined (Settings.KeepAwake): a host's
     * Mac that slept stopped everyone's game for four hours, and the guests' connections timed out (October 26,
     * 2026), and a guest's that sleeps drops out, to have everyone reload as it rejoins. System sleep only: the
     * display may sleep, as the game plays on without it, and a laptop's lid still puts it to sleep. On macOS a power
     * assertion, on Windows the main thread's execution state (Wine may not pass it on). Updated each frame from
     * BackgroundPlay, on the main thread, which Windows ties the state to; the game's end lets go of either.
     */
    public static class KeepAwake
    {
        private const string Reason = "Timberborn co-op game";
        private static bool _held;
        private static uint _assertion;

        /** Whether the computer is kept awake now, and how, for the log and the tests. */
        public static string State { get; private set; } = "off";

        public static void Update()
        {
            bool wanted = EventIO.Get() is ServerEventIO or ClientEventIO && Settings.KeepAwake;
            if (wanted == _held) return;
            _held = wanted;
            try
            {
                State = wanted ? Hold() : Release();
            }
            catch (Exception e)
            {
                State = "failed: " + e.Message;
            }
            Plugin.Log($"Keeping the computer awake: {State}");
        }

        private static string Hold()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.OSXPlayer:
                    IntPtr type = CFStringCreateWithCString(IntPtr.Zero, "PreventUserIdleSystemSleep", Utf8);
                    IntPtr name = CFStringCreateWithCString(IntPtr.Zero, Reason, Utf8);
                    int result = IOPMAssertionCreateWithName(type, AssertionLevelOn, name, out _assertion);
                    CFRelease(type);
                    CFRelease(name);
                    return result == 0 ? "on, a power assertion" : $"not on: the power assertion failed ({result})";
                case RuntimePlatform.WindowsPlayer:
                    return SetThreadExecutionState(Continuous | SystemRequired) != 0 ? "on, the execution state" : "not on: the execution state wasn't set";
                default:
                    return $"not on {Application.platform}";
            }
        }

        private static string Release()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.OSXPlayer:
                    if (_assertion != 0) IOPMAssertionRelease(_assertion);
                    _assertion = 0;
                    return "off";
                case RuntimePlatform.WindowsPlayer:
                    SetThreadExecutionState(Continuous);
                    return "off";
                default:
                    return "off";
            }
        }

        private const uint Utf8 = 0x08000100;
        private const uint AssertionLevelOn = 255;
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPStr)] string text, uint encoding);

        [DllImport(CoreFoundation)]
        private static extern void CFRelease(IntPtr value);

        [DllImport(IOKit)]
        private static extern int IOPMAssertionCreateWithName(IntPtr type, uint level, IntPtr name, out uint assertion);

        [DllImport(IOKit)]
        private static extern int IOPMAssertionRelease(uint assertion);

        private const uint Continuous = 0x80000000;
        private const uint SystemRequired = 0x00000001;

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint flags);
    }
}
