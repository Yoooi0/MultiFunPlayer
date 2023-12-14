using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;

namespace MultiFunPlayer.UI.Converters;

internal sealed class KeyToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Key key)
            return null;

        return key switch
        {
            Key.Add => "Num Pad +",
            Key.Apps => "Context Menu",
            Key.Back => "Backspace",
            Key.BrowserBack => "Browser Back",
            Key.BrowserFavorites => "Browser Favorites",
            Key.BrowserForward => "Browser Forward",
            Key.BrowserHome => "Browser Home",
            Key.BrowserRefresh => "Browser Refresh",
            Key.BrowserSearch => "Browser Search",
            Key.BrowserStop => "Browser Stop",
            Key.Cancel => "Break",
            Key.CapsLock => "Caps Lock",
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",
            Key.D3 => "3",
            Key.D4 => "4",
            Key.D5 => "5",
            Key.D6 => "6",
            Key.D7 => "7",
            Key.D8 => "8",
            Key.D9 => "9",
            Key.Decimal => "Numpad .",
            Key.Divide => "Numpad /",
            Key.Down => "Arrow Down",
            Key.JunjaMode => "Junja",
            Key.KanaMode => "Kana",
            Key.KanjiMode => "Kanji",
            Key.LaunchApplication1 => "Application 1",
            Key.LaunchApplication2 => "Application 2",
            Key.LaunchMail => "Mail",
            Key.Left => "Arrow Left",
            Key.LeftAlt => "Left Alt",
            Key.LeftCtrl => "Left Ctrl",
            Key.LeftShift => "Left Shift",
            Key.LineFeed => "Line Feed",
            Key.LWin => "Left Win",
            Key.MediaNextTrack => "Media Next Track",
            Key.MediaPlayPause => "Media Play/Pause",
            Key.MediaPreviousTrack => "Media Previous Track",
            Key.MediaStop => "Media Stop",
            Key.Multiply => "Numpad *",
            Key.NumLock => "Num Lock",
            Key.NumPad0 => "Numpad 0",
            Key.NumPad1 => "Numpad 1",
            Key.NumPad2 => "Numpad 2",
            Key.NumPad3 => "Numpad 3",
            Key.NumPad4 => "Numpad 4",
            Key.NumPad5 => "Numpad 5",
            Key.NumPad6 => "Numpad 6",
            Key.NumPad7 => "Numpad 7",
            Key.NumPad8 => "Numpad 8",
            Key.NumPad9 => "Numpad 9",
            Key.OemBackslash => "/",
            Key.OemBackTab => "Back Tab",
            Key.OemClear => "Clear",
            Key.OemCloseBrackets => "]",
            Key.OemComma => ",",
            Key.OemCopy => "Copy",
            Key.OemFinish => "Finish",
            Key.OemMinus => "-",
            Key.OemOpenBrackets => "[",
            Key.OemPeriod => ".",
            Key.OemPipe => "|",
            Key.OemPlus => "+",
            Key.OemQuestion => "?",
            Key.OemQuotes => "\"",
            Key.OemSemicolon => ";",
            Key.OemTilde => "~",
            Key.PageDown => "Page Down",
            Key.PageUp => "Page Up",
            Key.Pause => "Pause",
            Key.Play => "Play",
            Key.Print => "Print",
            Key.PrintScreen => "Print Screen",
            Key.Right => "Arrow Right",
            Key.RightAlt => "Right Alt",
            Key.RightCtrl => "Right Ctrl",
            Key.RightShift => "Right Shift",
            Key.RWin => "Right Win",
            Key.Scroll => "Scroll Lock",
            Key.SelectMedia => "Select Media",
            Key.Subtract => "Numpad -",
            Key.Tab => "Tab",
            Key.Up => "Arrow Up",
            Key.VolumeDown => "Volume Down",
            Key.VolumeMute => "Volume Mute",
            Key.VolumeUp => "Volume Up",
            _ => key.ToString()
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
