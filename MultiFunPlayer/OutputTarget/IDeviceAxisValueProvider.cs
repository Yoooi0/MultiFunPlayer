using MultiFunPlayer.Common;

namespace MultiFunPlayer.OutputTarget
{
    public interface IDeviceAxisValueProvider
    {
        public float GetValue(DeviceAxis axis);
    }
}
