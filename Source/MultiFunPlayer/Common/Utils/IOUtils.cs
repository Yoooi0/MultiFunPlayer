using System.IO;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace MultiFunPlayer.Common;
#pragma warning restore IDE0130 // Namespace does not match folder structure

public static class IOUtils
{
    public static EnumerationOptions CreateEnumerationOptions(bool recurseSubdirectories = false)
        => new() { MatchType = MatchType.Win32, RecurseSubdirectories = recurseSubdirectories };
}
