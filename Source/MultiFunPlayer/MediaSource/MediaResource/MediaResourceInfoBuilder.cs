using MultiFunPlayer.MediaSource.MediaResource.Modifier;
using System.IO;

namespace MultiFunPlayer.MediaSource.MediaResource;

internal sealed class MediaResourceInfoBuilder(string originalPath)
{
    private string _modifiedPath;

    public MediaResourceInfo Build()
    {
        var path = _modifiedPath ?? originalPath;
        if (path == null)
            return null;

        if (TryParseUri(path, out var result) || TryParsePath(path, out result))
            return result;

        return null;
    }

    private MediaResourceInfo Build(MediaResourcePathType pathType, string name, string source)
        => new(pathType, _modifiedPath, originalPath, source, name);

    private bool TryParseUri(string path, out MediaResourceInfo result)
    {
        result = null;

        try
        {
            if (!Uri.TryCreate(path, UriKind.RelativeOrAbsolute, out var uri))
                return false;

            if (!uri.IsAbsoluteUri)
                return false;
            if (uri.IsFile)
                return TryParsePath(uri.LocalPath, out result);

            var name = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(name))
                return true;

            var source = $"{uri.Scheme}://{uri.Host}{(uri.Port != 80 ? $":{uri.Port}" : "")}{uri.LocalPath[..^name.Length].TrimEnd('\\', '/')}";
            result = Build(MediaResourcePathType.Url, name, source);
            return true;
        }
        catch { }

        return false;
    }

    private bool TryParsePath(string path, out MediaResourceInfo result)
    {
        result = null;
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (!path.EndsWith(name))
            return false;

        if (!Path.IsPathFullyQualified(path))
        {
            var absolutePath = Path.Join(Environment.CurrentDirectory, path);
            if (File.Exists(absolutePath))
                path = absolutePath;
        }

        var source = path.Remove(path.Length - name.Length);
        if (Path.EndsInDirectorySeparator(source))
            if (Path.GetPathRoot(source) != source)
                source = source.TrimEnd('\\', '/');

        result = Build(MediaResourcePathType.File, name, source);
        return true;
    }

    public void WithModifiers(IEnumerable<IMediaPathModifier> mediaPathModifiers)
    {
        if (originalPath == null)
            return;

        var modifiedPath = originalPath;
        var modifier = mediaPathModifiers.FirstOrDefault(m => m.Process(ref modifiedPath));
        if (modifier != null)
            _modifiedPath = modifiedPath;
    }
}
