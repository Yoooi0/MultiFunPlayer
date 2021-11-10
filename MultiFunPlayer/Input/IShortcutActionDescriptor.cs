namespace MultiFunPlayer.Input;

public interface IShortcutActionDescriptor
{
    public string Name { get; }
}

public record ShortcutActionDescriptor(string Name) : IShortcutActionDescriptor
{
    public override string ToString() => $"[Name: {Name}]";
}
