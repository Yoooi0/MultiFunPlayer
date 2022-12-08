using System.Diagnostics;

namespace MultiFunPlayer.Common;

[DebuggerDisplay("[{Name}: {Position}]")]
public record Bookmark(string Name, TimeSpan Position);