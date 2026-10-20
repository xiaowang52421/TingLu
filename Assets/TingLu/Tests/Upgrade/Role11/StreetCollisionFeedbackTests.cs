using NUnit.Framework;
using TingLu.Config;
using TingLu.UI;

namespace TingLu.Tests.Upgrade.Role11
{
    public sealed class StreetCollisionFeedbackTests
    {
        [Test]
        public void ActiveContactsMergeWithoutRestartingOrExtendingThePulse()
        {
            var config = new StreetCollisionFeedbackConfig { Duration = .72f, RepeatInterval = .22f };
            var feedback = new StreetCollisionFeedback(config);

            Assert.IsTrue(feedback.Trigger());
            Assert.Greater(feedback.Alpha, 0, "The contact frame must be visibly marked before recovery can pause the session.");
            feedback.Advance(.4);
            Assert.Greater(feedback.Alpha, 0);
            Assert.IsFalse(feedback.Trigger(), "A contact during a live pulse must merge into that pulse.");
            Assert.AreEqual(.4, feedback.Age, 1e-8, "A merged contact must not restart the pulse age.");
            Assert.AreEqual(1, feedback.TriggerCount);

            feedback.Advance(.33);
            Assert.AreEqual(0f, feedback.Alpha, "The first pulse must still expire at its original duration.");
            Assert.IsTrue(feedback.Trigger(), "A new contact can start a fresh pulse after expiry and cooldown.");
            Assert.AreEqual(0, feedback.Age, 1e-8);
            Assert.AreEqual(2, feedback.TriggerCount);
        }

        [Test]
        public void RepeatIntervalThrottlesAContactAfterThePulseHasExpired()
        {
            var feedback = new StreetCollisionFeedback(
                new StreetCollisionFeedbackConfig { Duration = .1f, RepeatInterval = .3f });

            Assert.IsTrue(feedback.Trigger());
            feedback.Advance(.1);
            Assert.AreEqual(0f, feedback.Alpha);
            Assert.IsFalse(feedback.Trigger(), "The cooldown remains in force after the visible pulse ends.");
            feedback.Advance(.21);
            Assert.IsTrue(feedback.Trigger());
            Assert.AreEqual(2, feedback.TriggerCount);
        }

        [Test]
        public void ClearResetsPulseAndMakesTheNextContactEligible()
        {
            var feedback = new StreetCollisionFeedback(new StreetCollisionFeedbackConfig());
            Assert.IsTrue(feedback.Trigger());
            feedback.Advance(.4);

            feedback.Clear();

            Assert.AreEqual(0f, feedback.Alpha);
            Assert.AreEqual(0, feedback.TriggerCount);
            Assert.IsTrue(feedback.Trigger());
            Assert.AreEqual(0, feedback.Age, 1e-8);
        }

        [Test]
        public void PulseFlashesQuicklyThenFadesAndEdgeGradientLeavesTheCenterClear()
        {
            var feedback = new StreetCollisionFeedback(
                new StreetCollisionFeedbackConfig { Duration = .6f, Intensity = .8f });
            Assert.IsTrue(feedback.Trigger());
            Assert.Greater(feedback.Alpha, 0, "The pulse needs a visible floor on its trigger frame.");

            feedback.Advance(.045);
            float flashTrough = feedback.Alpha;
            feedback.Advance(.045);
            float secondFlash = feedback.Alpha;
            Assert.Greater(secondFlash, flashTrough, "The initial short flash should visibly flicker.");
            feedback.Advance(.31);
            Assert.Greater(feedback.Alpha, 0, "The pulse remains visible during its fade.");
            feedback.Advance(.2);
            Assert.AreEqual(0f, feedback.Alpha, "The pulse must finish at its configured duration.");

            Assert.AreEqual(1f, StreetCollisionFeedback.EdgeAlpha(0, 100), 1e-6);
            Assert.Greater(StreetCollisionFeedback.EdgeAlpha(25, 100),
                StreetCollisionFeedback.EdgeAlpha(50, 100));
            Assert.Greater(StreetCollisionFeedback.EdgeAlpha(50, 100),
                StreetCollisionFeedback.EdgeAlpha(75, 100));
            Assert.AreEqual(0f, StreetCollisionFeedback.EdgeAlpha(100, 100), 1e-6,
                "The gradient reaches zero before the center of even a square viewport.");
            Assert.AreEqual(0f, StreetCollisionFeedback.EdgeAlpha(150, 100), 1e-6);
        }

        [TestCase(1280, 720)]
        [TestCase(960, 640)]
        [TestCase(2560, 720)]
        [TestCase(720, 1280)]
        [TestCase(720, 720)]
        [TestCase(320, 240)]
        public void ScreenEdgeGradientScalesFromTheShortSideAndKeepsCornersAndCenterStable(
            int width, int height)
        {
            const float edgeWidthRatio = .14f;
            float shortestSide = System.Math.Min(width, height);
            float halfEdgeWidth = shortestSide * edgeWidthRatio * .5f;

            Assert.AreEqual(.5f, StreetCollisionFeedback.ScreenEdgeAlpha(
                width * .5f, halfEdgeWidth, width, height, edgeWidthRatio), 1e-5f,
                "The same normalized distance should have the same opacity at every resolution and aspect ratio.");
            Assert.AreEqual(.5f, StreetCollisionFeedback.ScreenEdgeAlpha(
                halfEdgeWidth, halfEdgeWidth, width, height, edgeWidthRatio), 1e-5f,
                "A corner should have one gradient value without overlapping edge accumulation.");
            Assert.AreEqual(0f, StreetCollisionFeedback.ScreenEdgeAlpha(
                width * .5f, height * .5f, width, height, edgeWidthRatio), 1e-6f,
                "The center stays clear even when the shorter side changes orientation.");
        }

        [TestCase(.6f)]
        [TestCase(.01f)]
        public void FlashToFadeTransitionIsContinuousForNormalAndVeryShortDurations(float duration)
        {
            var config = new StreetCollisionFeedbackConfig { Duration = duration, Intensity = .8f };
            var feedback = new StreetCollisionFeedback(config);
            Assert.IsTrue(feedback.Trigger());
            float triggerFrameAlpha = feedback.Alpha;
            Assert.Greater(triggerFrameAlpha, 0);

            double flashDuration = System.Math.Min(.18, config.Duration * .35);
            double transitionProbe = System.Math.Min(1e-6, flashDuration * .001);
            feedback.Advance(flashDuration - transitionProbe);
            float beforeFade = feedback.Alpha;
            feedback.Advance(transitionProbe);
            float atFadeStart = feedback.Alpha;

            Assert.LessOrEqual(System.Math.Abs(beforeFade - atFadeStart), .001f,
                "The flash envelope must meet the fade envelope without a second brightness jump.");
            feedback.Advance((config.Duration - flashDuration) * .5);
            Assert.Greater(feedback.Alpha, 0, "The short pulse remains visible as it starts to fade.");
            feedback.Advance(config.Duration);
            Assert.AreEqual(0f, feedback.Alpha, "The pulse still expires at its configured duration.");
        }
    }
}
