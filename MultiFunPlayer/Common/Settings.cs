using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MultiFunPlayer.Common
{
    public static class Settings
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static string FilePath => Path.Join(FileDirectory, FileName);
        public static string FileDirectory => Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
        public static string FileName => $"{nameof(MultiFunPlayer)}.config.json";

        public static JObject Read()
        {
            if (!File.Exists(FilePath))
                return new JObject();

            Logger.Info("Reading settings from \"{0}\"", FilePath);
            try
            {
                return JObject.Parse(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to read settings");
                return new JObject();
            }
        }

        public static void Write(JObject settings)
        {
            try
            {
                Logger.Info("Saving settings to \"{0}\"", FilePath);
                File.WriteAllText(FilePath, settings.ToString());
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to save settings");
            }
        }
    }
}
