using System.IO;
using System.IO.Compression;

namespace MultiFunPlayer.Common
{
    public interface IScriptFile
    {
        string Name { get; }
        public FileInfo Source { get; }
        string Data { get; }
    }

    public class ScriptFile : IScriptFile
    {
        protected ScriptFile(string name, FileInfo source, string data)
        {
            Name = name;
            Source = source;
            Data = data;
        }

        public string Name { get; }
        public FileInfo Source { get; }
        public string Data { get; }

        public static ScriptFile FromFileInfo(FileInfo file)
        {
            var path = file.FullName;
            if (!file.Exists)
                throw new FileNotFoundException("File not found!", path);

            return new ScriptFile(Path.GetFileName(path), file, File.ReadAllText(path));
        }

        public static ScriptFile FromPath(string path) => FromFileInfo(new FileInfo(path));        

        public static ScriptFile FromZipArchiveEntry(string archivePath, ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            return new ScriptFile(entry.Name, new FileInfo(archivePath), reader.ReadToEnd());
        }
    }
}
