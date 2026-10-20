using System;
using TingLu.Contracts;
using TingLu.Config;
using UnityEngine;

namespace TingLu.UI
{
    public sealed class StreetHudPresenter : MonoBehaviour, IStreetHud
    {
        private AccessibleMenu menu;
        private PlayerState player;
        private UserSettings settings;
        private string subtitle;
        private bool playing;
        private StreetCollisionFeedback collisionFeedback;
        private StreetCollisionFeedbackConfig collisionConfig;
        private Texture2D collisionEdgeTexture;
        private int collisionTextureScreenWidth = -1, collisionTextureScreenHeight = -1;
        private float collisionTextureEdgeWidth = -1f;
        public void ConfigureCollisionFeedback(StreetCollisionFeedback feedback, StreetCollisionFeedbackConfig config)
        { collisionFeedback=feedback; collisionConfig=config; }

        public void Configure(AccessibleMenu controller)
        {
            menu = controller ?? throw new ArgumentNullException("controller");
            AccessibleMenuPanel panel = GetComponent<AccessibleMenuPanel>();
            if (panel == null) panel = gameObject.AddComponent<AccessibleMenuPanel>();
            panel.Initialize(menu);
        }

        public void Present(PlayerState current, UserSettings userSettings, string currentSubtitle, bool isPlaying)
        {
            player = current;
            settings = userSettings;
            subtitle = currentSubtitle;
            playing = isPlaying;
        }

        private void OnGUI()
        {
            DrawCollisionGlow();
            if (menu == null || !playing || menu.Screen != MenuScreen.Playing ||
                settings == null || settings.Display != DisplayMode.Standard) return;

            Color oldColor = GUI.color;
            float scale = settings.FontScale;
            int fontSize = Mathf.RoundToInt(16f * scale);
            GUI.color = Color.white;

            float statusBottom = player == null ? 0f : DrawStatus(fontSize);
            if (settings.Subtitles && !string.IsNullOrWhiteSpace(subtitle)) DrawSubtitle(scale, statusBottom);

            GUI.color = oldColor;
        }

        private void DrawCollisionGlow()
        {
            if (collisionFeedback == null || collisionConfig == null || collisionFeedback.Alpha <= 0 ||
                Screen.width <= 0 || Screen.height <= 0) return;

            float edgeWidth = Mathf.Min(Screen.width, Screen.height) * collisionConfig.EdgeWidth;
            if (edgeWidth <= 0) return;
            EnsureCollisionEdgeTexture(edgeWidth);

            Color old = GUI.color;
            GUI.color = new Color(collisionConfig.Red, collisionConfig.Green, collisionConfig.Blue,
                collisionFeedback.Alpha);
            // One screen-space alpha field avoids band seams and corner overdraw. Its edge
            // distance uses current pixel dimensions, so the clear center survives any aspect ratio.
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), collisionEdgeTexture,
                ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        private void EnsureCollisionEdgeTexture(float edgeWidth)
        {
            if (collisionEdgeTexture != null && collisionTextureScreenWidth == Screen.width &&
                collisionTextureScreenHeight == Screen.height &&
                Mathf.Approximately(collisionTextureEdgeWidth, edgeWidth)) return;

            float scale = Mathf.Min(1f, 512f / Mathf.Max(Screen.width, Screen.height));
            int textureWidth = Mathf.Max(2, Mathf.RoundToInt(Screen.width * scale));
            int textureHeight = Mathf.Max(2, Mathf.RoundToInt(Screen.height * scale));
            if (collisionEdgeTexture == null || collisionEdgeTexture.width != textureWidth ||
                collisionEdgeTexture.height != textureHeight)
            {
                if (collisionEdgeTexture != null) Destroy(collisionEdgeTexture);
                collisionEdgeTexture = new Texture2D(textureWidth, textureHeight,
                    TextureFormat.RGBA32, false, true)
                {
                    name = "Street collision edge gradient",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            var pixels = new Color32[textureWidth * textureHeight];
            for (int y = 0; y < textureHeight; y++)
            {
                float screenY = (y + .5f) * Screen.height / textureHeight;
                for (int x = 0; x < textureWidth; x++)
                {
                    float screenX = (x + .5f) * Screen.width / textureWidth;
                    byte alpha = (byte)Mathf.RoundToInt(
                        StreetCollisionFeedback.ScreenEdgeAlpha(screenX, screenY,
                            Screen.width, Screen.height, collisionConfig.EdgeWidth) * 255f);
                    pixels[y * textureWidth + x] = new Color32(255, 255, 255, alpha);
                }
            }
            collisionEdgeTexture.SetPixels32(pixels);
            collisionEdgeTexture.Apply(false, false);
            collisionTextureScreenWidth = Screen.width;
            collisionTextureScreenHeight = Screen.height;
            collisionTextureEdgeWidth = edgeWidth;
        }

        private void OnDestroy()
        {
            if (collisionEdgeTexture == null) return;
            Destroy(collisionEdgeTexture);
            collisionEdgeTexture = null;
        }

        private float DrawStatus(int fontSize)
        {
            float margin = Mathf.Clamp(Screen.width * 0.025f, 8f, 24f);
            float width = Mathf.Min(Mathf.Clamp(Screen.width * 0.3f, 220f, 320f),
                Mathf.Max(0f, Screen.width - margin * 2f));
            float rowHeight = Mathf.Max(24f, fontSize + 8f);
            Rect background = new Rect(margin, margin, width, rowHeight * 2f + 12f);
            GUI.color = new Color(0.02f, 0.03f, 0.04f, 0.86f);
            GUI.DrawTexture(background, Texture2D.whiteTexture);
            GUI.color = Color.white;

            Color courage = settings.Palette == PaletteMode.ColorBlind
                ? new Color(1f, 0.67f, 0.18f)
                : new Color(0.96f, 0.39f, 0.32f);
            Color focus = settings.Palette == PaletteMode.ColorBlind
                ? new Color(0.20f, 0.62f, 0.90f)
                : new Color(0.20f, 0.78f, 0.65f);
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                wordWrap = false,
                alignment = TextAnchor.MiddleLeft
            };
            float contentX = margin + 8f;
            float contentWidth = Mathf.Max(0f, width - 16f);
            float labelTextWidth = Mathf.Max(labelStyle.CalcSize(new GUIContent("勇气")).x,
                labelStyle.CalcSize(new GUIContent("专注")).x) + 8f;
            float valueTextWidth = labelStyle.CalcSize(new GUIContent("100%")).x + 8f;
            while (labelTextWidth + valueTextWidth + Mathf.Max(4f, labelStyle.fontSize * 0.36f) > contentWidth && labelStyle.fontSize > 12)
            {
                labelStyle.fontSize--;
                labelTextWidth = Mathf.Max(labelStyle.CalcSize(new GUIContent("勇气")).x,
                    labelStyle.CalcSize(new GUIContent("专注")).x) + 8f;
                valueTextWidth = labelStyle.CalcSize(new GUIContent("100%")).x + 8f;
            }
            DrawMeter(new Rect(contentX, margin + 4f, contentWidth, rowHeight), "勇气", player.Courage, courage, labelStyle);
            DrawMeter(new Rect(contentX, margin + 4f + rowHeight, contentWidth, rowHeight), "专注", player.Focus, focus, labelStyle);
            return background.yMax;
        }

        private static void DrawMeter(Rect row, string label, float value, Color fill, GUIStyle labelStyle)
        {
            float labelWidth = labelStyle.CalcSize(new GUIContent(label)).x + 8f;
            string valueText = Mathf.RoundToInt(value) + "%";
            float valueWidth = labelStyle.CalcSize(new GUIContent(valueText)).x + 8f;
            float gap = Mathf.Max(4f, labelStyle.fontSize * 0.18f);
            GUI.Label(new Rect(row.x, row.y, labelWidth, row.height), label, labelStyle);

            float barX = row.x + labelWidth + gap;
            float barWidth = Mathf.Max(0f, row.width - labelWidth - valueWidth - gap * 2f);
            Rect bar = new Rect(barX, row.y + (row.height - 10f) * 0.5f, barWidth, 10f);
            if (barWidth > 0f)
            {
                GUI.color = new Color(0.26f, 0.29f, 0.31f, 1f);
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = fill;
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(value / 100f), bar.height), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
            GUI.Label(new Rect(row.xMax - valueWidth, row.y, valueWidth, row.height), valueText, labelStyle);
        }

        private void DrawSubtitle(float scale, float statusBottom)
        {
            float margin = Mathf.Clamp(Screen.width * 0.06f, 16f, 64f);
            float width = Mathf.Max(1f, Screen.width - margin * 2f);
            float textWidth = Mathf.Max(1f, width - 24f);
            float availableHeight = Mathf.Max(36f, Screen.height - statusBottom - 3f * margin);
            int subtitleFontSize = Mathf.Max(14, Mathf.RoundToInt(18f * scale));
            GUIStyle subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = subtitleFontSize,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter
            };
            float textHeight = subtitleStyle.CalcHeight(new GUIContent(subtitle), textWidth);
            while (textHeight + 20f > availableHeight && subtitleFontSize > 14)
            {
                subtitleStyle.fontSize = --subtitleFontSize;
                textHeight = subtitleStyle.CalcHeight(new GUIContent(subtitle), textWidth);
            }

            float height = Mathf.Min(availableHeight, Mathf.Max(44f, textHeight + 20f));
            Rect rect = new Rect(margin, Screen.height - height - margin, width, height);
            GUI.color = new Color(0.02f, 0.03f, 0.04f, 0.88f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, textWidth, rect.height - 16f), subtitle, subtitleStyle);
        }
    }
}
