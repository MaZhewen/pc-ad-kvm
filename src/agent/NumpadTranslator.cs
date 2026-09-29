using System.Collections.Generic;

namespace PcKvm
{
    /// <summary>Keep each keypad key's down/up scancodes paired across NumLock changes.</summary>
    public sealed class NumpadTranslator
    {
        readonly Dictionary<int, int> _pressed = new Dictionary<int, int>();

        public void Reset() { _pressed.Clear(); }

        /// <returns>The scancode to send, or 0 when this key has no Android action.</returns>
        public int Translate(int scancode, bool isUp, bool numLockOn)
        {
            if ((scancode & 0xE000) != 0) return scancode;
            int navigation = KeyMap.NumpadNavE0Scancode(scancode & 0xFF);
            if (navigation < 0) return scancode;

            int mapped;
            if (isUp)
            {
                if (_pressed.TryGetValue(scancode, out mapped))
                {
                    _pressed.Remove(scancode);
                    return mapped;
                }
                return numLockOn ? scancode : navigation;
            }
            if (_pressed.TryGetValue(scancode, out mapped)) return mapped;
            mapped = numLockOn ? scancode : navigation;
            _pressed.Add(scancode, mapped);
            return mapped;
        }
    }
}
