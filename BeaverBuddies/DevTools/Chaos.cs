using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using TimberNet;
using Timberborn.Coordinates;
using UnityEngine;

namespace BeaverBuddies.DevTools
{
    /**
     * The test harness shaking the game up, to find what only goes wrong
     * under some timing: frames that stall or run at another rate than the
     * other copy's, messages that arrive late, and a player who changes the
     * speed, pauses, ticks once, places and demolishes at random moments.
     * Nothing here touches the game's own random numbers; its own are seeded,
     * so the same seed does the same things (though not at the same instants).
     *
     *   chaos off
     *   chaos seed level [prefab,prefab,... x y z radius]
     *
     * level is 1 (mild) to 3. The places are around x y z (grid), of the
     * prefabs given; without them it only changes speeds and timing.
     */
    internal static class Chaos
    {
        private static System.Random _frames, _actions, _network;
        private static int _level;
        private static bool _on;
        private static float _nextAction, _nextRate;
        private static string[] _prefabs = new string[0];
        private static Vector3Int _around;
        private static int _radius;
        private static readonly List<(string prefab, Vector3Int at)> _placed = new();
        public static int Actions { get; private set; }

        public static void Start(string[] a)
        {
            if (a.Length > 1 && a[1] == "off")
            {
                _on = false;
                TimberNetBase.BeforeReceive = null;
                Application.targetFrameRate = -1;
                Plugin.Log($"[Chaos] off after {Actions} actions");
                return;
            }
            int seed = int.Parse(a[1], CultureInfo.InvariantCulture);
            _level = Math.Max(1, Math.Min(3, int.Parse(a[2], CultureInfo.InvariantCulture)));
            _frames = new System.Random(seed);
            _actions = new System.Random(seed + 1);
            _network = new System.Random(seed + 2);
            if (a.Length > 7)
            {
                _prefabs = a[3].Split(',');
                _around = new Vector3Int(int.Parse(a[4]), int.Parse(a[5]), int.Parse(a[6]));
                _radius = int.Parse(a[7]);
            }
            QualitySettings.vSyncCount = 0;
            _nextAction = Time.unscaledTime + 2;
            _nextRate = 0;
            Actions = 0;
            // Only the connection's reading thread calls this
            TimberNetBase.BeforeReceive = () =>
            {
                if (_network.NextDouble() < 0.12 * _level) Thread.Sleep(_network.Next(5, 80 * _level));
            };
            _on = true;
            Plugin.Log($"[Chaos] on: seed {seed}, level {_level}, {_prefabs.Length} prefabs");
        }

        public static void Update()
        {
            if (!_on) return;
            float now = Time.unscaledTime;
            // Another frame rate every few seconds, and the odd frame that stalls
            if (now >= _nextRate)
            {
                int[] rates = { 15, 24, 30, 60, -1 };
                Application.targetFrameRate = rates[_frames.Next(rates.Length)];
                _nextRate = now + 3 + (float)_frames.NextDouble() * 7;
            }
            if (_frames.NextDouble() < 0.015 * _level) Thread.Sleep(_frames.Next(20, 120 * _level));
            if (now < _nextAction) return;
            _nextAction = now + 0.3f + (float)_actions.NextDouble() * 3f / _level;
            Act();
        }

        private static readonly string[] Military = { "Barracks", "ArcheryRange", "Herbalist", "Monastery", "SmallArmory", "MediumArmory", "Armory", "WagonYard" };
        private static readonly string[] Machines = { "Catapult", "Ballista", "BatteringRam", "SiegeTrebuchet", "SiegeLadder", "SiegeRamp", "MineLauncher" };

        private static bool Is(string prefab, string[] kinds) => kinds.Any(k => prefab.StartsWith(k + "."));

        private static Vector3Int RandomSpot(TestHarness harness)
        {
            int x = _around.x + _actions.Next(-_radius, _radius + 1), y = _around.y + _actions.Next(-_radius, _radius + 1);
            return new Vector3Int(x, y, harness.GroundAt(x, y));
        }

        private static string Pick(string[] list) => list[_actions.Next(list.Length)];

        private static void Act()
        {
            var harness = TestHarness.Current;
            if (harness == null) return;
            string command = null;
            int roll = _actions.Next(100);
            if (roll < 12) command = "speed " + Pick(new[] { "0", "1", "3", "7" });
            else if (roll < 18) command = "speedkey " + Pick(new[] { "0", "1", "3" });
            else if (roll < 21) command = "tickonce";
            else if (_prefabs.Length == 0) return;
            else if (roll < 58 || _placed.Count == 0)
            {
                // Spots until one fits: most of the map's tiles don't suit most buildings
                string[] rotations = { "0", "90", "180", "270" };
                // A third of them the army's: barracks, ranges, armories, siege machines
                var army = _prefabs.Where(f => Is(f, Military) || Is(f, Machines)).ToArray();
                var machines = _prefabs.Where(f => Is(f, Machines)).ToArray();
                string prefab = _actions.Next(3) != 0 || army.Length == 0 ? Pick(_prefabs)
                    : machines.Length > 0 && _actions.Next(2) == 0 ? Pick(machines) : Pick(army);
                for (int tries = 0; tries < 25 && command == null; tries++)
                {
                    Vector3Int at = RandomSpot(harness);
                    int turn = _actions.Next(4);
                    Orientation orientation = new[] { Orientation.Cw0, Orientation.Cw90, Orientation.Cw180, Orientation.Cw270 }[turn];
                    bool fits;
                    try { fits = harness.CanPlace(prefab, at, orientation); }
                    catch { fits = false; }
                    if (!fits) continue;
                    command = $"place {prefab} {at.x} {at.y} {at.z} {rotations[turn]}{(_actions.Next(3) == 0 ? " instant" : "")}";
                    _placed.Add((prefab, at));
                }
                if (command == null) return;
            }
            else if (roll < 68)
            {
                int i = _actions.Next(_placed.Count);
                command = $"delete {_placed[i].at.x} {_placed[i].at.y} {_placed[i].at.z}";
                _placed.RemoveAt(i);
            }
            else
            {
                // Troops: whoever works at one of the military buildings placed
                var barracks = _placed.Where(p => Is(p.prefab, Military)).ToList();
                var machines = _placed.Where(p => Is(p.prefab, Machines)).ToList();
                if (barracks.Count == 0) return;
                var from = barracks[_actions.Next(barracks.Count)].at;
                int kind = _actions.Next(10);
                if (kind < 2) command = $"train {from.x} {from.y} {from.z}";
                else if (kind < 4 && machines.Count > 0)
                {
                    var to = machines[_actions.Next(machines.Count)].at;
                    command = $"board {from.x} {from.y} {from.z} {to.x} {to.y} {to.z}";
                }
                else
                {
                    var to = RandomSpot(harness);
                    command = $"send {from.x} {from.y} {from.z} {to.x} {to.y} {to.z} {Pick(new[] { "attack", "attack", "move", "queue", "patrol", "halt", "clear" })}";
                }
            }
            Actions++;
            Plugin.Log($"[Chaos] {command}");
            try
            {
                harness.Run(command.Split(' '));
            }
            catch (Exception e)
            {
                // A demolition of nothing, a placement with no game to play it in: part of the noise
                Plugin.Log($"[Chaos] {command} did nothing: {e.Message}");
            }
        }
    }
}
