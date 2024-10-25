using MultiFunPlayer.Common;

namespace MultiFunPlayer.Tests;

public class SplittingStringBufferTests
{
    public static IEnumerable<object[]> SeparatorInputAndExpectedOutputs => [
        ['|', "a|b|", new[] { "a", "b" }],
        ['|', "a|b||", new[] { "a", "b", "" }],
        ['|', "a||b", new[] { "a", "" }],
        ['|', "|a|b", new[] { "", "a" }],
        ['|', "||a|b", new[] { "", "", "a" }],
        ['|', "|a|b|", new[] { "", "a", "b" }],
        ['|', "||a||b||", new[] { "", "", "a", "", "b", "" }],
        ['|', "a|bbbbbbbbbbbbbbbbbbbb", new[] { "a" }],
        ['|', "aaaaaaaaaaaaaaaaaaaa|b", new[] { "aaaaaaaaaaaaaaaaaaaa" }],
        ['|', "a|bbbbbbbbbbbbbbbbbbbb|", new[] { "a", "bbbbbbbbbbbbbbbbbbbb" }],
        ['|', "aaaaaaaaaaaaaaaaaaaa|b|", new[] { "aaaaaaaaaaaaaaaaaaaa", "b" }],
    ];

    [Theory]
    [MemberData(nameof(SeparatorInputAndExpectedOutputs))]
    public void InputHasExpectedOutputsWhenConsumed(char separator, string input, string[] expectedOutputs)
    {
        var buffer = new SplittingStringBuffer(separator);
        buffer.Push(input);

        var outputs = buffer.Consume().ToArray();
        Assert.Equal(expectedOutputs, outputs);
    }

    public static IEnumerable<object[]> SeparatorInputsAndExpectedOutputs => [
        ['|', new[] { "a|", "b|" }, new string[][] { ["a"] , ["b"] }],
        ['|', new[] { "a||", "b|" }, new string[][] { ["a", ""], ["b"] }],
        ['|', new[] { "a", "|", "bbbbbbbbbbbbbbbbbbbb", "|" }, new string[][] { [], ["a"], [], ["bbbbbbbbbbbbbbbbbbbb"] }],
        ['|', new[] { "aaaaaaaaaaaaaaaaaaaa", "|", "b", "|" }, new string[][] { [], ["aaaaaaaaaaaaaaaaaaaa"], [], ["b"] }],
        ['|', new[] { "a", "|", "bbbbbbbbbbbbbbbbbbbb" }, new string[][] { [], ["a"], [] }],
        ['|', new[] { "aaaaaaaaaaaaaaaaaaaa", "|", "b" }, new string[][] { [], ["aaaaaaaaaaaaaaaaaaaa"], [] }],
    ];

    [Theory]
    [MemberData(nameof(SeparatorInputsAndExpectedOutputs))]
    public void InputsHaveExpectedOutputWhenEachConsumed(char separator, string[] inputs, string[][] expectedOutputsPerInput)
    {
        var buffer = new SplittingStringBuffer(separator);

        var expectedOuputsEnumerator = expectedOutputsPerInput.GetEnumerator();
        foreach (var input in inputs)
        {
            buffer.Push(input);

            expectedOuputsEnumerator.MoveNext();
            var expectedOutputs = expectedOuputsEnumerator.Current;

            var outputs = buffer.Consume().ToArray();
            Assert.Equal(expectedOutputs, outputs);
        }
    }
}
