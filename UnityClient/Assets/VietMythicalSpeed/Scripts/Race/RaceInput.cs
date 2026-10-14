using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace VMS.Race
{
    /// <summary>
    /// Turns touches, keys and tilt into lane changes or an analogue steering value.
    /// Uses the legacy Input Manager when it is enabled, otherwise the Input System package.
    /// Touches that start on UI (pause / skill buttons) are ignored.
    /// </summary>
    public sealed class RaceInput
    {
        const float SwipeThreshold = 0.06f;
        const float DragRange = 0.32f;

        public ControlScheme Scheme;
        public float TiltSensitivity = 1f;

        /// <summary>Lane change requested this frame (-1, 0 or +1; several taps in one frame add up).</summary>
        public int LaneDelta { get; private set; }
        /// <summary>True when <see cref="Analog"/> is driving the vessel this frame.</summary>
        public bool HasAnalog { get; private set; }
        /// <summary>-1 (leftmost lane) .. +1 (rightmost lane).</summary>
        public float Analog { get; private set; }
        public bool PausePressed { get; private set; }

        Vector2 _pressStart;
        bool _pressActive;
        float _dragStartAnalog;

        public void Poll(float currentAnalog)
        {
            LaneDelta = 0;
            HasAnalog = false;
            PausePressed = false;

            bool began, held, ended;
            Vector2 position;
            ReadPointer(out began, out held, out ended, out position);
            ReadKeys();

            float width = Mathf.Max(1, Screen.width);
            if (began)
            {
                _pressActive = true;
                _pressStart = position;
                _dragStartAnalog = currentAnalog;
                if (Scheme == ControlScheme.Tap) LaneDelta += position.x < width * 0.5f ? -1 : 1;
            }
            if (_pressActive && (held || ended))
            {
                float dx = (position.x - _pressStart.x) / width;
                if (Scheme == ControlScheme.Swipe && Mathf.Abs(dx) > SwipeThreshold)
                {
                    LaneDelta += dx < 0f ? -1 : 1;
                    // Re-anchor so chained swipes work without lifting the finger.
                    _pressStart = position;
                }
                if (Scheme == ControlScheme.Drag)
                {
                    HasAnalog = true;
                    Analog = Mathf.Clamp(_dragStartAnalog + dx / DragRange, -1f, 1f);
                }
            }
            if (ended) _pressActive = false;

            if (Scheme == ControlScheme.Tilt)
            {
                HasAnalog = true;
                Analog = Mathf.Clamp(ReadTilt() * 2.4f * TiltSensitivity, -1f, 1f);
            }
            else if (Scheme == ControlScheme.Drag && !_pressActive)
            {
                HasAnalog = true;
                Analog = currentAnalog;
            }
        }

        static bool OverUi(int pointerId)
        {
            var es = EventSystem.current;
            return es != null && (pointerId < 0 ? es.IsPointerOverGameObject() : es.IsPointerOverGameObject(pointerId));
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        void ReadPointer(out bool began, out bool held, out bool ended, out Vector2 position)
        {
            began = held = ended = false;
            position = _pressStart;
            if (Input.touchCount > 0)
            {
                var touch = Input.GetTouch(0);
                position = touch.position;
                switch (touch.phase)
                {
                    case TouchPhase.Began: began = !OverUi(touch.fingerId); break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary: held = true; break;
                    default: ended = true; break;
                }
                // Extra simultaneous taps (second thumb) also count in Tap mode.
                for (int i = 1; i < Input.touchCount; i++)
                {
                    var extra = Input.GetTouch(i);
                    if (extra.phase == TouchPhase.Began && Scheme == ControlScheme.Tap && !OverUi(extra.fingerId))
                        LaneDelta += extra.position.x < Screen.width * 0.5f ? -1 : 1;
                }
                return;
            }
            position = Input.mousePosition;
            if (Input.GetMouseButtonDown(0)) began = !OverUi(-1);
            else if (Input.GetMouseButton(0)) held = true;
            else if (Input.GetMouseButtonUp(0)) ended = true;
        }

        void ReadKeys()
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) LaneDelta -= 1;
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) LaneDelta += 1;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)) PausePressed = true;
        }

        static float ReadTilt() => Input.acceleration.x;
#else
        void ReadPointer(out bool began, out bool held, out bool ended, out Vector2 position)
        {
            began = held = ended = false;
            position = _pressStart;
            var touchscreen = Touchscreen.current;
            if (touchscreen != null && (touchscreen.primaryTouch.press.isPressed || touchscreen.primaryTouch.press.wasReleasedThisFrame))
            {
                var touch = touchscreen.primaryTouch;
                position = touch.position.ReadValue();
                if (touch.press.wasPressedThisFrame) began = !OverUi(touch.touchId.ReadValue());
                else if (touch.press.wasReleasedThisFrame) ended = true;
                else held = true;
                return;
            }
            var mouse = Mouse.current;
            if (mouse == null) return;
            position = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame) began = !OverUi(-1);
            else if (mouse.leftButton.wasReleasedThisFrame) ended = true;
            else if (mouse.leftButton.isPressed) held = true;
        }

        void ReadKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) LaneDelta -= 1;
            if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) LaneDelta += 1;
            if (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame) PausePressed = true;
        }

        static float ReadTilt()
        {
            var accel = Accelerometer.current;
            if (accel == null) return 0f;
            if (!accel.enabled) InputSystem.EnableDevice(accel);
            return accel.acceleration.ReadValue().x;
        }
#endif
    }
}
