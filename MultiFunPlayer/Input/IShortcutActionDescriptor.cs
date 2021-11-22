namespace MultiFunPlayer.Input;

[Flags]
public enum ShortcutActionDescriptorFlags
{
    AcceptsSimpleGesture = 1 << 1,
    AcceptsAxisGesture = 1 << 2,
    All = AcceptsSimpleGesture | AcceptsAxisGesture
}

public interface IShortcutActionDescriptor
{
    public string Name { get; }
    public ShortcutActionDescriptorFlags Flags { get; }

    public bool AcceptsSimpleGesture => Flags.HasFlag(ShortcutActionDescriptorFlags.AcceptsSimpleGesture);
    public bool AcceptsAxisGesture => Flags.HasFlag(ShortcutActionDescriptorFlags.AcceptsAxisGesture);
}

public record ShortcutActionDescriptor(string Name, ShortcutActionDescriptorFlags Flags) : IShortcutActionDescriptor
{
    public ShortcutActionDescriptor(string name) : this(name, ShortcutActionDescriptorFlags.All) { }

    public virtual bool Equals(ShortcutActionDescriptor other) => other != null && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    public bool Equals(IShortcutActionDescriptor other) => other is IShortcutActionDescriptor d && Equals(d);
    public override int GetHashCode() => Name.GetHashCode();

    public override string ToString() => $"[Name: {Name}]";
}
