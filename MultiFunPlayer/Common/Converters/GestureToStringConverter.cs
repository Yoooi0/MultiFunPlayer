using MultiFunPlayer.Common.Input.Gesture;
using System;
using System.Globalization;
using System.Windows.Data;

namespace MultiFunPlayer.Common.Converters
{
    public class GestureToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if(value is not IInputGesture gesture)
                return null;

            return gesture switch
            {
                KeyboardGesture k => $"[Keyboard Keys: {string.Join(", ", k.Keys)}]",
                MouseAxisGesture ma => $"[Mouse Axis: {ma.Axis}]",
                MouseButtonGesture mb => $"[Mouse Button: {mb.Button}]",
                HidAxisGesture ha => $"[Hid Axis: {ha.VendorId}/{ha.ProductId}/{ha.Axis}]",
                HidButtonGesture hb => $"[Hid Button: {hb.VendorId}/{hb.ProductId}/{hb.Button}]",
                _ => null
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
