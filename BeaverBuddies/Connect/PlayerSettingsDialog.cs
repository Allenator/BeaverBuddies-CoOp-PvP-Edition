using System.Collections.Generic;
using System.Linq;
using BeaverBuddies.Players;
using Timberborn.CoreUI;
using Timberborn.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace BeaverBuddies.Connect
{
    /**
     * A player's nickname and color, from the host's box: the same two
     * settings the mod's settings have. A color another player has is
     * grayed out. The change reaches the others as it's made (see
     * PlayerPresence), and the game, once it runs.
     */
    public static class PlayerSettingsDialog
    {
        private const float Swatch = 26;
        private static readonly Color Gold = new(0.78f, 0.64f, 0.38f);

        public static void Show(DialogBoxShower shower)
        {
            ILoc loc = shower._loc;
            // The colors the others have, as the match gives them out
            var taken = new HashSet<int>(PlayerPresence.Others().Select(o => o.Color));
            // and those of players still joining, as they asked for them
            foreach (int pending in HostLobby.PendingColors()) taken.Add(pending);
            int color = PlayerPresence.LocalColorIndex();
            string name = PlayerIdentity.LocalName;

            var content = new VisualElement();
            content.style.width = 340;
            content.Add(Heading(loc.T("BeaverBuddies.Lobby.Nickname")));
            var field = new TextField { value = name, maxLength = 24 };
            field.AddToClassList("game-text-normal");
            field.style.marginLeft = 0;
            field.style.marginRight = 0;
            field.style.marginBottom = 12;
            content.Add(field);
            content.Add(Heading(loc.T("BeaverBuddies.Lobby.Color")));
            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.NoWrap;
            var swatches = new List<VisualElement>();
            for (int i = 0; i < PlayerColors.Palette.Length; i++)
            {
                int index = i;
                bool used = taken.Contains(i);
                var swatch = new VisualElement();
                swatch.style.width = Swatch;
                swatch.style.height = Swatch;
                swatch.style.marginRight = i < PlayerColors.Palette.Length - 1 ? 4 : 0;
                swatch.style.backgroundColor = PlayerColors.Palette[i].Color;
                swatch.style.borderTopWidth = swatch.style.borderBottomWidth = swatch.style.borderLeftWidth = swatch.style.borderRightWidth = 2;
                if (used)
                {
                    // Someone has it: grayed out, and not to be picked
                    Color own = PlayerColors.Palette[i].Color;
                    swatch.style.backgroundColor = new Color(own.r * 0.35f, own.g * 0.35f, own.b * 0.35f);
                    swatch.pickingMode = PickingMode.Ignore;
                    foreach (float degrees in new[] { 45f, -45f })
                    {
                        var bar = new VisualElement();
                        bar.pickingMode = PickingMode.Ignore;
                        bar.style.position = Position.Absolute;
                        bar.style.left = Swatch / 2 - 1 - 2;
                        bar.style.top = -2;
                        bar.style.width = 2;
                        bar.style.height = Swatch + 4;
                        bar.style.backgroundColor = new Color(0.85f, 0.85f, 0.85f, 0.9f);
                        bar.style.rotate = new Rotate(new Angle(degrees, AngleUnit.Degree));
                        swatch.Add(bar);
                    }
                }
                else swatch.RegisterCallback((ClickEvent _) =>
                {
                    color = index;
                    Mark(swatches, color);
                });
                swatches.Add(swatch);
                grid.Add(swatch);
            }
            Mark(swatches, color);
            content.Add(grid);

            // The box behind it dimmed, so this one stands out: a layer under the dialog, gone with it
            var dim = new VisualElement();
            dim.style.position = Position.Absolute;
            dim.style.left = dim.style.right = dim.style.top = dim.style.bottom = 0;
            dim.style.backgroundColor = new Color(0, 0, 0, 0.6f);
            shower._panelStack._container.Add(dim);
            shower.Create()
                .SetMessage(loc.T("BeaverBuddies.Lobby.Profile"))
                .AddContent(content)
                .SetConfirmButton(() =>
                {
                    dim.RemoveFromHierarchy();
                    Settings.SetPlayer(field.value, color);
                }, loc.T("BeaverBuddies.Lobby.Done"))
                .SetCancelButton(() => dim.RemoveFromHierarchy(), loc.T(Timberborn.Common.CommonLocKeys.CancelKey))
                .Show();
        }

        // The picked one has a bright frame
        private static void Mark(List<VisualElement> swatches, int picked)
        {
            for (int i = 0; i < swatches.Count; i++)
            {
                Color frame = i == picked ? Color.white : new Color(0, 0, 0, 0.4f);
                swatches[i].style.borderTopColor = swatches[i].style.borderBottomColor = swatches[i].style.borderLeftColor = swatches[i].style.borderRightColor = frame;
            }
        }

        private static Label Heading(string text)
        {
            var label = new Label(text);
            label.AddToClassList("game-text-normal");
            label.AddToClassList("text--yellow");
            label.style.marginBottom = 4;
            return label;
        }
    }
}
