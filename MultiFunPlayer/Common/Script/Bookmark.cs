using System.Diagnostics;

namespace MultiFunPlayer.Common;

[DebuggerDisplay("[{Name}: {Position}s]")]
public record Bookmark(string Name, double Position);