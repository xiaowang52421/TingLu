using System;
using System.Collections.Generic;
using TingLu.Contracts;

namespace TingLu.UI
{
    public enum MenuScreen { Main, Pause, Results, Settings, Playing }
    public enum MenuOption { Start, Continue, Resume, Retry, Settings, ReplayGoal, Quit, DisplayMode, OutputMode, Volume, Subtitles, Haptics, Palette, FontSize, VoiceIntensity, Back }

    public sealed class AccessibilityOptions
    {
        public DisplayMode Display { get; private set; }
        public OutputMode Output { get; private set; }
        public float MasterVolume { get; private set; }
        public bool Subtitles { get; private set; }
        public bool Haptics { get; private set; }
        public PaletteMode Palette { get; private set; }
        public float FontScale { get; private set; }
        public float VoiceIntensity { get; private set; }

        public AccessibilityOptions(DisplayMode display, OutputMode output, float volume, bool subtitles,
            bool haptics, PaletteMode palette, float fontScale, float voiceIntensity)
        {
            Display = display; Output = output; MasterVolume = Clamp(volume, 0f, 1f);
            Subtitles = subtitles; Haptics = haptics; Palette = palette;
            FontScale = Clamp(fontScale, 0.75f, 2f); VoiceIntensity = Clamp(voiceIntensity, 0f, 2f);
        }

        public static AccessibilityOptions From(UserSettings settings)
        {
            if (settings == null) return new AccessibilityOptions(DisplayMode.Standard, OutputMode.Headphones, 1f, true, true, PaletteMode.Default, 1f, 1f);
            return new AccessibilityOptions(settings.Display, settings.Output, settings.MasterVolume,
                settings.Subtitles, settings.Haptics, settings.Palette, settings.FontScale, settings.VoiceprintIntensity);
        }

        public UserSettings ToUserSettings()
        { return new UserSettings(Display, Output, MasterVolume, Subtitles, Haptics, Palette, FontScale, VoiceIntensity); }

        internal AccessibilityOptions Change(MenuOption action, int direction)
        {
            DisplayMode display = Display; OutputMode output = Output; bool subtitles = Subtitles;
            bool haptics = Haptics; float volume = MasterVolume;
            PaletteMode palette = Palette; float font = FontScale; float intensity = VoiceIntensity;
            switch (action)
            {
                case MenuOption.DisplayMode: display = (DisplayMode)Wrap((int)display + direction, 3); break;
                case MenuOption.OutputMode: output = output == OutputMode.Headphones ? OutputMode.Speakers : OutputMode.Headphones; break;
                case MenuOption.Volume: volume = Clamp(volume + direction * 0.1f, 0f, 1f); break;
                case MenuOption.Subtitles: subtitles = !subtitles; break;
                case MenuOption.Haptics: haptics = !haptics; break;
                case MenuOption.Palette: palette = palette == PaletteMode.Default ? PaletteMode.ColorBlind : PaletteMode.Default; break;
                case MenuOption.FontSize: font = Clamp(font + direction * 0.1f, 0.75f, 2f); break;
                case MenuOption.VoiceIntensity: intensity = Clamp(intensity + direction * 0.1f, 0f, 2f); break;
            }
            return new AccessibilityOptions(display, output, volume, subtitles, haptics, palette, font, intensity);
        }

        private static int Wrap(int value, int count) { return (value % count + count) % count; }
        private static float Clamp(float value, float min, float max) { return Math.Max(min, Math.Min(max, value)); }
    }

    public sealed class MenuItem
    {
        public readonly MenuOption Action;
        public readonly string Label;
        public readonly bool Enabled;
        public MenuItem(MenuOption action, string label, bool enabled = true) { Action = action; Label = label; Enabled = enabled; }
    }

    public sealed class MenuCallbacks
    {
        public Action Start, Continue, Pause, Resume, Retry, ReturnToMenu, ReplayGoal, Quit;
        public Func<bool> CanResume;
        public Action<UserSettings> SaveOptions;
    }

    /// <summary>Pure menu state and focus rules. Runtime owns game state, saves, time, and scene changes.</summary>
    public sealed class AccessibleMenu
    {
        private IAudioSpeech speech;
        private MenuCallbacks callbacks;
        private int generation;
        private bool canContinue;
        private string goalText;
        private int lastAdjustDirection;
        private int lastMenuDirection;
        private readonly List<MenuItem> items = new List<MenuItem>();
        public MenuScreen Screen { get; private set; }
        public int FocusedIndex { get; private set; }
        public AccessibilityOptions Options { get; private set; }
        public IReadOnlyList<MenuItem> Items { get { return items.AsReadOnly(); } }
        public event Action<string> Subtitle;

        public void Initialize(UserSettings settings, bool hasSavedRun, string knownGoalText,
            MenuCallbacks actions, IAudioSpeech speechService, int recoveryGeneration)
        {
            callbacks = actions ?? throw new ArgumentNullException("actions");
            speech = speechService; generation = recoveryGeneration; canContinue = hasSavedRun;
            goalText = string.IsNullOrWhiteSpace(knownGoalText) ? "路线说明暂不可用。" : knownGoalText;
            Options = AccessibilityOptions.From(settings);
            Open(MenuScreen.Main);
        }

        public void OpenPause() { Open(MenuScreen.Pause); }
        public string ScreenMessage { get; private set; }
        public void OpenResults(string summary = null)
        {
            ScreenMessage = summary ?? "这段路走完了。";
            Open(MenuScreen.Results);
            PresentKnownText(ScreenMessage, "results-summary", FeedbackPriority.Interface);
        }
        public void OpenSettings() { previousScreen = Screen; Open(MenuScreen.Settings); }
        public void SetPlaying() { Open(MenuScreen.Playing); }

        public void SetCanContinue(bool available)
        {
            if (canContinue == available) return;
            MenuOption focusedAction = items.Count > FocusedIndex ? items[FocusedIndex].Action : MenuOption.Start;
            canContinue = available;
            if (Screen != MenuScreen.Main) return;

            items.Clear();
            BuildMainItems();
            int focus = items.FindIndex(item => item.Action == focusedAction && item.Enabled);
            FocusedIndex = focus >= 0 ? focus : 0;
            AnnounceFocus();
        }

        public bool HandleInput(InputFrame input)
        {
            if (Screen == MenuScreen.Playing)
            {
                if (input.PausePressed) { Invoke(callbacks.Pause); OpenPause(); }
                else if (input.Menu == TingLu.Contracts.MenuAction.QueryGoal) Activate(MenuOption.ReplayGoal);
                return input.PausePressed || input.Menu == TingLu.Contracts.MenuAction.QueryGoal;
            }
            if (input.PausePressed && Screen == MenuScreen.Settings)
            {
                Back();
                return true;
            }
            if (input.PausePressed && Screen == MenuScreen.Pause) { TryResume(); return true; }
            if (input.Menu == TingLu.Contracts.MenuAction.None)
            {
                int adjustDirection = Screen == MenuScreen.Settings
                    ? (input.Move.X < -0.5f ? -1 : input.Move.X > 0.5f ? 1 : 0) : 0;
                if (adjustDirection != 0 && adjustDirection != lastAdjustDirection) AdjustFocused(adjustDirection);
                lastAdjustDirection = adjustDirection;
                int menuDirection = input.Move.Y > 0.5f ? -1 : input.Move.Y < -0.5f ? 1 : 0;
                if (menuDirection != 0 && menuDirection != lastMenuDirection) MoveFocus(menuDirection);
                lastMenuDirection = menuDirection;
            }
            switch (input.Menu)
            {
                case TingLu.Contracts.MenuAction.Next: MoveFocus(1); break;
                case TingLu.Contracts.MenuAction.Previous: MoveFocus(-1); break;
                case TingLu.Contracts.MenuAction.Decrease: AdjustFocused(-1); break;
                case TingLu.Contracts.MenuAction.Increase: AdjustFocused(1); break;
                case TingLu.Contracts.MenuAction.Confirm: ActivateFocused(); break;
                case TingLu.Contracts.MenuAction.Back: Back(); break;
                case TingLu.Contracts.MenuAction.QueryGoal: Activate(MenuOption.ReplayGoal); break;
            }
            return true;
        }

        private MenuScreen previousScreen = MenuScreen.Main;
        public void Back()
        {
            if (Screen == MenuScreen.Settings) Open(previousScreen);
            else if (Screen != MenuScreen.Main)
            {
                Invoke(callbacks.ReturnToMenu);
                Open(MenuScreen.Main);
            }
        }

        public void MoveFocus(int delta)
        {
            if (items.Count == 0 || delta == 0) return;
            int index = FocusedIndex;
            for (int i = 0; i < items.Count; i++)
            {
                index = (index + (delta > 0 ? 1 : items.Count - 1)) % items.Count;
                if (items[index].Enabled) { FocusedIndex = index; AnnounceFocus(); return; }
            }
        }

        public void FocusItem(int index)
        {
            if (index < 0 || index >= items.Count || !items[index].Enabled) return;
            FocusedIndex = index;
            AnnounceFocus();
        }

        public void ActivateFocused() { if (items.Count > 0) Activate(items[FocusedIndex].Action); }

        public void AdjustFocused(int direction)
        {
            if (items.Count == 0 || Screen != MenuScreen.Settings) return;
            MenuOption action = items[FocusedIndex].Action;
            Options = Options.Change(action, direction == 0 ? 1 : direction);
            if (callbacks.SaveOptions != null) callbacks.SaveOptions(Options.ToUserSettings());
            if (action != MenuOption.Back) RefreshSettingsItems();
            AnnounceFocus();
        }

        public void Activate(MenuOption action)
        {
            switch (action)
            {
                case MenuOption.Start: Invoke(callbacks.Start); break;
                case MenuOption.Continue: if (canContinue) Invoke(callbacks.Continue); break;
                case MenuOption.Resume: TryResume(); break;
                case MenuOption.Retry: Invoke(callbacks.Retry); break;
                case MenuOption.Quit: Invoke(callbacks.Quit); break;
                case MenuOption.Settings: OpenSettings(); break;
                case MenuOption.ReplayGoal:
                    if (callbacks.ReplayGoal != null) callbacks.ReplayGoal(); else Say(goalText, "goal");
                    break;
                case MenuOption.DisplayMode: case MenuOption.OutputMode: case MenuOption.Volume: case MenuOption.Subtitles: case MenuOption.Haptics:
                case MenuOption.Palette: case MenuOption.FontSize: case MenuOption.VoiceIntensity: AdjustFocused(1); break;
                case MenuOption.Back: Back(); break;
            }
        }

        /// <summary>Use only after Runtime has routed a known semantic line for display.</summary>
        public void PresentKnownText(string text, string supersessionKey, FeedbackPriority priority)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (Options.Display == DisplayMode.Standard && Options.Subtitles && Subtitle != null) Subtitle(text);
            if (speech != null)
                speech.Speak(new SpeechRequest(Guid.NewGuid().ToString("N"), text, supersessionKey, priority, generation));
        }

        public void ReportSpeechFailure(string providerError)
        {
            if (string.IsNullOrWhiteSpace(providerError)) providerError = "语音服务不可用";
            ScreenMessage = "语音没能播放：" + providerError;
            if (Options.Display == DisplayMode.Standard && Options.Subtitles && Subtitle != null) Subtitle(ScreenMessage);
        }

        private void Open(MenuScreen screen)
        {
            Screen = screen; FocusedIndex = 0; items.Clear();
            if (screen != MenuScreen.Results) ScreenMessage = null;
            switch (screen)
            {
                case MenuScreen.Main:
                    BuildMainItems(); break;
                case MenuScreen.Pause:
                    items.Add(new MenuItem(MenuOption.Resume, "接着走"));
                    items.Add(new MenuItem(MenuOption.Retry, "回到最近检查点"));
                    items.Add(new MenuItem(MenuOption.Settings, "设置"));
                    items.Add(new MenuItem(MenuOption.Back, "回到首页"));
                    items.Add(new MenuItem(MenuOption.Quit, "退出游戏")); break;
                case MenuScreen.Results:
                    items.Add(new MenuItem(MenuOption.Retry, "再走一次"));
                    items.Add(new MenuItem(MenuOption.ReplayGoal, "重听目标"));
                    items.Add(new MenuItem(MenuOption.Back, "回到首页")); break;
                case MenuScreen.Playing: break;
                case MenuScreen.Settings: BuildSettingsItems(); break;
            }
            AnnounceFocus();
        }

        private void AnnounceFocus()
        {
            if (items.Count == 0) return;
            string line = items[FocusedIndex].Label + (items[FocusedIndex].Enabled ? "" : "，现在还没有存档");
            if (Options.Display == DisplayMode.Standard && Options.Subtitles && Subtitle != null) Subtitle(line);
            if (speech != null) speech.Speak(new SpeechRequest(Guid.NewGuid().ToString("N"), line, "menu-focus", FeedbackPriority.Interface, generation));
        }

        private void RefreshSettingsItems()
        {
            int focus = FocusedIndex;
            items.Clear();
            BuildSettingsItems();
            FocusedIndex = Math.Max(0, Math.Min(items.Count - 1, focus));
        }

        private void BuildSettingsItems()
        {
            items.Add(new MenuItem(MenuOption.DisplayMode, "显示方式：" + DisplayLabel(Options.Display)));
            items.Add(new MenuItem(MenuOption.OutputMode, "声音：" + (Options.Output == OutputMode.Headphones ? "耳机" : "扬声器")));
            items.Add(new MenuItem(MenuOption.Volume, "音量：" + Math.Round(Options.MasterVolume * 100) + "%"));
            items.Add(new MenuItem(MenuOption.Subtitles, "字幕：" + (Options.Subtitles ? "开" : "关")));
            items.Add(new MenuItem(MenuOption.Haptics, "震动：" + (Options.Haptics ? "开" : "关")));
            items.Add(new MenuItem(MenuOption.Palette, "颜色：" + PaletteLabel(Options.Palette)));
            items.Add(new MenuItem(MenuOption.FontSize, "文字大小：" + Math.Round(Options.FontScale * 100) + "%"));
            items.Add(new MenuItem(MenuOption.VoiceIntensity, "声纹强度：" + Math.Round(Options.VoiceIntensity * 100) + "%"));
            items.Add(new MenuItem(MenuOption.Back, "保存并返回"));
        }

        private void BuildMainItems()
        {
            items.Add(new MenuItem(MenuOption.Start, "开始新游戏"));
            items.Add(new MenuItem(MenuOption.Continue, "继续上次", canContinue));
            items.Add(new MenuItem(MenuOption.Settings, "设置"));
            items.Add(new MenuItem(MenuOption.ReplayGoal, "重听目标"));
            items.Add(new MenuItem(MenuOption.Quit, "退出游戏"));
        }

        private void Say(string text, string key)
        {
            if (speech == null) return;
            speech.Speak(new SpeechRequest(Guid.NewGuid().ToString("N"), text, key, FeedbackPriority.Interface, generation));
        }

        private static void Invoke(Action action) { if (action != null) action(); }
        private void TryResume()
        {
            if (callbacks.CanResume != null && !callbacks.CanResume()) return;
            Invoke(callbacks.Resume);
            if (callbacks.CanResume != null && !callbacks.CanResume()) return;
            SetPlaying();
        }
        private static string DisplayLabel(DisplayMode mode)
        { return mode == DisplayMode.Standard ? "标准" : mode == DisplayMode.NoUI ? "隐藏界面" : "纯音频"; }
        private static string PaletteLabel(PaletteMode style)
        { return style == PaletteMode.Default ? "标准" : "色觉辅助"; }
    }
}

