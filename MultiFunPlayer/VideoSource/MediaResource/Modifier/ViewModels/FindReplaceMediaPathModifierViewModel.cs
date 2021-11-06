using Newtonsoft.Json;
using PropertyChanged;
using Stylet;
using System;
using System.Text.RegularExpressions;

namespace MultiFunPlayer.VideoSource.MediaResource.Modifier.ViewModels
{
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class FindReplaceMediaPathModifierViewModel : PropertyChangedBase, IMediaPathModifier
    {
        public string Name => "Find/Replace";

        [DependsOn(nameof(Find))]
        public string Description => Find?.Length > 50 ? $"{Find[..50]}..." : Find;

        [JsonProperty] public string Find { get; set; }
        [JsonProperty] public string Replace { get; set; }
        [JsonProperty] public bool MatchCase { get; set; } = true;
        [JsonProperty] public bool UseRegularExpressions { get; set; }

        public bool Process(ref string path)
        {
            try
            {
                if (UseRegularExpressions)
                {
                    var replaced = Regex.Replace(path, Find, Replace, MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
                    if (ReferenceEquals(replaced, path))
                        return false;

                    path = replaced;
                }
                else
                {
                    var replaced = path.Replace(Find, Replace, MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
                    if (string.Equals(replaced, path, StringComparison.Ordinal))
                        return false;

                    path = replaced;
                }

                return true;
            }
            catch { }

            return false;
        }
    }
}
