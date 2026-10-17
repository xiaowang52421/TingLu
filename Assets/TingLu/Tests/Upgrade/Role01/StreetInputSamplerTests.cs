using NUnit.Framework;
using TingLu.Config;
using TingLu.Contracts;
using TingLu.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace TingLu.Tests.Upgrade.Role01
{
    public sealed class StreetInputSamplerTests : InputTestFixture
    {
        private Keyboard keyboard;
        private Mouse mouse;
        private GameObject host;
        private StreetInputSampler sampler;
        private CursorLockMode previousLockMode;
        private bool previousCursorVisible;

        [SetUp]
        public override void Setup()
        {
            // Editor-only updates intentionally do not dispatch InputAction callbacks.
            base.Setup();
            previousLockMode = Cursor.lockState;
            previousCursorVisible = Cursor.visible;

            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            Pump(new KeyboardState(), new MouseState());

            host = new GameObject("StreetInputSamplerTests");
            sampler = host.AddComponent<StreetInputSampler>();
            sampler.Configure(new StreetConfig());
            sampler.SetGameplayActive(true);
            Pump(new KeyboardState(), new MouseState());
            sampler.Read(); // Clear the focus/activation transition frame.
            Pump(new KeyboardState(), new MouseState());
            sampler.Read();
        }

        [TearDown]
        public override void TearDown()
        {
            try
            {
                if (host != null) Object.DestroyImmediate(host);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                Cursor.lockState = previousLockMode;
                Cursor.visible = previousCursorVisible;
            }
            finally
            {
                base.TearDown();
            }
        }

        [Test]
        public void MovementUsesScreenMoveAndDiagonalSpeedIsNormalized()
        {
            AssertMove(new KeyboardState(Key.W), 0, 1);
            AssertMove(new KeyboardState(Key.D), 1, 0);
            AssertMove(new KeyboardState(Key.S), 0, -1);
            AssertMove(new KeyboardState(Key.A), -1, 0);

            Pump(new KeyboardState(Key.W, Key.D), new MouseState());
            StreetInputFrame diagonal = sampler.Read();
            Assert.That(diagonal.Move.X, Is.EqualTo(0.7071068f).Within(1e-5f));
            Assert.That(diagonal.Move.Y, Is.EqualTo(0.7071068f).Within(1e-5f));
            Assert.That(diagonal.TurnDegrees, Is.Zero);
        }

        [Test]
        public void RelativeHorizontalDeltaTurnsWithoutFrameScalingOrVerticalTurn()
        {
            Pump(new KeyboardState(Key.W), new MouseState { delta = new Vector2(4, 80) });
            StreetInputFrame frame = sampler.Read();
            Assert.That(frame.TurnDegrees, Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(frame.Move.Y, Is.EqualTo(1));

            // A second poll in the same Input System update must not apply the same delta twice.
            Assert.That(sampler.Read().TurnDegrees, Is.Zero);

            var reversed = new StreetConfig { InvertMouse = true };
            sampler.Configure(reversed);
            sampler.SetGameplayActive(true);
            Pump(new KeyboardState(), new MouseState());
            sampler.Read();
            Pump(new KeyboardState(), new MouseState());
            sampler.Read();
            Pump(new KeyboardState(), new MouseState { delta = new Vector2(4, -80) });
            Assert.That(sampler.Read().TurnDegrees, Is.EqualTo(-0.6f).Within(1e-5f));
        }

        [Test]
        public void RightButtonIsOneProbePerPressAndSpaceOrShiftDoesNotProbe()
        {
            var rightDown = new MouseState().WithButton(MouseButton.Right);
            Pump(new KeyboardState(), rightDown);
            Assert.That(sampler.Read().ProbePressed, Is.True);
            Assert.That(sampler.Read().ProbePressed, Is.False);

            Pump(new KeyboardState(), rightDown);
            Assert.That(sampler.Read().ProbePressed, Is.False);
            Pump(new KeyboardState(), new MouseState());
            Assert.That(sampler.Read().ProbePressed, Is.False);
            Pump(new KeyboardState(), rightDown);
            Assert.That(sampler.Read().ProbePressed, Is.True);

            Pump(new KeyboardState(Key.Space, Key.LeftShift), new MouseState());
            Assert.That(sampler.Read().ProbePressed, Is.False);
        }

        [Test]
        public void RightButtonPressAndReleaseInOneInputUpdateStillProducesOneProbeEdge()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right));
            InputSystem.QueueStateEvent(mouse, new MouseState());
            InputSystem.Update();

            Assert.That(mouse.rightButton.isPressed, Is.False, "the click has already been released before sampling");
            Assert.That(sampler.Read().ProbePressed, Is.True);
            Assert.That(sampler.Read().ProbePressed, Is.False);
        }

        [Test]
        public void MenuLeftClickIsLeftForTheUiAndRightClickOnlyConfirmsMenu()
        {
            sampler.SetGameplayActive(false);
            Pump(new KeyboardState(), new MouseState());
            sampler.Read();

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
            InputSystem.QueueStateEvent(mouse, new MouseState());
            InputSystem.Update();
            Assert.That(sampler.Read().Menu, Is.EqualTo(MenuAction.None));
            Pump(new KeyboardState(), new MouseState());
            sampler.Read();

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right));
            InputSystem.QueueStateEvent(mouse, new MouseState());
            InputSystem.Update();
            StreetInputFrame frame = sampler.Read();
            Assert.That(frame.Menu, Is.EqualTo(MenuAction.Confirm));
            Assert.That(frame.ProbePressed, Is.False);
        }

        [Test]
        public void EscapePausesAndSuppressesMovementAndProbeOnThatFrame()
        {
            Pump(new KeyboardState(Key.W, Key.Escape), new MouseState().WithButton(MouseButton.Right));
            StreetInputFrame frame = sampler.Read();
            Assert.That(frame.PausePressed, Is.True);
            Assert.That(frame.Move.X, Is.Zero);
            Assert.That(frame.Move.Y, Is.Zero);
            Assert.That(frame.ProbePressed, Is.False);

            Pump(new KeyboardState(Key.W), new MouseState());
            StreetInputFrame heldThroughPause = sampler.Read();
            Assert.That(heldThroughPause.Move.X, Is.Zero);
            Assert.That(heldThroughPause.Move.Y, Is.Zero);
            Assert.That(heldThroughPause.Menu, Is.EqualTo(MenuAction.None),
                "a movement key held through the pause transition must not navigate the menu");

            Pump(new KeyboardState(), new MouseState());
            Assert.That(sampler.Read().Menu, Is.EqualTo(MenuAction.None));
            Pump(new KeyboardState(Key.W), new MouseState());
            StreetInputFrame menuNavigation = sampler.Read();
            Assert.That(menuNavigation.Move.X, Is.Zero);
            Assert.That(menuNavigation.Move.Y, Is.Zero);
            Assert.That(menuNavigation.Menu, Is.EqualTo(MenuAction.Previous));
        }

        [Test]
        public void ClearDropsAnUnconsumedShortClickAndAllowsANewPress()
        {
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right));
            InputSystem.QueueStateEvent(mouse, new MouseState());
            InputSystem.Update();

            sampler.Clear();
            Assert.That(sampler.Read().ProbePressed, Is.False);
            Assert.That(sampler.Read().ProbePressed, Is.False);

            Pump(new KeyboardState(), new MouseState());
            sampler.Read();
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right));
            InputSystem.QueueStateEvent(mouse, new MouseState());
            InputSystem.Update();
            Assert.That(sampler.Read().ProbePressed, Is.True);
            Assert.That(sampler.Read().ProbePressed, Is.False);
        }

        [Test]
        public void ClearBlocksHeldRightButtonUntilReleaseThenAcceptsSameUpdatePress()
        {
            var rightDown = new MouseState().WithButton(MouseButton.Right);
            Pump(new KeyboardState(), rightDown);
            Assert.That(sampler.Read().ProbePressed, Is.True);

            sampler.Clear();
            Assert.That(sampler.Read().ProbePressed, Is.False,
                "Clear's transition read is discarded while the original right press remains held");

            InputSystem.QueueStateEvent(mouse, new MouseState());
            InputSystem.QueueStateEvent(mouse, rightDown);
            InputSystem.Update();

            Assert.That(sampler.Read().ProbePressed, Is.True,
                "a release must unblock a fresh press even when both events are processed in one update");
            Assert.That(sampler.Read().ProbePressed, Is.False);
        }

        [Test]
        public void FocusLossDropsAnUnconsumedClickBeforeGameplayRestores()
        {
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right));
            InputSystem.QueueStateEvent(mouse, new MouseState());
            InputSystem.Update();

            sampler.OnApplicationFocus(false);
            Assert.That(sampler.Read().ProbePressed, Is.False);
            sampler.OnApplicationFocus(true);
            Assert.That(sampler.Read().ProbePressed, Is.False);
            Pump(new KeyboardState(), new MouseState());
            Assert.That(sampler.Read().ProbePressed, Is.False);

            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right));
            InputSystem.QueueStateEvent(mouse, new MouseState());
            InputSystem.Update();
            Assert.That(sampler.Read().ProbePressed, Is.True);
            Assert.That(sampler.Read().ProbePressed, Is.False);
        }

        [Test]
        public void MenuConfirmationDoesNotBecomeGameplayInputAndFocusLossClearsInput()
        {
            sampler.SetGameplayActive(false);
            Pump(new KeyboardState(), new MouseState());
            sampler.Read();

            Pump(new KeyboardState(Key.W, Key.Enter), new MouseState { delta = new Vector2(90, 0) }.WithButton(MouseButton.Right));
            StreetInputFrame menu = sampler.Read();
            Assert.That(menu.Menu, Is.EqualTo(MenuAction.Confirm));
            Assert.That(menu.Move.X, Is.Zero);
            Assert.That(menu.Move.Y, Is.Zero);
            Assert.That(menu.TurnDegrees, Is.Zero);
            Assert.That(menu.ProbePressed, Is.False);

            sampler.SetGameplayActive(true);
            Pump(new KeyboardState(Key.W, Key.Enter), new MouseState { delta = new Vector2(900, 0) }.WithButton(MouseButton.Right));
            StreetInputFrame transition = sampler.Read();
            Assert.That(transition.Move.X, Is.Zero);
            Assert.That(transition.Move.Y, Is.Zero);
            Assert.That(transition.TurnDegrees, Is.Zero);
            Assert.That(transition.ProbePressed, Is.False);

            sampler.OnApplicationFocus(false);
            Assert.That(sampler.HasFocus, Is.False);
            Pump(new KeyboardState(Key.W), new MouseState { delta = new Vector2(300, 0) });
            StreetInputFrame unfocused = sampler.Read();
            Assert.That(unfocused.Move.X, Is.Zero);
            Assert.That(unfocused.Move.Y, Is.Zero);
            Assert.That(unfocused.TurnDegrees, Is.Zero);
            Assert.That(unfocused.ProbePressed, Is.False);

            sampler.OnApplicationFocus(true);
            Assert.That(sampler.HasFocus, Is.True);
            sampler.Read(); // Discard the focus-restoration transition.
            Pump(new KeyboardState(Key.W), new MouseState());
            StreetInputFrame stillHeld = sampler.Read();
            Assert.That(stillHeld.Move.X, Is.Zero);
            Assert.That(stillHeld.Move.Y, Is.Zero);

            Pump(new KeyboardState(), new MouseState());
            sampler.Read();
            Pump(new KeyboardState(Key.W), new MouseState());
            Assert.That(sampler.Read().Move.Y, Is.EqualTo(1));
        }

        private void AssertMove(KeyboardState state, float x, float y)
        {
            Pump(state, new MouseState());
            StreetInputFrame frame = sampler.Read();
            Assert.That(frame.Move.X, Is.EqualTo(x).Within(1e-5f));
            Assert.That(frame.Move.Y, Is.EqualTo(y).Within(1e-5f));
            Assert.That(frame.TurnDegrees, Is.Zero);
            Assert.That(frame.ProbePressed, Is.False);
        }

        private void Pump(KeyboardState keyboardState, MouseState mouseState)
        {
            InputSystem.QueueStateEvent(keyboard, keyboardState);
            InputSystem.QueueStateEvent(mouse, mouseState);
            InputSystem.Update();
        }
    }
}
