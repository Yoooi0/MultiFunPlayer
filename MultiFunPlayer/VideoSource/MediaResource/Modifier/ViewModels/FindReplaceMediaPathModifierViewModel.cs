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
        private Regex _compiledRegex;

        public string Name => "Find/Replace";

        [DependsOn(nameof(Find))]
        public string Description => Find?.Length > 50 ? $"{Find[..50]}..." : Find;

        [JsonProperty] public string Find { get; set; }
        [JsonProperty] public string Replace { get; set; }
        [JsonProperty] public bool MatchCase { get; set; } = true;
        [JsonProperty] public bool UseRegularExpressions { get; set; }

        private void OnFindChanged()
        {
            if(UseRegularExpressions)
                CompileRegex();
        }

        private void OnMatchCaseChanged()
        {
            if (UseRegularExpressions)
                CompileRegex();
        }

        private void OnUseRegularExpressionsChanged()
        {
            if (UseRegularExpressions)
                CompileRegex();
            else
                _compiledRegex = null;
        }

        private void CompileRegex() 
        {
            var flags = RegexOptions.Compiled;

            if (!MatchCase)
                flags |= RegexOptions.IgnoreCase;

            _compiledRegex = new Regex(Find, flags);
        }

        public bool Process(ref string path)
        {
            try
            {
                if (UseRegularExpressions)
                {
                    if (_compiledRegex == null)
                        CompileRegex();

                    var replaced = _compiledRegex.Replace(path, Replace);
                    if (ReferenceEquals(replaced, path))
                        return false;

                    path = replaced;
                }
                else
                {
                    var replaced = path.Replace(Find, Replace, MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
                    if (!string.Equals(path, Replace, StringComparison.Ordinal))
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
