using System;

namespace MultiFunPlayer.Common.Input.Gesture
{
    public record HidButtonGestureDescriptor(int VendorId, int ProductId, int Button) : IInputGestureDescriptor
    {
        public override string ToString() => $"[Hid Button: {VendorId}/{ProductId}/{Button}]";
    }

    public class HidButtonGesture : IInputGesture
    {
        private readonly HidButtonGestureDescriptor _descriptor;

        public int VendorId => _descriptor.VendorId;
        public int ProductId => _descriptor.ProductId;
        public int Button => _descriptor.Button;

        public IInputGestureDescriptor Descriptor => _descriptor;

        public HidButtonGesture(HidButtonGestureDescriptor descriptor) => _descriptor = descriptor;

        public override string ToString() => $"[Hid Button: {VendorId}/{ProductId}/{Button}]";

        public static HidButtonGesture Create(int vendorId, int productId, int button) => new(new(vendorId, productId, button));
    }
}
