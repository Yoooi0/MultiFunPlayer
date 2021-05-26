using Linearstar.Windows.RawInput;
using Linearstar.Windows.RawInput.Native;
using MultiFunPlayer.Common.Input.Gesture;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace MultiFunPlayer.Common.Input.Processor
{
    public class KeyboardInputProcessor : IInputProcessor
    {
        private readonly Dictionary<Key, bool> _state;

        public KeyboardInputProcessor()
        {
            _state = new Dictionary<Key, bool>();
        }

        public IEnumerable<IInputGesture> GetGestures(RawInputData data)
        {
            if (data is not RawInputKeyboardData keyboard)
                yield break;

            var key = KeyInterop.KeyFromVirtualKey(keyboard.Keyboard.VirutalKey);
            var pressed = !keyboard.Keyboard.Flags.HasFlag(RawKeyboardFlags.Up);

            if (pressed)
            {
                _state[key] = true;
            }
            else
            {
                var pressedKeys = _state.Where(x => x.Value).Select(x => x.Key).ToList();
                if (pressedKeys.Count > 0)
                    yield return new KeyboardGesture(pressedKeys);

                foreach (var pressedKey in pressedKeys)
                    _state[pressedKey] = false;
            }
        }
    }
}
