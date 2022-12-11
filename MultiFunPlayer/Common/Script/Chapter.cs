using System.Diagnostics;

namespace MultiFunPlayer.Common;

[DebuggerDisplay("[{Name}: {StartPosition}s -> {EndPosition}s]")]
public record Chapter(string Name, double StartPosition, double EndPosition);