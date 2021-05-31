using System;

namespace MultiFunPlayer.Common.Input.Gesture
{
    public class HidAxisGestureDescriptor : IInputGestureDescriptor
    {
        public int VendorId { get; }
        public int ProductId { get; }
        public int Axis { get; }

        public HidAxisGestureDescriptor(int vendorId, int productId, int axis)
        {
            VendorId = vendorId;
            ProductId = productId;
            Axis = axis;
        }

        public bool Equals(IInputGestureDescriptor other) => other is HidAxisGestureDescriptor d && Axis == d.Axis && VendorId == d.VendorId && ProductId == d.ProductId;
        public override int GetHashCode() => HashCode.Combine(VendorId, ProductId, Axis);
        public override string ToString() => $"[Hid Axis: {VendorId}/{ProductId}/{Axis}]";
    }

    public class HidAxisGesture : IAxisInputGesture
    {
        private readonly HidAxisGestureDescriptor _descriptor;

        public int VendorId => _descriptor.VendorId;
        public int ProductId => _descriptor.ProductId;
        public int Axis => _descriptor.Axis;

        public float Value { get; }
        public float Delta { get; }

        public IInputGestureDescriptor Descriptor => _descriptor;

        public HidAxisGesture(HidAxisGestureDescriptor descriptor, float value, float delta)
        {
            _descriptor = descriptor;

            Value = value;
            Delta = delta;
        }

        public override string ToString() => $"[Hid Axis: {VendorId}/{ProductId}/{Axis}, Value: {Value}, Delta: {Delta}]";

        public static HidAxisGesture Create(int vendorId, int productId, int axis, float value, float delta) => new(new(vendorId, productId, axis), value, delta);
    }
}
