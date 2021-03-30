using System;
using System.IO;
using System.IO.Compression;

namespace MultiFunPlayer.Common
{
    public enum ScriptFileOrigin
    {
        Automatic,
        User,
        Link
    }

    public interface IScriptFile
    {
        string Name { get; }
        public FileInfo Source { get; }
        string Data { get; }
        ScriptFileOrigin Origin { get; }
    }

    public class ScriptFile : IScriptFile
    {
        public string Name { get; }
        public FileInfo Source { get; }
        public string Data { get; }
        public ScriptFileOrigin Origin { get; }

        protected ScriptFile(string name, FileInfo source, string data, ScriptFileOrigin origin)
        {
            Name = name;
            Source = source;
            Data = data;
            Origin = origin;
        }

        public static IScriptFile FromFileInfo(FileInfo file, bool userLoaded = false)
        {
            var path = file.FullName;
            if (!file.Exists)
                throw new FileNotFoundException("File not found!", path);

            var origin = userLoaded ? ScriptFileOrigin.User : ScriptFileOrigin.Automatic;
            return new ScriptFile(Path.GetFileName(path), file, File.ReadAllText(path), origin);
        }

        public static IScriptFile FromPath(string path, bool userLoaded = false) => FromFileInfo(new FileInfo(path), userLoaded);

        public static IScriptFile FromZipArchiveEntry(string archivePath, ZipArchiveEntry entry, bool userLoaded = false)
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);

            var origin = userLoaded ? ScriptFileOrigin.User : ScriptFileOrigin.Automatic;
            return new ScriptFile(entry.Name, new FileInfo(archivePath), reader.ReadToEnd(), origin);
        }
    }

    public class LinkedScriptFile : IScriptFile
    {
        private readonly IScriptFile _linked;

        public string Name => _linked.Name;
        public FileInfo Source => _linked.Source;
        public string Data => _linked.Data;
        public ScriptFileOrigin Origin => ScriptFileOrigin.Link;

        protected LinkedScriptFile(IScriptFile linked)
        {
            _linked = linked;
        }

        public static IScriptFile LinkTo(IScriptFile other) => other != null ? new LinkedScriptFile(other) : null;
    }
}
