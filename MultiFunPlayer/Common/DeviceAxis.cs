using System;

namespace MultiFunPlayer.Common
{
    public enum DeviceAxis
    {
        L0,
        L1,
        L2,
        R0,
        R1,
        R2,
        V0,
        V1
    }

    public static class DeviceAxisExtensions
    {
        public static float DefaultValue(this DeviceAxis axis)
            => axis switch
            {
                DeviceAxis.L0 => 0.5f,
                DeviceAxis.L1 => 0.5f,
                DeviceAxis.L2 => 0.5f,
                DeviceAxis.R0 => 0.5f,
                DeviceAxis.R1 => 0.5f,
                DeviceAxis.R2 => 0.5f,
                DeviceAxis.V0 => 0.0f,
                DeviceAxis.V1 => 0.0f,
                _ => throw new NotImplementedException()
            };

        public static string Name(this DeviceAxis axis) => axis.ToString();
        public static string AltName(this DeviceAxis axis)
            => axis switch
            {
                DeviceAxis.L0 => "stroke",
                DeviceAxis.L1 => "surge",
                DeviceAxis.L2 => "sway",
                DeviceAxis.R0 => "twist",
                DeviceAxis.R1 => "roll",
                DeviceAxis.R2 => "pitch",
                DeviceAxis.V0 => "vib",
                DeviceAxis.V1 => "pump",
                _ => throw new NotImplementedException()
            };

        public static string FriendlyName(this DeviceAxis axis)
            => axis switch
            {
                DeviceAxis.L0 => "Up/Down",
                DeviceAxis.L1 => "Forward/Backward",
                DeviceAxis.L2 => "Left/Right",
                DeviceAxis.R0 => "Twist",
                DeviceAxis.R1 => "Roll",
                DeviceAxis.R2 => "Pitch",
                DeviceAxis.V0 => "Vibrate",
                DeviceAxis.V1 => "Pump",
                _ => throw new NotImplementedException()
            };
        }
}
