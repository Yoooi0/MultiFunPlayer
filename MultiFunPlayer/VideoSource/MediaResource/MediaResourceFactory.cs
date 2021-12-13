using MultiFunPlayer.Common;
using System.IO;
using System.Net;
using System.Text;

namespace MultiFunPlayer.VideoSource.MediaResource;

public interface IMediaResourceFactory
{
    ObservableConcurrentCollection<IMediaPathModifier> PathModifiers { get; }
    MediaResourceInfo CreateFromPath(string path);
}

public class MediaResourceFactory : IMediaResourceFactory
{
    public ObservableConcurrentCollection<IMediaPathModifier> PathModifiers { get; }

    public MediaResourceFactory()
    {
        PathModifiers = new ObservableConcurrentCollection<IMediaPathModifier>();
    }

    public MediaResourceInfo CreateFromPath(string path)
    {
        if (path == null)
            return null;

        var builder = new MediaResourceInfoBuilder();
        builder.WithOriginalPath(path);

        var modifier = PathModifiers.FirstOrDefault(m => m.Process(ref path));
        if (modifier != null)
            builder.AsModified(path);

        if (TryParseUri(path, builder) || TryParsePath(path, builder))
            return builder.Build();

        return null;
    }

    private bool TryParseUri(string path, IMediaResourceInfoBuilder builder)
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

            var name = uri.Segments.Last().Trim('/');
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            name = name.Trim('_');

            var sb = new StringBuilder();
            sb.Append(uri.Scheme).Append("://").Append(uri.Host);
            if (uri.Port != 80)
                sb.Append(':').Append(uri.Port);
            sb.Append(string.Concat(uri.Segments.Take(uri.Segments.Length - 1)));

            var source = sb.ToString();
            source = source.TrimEnd('\\', '/');

            builder.WithSourceAndName(source, name);
            builder.AsUrl(uri.IsLoopback || IsLocalHost(uri.Host));

            return true;
        }
        catch { }

        return false;
    }

    private bool TryParsePath(string path, IMediaResourceInfoBuilder builder)
    {
        var name = Path.GetFileName(path);
        if (name == null)
            return false;

        if (!path.EndsWith(name))
            return false;

        var source = path.Remove(path.Length - name.Length);
        source = source.TrimEnd('\\', '/');

        builder.WithSourceAndName(source, name);

        var root = Path.GetPathRoot(path);
        if (root.StartsWith(@"\\"))
        {
            builder.AsUnc();
        }
        else
        {
            var driveInfo = new DriveInfo(root);
            builder.AsPath(driveInfo.DriveType != DriveType.Network);
        }

        return true;
    }
}
