using Microsoft.WindowsAPICodePack.Dialogs;
using Newtonsoft.Json;
using Stylet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource.Settings
{
    [JsonObject(MemberSerialization.OptIn)]
    public class MpvVideoSourceSettingsViewModel : Screen
    {
        [JsonProperty] public FileInfo Executable { get; set; } = null;

        public void OnLoad()
        {
            var dialog = new CommonOpenFileDialog()
            {
                EnsureFileExists = true
            };
            dialog.Filters.Add(new CommonFileDialogFilter("Executable files", "*.exe"));

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;

            if (!string.Equals(Path.GetFileNameWithoutExtension(dialog.FileName), "mpv", StringComparison.OrdinalIgnoreCase))
                return;

            Executable = new FileInfo(dialog.FileName);
        }

        public void OnClear() => Executable = null;

        public void OnDownload() { }
    }
}
