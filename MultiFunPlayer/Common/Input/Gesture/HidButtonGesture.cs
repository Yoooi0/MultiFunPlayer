using System;

namespace MultiFunPlayer.Common.Input.Gesture
{
    public class HidButtonGestureDescriptor : IInputGestureDescriptor
    {
        public int VendorId { get; }
        public int ProductId { get; }
        public int Button { get; }

        public HidButtonGestureDescriptor(int vendorId, int productId, int button)
        {
            VendorId = vendorId;
            ProductId = productId;
            Button = button;
        }

        public bool Equals(IInputGestureDescriptor other) => other is HidButtonGestureDescriptor d && d.Button == Button;
        public override int GetHashCode() => HashCode.Combine(Button);
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
