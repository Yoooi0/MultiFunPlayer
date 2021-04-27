using Microsoft.WindowsAPICodePack.Dialogs;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MultiFunPlayer.Common
{
    public enum ScriptFileOrigin
    {
        Automatic,
        User,
        Link
    }

    public enum ScriptDataType
    {
        Funscript
    }

    public interface IScriptFile
    {
        string Name { get; }
        public FileInfo Source { get; }
        ScriptFileOrigin Origin { get; }
        KeyframeCollection Keyframes { get; }
    }

    public class ScriptFile : IScriptFile
    {
        public string Name { get; }
        public FileInfo Source { get; }
        public ScriptFileOrigin Origin { get; }
        public KeyframeCollection Keyframes { get; }

        protected ScriptFile(string name, FileInfo source, byte[] data, ScriptDataType type, ScriptFileOrigin origin)
        {
            Name = name;
            Source = source;
            Origin = origin;
            Keyframes = type.Parse(data);
        }

        public static IScriptFile FromFileInfo(FileInfo file, bool userLoaded = false)
        {
            var path = file.FullName;
            if (!file.Exists)
                throw new FileNotFoundException("File not found!", path);

            var origin = userLoaded ? ScriptFileOrigin.User : ScriptFileOrigin.Automatic;
            return new ScriptFile(Path.GetFileName(path), file, File.ReadAllBytes(path), ScriptDataType.Funscript, origin);
        }

        public static IScriptFile FromPath(string path, bool userLoaded = false) => FromFileInfo(new FileInfo(path), userLoaded);

        public static IScriptFile FromZipArchiveEntry(string archivePath, ZipArchiveEntry entry, bool userLoaded = false)
        {
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);

            var origin = userLoaded ? ScriptFileOrigin.User : ScriptFileOrigin.Automatic;
            return new ScriptFile(entry.Name, new FileInfo(archivePath), memory.ToArray(), ScriptDataType.Funscript, origin);
        }
    }

    public class LinkedScriptFile : IScriptFile
    {
        private readonly IScriptFile _linked;

        public string Name => _linked.Name;
        public FileInfo Source => _linked.Source;
        public ScriptFileOrigin Origin => ScriptFileOrigin.Link;
        public KeyframeCollection Keyframes => _linked.Keyframes;

        protected LinkedScriptFile(IScriptFile linked)
        {
            _linked = linked;
        }

        public static IScriptFile LinkTo(IScriptFile other) => other != null ? new LinkedScriptFile(other) : null;
    }

    public static class ScriptDataTypeExtensions
    {
        public static KeyframeCollection Parse(this ScriptDataType type, byte[] data)
        {
            try
            {
                return type switch
                {
                    ScriptDataType.Funscript => ParseFunscript(Encoding.UTF8.GetString(data)),
                    _ => throw new NotSupportedException(),
                };
            }
            catch { }

            return null;
        }

        private static KeyframeCollection ParseFunscript(string data)
        {
            var document = JObject.Parse(data);

            var isRaw = document.TryGetValue("rawActions", out var actions) && (actions as JArray)?.Count != 0;
            if (!isRaw && (!document.TryGetValue("actions", out actions) || (actions as JArray)?.Count == 0))
                return null;

            var keyframes = new KeyframeCollection()
            {
                IsRawCollection = isRaw
            };

            foreach (var child in actions)
            {
                var position = child["at"].ToObject<long>() / 1000.0f;
                if (position < 0)
                    continue;

                var value = child["pos"].ToObject<float>() / 100;
                keyframes.Add(new Keyframe(position, value));
            }

            return keyframes;
        }
    }
}
