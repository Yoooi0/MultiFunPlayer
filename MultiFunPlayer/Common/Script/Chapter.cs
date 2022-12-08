using System.Diagnostics;

namespace MultiFunPlayer.Common;

[DebuggerDisplay("[{Name}: {StartPosition} -> {EndPosition}]")]
public record Chapter(string Name, TimeSpan StartPosition, TimeSpan EndPosition);