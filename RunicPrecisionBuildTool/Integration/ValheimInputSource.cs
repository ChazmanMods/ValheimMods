using System;
using System.Collections.Generic;
using UnityEngine;

namespace QuietBuildRotation
{
    /// <summary>
    /// Runic's single input boundary. Every held key, key-down edge, and wheel value is sampled
    /// through Valheim's ZInput layer so Runic observes the same keyboard/mouse state as placement.
    /// </summary>
    internal sealed class ValheimInputSource : IInputSource
    {
        private readonly Func<KeyCode, bool> _readHeld;
        private readonly Func<KeyCode, bool> _readDown;
        private readonly Func<float> _readScroll;
        private readonly Func<int> _readFrame;
        private readonly Dictionary<KeyCode, KeyFrameState> _keyStates =
            new Dictionary<KeyCode, KeyFrameState>();
        private readonly Dictionary<KeyCode, int> _keyDownFrames =
            new Dictionary<KeyCode, int>();
        private int _scrollFrame = int.MinValue;

        internal ValheimInputSource()
            : this(ReadHeldFromValheim, ReadDownFromValheim, ReadScrollFromValheim, ReadUnityFrame)
        {
        }

        /// <summary>
        /// Injectable seam for deterministic adapter tests. Production always uses the parameterless
        /// constructor above, whose four readers are exclusively backed by ZInput and Time.frameCount.
        /// </summary>
        internal ValheimInputSource(
            Func<KeyCode, bool> readHeld,
            Func<KeyCode, bool> readDown,
            Func<float> readScroll,
            Func<int> readFrame)
        {
            _readHeld = readHeld ?? throw new ArgumentNullException(nameof(readHeld));
            _readDown = readDown ?? throw new ArgumentNullException(nameof(readDown));
            _readScroll = readScroll ?? throw new ArgumentNullException(nameof(readScroll));
            _readFrame = readFrame ?? throw new ArgumentNullException(nameof(readFrame));
        }

        public bool IsPressed(KeyCode key)
        {
            if (key == KeyCode.None)
                return false;

            KeyFrameState state = ReadKeyState(key);
            // Input System can report a press and release in one dynamic update. In that case
            // isPressed is already false while wasPressedThisFrame is still true. Counting that
            // edge as active prevents a quick modifier tap and wheel notch from missing each other.
            return state.Held || state.PressedThisFrame;
        }

        public bool WasPressedThisFrame(KeyCode key)
        {
            if (key == KeyCode.None)
                return false;

            int frame = _readFrame();
            if (_keyDownFrames.TryGetValue(key, out int consumedFrame) && consumedFrame == frame)
                return false;
            if (!ReadKeyState(key, frame).PressedThisFrame)
                return false;

            _keyDownFrames[key] = frame;
            return true;
        }

        public float ReadScrollDelta()
        {
            int frame = _readFrame();
            if (_scrollFrame == frame)
                return 0f;

            _scrollFrame = frame;
            return _readScroll();
        }

        private KeyFrameState ReadKeyState(KeyCode key) => ReadKeyState(key, _readFrame());

        private KeyFrameState ReadKeyState(KeyCode key, int frame)
        {
            if (_keyStates.TryGetValue(key, out KeyFrameState state) && state.Frame == frame)
                return state;

            state = new KeyFrameState(frame, _readHeld(key), _readDown(key));
            _keyStates[key] = state;
            return state;
        }

        private static bool ReadHeldFromValheim(KeyCode key) => ZInput.GetKey(key, false);

        private static bool ReadDownFromValheim(KeyCode key) => ZInput.GetKeyDown(key, false);

        private static float ReadScrollFromValheim() => ZInput.GetMouseScrollWheel();

        private static int ReadUnityFrame() => Time.frameCount;

        private readonly struct KeyFrameState
        {
            internal KeyFrameState(int frame, bool held, bool pressedThisFrame)
            {
                Frame = frame;
                Held = held;
                PressedThisFrame = pressedThisFrame;
            }

            internal int Frame { get; }

            internal bool Held { get; }

            internal bool PressedThisFrame { get; }
        }
    }
}
