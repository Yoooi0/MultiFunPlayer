using System;
using System.IO;
using System.Linq;
using System.Text;

namespace MultiFunPlayer.Common
{
    public class VideoFile
    {
        public string Source { get; init; }
        public string Name { get; init; }
    }

    public class VideoFileChangedMessage
    {
        public VideoFile VideoFile { get; }

        public VideoFileChangedMessage(string path)
        {
            if (TryParseUri(path, out var source, out var name) || TryParsePath(path, out source, out name))
                VideoFile = new VideoFile() { Source = source, Name = name };
        }

        private bool TryParseUri(string path, out string source, out string name)
        {
            source = name = null;

            try
            {
                var uri = new Uri(new Uri("file://"), path);
                if (!string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase))
                {
                    name = uri.Segments.Last();

                    var sb = new StringBuilder();
                    sb.Append(uri.Scheme).Append("://").Append(uri.Host);
                    if (uri.Port != 80)
                        sb.Append(':').Append(uri.Port);
                    sb.Append(string.Concat(uri.Segments.Take(uri.Segments.Length - 1)));
                    source = sb.ToString();

                    return true;
                }
            }
            catch { }

            return false;
        }

        private bool TryParsePath(string path, out string source, out string name)
        {
            source = name = null;

            try
            {
                name = Path.GetFileName(path);
                if (name == null)
                    return false;

                if (path.EndsWith(name))
                    source = path.Remove(path.Length - name.Length);

                return true;
            }
            catch { }

            return false;
        }
    }
}
