using Vortice.XInput;

namespace MultiFunPlayer.Common.Input.XInput
{
    public record GamepadButtonGestureDescriptor(int UserIndex, GamepadVirtualKey Button) : IInputGestureDescriptor
    {
        public override string ToString() => $"[Gamepad Button: {UserIndex}/{Button}]";
    }

    public class GamepadButtonGesture : IInputGesture
    {
        private readonly GamepadButtonGestureDescriptor _descriptor;

        public IInputGestureDescriptor Descriptor => _descriptor;
        public int UserIndex => _descriptor.UserIndex;
        public GamepadVirtualKey Button => _descriptor.Button;

        public GamepadButtonGesture(GamepadButtonGestureDescriptor descriptor) => _descriptor = descriptor;

        internal static GamepadButtonGesture Create(int userIndex, GamepadVirtualKey button) => new(new(userIndex, button));
    }
}
