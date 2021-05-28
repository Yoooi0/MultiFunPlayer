using Linearstar.Windows.RawInput;
using Linearstar.Windows.RawInput.Native;
using MultiFunPlayer.Common.Input.Gesture;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;

namespace MultiFunPlayer.Common.Input.Processor
{
    public class HidInputProcessor : IInputProcessor
    {
        private readonly Dictionary<int, int> _axisStates;
        private readonly Dictionary<ushort, bool> _buttonStates;

        public HidInputProcessor()
        {
            _axisStates = new Dictionary<int, int>();
            _buttonStates = new Dictionary<ushort, bool>();
        }

        public IEnumerable<IInputGesture> GetGestures(RawInputData data)
        {
            if (data is not RawInputHidData hid)
                yield break;

            var vid = hid.Device.VendorId;
            var pid = hid.Device.ProductId;

            var valueIndex = 0;
            foreach (var o in hid.ValueSetStates)
            {
                foreach(var value in o.CurrentValues)
                {
                    var isGesture = _axisStates.TryGetValue(valueIndex, out var state) && state != value && state > 0;
                    _axisStates[valueIndex] = value;

                    if (isGesture)
                        yield return new HidAxisGesture(vid, pid, valueIndex, value / (float)short.MaxValue, (value - state) / (float)short.MaxValue);

                    valueIndex++;
                }
            }

            foreach (var o in hid.ButtonSetStates)
            {
                foreach(var index in o.ActiveUsages)
                {
                    var isGesture = !_buttonStates.TryGetValue(index, out var state) || !state;
                    _buttonStates[index] = true;

                    if (isGesture)
                        yield return new HidButtonGesture(vid, pid, index);
                }

                foreach(var (index, _) in _buttonStates)
                {
                    if (!o.ActiveUsages.Contains(index))
                        _buttonStates[index] = false;
                }
            }
        }
    }
}
