namespace MultiFunPlayer.Common;

[AttributeUsage(AttributeTargets.Class)]
internal class DisplayIndexAttribute(int index) : Attribute
{
    public int Index { get; } = index;
}