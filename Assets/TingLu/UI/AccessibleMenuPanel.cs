using System;
using TingLu.Contracts;
using UnityEngine;

namespace TingLu.UI
{
    /// <summary>Small keyboard, controller-submit, and mouse-accessible IMGUI shell around the pure menu.</summary>
    public sealed class AccessibleMenuPanel : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        private AccessibleMenu menu;
        private string subtitle = "";
        private Vector2 scrollPosition;
        private MenuScreen lastScreen;
        private bool screenInitialized;

        public void Initialize(AccessibleMenu controller)
        {
            if (menu != null) menu.Subtitle -= ShowSubtitle;
            menu = controller ?? throw new ArgumentNullException("controller");
            menu.Subtitle += ShowSubtitle;
        }

        public bool HandleInput(InputFrame frame)
        {
            if (menu == null) return false;
            return menu.HandleInput(frame);
        }

        private void OnGUI()
        {
            if (!visible || menu == null || menu.Options.Display != DisplayMode.Standard || menu.Screen == MenuScreen.Playing) return;
            if (!screenInitialized || lastScreen != menu.Screen)
            {
                scrollPosition = Vector2.zero;
                lastScreen = menu.Screen;
                screenInitialized = true;
            }
            float scale = menu.Options.FontScale;
            bool oldEnabled = GUI.enabled;
            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(18f * scale),
                wordWrap = true
            };
            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(21f * scale),
                wordWrap = true
            };
            Color oldBackground = GUI.backgroundColor;
            Color oldContent = GUI.contentColor;
            if (menu.Options.Palette == PaletteMode.ColorBlind)
            { GUI.backgroundColor = new Color(0.98f, 0.82f, 0.35f); GUI.contentColor = Color.black; }
            float viewportWidth = Mathf.Max(0f, Screen.width);
            float viewportHeight = Mathf.Max(0f, Screen.height);
            float marginX = Mathf.Min(24f, viewportWidth * 0.05f);
            float marginY = Mathf.Min(24f, viewportHeight * 0.05f);
            float areaWidth = Mathf.Min(440f, Mathf.Max(0f, viewportWidth - 2f * marginX));
            float areaHeight = Mathf.Max(0f, viewportHeight - 2f * marginY);
            GUILayout.BeginArea(new Rect(marginX, marginY, areaWidth, areaHeight), GUI.skin.box);
            float labelContentWidth = Mathf.Max(1f, areaWidth - 32f);
            DrawMeasuredLabel(Title(menu.Screen), labelStyle, labelContentWidth);
            if (!string.IsNullOrEmpty(menu.ScreenMessage))
                DrawMeasuredLabel(menu.ScreenMessage, labelStyle, labelContentWidth);
            if (menu.Options.Subtitles && !string.IsNullOrEmpty(subtitle))
                DrawMeasuredLabel(subtitle, labelStyle, labelContentWidth);
            GUILayout.Space(12f);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            float buttonTextWidth = Mathf.Max(1f, areaWidth - 52f);
            for (int i = 0; i < menu.Items.Count; i++)
            {
                MenuItem item = menu.Items[i];
                GUI.enabled = item.Enabled;
                string label = (i == menu.FocusedIndex ? "› " : "  ") + item.Label;
                float buttonHeight = Mathf.Max(42f, buttonStyle.CalcHeight(new GUIContent(label), buttonTextWidth) + 10f);
                if (GUILayout.Button(label, buttonStyle, GUILayout.MinHeight(buttonHeight), GUILayout.ExpandWidth(true)))
                {
                    if (i == menu.FocusedIndex) menu.ActivateFocused();
                    else menu.FocusItem(i);
                }
            }
            if (menu.Screen == MenuScreen.Settings)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("调低", buttonStyle, GUILayout.MinHeight(42f)))
                    menu.HandleInput(new InputFrame(new Point2(0, 0), false, false, false, MenuAction.Decrease));
                if (GUILayout.Button("调高", buttonStyle, GUILayout.MinHeight(42f)))
                    menu.HandleInput(new InputFrame(new Point2(0, 0), false, false, false, MenuAction.Increase));
                GUILayout.EndHorizontal();
            }
            GUI.enabled = true;
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.backgroundColor = oldBackground;
            GUI.contentColor = oldContent;
            GUI.enabled = oldEnabled;
        }

        private static void DrawMeasuredLabel(string text, GUIStyle style, float contentWidth)
        {
            var content = new GUIContent(text);
            float height = style.CalcHeight(content, contentWidth);
            GUILayout.Label(content, style, GUILayout.MinHeight(height));
        }

        private void ShowSubtitle(string text) { subtitle = text; }
        private static string Title(MenuScreen screen)
        {
            switch (screen)
            {
                case MenuScreen.Pause: return "先歇一会儿";
                case MenuScreen.Results: return "这一段走完了";
                case MenuScreen.Settings: return "设置";
                case MenuScreen.Playing: return "";
                default: return "听路";
            }
        }

        private void OnDestroy() { if (menu != null) menu.Subtitle -= ShowSubtitle; }
    }
}
