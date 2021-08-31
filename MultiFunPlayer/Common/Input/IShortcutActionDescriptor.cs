namespace MultiFunPlayer.Common.Input
{
    public interface IShortcutActionDescriptor
    {
        public string Name { get; }
    }

    public interface ISimpleShortcutActionDescriptor : IShortcutActionDescriptor { }
    public interface IAxisShortcutActionDescriptor : IShortcutActionDescriptor { }

    public record SimpleShortcutActionDescriptor(string Name) : ISimpleShortcutActionDescriptor
    {
        public override string ToString() => $"[Name: {Name}]";
    }

    public record AxisShortcutActionDescriptor(string Name) : IAxisShortcutActionDescriptor
    {
        public override string ToString() => $"[Name: {Name}]";
    }
}
