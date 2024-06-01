using Newtonsoft.Json;
using System.ComponentModel;
using System.Web;

namespace MultiFunPlayer.MediaSource.MediaResource.Modifier.ViewModels;

[DisplayName("Decode")]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
internal sealed class DecodeMediaPathModifier : AbstractMediaPathModifier
{
    [JsonProperty] public DecodeType DecodeType { get; set; } = DecodeType.UriUnescapeDataString;

    public override string Process(string path)
    {
        if (path == null)
            return path;

        try
        {
            var decoded = DecodeType switch
            {
                DecodeType.UriUnescapeDataString => Uri.UnescapeDataString(path),
                DecodeType.HttpUtilityHtmlDecode => HttpUtility.HtmlDecode(path),
                DecodeType.HttpUtilityUrlDecode => HttpUtility.UrlDecode(path),
                _ => throw new NotImplementedException()
            };

            if (ReferenceEquals(decoded, path))
                return path;
            if (DecodeType == DecodeType.HttpUtilityUrlDecode)
                if (string.Equals(decoded, path, StringComparison.Ordinal))
                    return path;

            return decoded;
        }
        catch { }

        return path;
    }
}

internal enum DecodeType
{
    UriUnescapeDataString,
    HttpUtilityHtmlDecode,
    HttpUtilityUrlDecode
}