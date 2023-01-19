using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace MultiFunPlayer.UI.Converters;

public class FileSystemInfoToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is FileSystemInfo info ? info.FullName : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path)
            return null;

        if (File.Exists(path))
            return new FileInfo(path);

        if (Directory.Exists(path))
            return new DirectoryInfo(path);

        return null;
    }
}
