namespace MultiFunPlayer.Input;

public interface IShortcutActionDescriptor
{
    public string Name { get; }
}

public record ShortcutActionDescriptor(string Name) : IShortcutActionDescriptor
{
    public virtual bool Equals(ShortcutActionDescriptor other) => other != null && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    public bool Equals(IShortcutActionDescriptor other) => other is IShortcutActionDescriptor d && Equals(d);
    public override int GetHashCode() => Name.GetHashCode();

    public override string ToString() => $"[Name: {Name}]";
}
