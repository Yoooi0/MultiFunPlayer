using System;
using System.Collections.Generic;

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
        V1,
        L3,
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
                DeviceAxis.L3 => 0.5f,
                _ => throw new NotSupportedException()
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
                DeviceAxis.L3 => "Suction",
                _ => throw new NotSupportedException()
            };

        public static IEnumerable<string> Names(this DeviceAxis axis)
        {
            yield return axis.ToString();
            switch (axis)
            {
                case DeviceAxis.L0: yield return "stroke"; break;
                case DeviceAxis.L1: yield return "surge"; break;
                case DeviceAxis.L2: yield return "sway"; break;
                case DeviceAxis.R0: yield return "twist"; break;
                case DeviceAxis.R1: yield return "roll"; break;
                case DeviceAxis.R2: yield return "pitch"; break;
                case DeviceAxis.V0: yield return "vib"; break;
                case DeviceAxis.V1:
                    yield return "pump";
                    yield return "lube";
                    break;
                case DeviceAxis.L3:
                    yield return "suck";
                    yield return "valve";
                    break;
                default:
                    throw new NotSupportedException();
            }
        }
    }
}
