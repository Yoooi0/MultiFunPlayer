using MaterialDesignThemes.Wpf;
using MultiFunPlayer.Common;
using Newtonsoft.Json.Linq;
using Stylet;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace MultiFunPlayer.UI.Controls.ViewModels;

internal sealed class InformationViewModel : Screen
{
    private static readonly Uri GitHubApiBase = new("https://api.github.com/repos/Yoooi0/MultiFunPlayer/");

    public string VersionText => $"v{GitVersionInformation.InformationalVersion}";
    public UpdateData Update { get; private set; }

    public InformationViewModel()
    {
        _ = CheckForUpdate();
    }

    public void OnNavigate(string target)
    {
        if (!Uri.IsWellFormedUriString(target, UriKind.Absolute))
            return;

        Process.Start(new ProcessStartInfo()
        {
            FileName = target,
            UseShellExecute = true
        });
    }

    public void OnExternalNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        Process.Start(new ProcessStartInfo()
        {
            FileName = e.Uri.ToString(),
            UseShellExecute = true
        });
    }

    private async Task CheckForUpdate()
    {
        if (GitVersionInformation.UncommittedChanges != "0")
            return;

        using var client = NetUtils.CreateHttpClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Add("User-Agent", nameof(MultiFunPlayer));
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

        var rateLimit = await GitHubApiGet<JObject>(client, new Uri("https://api.github.com/rate_limit"));
        if (rateLimit["resources"]["core"]["remaining"].ToObject<int>() < 5)
            return;

        var branchIsMaster = GitVersionInformation.BranchName == "master";
        var branchIsTag = GitVersionInformation.BranchName.StartsWith("tags/");
        if (branchIsMaster || branchIsTag)
        {
            if (!Version.TryParse(GitVersionInformation.MajorMinorPatch, out var currentVersion))
                return;

            var tagsUri = new Uri(GitHubApiBase, "git/matching-refs/tags");
            var tags = await GitHubApiGet<JArray>(client, tagsUri);
            var versions = tags.OfType<JObject>()
                               .Select(o => Regex.Match(o["ref"].ToString(), @"^refs\/tags\/(?<version>\d+\.\d+\.\d+)$"))
                               .Select(m => m.Success && Version.TryParse(m.Groups["version"].ToString(), out var v) ? v : null)
                               .NotNull()
                               .Where(v => v > currentVersion || (branchIsMaster && v == currentVersion))
                               .GroupBy(v => $"{v.Major}.{v.Minor}")
                               .Select(g => g.Max())
                               .Order()
                               .ToList();

            if (versions.Count == 0)
                return;

            var releasesUri = new Uri(GitHubApiBase, "releases");
            var releases = await GitHubApiGet<JArray>(client, releasesUri);
            var releasesMap = new Dictionary<Version, JObject>();
            foreach(var release in releases.OfType<JObject>())
            {
                if (!Version.TryParse(release["tag_name"].ToString(), out var releaseVersion))
                    continue;
                releasesMap.Add(releaseVersion, release);
            }

            var latestRelease = releasesMap[versions[^1]];
            var updateLabel = default(Span);
            var updateContent = default(Span);
            var updateUri = new Uri(latestRelease["html_url"].ToString());

            await Execute.OnUIThreadAsync(() =>
            {
                updateContent = new Span() { BaselineAlignment = BaselineAlignment.Center };
                foreach (var version in versions)
                {
                    var release = releasesMap[version];
                    var releaseUri = new Uri(release["html_url"].ToString());
                    var releaseContent = new Span() { BaselineAlignment = BaselineAlignment.Center };

                    releaseContent.Inlines.Add(new Run($"{release["published_at"].ToObject<DateTime>():yyyy/MM/dd}: "));
                    releaseContent.Inlines.Add(CreateHyperlink(release["tag_name"].ToString(), releaseUri));
                    releaseContent.Inlines.Add(new LineBreak());

                    updateContent.Inlines.Add(releaseContent);
                }

                var fromText = $"v{GitVersionInformation.MajorMinorPatch}";
                var toText = $"v{latestRelease["tag_name"]}";
                if (branchIsMaster)
                    fromText += $".{GitVersionInformation.ShortSha}";

                updateLabel = CreateUpdateLabel(fromText, toText, updateUri);
            });

            Update = new UpdateData(updateUri, updateLabel, updateContent);
        }
        else
        {
            var commitSha = GitVersionInformation.Sha;

            var runsUri = new Uri(GitHubApiBase, $"actions/runs?per_page=1&branch={GitVersionInformation.EscapedBranchName}&status=success");
            var runs = await GitHubApiGet<JObject>(client, runsUri);
            if (runs["total_count"].ToObject<int>() == 0)
                return;

            var latestRun = runs["workflow_runs"][0];
            var runSha = latestRun["head_sha"].ToString();
            if (runSha == commitSha)
                return;

            var compareUri = new Uri(GitHubApiBase, $"compare/{commitSha}...{runSha}");
            var compare = await GitHubApiGet<JObject>(client, compareUri);
            if (compare["status"].ToString() != "ahead")
                return;

            var updateLabel = default(Span);
            var updateContent = default(Span);
            var updateUri = new Uri(latestRun["html_url"].ToString().Replace("github.com", "nightly.link"));

            await Execute.OnUIThreadAsync(() =>
            {
                updateContent = new Span() { BaselineAlignment = BaselineAlignment.Center };
                foreach (var commit in compare["commits"].OfType<JObject>())
                {
                    var commitMessage = commit["commit"]["message"].ToString().Trim();
                    if (string.Equals(commitMessage, "cleanup", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var commitContent = new Span() { BaselineAlignment = BaselineAlignment.Center };

                    commitContent.Inlines.Add(new Run($"{commit["commit"]["author"]["date"].ToObject<DateTime>():yyyy/MM/dd} ("));
                    commitContent.Inlines.Add(CreateHyperlink(commit["sha"].ToString()[..7], new Uri(commit["html_url"].ToString())));
                    commitContent.Inlines.Add(new Run("): "));
                    commitContent.Inlines.Add(new Run(commitMessage));
                    commitContent.Inlines.Add(new LineBreak());

                    updateContent.Inlines.Add(commitContent);
                }

                updateLabel = CreateUpdateLabel(GitVersionInformation.ShortSha, runSha[..7], updateUri);
            });

            Update = new UpdateData(updateUri, updateLabel, updateContent);
        }

        Hyperlink CreateHyperlink(string text, Uri navigateUri)
        {
            var hyperlink = new Hyperlink(new Run(text)) { NavigateUri = navigateUri };
            hyperlink.RequestNavigate += OnExternalNavigate;
            return hyperlink;
        }

        Span CreateUpdateLabel(string fromText, string toText, Uri navigateUri)
        {
            var span = new Span() { BaselineAlignment = BaselineAlignment.Center };
            span.Inlines.Add(new Run("Update: ") { FontWeight = FontWeights.Bold });
            span.Inlines.Add(new Run(fromText));
            span.Inlines.Add(new InlineUIContainer(new PackIcon() { Kind = PackIconKind.ArrowRightThin }));
            span.Inlines.Add(CreateHyperlink(toText, navigateUri));
            return span;
        }

        static async Task<T> GitHubApiGet<T>(HttpClient client, Uri uri) where T : JToken
        {
            var response = await client.GetAsync(uri);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            return (T)JToken.Parse(content);
        }
    }

    public sealed record class UpdateData(Uri Uri, Inline Label, Inline Content);
}
