using System;
using System.Collections.Generic;
using TingLu.Config;
using TingLu.Contracts;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace TingLu.Input
{
    /// <summary>
    /// Samples keyboard movement, relative mouse turning, and edge-triggered street actions.
    /// Bootstrap owns when gameplay is active; this component owns pointer capture and focus cleanup.
    /// </summary>
    public sealed class StreetInputSampler : MonoBehaviour, IStreetInput
    {
        private static readonly Key[] TrackedKeys =
        {
            Key.W, Key.A, Key.S, Key.D,
            Key.UpArrow, Key.LeftArrow, Key.DownArrow, Key.RightArrow,
            Key.Escape, Key.Enter, Key.Space, Key.Backspace, Key.G
        };

        private readonly HashSet<Key> previousKeys = new HashSet<Key>();
        private readonly HashSet<Key> blockedKeys = new HashSet<Key>();
        private readonly List<Key> releasedKeys = new List<Key>();
        private InputAction rightClickAction;
        private StreetConfig config;
        private bool gameplayActive;
        private bool pointerCaptured;
        private bool previousRightButton;
        private bool observedRightButton;
        private bool blockedRightButton;
        private bool pendingRightPress;
        private bool ignoreNextRead;
        private uint lastMouseDeltaUpdate;
        private uint lastRightPressUpdate;
        private bool applicationPaused;

        public bool HasFocus { get; private set; } = true;

        /// <summary>Installs the shared tuning used by the street input contract.</summary>
        public void Configure(StreetConfig tuning)
        {
            if (tuning == null) throw new ArgumentNullException("tuning");
            tuning.Validate();

            if (gameplayActive)
            {
                gameplayActive = false;
                ReleasePointer();
            }

            config = tuning;
            if (isActiveAndEnabled) EnableRightClickAction();
            HasFocus = !Application.isPlaying || Application.isFocused;
            Clear();
            SyncPointer();
        }

        /// <summary>Returns the current input frame. Call once from the owning update loop.</summary>
        public StreetInputFrame Read()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            RefreshBlockedControls(keyboard, mouse);

            if (ignoreNextRead)
            {
                ignoreNextRead = false;
                pendingRightPress = false;
                SnapshotControls(keyboard, mouse);
                return EmptyFrame();
            }

            if (!HasFocus || config == null)
            {
                pendingRightPress = false;
                SnapshotControls(keyboard, mouse);
                return EmptyFrame();
            }

            bool rightDown = mouse != null && mouse.rightButton.isPressed;
            uint inputUpdate = InputState.updateCount;
            bool rightEdge = pendingRightPress
                             || (rightClickAction != null && rightClickAction.WasPressedThisFrame())
                             || (rightDown && !previousRightButton);
            bool rightPressed = rightEdge && inputUpdate != lastRightPressUpdate && !blockedRightButton;
            pendingRightPress = false;
            if (rightPressed) lastRightPressUpdate = inputUpdate;

            bool pause = Pressed(Key.Escape, keyboard);
            MenuAction menu = ReadMenuAction(keyboard, rightPressed);

            Point2 move = new Point2(0, 0);
            float turn = 0;
            bool probe = false;
            if (gameplayActive && !pause)
            {
                float horizontal = (IsPressed(Key.D, keyboard) || IsPressed(Key.RightArrow, keyboard) ? 1f : 0f)
                                   - (IsPressed(Key.A, keyboard) || IsPressed(Key.LeftArrow, keyboard) ? 1f : 0f);
                float vertical = (IsPressed(Key.W, keyboard) || IsPressed(Key.UpArrow, keyboard) ? 1f : 0f)
                                 - (IsPressed(Key.S, keyboard) || IsPressed(Key.DownArrow, keyboard) ? 1f : 0f);
                move = StreetCoordinates.ScreenMove(new Point2(horizontal, vertical));
                probe = rightPressed;
                turn = ReadTurn(mouse);
            }

            SnapshotControls(keyboard, mouse);
            var frame = new StreetInputFrame(move, turn, probe, pause, menu);
            if (pause && gameplayActive) SetGameplayActive(false);
            return frame;
        }

        /// <summary>Captures the pointer only while focused gameplay is active.</summary>
        public void SetGameplayActive(bool active)
        {
            if (gameplayActive != active)
            {
                gameplayActive = active;
                Clear();
            }
            SyncPointer();
        }

        /// <summary>
        /// Drops pending relative motion and edges, and blocks controls already held until released.
        /// This prevents a held key or focus-transition mouse delta from leaking into resumed play.
        /// </summary>
        public void Clear()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            blockedKeys.Clear();
            if (keyboard != null)
            {
                foreach (var key in TrackedKeys)
                    if (keyboard[key].isPressed) blockedKeys.Add(key);
            }

            blockedRightButton = mouse != null && mouse.rightButton.isPressed;
            observedRightButton = blockedRightButton;
            pendingRightPress = false;
            SnapshotControls(keyboard, mouse);
            ignoreNextRead = true;
            lastMouseDeltaUpdate = InputState.updateCount;
            lastRightPressUpdate = InputState.updateCount;
        }

        /// <summary>Unity focus callback. A return to focus starts with clean input state.</summary>
        public void OnApplicationFocus(bool hasFocus)
        {
            SetFocus(hasFocus && !applicationPaused);
        }

        public void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            SetFocus(!paused && Application.isFocused);
        }

        private void SetFocus(bool hasFocus)
        {
            if (HasFocus == hasFocus) return;
            HasFocus = hasFocus;
            Clear();
            SyncPointer();
        }

        private void OnDisable()
        {
            InputSystem.onEvent -= OnInputEvent;
            if (rightClickAction != null) rightClickAction.Disable();
            gameplayActive = false;
            Clear();
            ReleasePointer();
        }

        private void OnEnable()
        {
            InputSystem.onEvent += OnInputEvent;
            observedRightButton = Mouse.current != null && Mouse.current.rightButton.isPressed;
            EnableRightClickAction();
            SyncPointer();
        }

        private void OnInputEvent(InputEventPtr eventPtr, InputDevice device)
        {
            var mouse = device as Mouse;
            if (mouse == null || !HasFocus || !isActiveAndEnabled ||
                (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()))
                return;

            // Inspect each raw state event so a down/up pair processed in one Input
            // System update is preserved even when polling sees only the released state
            // and InputAction callbacks are not dispatched by the active update mode.
            float value;
            if (!mouse.rightButton.ReadValueFromEvent(eventPtr, out value)) return;

            bool pressed = mouse.rightButton.IsValueConsideredPressed(value);
            if (pressed && !observedRightButton && !blockedRightButton)
                pendingRightPress = true;
            if (!pressed)
                blockedRightButton = false;
            observedRightButton = pressed;
        }

        private void EnableRightClickAction()
        {
            if (rightClickAction == null)
            {
                rightClickAction = new InputAction("StreetRightClick", InputActionType.Button, "<Mouse>/rightButton");
                rightClickAction.started += OnRightClickStarted;
                rightClickAction.canceled += OnRightClickCanceled;
            }
            rightClickAction.Enable();
        }

        private void OnDestroy()
        {
            InputSystem.onEvent -= OnInputEvent;
            if (rightClickAction == null) return;
            rightClickAction.Dispose();
            rightClickAction = null;
        }

        private void OnRightClickStarted(InputAction.CallbackContext context)
        {
            if (!blockedRightButton) pendingRightPress = true;
        }

        private void OnRightClickCanceled(InputAction.CallbackContext context)
        {
            blockedRightButton = false;
        }

        private void OnApplicationQuit()
        {
            gameplayActive = false;
            Clear();
            ReleasePointer();
        }

        private MenuAction ReadMenuAction(Keyboard keyboard, bool rightPressed)
        {
            if (!gameplayActive)
            {
                if (Pressed(Key.Enter, keyboard) || Pressed(Key.Space, keyboard) || rightPressed)
                    return MenuAction.Confirm;
                if (Pressed(Key.Backspace, keyboard)) return MenuAction.Back;
                if (Pressed(Key.G, keyboard)) return MenuAction.QueryGoal;
                if (Pressed(Key.UpArrow, keyboard) || Pressed(Key.W, keyboard)) return MenuAction.Previous;
                if (Pressed(Key.DownArrow, keyboard) || Pressed(Key.S, keyboard)) return MenuAction.Next;
                if (Pressed(Key.LeftArrow, keyboard) || Pressed(Key.A, keyboard)) return MenuAction.Decrease;
                if (Pressed(Key.RightArrow, keyboard) || Pressed(Key.D, keyboard)) return MenuAction.Increase;
                return MenuAction.None;
            }

            return Pressed(Key.G, keyboard) ? MenuAction.QueryGoal : MenuAction.None;
        }

        private float ReadTurn(Mouse mouse)
        {
            if (mouse == null) return 0;

            uint update = InputState.updateCount;
            if (update == lastMouseDeltaUpdate) return 0;
            lastMouseDeltaUpdate = update;

            float direction = config.InvertMouse ? -1f : 1f;
            return mouse.delta.ReadValue().x * config.MouseSensitivity * direction;
        }

        private bool IsPressed(Key key, Keyboard keyboard)
        {
            return keyboard != null && !blockedKeys.Contains(key) && keyboard[key].isPressed;
        }

        private bool Pressed(Key key, Keyboard keyboard)
        {
            return keyboard != null && !blockedKeys.Contains(key) && keyboard[key].isPressed && !previousKeys.Contains(key);
        }

        private void RefreshBlockedControls(Keyboard keyboard, Mouse mouse)
        {
            if (keyboard != null)
            {
                releasedKeys.Clear();
                foreach (var key in blockedKeys)
                    if (!keyboard[key].isPressed) releasedKeys.Add(key);
                foreach (var key in releasedKeys) blockedKeys.Remove(key);
            }
            else
            {
                blockedKeys.Clear();
            }

            if (mouse == null)
            {
                blockedRightButton = false;
            }
            else
            {
                if (!mouse.rightButton.isPressed) blockedRightButton = false;
            }
        }

        private void SnapshotControls(Keyboard keyboard, Mouse mouse)
        {
            previousKeys.Clear();
            if (keyboard != null)
                foreach (var key in TrackedKeys)
                    if (keyboard[key].isPressed) previousKeys.Add(key);

            previousRightButton = mouse != null && mouse.rightButton.isPressed;
        }

        private void SyncPointer()
        {
            if (config != null && gameplayActive && HasFocus && isActiveAndEnabled)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                pointerCaptured = true;
            }
            else
            {
                ReleasePointer();
            }
        }

        private void ReleasePointer()
        {
            if (!pointerCaptured) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            pointerCaptured = false;
        }

        private static StreetInputFrame EmptyFrame()
        {
            return new StreetInputFrame(new Point2(0, 0));
        }
    }
}
