namespace ILMask.Tests;

public sealed class IlMaskTests
{
    [Fact]
    public void TryCreate_KeepsPatternWithWildcards()
    {
        IlMask? mask = null;

        Assert.True(IlMask.TryCreate([0x02, null, 0x2A], out mask));
        Assert.Equal([0x02, null, 0x2A], mask.Pattern);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[0])]
    public void TryCreate_RejectsNullOrEmptyPattern_WithReason(byte[]? pattern)
    {
        IlMask? mask = null;
        string? error = null;

        Assert.False(IlMask.TryCreate(pattern?.Select(b => (byte?)b), out mask, out error));
        Assert.Null(mask);
        Assert.Equal("A mask needs at least one byte.", error);
    }

    [Fact]
    public void TryCreate_WithoutErrorOverload_RejectsEmptyPattern()
    {
        IlMask? mask = null;

        Assert.False(IlMask.TryCreate([], out mask));
    }

    [Theory]
    [InlineData("02 7B ?? ?? ?? ?? 2A", "02 7B ?? ?? ?? ?? 2A")]
    [InlineData("  02\t7b ?  2a\n", "02 7B ?? 2A")]
    [InlineData("??", "??")]
    public void TryParse_ReadsBytesAndWildcards(string text, string expected)
    {
        IlMask mask = Parse(text);

        Assert.Equal(expected, mask.ToString());
    }

    [Theory]
    [InlineData(null, "A mask needs at least one byte.")]
    [InlineData("", "A mask needs at least one byte.")]
    [InlineData("   ", "A mask needs at least one byte.")]
    [InlineData("0", "Invalid mask byte '0' at position 0")]
    [InlineData("123", "Invalid mask byte '123' at position 0")]
    [InlineData("GG", "Invalid mask byte 'GG' at position 0")]
    [InlineData("02 ???", "Invalid mask byte '???' at position 1")]
    [InlineData("+1", "Invalid mask byte '+1' at position 0")]
    public void TryParse_RejectsInvalidText_WithReason(string? text, string reason)
    {
        IlMask? mask = null;
        string? error = null;

        Assert.False(IlMask.TryParse(text, out mask, out error));
        Assert.Null(mask);
        Assert.StartsWith(reason, error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_WithoutErrorOverload_RejectsInvalidText()
    {
        IlMask? mask = null;

        Assert.False(IlMask.TryParse("GG", out mask));
    }

    [Fact]
    public void Find_ReturnsOffsetBytesAndWildcards()
    {
        byte[] il = [0x00, 0x02, 0x7B, 0x01, 0x00, 0x00, 0x04, 0x2A];
        IlMaskMatch match = Assert.Single(Parse("02 7B ?? ?? ?? ?? 2A").Find(il));

        Assert.Equal(1, match.Offset);
        Assert.Equal(il[1..], match.Bytes.ToArray());
        Assert.Equal([0x01, 0x00, 0x00, 0x04], match.Wildcards.ToArray());
    }

    [Fact]
    public void Find_OnlyMatchesOnInstructions_WhileFindBytesMatchesAnywhere()
    {
        byte[] il = [0x20, 0x2A, 0x00, 0x00, 0x00, 0x2A]; // ldc.i4 42; ret
        IlMask ret = Parse("2A");

        Assert.Equal([5], ret.Find(il).Select(m => m.Offset));
        Assert.Equal([1, 5], ret.FindBytes(il).Select(m => m.Offset));
    }

    [Fact]
    public void Find_SkipsBytesThatAreNotInstructions()
    {
        byte[] il = [0xA6, 0x2A];
        IlMask ret = Parse("2A");

        Assert.Equal([1], ret.Find(il).Select(m => m.Offset));
    }

    [Fact]
    public void FindBytes_ReturnsOverlappingMatchesInOrder()
    {
        byte[] bytes = [0x00, 0x00, 0x00];
        IEnumerable<int> offsets = Parse("00 ??").FindBytes(bytes).Select(m => m.Offset);

        Assert.Equal([0, 1], offsets);
    }

    [Theory]
    [InlineData(new byte[] { 0x02, 0x2B })]
    [InlineData(new byte[] { 0x02 })]
    [InlineData(new byte[0])]
    public void Find_And_FindBytes_ReturnNothingWithoutMatch(byte[] il)
    {
        IlMask mask = Parse("02 2A");

        Assert.Empty(mask.Find(il));
        Assert.Empty(mask.FindBytes(il));
    }

    private static IlMask Parse(string text)
    {
        IlMask? mask = null;

        Assert.True(IlMask.TryParse(text, out mask));
        return mask;
    }
}
