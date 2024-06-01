using Newtonsoft.Json;
using PropertyChanged;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.MediaSource.MediaResource.Modifier.ViewModels;

[DisplayName("Find/Replace")]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
internal sealed class FindReplaceMediaPathModifier : AbstractMediaPathModifier
{
    [JsonProperty] public string Find { get; set; } = string.Empty;
    [JsonProperty] public string Replace { get; set; } = string.Empty;
    [JsonProperty] public bool MatchCase { get; set; } = true;
    [JsonProperty] public bool UseRegularExpressions { get; set; } = false;

    public override string Process(string path)
    {
        if (path == null)
            return path;

        try
        {
            var replaced = UseRegularExpressions switch
            {
                true when Find != null && Replace != null => Regex.Replace(path, Find, Replace, MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase),
                false when !string.IsNullOrEmpty(Find) => path.Replace(Find, Replace, MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase),
                _ => path
            };

            if (ReferenceEquals(replaced, path))
                return path;

            return replaced;
        }
        catch { }

        return path;
    }
}
