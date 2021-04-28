using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.Common
{
    public class VideoFileInfo
    {
        public string Source { get; }
        public string Name { get; }

        public VideoFileInfo(string source, string name)
        {
            Source = source;
            Name = name;
        }
    }

    public class VideoFileChangedMessage
    {
        public VideoFileInfo VideoFile { get; }

        public VideoFileChangedMessage(string path)
        {
            if(path == null)
                return;

            if (TryParseUri(path, out var source, out var name) || TryParsePath(path, out source, out name))
                VideoFile = new VideoFileInfo(source, name);
        }

        private bool TryParseUri(string path, out string source, out string name)
        {
            source = name = null;

            try
            {
                var postprocess = new Dictionary<string, string>()
                {
                    {@"(.*pornhub.com\/)view_video.php\?viewkey=(.+)", "$1$2"},
                };

                foreach (var (pattern, replacement) in postprocess)
                {
                    var replaced = Regex.Replace(path, pattern, replacement);
                    if (replaced != path)
                        path = replaced;
                }

                var uri = new Uri(new Uri("file://"), path);
                if (!string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase))
                {
                    name = uri.Segments.Last()
                                       .TrimEnd('/')
                                       .Replace(".html", null)
                                       .Replace(".php", null);

                    foreach (var c in Path.GetInvalidFileNameChars())
                        name = name.Replace(c, '_');

                    name = name.Trim('_');

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
                {
                    source = path.Remove(path.Length - name.Length);
                    source = source.TrimEnd('\\', '/');
                }

                return true;
            }
            catch { }

            return false;
        }
    }
}
