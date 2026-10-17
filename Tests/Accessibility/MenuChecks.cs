using System;
using System.Collections.Generic;
using TingLu.Contracts;
using TingLu.UI;

public static class MenuChecks
{
    private sealed class SpeechRecorder : IAudioSpeech
    {
        public readonly List<SpeechRequest> Requests = new List<SpeechRequest>();
        public void Speak(SpeechRequest request) { Requests.Add(request); }
        public void StopSpeech(string eventId) { }
    }

    public static string[] Run()
    {
        var passed = new List<string>();
        var speech = new SpeechRecorder();
        int starts = 0, continues = 0, pauses = 0, resumes = 0, retries = 0, replays = 0, returns = 0, saves = 0;
        UserSettings saved = null;
        var actions = new MenuCallbacks {
            Start = delegate { starts++; }, Continue = delegate { continues++; },
            Pause = delegate { pauses++; }, Resume = delegate { resumes++; }, Retry = delegate { retries++; },
            ReturnToMenu = delegate { returns++; },
            ReplayGoal = delegate { replays++; }, SaveOptions = delegate(UserSettings value) { saves++; saved = value; }
        };
        var menu = new AccessibleMenu();
        menu.Initialize(null, false, "去街角的公交站。", actions, speech, 4);
        Require(menu.Screen == MenuScreen.Main && menu.Items.Count == 4, "main menu initialized");
        Require(!menu.Items[1].Enabled, "continue disabled without save");
        menu.MoveFocus(1); Require(menu.FocusedIndex == 2, "focus skips disabled continue");
        menu.Activate(MenuOption.Start); Require(starts == 1 && menu.Screen == MenuScreen.Main, "start callback leaves screen ownership to runtime");
        menu.SetPlaying();
        var pauseInput = new InputFrame(new Point2(0, 0), false, false, true, MenuAction.None);
        Require(menu.HandleInput(pauseInput) && pauses == 1 && menu.Screen == MenuScreen.Pause, "pause callback and screen");
        Require(menu.HandleInput(pauseInput) && resumes == 1 && menu.Screen == MenuScreen.Playing, "pause button resumes");
        var goalInput = new InputFrame(new Point2(0, 0), false, false, false, MenuAction.QueryGoal);
        Require(menu.HandleInput(goalInput) && replays == 1, "in game goal query");
        Require(speech.Requests.Count > 0 && speech.Requests[speech.Requests.Count - 1].RecoveryGeneration == 4,
            "spoken focus carries recovery generation");
        passed.Add("main_menu_and_spoken_focus");

        menu.OpenPause(); menu.MoveFocus(1); menu.ActivateFocused(); Require(retries == 1 && menu.Screen == MenuScreen.Pause, "retry callback leaves failure handling to runtime");
        menu.OpenResults("三星，碰撞零次。"); Require(menu.ScreenMessage == "三星，碰撞零次。", "result summary");
        menu.Activate(MenuOption.ReplayGoal); Require(replays == 2, "goal replay callback");
        passed.Add("pause_retry_results_goal");

        menu.OpenSettings();
        menu.FocusItem(2);
        menu.AdjustFocused(1);
        float volumeBeforeLowering = menu.Options.MasterVolume;
        menu.HandleInput(new InputFrame(new Point2(0, 0), false, false, false, MenuAction.Decrease));
        Require(menu.Options.MasterVolume < volumeBeforeLowering, "decrease action lowers focused setting");
        menu.OpenPause(); menu.OpenSettings();
        menu.HandleInput(new InputFrame(new Point2(0, 0), false, false, true, MenuAction.None));
        Require(menu.Screen == MenuScreen.Pause && resumes == 1, "pause key backs out of settings without resuming the game");
        menu.OpenResults("三星，碰撞零次。");
        menu.OpenSettings();
        menu.AdjustFocused(1); Require(menu.Options.Display == DisplayMode.NoUI, "display mode reaches no UI");
        menu.HandleInput(new InputFrame(new Point2(0, 0), false, false, false, MenuAction.Increase));
        Require(menu.Options.Display == DisplayMode.AudioOnly, "display mode reaches audio only");
        menu.HandleInput(new InputFrame(new Point2(0, 0), false, false, false, MenuAction.Increase));
        Require(menu.Options.Display == DisplayMode.Standard, "display mode cycles back to standard");
        menu.FocusItem(1); menu.AdjustFocused(1); Require(menu.Options.Output == OutputMode.Speakers, "output mode cycles");
        menu.FocusItem(3); menu.ActivateFocused(); Require(!menu.Options.Subtitles, "subtitle toggle");
        int spokenBeforeCaptionless = speech.Requests.Count;
        menu.PresentKnownText("前方有一段石板路。", "route", FeedbackPriority.Navigation);
        Require(speech.Requests.Count == spokenBeforeCaptionless + 1 &&
            speech.Requests[speech.Requests.Count - 1].Text == "前方有一段石板路。",
            "speech remains available when subtitles are off");
        menu.FocusItem(4); menu.ActivateFocused(); Require(!menu.Options.Haptics, "haptics toggle");
        menu.FocusItem(5); menu.AdjustFocused(1); Require(menu.Options.Palette == PaletteMode.ColorBlind, "color style cycles");
        menu.FocusItem(6); menu.AdjustFocused(1); Require(menu.Options.FontScale > 1f, "font scale changes");
        menu.FocusItem(7); menu.AdjustFocused(1); Require(menu.Options.VoiceIntensity > 1f, "voice intensity changes");
        Require(menu.FocusedIndex == 7 && menu.Items[7].Label.Contains("声纹强度"),
            "settings refresh keeps current focus and label");
        Require(saves >= 7 && saved != null && saved.Palette == PaletteMode.ColorBlind && saved.FontScale > 1f,
            "settings saved in shared contract");
        menu.Back(); Require(menu.Screen == MenuScreen.Results, "settings returns to prior screen");
        menu.Back(); Require(menu.Screen == MenuScreen.Main && returns == 1, "return to menu callback");
        passed.Add("settings_and_return");
        return passed.ToArray();
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
