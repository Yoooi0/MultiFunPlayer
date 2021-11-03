using MultiFunPlayer.VideoSource.MediaResource.Modifier.ViewModels;
using Stylet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace MultiFunPlayer.VideoSource.MediaResource
{
    public class MediaResourceFactory : IMediaResourceFactory
    {
        public BindableCollection<IMediaPathModifier> PathModifiers { get; }

        public MediaResourceFactory()
        {
            PathModifiers = new BindableCollection<IMediaPathModifier>();
        }

        public MediaResourceInfo CreateFromPath(string path)
        {
            if (path == null)
                return null;

            _ = PathModifiers.FirstOrDefault(m => m.Process(ref path));

            if (TryParseUri(path, out var source, out var name))
                return new MediaResourceInfo(source, name);

            if (TryParsePath(path, out source, out name))
                return new MediaResourceInfo(source, name);

            return null;
        }

        private bool TryParseUri(string path, out string source, out string name)
        {
            source = name = null;

            try
            {
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
