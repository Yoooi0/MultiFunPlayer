using System.IO;

namespace MultiFunPlayer.Common;

public static class IOUtils
{
    public static EnumerationOptions CreateEnumerationOptions(bool recurseSubdirectories = false)
        => new() { MatchType = MatchType.Win32, RecurseSubdirectories = recurseSubdirectories };
}
