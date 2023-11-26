using MultiFunPlayer.MediaSource.MediaResource.Modifier;
using System.IO;
using System.Net;
using System.Text;

namespace MultiFunPlayer.MediaSource.MediaResource;

internal sealed class MediaResourceInfo
{
    public bool IsPath { get; init; }
    public bool IsUrl { get; init; }

    public bool Local { get; init; }
    public bool Remote => !Local;

    public bool IsModified { get; init; }
    public string ModifiedPath { get; init; }

    public string OriginalPath { get; init; }
    public string Source { get; init; }
    public string Name { get; init; }

    public static MediaResourceInfo CreateFromPath(string path, IEnumerable<IMediaPathModifier> modifiers)
    {
        if (path == null)
            return null;

        var builder = new MediaResourceInfoBuilder();
        builder.WithOriginalPath(path);

        var modifier = modifiers.FirstOrDefault(m => m.Process(ref path));
        if (modifier != null)
            builder.AsModified(path);

        if (TryParseUri(path, builder) || TryParsePath(path, builder))
            return builder.Build();

        return null;

        static bool TryParseUri(string path, IMediaResourceInfoBuilder builder)
        {
            static bool IsLocalHost(string host)
            {
                var remote = Dns.GetHostAddresses(host);
                var local = Dns.GetHostAddresses(Dns.GetHostName());

                return remote.Any(r => IPAddress.IsLoopback(r) || local.Any(l => l.Equals(r)));
            }

            try
            {
                var uri = new Uri(new Uri("file://"), path);
                if (string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase))
                    return false;

                var name = uri.Segments[^1].Trim('/');
                foreach (var c in Path.GetInvalidFileNameChars())
                    name = name.Replace(c, '_');

                name = name.Trim('_');

                var sb = new StringBuilder();
                sb.Append(uri.Scheme).Append("://").Append(uri.Host);
                if (uri.Port != 80)
                    sb.Append(':').Append(uri.Port);
                sb.Append(string.Concat(uri.Segments.Take(uri.Segments.Length - 1)));

                if (uri.IsLoopback || IsLocalHost(uri.Host))
                    builder.AsLocal();

                var source = sb.ToString().TrimEnd('\\', '/');
                builder.WithSourceAndName(source, name)
                        .AsUrl();

                return true;
            }
            catch { }

            return false;
        }

        static bool TryParsePath(string path, IMediaResourceInfoBuilder builder)
        {
            var name = Path.GetFileName(path);
            if (name == null)
                return false;

            if (!path.EndsWith(name))
                return false;

            if (Path.IsPathFullyQualified(path))
            {
                if (File.Exists(path))
                    builder.AsLocal();
            }
            else
            {
                var absolutePath = Path.Join(Environment.CurrentDirectory, path);
                if (File.Exists(absolutePath))
                {
                    builder.AsLocal();
                    path = absolutePath;
                }
            }

            var source = path.Remove(path.Length - name.Length).TrimEnd('\\', '/');
            builder.WithSourceAndName(source, name)
                    .AsPath();

            return true;
        }
    }
}
