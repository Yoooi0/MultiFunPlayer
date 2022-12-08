namespace MultiFunPlayer.Common;

public interface IScriptResource
{
    string Name { get; }
    string Source { get; }
    KeyframeCollection Keyframes { get; }
}

public class ScriptResource : IScriptResource
{
    public string Name { get; init; }
    public string Source { get; init; }
    public KeyframeCollection Keyframes { get; init; }

    public static LinkedScriptResource LinkTo(IScriptResource other) => other != null ? new LinkedScriptResource(other) : null;
}

public class LinkedScriptResource : IScriptResource
{
    private readonly IScriptResource _linked;

    public string Name => _linked.Name;
    public string Source => _linked.Source;
    public KeyframeCollection Keyframes => _linked.Keyframes;

    public LinkedScriptResource(IScriptResource linked)
    {
        _linked = linked;
    }
}