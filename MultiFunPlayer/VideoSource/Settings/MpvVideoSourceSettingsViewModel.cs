using Microsoft.WindowsAPICodePack.Dialogs;
using Newtonsoft.Json;
using Stylet;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;

namespace MultiFunPlayer.VideoSource.Settings
{
    [JsonObject(MemberSerialization.OptIn)]
    public class MpvVideoSourceSettingsViewModel : Screen
    {
        [JsonProperty] public FileInfo Executable { get; set; } = null;
        [JsonProperty] public string Arguments { get; set; } = "--keep-open=always --pause";

        public bool IsDownloading { get; set; } = false;

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

        public async void OnDownload()
        {
            IsDownloading = true;
            Executable = null;

            try
            {
                var downloadRoot = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "bin", "mpv"));
                if (downloadRoot.Exists)
                    downloadRoot.Delete(true);

                downloadRoot = Directory.CreateDirectory(downloadRoot.FullName);
                var bootstrapperUri = new Uri("https://sourceforge.net/projects/mpv-player-windows/files/bootstrapper.zip/download");
                var bootstrapperZip = new FileInfo(Path.Combine(downloadRoot.FullName, "bootstrapper.zip"));

                {
                    using var client = new WebClient();
                    await client.DownloadFileTaskAsync(bootstrapperUri, bootstrapperZip.FullName).ConfigureAwait(false);
                }

                ZipFile.ExtractToDirectory(bootstrapperZip.FullName, downloadRoot.FullName, true);
                bootstrapperZip.Delete();

                var updater = new FileInfo(Path.Combine(downloadRoot.FullName, "updater.bat"));
                using var process = new Process
                {
                    StartInfo =
                    {
                        FileName = updater.FullName,
                        UseShellExecute = true
                    },
                    EnableRaisingEvents = true
                };

                var completionSource = new TaskCompletionSource<int>();
                process.Exited += (s, e) => completionSource.SetResult(process.ExitCode);
                process.Start();

                var result = await completionSource.Task.ConfigureAwait(false);
                if (result == 0)
                    Executable = new FileInfo(Path.Combine(downloadRoot.FullName, "mpv.exe"));

                foreach (var file in downloadRoot.EnumerateFiles("mpv*.7z"))
                    file.Delete();

                if (!Executable.Exists)
                    Executable = null;
            }
            catch { }

            IsDownloading = false;
        }
    }
}
