using System;

namespace MultiFunPlayer.Common.Input.Gesture
{
    public class HidAxisGesture : IAxisInputGesture
    {
        public int VendorId { get; }
        public int ProductId { get; }

        public float Value { get; }
        public float Delta { get; }
        public int Axis { get; }

        public HidAxisGesture(int vendorId, int productId, int axis) : this(vendorId, productId, axis, 0.5f, 0f) { }
        public HidAxisGesture(int vendorId, int productId, int axis, float value, float delta)
        {
            Value = value;
            Delta = delta;
            Axis = axis;
            VendorId = vendorId;
            ProductId = productId;
        }

        public override bool Equals(object other) => Equals(other as IInputGesture);
        public bool Equals(IInputGesture other) => other is HidAxisGesture g && Axis == g.Axis && VendorId == g.VendorId && ProductId == g.ProductId;
        public override int GetHashCode() => HashCode.Combine(Axis);
        public override string ToString() => $"[Hid Axis: {VendorId}/{ProductId}/{Axis}]";
    }
}
