using MultiFunPlayer.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiFunPlayer.Tests;

public class SplittingStringBufferTests
{
    public static IEnumerable<object[]> SeparatorInputsAndExpectedOutputs => [
        ['|', new string[] { "a|b|" }, new string[]{ "a", "b" }],
        ['|', new string[] { "a|b||" }, new string[]{ "a", "b", "" }],
        ['|', new string[] { "a||b" }, new string[]{ "a", "" }],
        ['|', new string[] { "|a|b" }, new string[]{ "", "a" }],
        ['|', new string[] { "||a|b" }, new string[]{ "", "", "a" }],
        ['|', new string[] { "|a|b|" }, new string[]{ "", "a", "b" }],
        ['|', new string[] { "||a||b||" }, new string[]{ "", "", "a", "", "b", "" }],
        ['|', new string[] { "a|bbbbbbbbbbbbbbbbbbbb" }, new string[] { "a" }],
        ['|', new string[] { "aaaaaaaaaaaaaaaaaaaa|b" }, new string[] { "aaaaaaaaaaaaaaaaaaaa" }],
        ['|', new string[] { "a|bbbbbbbbbbbbbbbbbbbb|" }, new string[] { "a", "bbbbbbbbbbbbbbbbbbbb" }],
        ['|', new string[] { "aaaaaaaaaaaaaaaaaaaa|b|" }, new string[] { "aaaaaaaaaaaaaaaaaaaa", "b" }],
        ['|', new string[] { "a|", "b|" }, new string[] { "a", "b" }],
        ['|', new string[] { "a||", "b|" }, new string[] { "a", "", "b" }],
        ['|', new string[] { "a", "|", "bbbbbbbbbbbbbbbbbbbb", "|" }, new string[] { "a", "bbbbbbbbbbbbbbbbbbbb" }],
        ['|', new string[] { "aaaaaaaaaaaaaaaaaaaa", "|", "b", "|" }, new string[] { "aaaaaaaaaaaaaaaaaaaa", "b" }],
        ['|', new string[] { "a", "|", "bbbbbbbbbbbbbbbbbbbb" }, new string[] { "a" }],
        ['|', new string[] { "aaaaaaaaaaaaaaaaaaaa", "|", "b" }, new string[] { "aaaaaaaaaaaaaaaaaaaa" }],
    ];

    [Theory]
    [MemberData(nameof(SeparatorInputsAndExpectedOutputs))]
    public void ConsumedInputsHaveExpectedOutputsWhenConsumed(char separator, string[] inputs, string[] expectedOutputs)
    {
        var buffer = new SplittingStringBuffer(separator);
        foreach(var input in inputs)
            buffer.Push(input);

        var outputs = buffer.Consume().ToArray();
        Assert.Equal(expectedOutputs, outputs);
    }
}
