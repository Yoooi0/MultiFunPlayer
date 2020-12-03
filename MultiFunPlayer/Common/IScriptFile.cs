using System.IO;
using System.IO.Compression;

namespace MultiFunPlayer.Common
{
    public interface IScriptFile
    {
        string Name { get; }
        string Data { get; }
    }

    public class ScriptFile : IScriptFile
    {
        protected ScriptFile(string name, string data)
        {
            Name = name;
            Data = data;
        }

        public string Name { get; }
        public string Data { get; }

        public static ScriptFile FromPath(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("File not found!", path);

            return new ScriptFile(Path.GetFileName(path), File.ReadAllText(path));
        }
        public static ScriptFile FromFileInfo(FileInfo file) => FromPath(file.FullName);
        public static ScriptFile FromZipArchiveEntry(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            return new ScriptFile(entry.Name, reader.ReadToEnd());
        }
    }
}
