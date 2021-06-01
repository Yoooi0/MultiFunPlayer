namespace MultiFunPlayer.Common.Input
{
    public enum ShortcutActionType
    {
        Simple,
        Axis
    }

    public record ShortcutActionDescriptor(string Name, ShortcutActionType Type)
    {
        public override string ToString() => $"[Name: {Name}, Type: {Type}]";
    }
}
