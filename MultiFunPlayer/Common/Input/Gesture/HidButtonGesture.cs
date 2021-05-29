using System;
using System.Windows.Input;

namespace MultiFunPlayer.Common.Input.Gesture
{
    public class HidButtonGesture : IInputGesture
    {
        public int VendorId { get; }
        public int ProductId { get; }
        public int Button { get; }

        public HidButtonGesture(int vendorId, int productId, int button)
        {
            Button = button;
            VendorId = vendorId;
            ProductId = productId;
        }

        public override bool Equals(object other) => Equals(other as IInputGesture);
        public bool Equals(IInputGesture other) => other is HidButtonGesture g && g.Button == Button;
        public override int GetHashCode() => HashCode.Combine(Button);
        public override string ToString() => $"[Hid Button: {VendorId}/{ProductId}/{Button}]";
    }
}
