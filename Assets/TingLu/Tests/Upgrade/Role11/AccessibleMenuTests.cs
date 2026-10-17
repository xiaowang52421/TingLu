using System.Collections.Generic;
using NUnit.Framework;
using TingLu.Contracts;
using TingLu.UI;
using UnityEngine;

namespace TingLu.Tests.Upgrade.Role11
{
    public sealed class AccessibleMenuTests
    {
        private sealed class SpeechRecorder : IAudioSpeech
        {
            public readonly List<SpeechRequest> Requests = new List<SpeechRequest>();
            public void Speak(SpeechRequest request) { Requests.Add(request); }
            public void StopSpeech(string eventId) { }
        }

        [Test]
        public void MainAndPauseKeepNormalActionsAndRouteQuit()
        {
            int quitCount = 0;
            var menu = CreateMenu(false, new MenuCallbacks { Quit = () => quitCount++ }, new SpeechRecorder());

            Assert.That(menu.Screen, Is.EqualTo(MenuScreen.Main));
            Assert.That(Find(menu, MenuScreen.Main, MenuOption.Start), Is.True);
            Assert.That(Find(menu, MenuScreen.Main, MenuOption.Settings), Is.True);
            Assert.That(Find(menu, MenuScreen.Main, MenuOption.Quit), Is.True);
            Assert.That(Find(menu, MenuScreen.Main, MenuOption.Continue), Is.True);
            Assert.That(menu.Items[1].Enabled, Is.False);

            menu.Activate(MenuOption.Quit);
            menu.OpenPause();
            Assert.That(Find(menu, MenuScreen.Pause, MenuOption.Resume), Is.True);
            Assert.That(Find(menu, MenuScreen.Pause, MenuOption.Settings), Is.True);
            Assert.That(Find(menu, MenuScreen.Pause, MenuOption.Quit), Is.True);
            menu.Activate(MenuOption.Quit);

            Assert.That(quitCount, Is.EqualTo(2));
        }

        [Test]
        public void ContinueAvailabilityRefreshesAndPreservesUsableFocus()
        {
            int continueCount = 0;
            var menu = CreateMenu(false, new MenuCallbacks { Continue = () => continueCount++ }, new SpeechRecorder());
            menu.FocusItem(2);
            menu.SetCanContinue(true);

            Assert.That(menu.Items[1].Enabled, Is.True);
            Assert.That(menu.FocusedIndex, Is.EqualTo(2));
            menu.Activate(MenuOption.Continue);
            Assert.That(continueCount, Is.EqualTo(1));

            menu.FocusItem(1);
            menu.SetCanContinue(false);
            Assert.That(menu.Items[1].Enabled, Is.False);
            Assert.That(menu.FocusedIndex, Is.EqualTo(0));

            menu.SetPlaying();
            menu.SetCanContinue(true);
            menu.Back();
            Assert.That(menu.Screen, Is.EqualTo(MenuScreen.Main));
            Assert.That(menu.Items[1].Enabled, Is.True);
        }

        [Test]
        public void ResumeMenuOptionAndEscapeHonorCanResumePrecondition()
        {
            bool canResume = false;
            int resumeCount = 0;
            var menu = CreateMenu(false, new MenuCallbacks
            {
                CanResume = () => canResume,
                Resume = () => resumeCount++
            }, new SpeechRecorder());
            menu.OpenPause();

            menu.Activate(MenuOption.Resume);
            Assert.That(menu.Screen, Is.EqualTo(MenuScreen.Pause));
            Assert.That(resumeCount, Is.Zero);

            Assert.That(menu.HandleInput(new InputFrame(new Point2(0, 0), false, false, true, MenuAction.None)), Is.True);
            Assert.That(menu.Screen, Is.EqualTo(MenuScreen.Pause));
            Assert.That(resumeCount, Is.Zero);
        }

        [Test]
        public void ResumeCallbackFailureKeepsPauseMenuOpen()
        {
            bool canResume = true;
            int resumeCount = 0;
            var menu = CreateMenu(false, new MenuCallbacks
            {
                CanResume = () => canResume,
                Resume = () => { resumeCount++; canResume = false; }
            }, new SpeechRecorder());
            menu.OpenPause();

            Assert.That(menu.HandleInput(new InputFrame(new Point2(0, 0), false, false, true, MenuAction.None)), Is.True);

            Assert.That(resumeCount, Is.EqualTo(1));
            Assert.That(menu.Screen, Is.EqualTo(MenuScreen.Pause));
        }

        [Test]
        public void SuccessfulResumeCallbackEntersPlayingAndMissingGuardRemainsCompatible()
        {
            int resumeCount = 0;
            var guarded = CreateMenu(false, new MenuCallbacks
            {
                CanResume = () => true,
                Resume = () => resumeCount++
            }, new SpeechRecorder());
            guarded.OpenPause();

            guarded.Activate(MenuOption.Resume);

            Assert.That(resumeCount, Is.EqualTo(1));
            Assert.That(guarded.Screen, Is.EqualTo(MenuScreen.Playing));

            var legacy = CreateMenu(false, new MenuCallbacks(), new SpeechRecorder());
            legacy.OpenPause();
            legacy.Activate(MenuOption.Resume);
            Assert.That(legacy.Screen, Is.EqualTo(MenuScreen.Playing));
        }

        [Test]
        public void DisplayModeAndSubtitleSettingsControlCaptionsButKeepSpeech()
        {
            var speech = new SpeechRecorder();
            var menu = CreateMenu(true, new MenuCallbacks(), speech);
            var captions = new List<string>();
            menu.Subtitle += captions.Add;

            menu.PresentKnownText("周围传来持续的车声。", "street-audio", FeedbackPriority.Navigation);
            Assert.That(captions, Does.Contain("周围传来持续的车声。"));

            menu.OpenSettings();
            menu.FocusItem(3);
            menu.ActivateFocused();
            int captionCount = captions.Count;
            int speechCount = speech.Requests.Count;
            menu.PresentKnownText("这里站稳了。", "checkpoint", FeedbackPriority.Interface);
            Assert.That(captions, Has.Count.EqualTo(captionCount));
            Assert.That(speech.Requests, Has.Count.EqualTo(speechCount + 1));

            menu.FocusItem(0);
            menu.AdjustFocused(1);
            Assert.That(menu.Options.Display, Is.EqualTo(DisplayMode.NoUI));
            menu.AdjustFocused(1);
            Assert.That(menu.Options.Display, Is.EqualTo(DisplayMode.AudioOnly));
            captionCount = captions.Count;
            menu.PresentKnownText("路线说明。", "goal", FeedbackPriority.Interface);
            Assert.That(captions, Has.Count.EqualTo(captionCount));
            Assert.That(speech.Requests[speech.Requests.Count - 1].Text, Is.EqualTo("路线说明。"));
        }

        [Test]
        public void MissingGoalTextUsesAContentNeutralFallback()
        {
            var speech = new SpeechRecorder();
            var menu = new AccessibleMenu();
            menu.Initialize(null, false, " ", new MenuCallbacks(), speech, 0);
            speech.Requests.Clear();
            menu.Activate(MenuOption.ReplayGoal);

            Assert.That(speech.Requests, Has.Count.EqualTo(1));
            Assert.That(speech.Requests[0].Text, Is.EqualTo("路线说明暂不可用。"));
        }

        [Test]
        public void HudPresenterConfiguresTheExistingMenuShellAndAcceptsRuntimeFrames()
        {
            var host = new GameObject("Street HUD test");
            try
            {
                var menu = CreateMenu(false, new MenuCallbacks(), new SpeechRecorder());
                var panel = host.AddComponent<AccessibleMenuPanel>();
                var presenter = host.AddComponent<StreetHudPresenter>();
                presenter.Configure(menu);
                var state = new PlayerState(PlayerMainState.Active, ActionMode.Walking,
                    new Point2(0, 0), new Point2(0, 1), 80, 65, 0, 0, 0);
                var settings = new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1f,
                    true, false, PaletteMode.ColorBlind, 1.2f, 1f);

                Assert.That(presenter, Is.InstanceOf<IStreetHud>());
                Assert.That(host.GetComponent<AccessibleMenuPanel>(), Is.SameAs(panel));
                Assert.DoesNotThrow(() => presenter.Present(state, settings,
                    "用 WASD 移动，移动鼠标转向，按右键敲杖。", true));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static AccessibleMenu CreateMenu(bool hasSavedRun, MenuCallbacks callbacks, SpeechRecorder speech)
        {
            var menu = new AccessibleMenu();
            menu.Initialize(new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1f, true, false),
                hasSavedRun, "去街对面的巷口。", callbacks, speech, 0);
            return menu;
        }

        private static bool Find(AccessibleMenu menu, MenuScreen expectedScreen, MenuOption option)
        {
            Assert.That(menu.Screen, Is.EqualTo(expectedScreen));
            for (int i = 0; i < menu.Items.Count; i++)
                if (menu.Items[i].Action == option) return true;
            return false;
        }
    }
}
