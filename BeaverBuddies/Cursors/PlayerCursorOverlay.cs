using System.Collections.Generic;
using Timberborn.CameraSystem;
using Timberborn.Coordinates;
using Timberborn.PlatformUtilities;
using Timberborn.TerrainQueryingSystem;
using UnityEngine;
using static BeaverBuddies.Util.OverlayDrawing;

namespace BeaverBuddies.Cursors
{
    public class PlayerCursorOverlay : MonoBehaviour
    {
        // Whether another player's cursor at this world position may be
        // shown, for mods that hide parts of the map; any false hides it
        public static readonly List<System.Func<Vector3, bool>> ShowAt = new();

        public PlayerCursorService Service;
        public CameraService CameraService;
        public TerrainPicker TerrainPicker;

        // How see-through a cursor is while something between the camera and it hides it
        private const float HiddenAlpha = 0.35f;
        private const float FadeSpeed = 12f;
        // Slack so the surface the cursor sits on doesn't count as covering it
        private const float OcclusionMargin = 0.3f;

        private readonly Dictionary<Color32, Texture2D> _textures = new();
        private float _pixelsPerPoint;

        public void OnGUI()
        {
            if (Service == null || !Settings.ShowCursors) return;
            if (Event.current.type != EventType.Repaint) return;

            Camera cam = TryGetCamera(CameraService);
            if (cam == null || !cam.isActiveAndEnabled) return;

            UpdatePixelsPerPoint();
            float screenH = Screen.height;

            foreach (RemoteCursor cursor in Service.Cursors)
            {
                if (!cursor.Visible) continue;
                if (!ShowAt.TrueForAll(show => show(cursor.DisplayedPosition))) continue;

                Vector3 sp = cam.WorldToScreenPoint(cursor.DisplayedPosition);
                if (sp.z < 0f) continue;
                Vector2 guiPos = new Vector2(sp.x, screenH - sp.y);

                Texture2D texture = GetTexture(cursor.Color);
                if (guiPos.x < -texture.width || guiPos.x > Screen.width
                    || guiPos.y < -texture.height || guiPos.y > screenH) continue;

                float targetAlpha = IsOccluded(cam, cursor.DisplayedPosition, sp) ? HiddenAlpha : 1f;
                cursor.Alpha = Mathf.MoveTowards(cursor.Alpha, targetAlpha, FadeSpeed * Time.unscaledDeltaTime);

                // The arrow's tip is the texture's top left corner
                Color previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, cursor.Alpha);
                GUI.DrawTexture(new Rect(guiPos.x, guiPos.y, texture.width, texture.height), texture);
                GUI.color = previous;
            }
        }

        /**
         * Whether something stands between the camera and the point: the
         * terrain, or a building, tree or anything else with a collider.
         */
        private bool IsOccluded(Camera cam, Vector3 worldPos, Vector3 screenPos)
        {
            Vector2 screenPoint = new Vector2(screenPos.x, screenPos.y);

            if (TerrainPicker != null)
            {
                Ray gridRay = CameraService.ScreenPointToRayInGridSpace(screenPoint);
                var hit = TerrainPicker.PickTerrainCoordinates(gridRay);
                if (hit.HasValue)
                {
                    float toHit = Vector3.Distance(gridRay.origin, hit.Value.Intersection);
                    float toCursor = Vector3.Distance(gridRay.origin, CoordinateSystem.WorldToGrid(worldPos));
                    if (toHit < toCursor - OcclusionMargin) return true;
                }
            }

            Ray worldRay = cam.ScreenPointToRay(screenPos);
            float distance = Vector3.Distance(worldRay.origin, worldPos) - OcclusionMargin;
            return distance > 0f
                && Physics.Raycast(worldRay, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        public void OnDestroy()
        {
            ClearTextures();
        }

        /**
         * The OS cursor is sized in points, so match the display's scale:
         * Windows reports its scaling as the DPI (96 at 100%), and Macs
         * are either 1x or 2x (Retina). If the game renders below the
         * display's resolution, shrink to match.
         */
        private void UpdatePixelsPerPoint()
        {
            float dpi = Screen.dpi;
            float osScale;
            if (dpi <= 0) osScale = 1f;
            else if (ApplicationPlatform.IsMacOS()) osScale = dpi > 150f ? 2f : 1f;
            else osScale = dpi / 96f;

            int displayWidth = Display.main.systemWidth;
            float renderScale = displayWidth > 0 ? Mathf.Min(1f, (float)Screen.width / displayWidth) : 1f;
            float pixelsPerPoint = Mathf.Max(1f, osScale * renderScale);

            if (!Mathf.Approximately(pixelsPerPoint, _pixelsPerPoint))
            {
                Plugin.Log($"Player cursor scale {pixelsPerPoint} (dpi {dpi}, screen {Screen.width}, display {displayWidth})");
                _pixelsPerPoint = pixelsPerPoint;
                ClearTextures();
            }
        }

        private Texture2D GetTexture(Color color)
        {
            Color32 key = color;
            if (!_textures.TryGetValue(key, out Texture2D texture))
            {
                // Same look as the OS arrow, with the player's color as the fill
                Color outline = ApplicationPlatform.IsMacOS() ? Color.white : Color.black;
                texture = ArrowTexture.Create(_pixelsPerPoint, color, outline);
                _textures[key] = texture;
            }
            return texture;
        }

        private void ClearTextures()
        {
            foreach (Texture2D texture in _textures.Values)
            {
                Destroy(texture);
            }
            _textures.Clear();
        }

        /**
         * The standard arrow pointer, in points, with its tip at the origin.
         * It's about as big as the OS arrow at its default size.
         */
        private static class ArrowTexture
        {
            private const float Width = 12f;
            private const float Height = 19f;
            private const float OutlineWidth = 1f;
            private const int Samples = 4;

            private static readonly Vector2[] Points =
            {
                new(0f, 0f),
                new(0f, 16.5f),
                new(3.9f, 13.1f),
                new(6.2f, 19f),
                new(9.7f, 17.6f),
                new(7.4f, 12f),
                new(12f, 12f),
            };

            public static Texture2D Create(float pixelsPerPoint, Color fill, Color outline)
            {
                int width = Mathf.CeilToInt(Width * pixelsPerPoint);
                int height = Mathf.CeilToInt(Height * pixelsPerPoint);
                var pixels = new Color[width * height];
                float step = 1f / Samples;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // Supersample each pixel so the edges are smooth
                        int fillCount = 0, outlineCount = 0;
                        for (int sy = 0; sy < Samples; sy++)
                        {
                            for (int sx = 0; sx < Samples; sx++)
                            {
                                // Texture rows start at the bottom, the arrow's at the top
                                Vector2 p = new Vector2(
                                    x + (sx + 0.5f) * step,
                                    height - y - (sy + 0.5f) * step) / pixelsPerPoint;
                                if (!IsInside(p)) continue;
                                if (DistanceToEdge(p) < OutlineWidth) outlineCount++;
                                else fillCount++;
                            }
                        }

                        int covered = fillCount + outlineCount;
                        Color c = Color.clear;
                        if (covered > 0)
                        {
                            c = (fill * fillCount + outline * outlineCount) / covered;
                            c.a = (float)covered / (Samples * Samples);
                        }
                        pixels[y * width + x] = c;
                    }
                }

                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                texture.SetPixels(pixels);
                texture.Apply();
                return texture;
            }

            private static bool IsInside(Vector2 p)
            {
                bool inside = false;
                for (int i = 0, j = Points.Length - 1; i < Points.Length; j = i++)
                {
                    Vector2 a = Points[i], b = Points[j];
                    if ((a.y > p.y) != (b.y > p.y)
                        && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    {
                        inside = !inside;
                    }
                }
                return inside;
            }

            private static float DistanceToEdge(Vector2 p)
            {
                float min = float.PositiveInfinity;
                for (int i = 0, j = Points.Length - 1; i < Points.Length; j = i++)
                {
                    Vector2 a = Points[j], b = Points[i];
                    Vector2 ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    min = Mathf.Min(min, Vector2.Distance(p, a + ab * t));
                }
                return min;
            }
        }
    }
}
