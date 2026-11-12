using System.Reflection;

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

    [Theory]
    [InlineData("02 2A", "022A", new string[0])]
    [InlineData("7B ?? ?? ?? ?? 28 ?? ?? ?? ??", "7B01000004280200000A", new[] { "01000004", "0200000A" })]
    [InlineData("?? 7B ?? 2A ??", "007B112A22", new[] { "00", "11", "22" })]
    public void Find_GroupsEachRunOfWildcards(string text, string bytes, string[] expected)
    {
        IlMaskMatch match = Assert.Single(Parse(text).FindBytes(Convert.FromHexString(bytes)));

        Assert.Equal(expected, match.Groups.Select(group => Convert.ToHexString(group.Span)));
    }

    [Fact]
    public void Equals_ComparesPatterns()
    {
        IlMask mask = Parse("02 ?? 2A");

        Assert.Equal(Parse("02 ? 2a"), mask);
        Assert.Equal(Parse("02 ? 2a").GetHashCode(), mask.GetHashCode());
        Assert.NotEqual(Parse("02 00 2A"), mask);
        Assert.False(mask.Equals(null));
        Assert.False(mask.Equals((object)"02 ?? 2A"));
    }

    [Fact]
    public void TryFromMethod_And_Find_ReadTheMethodBody()
    {
        MethodBase method = typeof(IlMaskTests).GetMethod(nameof(Parse), BindingFlags.NonPublic | BindingFlags.Static)!;
        IlMask? mask = null;
        IlMask? fromIl = null;

        Assert.True(IlMask.TryFromMethod(method, out mask));
        Assert.True(IlMask.TryFromIl(method.GetMethodBody()!.GetILAsByteArray(), out fromIl));
        Assert.Equal(fromIl, mask);
        Assert.Equal(0, mask.Find(method).First().Offset);
    }

    [Fact]
    public void TryFromMethod_And_Find_RejectMethodsWithoutBody()
    {
        MethodBase toString = typeof(IFormattable).GetMethod(nameof(IFormattable.ToString))!;
        IlMask? mask = null;
        string? error = null;

        Assert.False(IlMask.TryFromMethod(null, out mask));
        Assert.False(IlMask.TryFromMethod(toString, out mask, out error));
        Assert.Null(mask);
        Assert.StartsWith("The method has no IL body", error, StringComparison.Ordinal);
        Assert.Empty(Parse("2A").Find((MethodBase?)null));
        Assert.Empty(Parse("2A").Find(toString));
    }

    [Theory]
    [InlineData("02 7B ?? ?? ?? ?? 2A", "IL_0000: ldarg.0\nIL_0001: ldfld ??\nIL_0006: ret")]
    [InlineData("1F FF", "IL_0000: ldc.i4.s -1")]
    [InlineData("20 E8 03 00 00", "IL_0000: ldc.i4 1000")]
    [InlineData("21 01 00 00 00 00 00 00 00", "IL_0000: ldc.i8 1")]
    [InlineData("22 00 00 C0 3F", "IL_0000: ldc.r4 1.5")]
    [InlineData("22 00 00 C0 FF", "IL_0000: ldc.r4 (00 00 C0 FF)")]
    [InlineData("23 00 00 00 00 00 00 F8 3F", "IL_0000: ldc.r8 1.5")]
    [InlineData("23 00 00 00 00 00 00 F0 7F", "IL_0000: ldc.r8 (00 00 00 00 00 00 F0 7F)")]
    [InlineData("0E 01", "IL_0000: ldarg.s 1")]
    [InlineData("FE 09 01 00", "IL_0000: ldarg 1")]
    [InlineData("2B FE", "IL_0000: br.s IL_0000")]
    [InlineData("38 00 00 00 00", "IL_0000: br IL_0005")]
    [InlineData("45 02 00 00 00 00 00 00 00 01 00 00 00 2A 2A", "IL_0000: switch (IL_000d, IL_000e)\nIL_000d: ret\nIL_000e: ret")]
    [InlineData("7B 01 00 00 04", "IL_0000: ldfld 0x04000001")]
    [InlineData("2B ??", "IL_0000: br.s ??")]
    public void TryToListing_WritesInstructions(string text, string expected)
    {
        string? listing = null;

        Assert.True(Parse(text).TryToListing(out listing));
        Assert.Equal(expected, listing);
    }

    [Fact]
    public void TryToListing_IsReadBackByTryFromListing()
    {
        IlMask mask = Parse(
            "1F FF 20 E8 03 00 00 21 01 00 00 00 00 00 00 00 22 00 00 C0 3F 22 00 00 C0 FF 23 00 00 00 00 00 00 F8 3F "
            + "0E 01 FE 09 01 00 2B FE 38 00 00 00 00 45 02 00 00 00 00 00 00 00 05 00 00 00 7B ?? ?? ?? ?? 2A");
        string? listing = null;
        IlMask? reread = null;

        Assert.True(mask.TryToListing(out listing));
        Assert.True(IlMask.TryFromListing(listing, out reread));
        Assert.Equal(mask, reread);
    }

    [Theory]
    [InlineData("1F 2A 2B 00", IlOperands.None, "1F 2A 2B 00")]
    [InlineData(
        "1F 2A 20 01 00 00 00 21 01 02 03 04 05 06 07 08 22 00 00 80 3F 23 01 02 03 04 05 06 07 08 2B 00",
        IlOperands.Constants,
        "1F ?? 20 ?? ?? ?? ?? 21 ?? ?? ?? ?? ?? ?? ?? ?? 22 ?? ?? ?? ?? 23 ?? ?? ?? ?? ?? ?? ?? ?? 2B 00")]
    [InlineData(
        "1F 2A 2B 00 38 00 00 00 00 45 01 00 00 00 00 00 00 00 0E 01 FE 09 01 00 7B ?? ?? ?? ?? 2A",
        IlOperands.Branches,
        "1F 2A 2B ?? 38 ?? ?? ?? ?? 45 01 00 00 00 ?? ?? ?? ?? 0E 01 FE 09 01 00 7B ?? ?? ?? ?? 2A")]
    [InlineData("1F 2A 2B 00", IlOperands.Constants | IlOperands.Branches, "1F ?? 2B ??")]
    public void TryWildcard_WildcardsTheChosenOperands(string text, IlOperands operands, string expected)
    {
        IlMask? mask = null;

        Assert.True(Parse(text).TryWildcard(operands, out mask));
        Assert.Equal(expected, mask.ToString());
    }

    [Theory]
    [InlineData("?? 2A", "Wildcard in the opcode or switch count at offset 0")]
    [InlineData("2A FE ??", "Wildcard in the opcode or switch count at offset 1")]
    [InlineData("45 ?? 00 00 00", "Wildcard in the opcode or switch count at offset 0")]
    [InlineData("02 7B ?? ??", "Truncated ldfld at offset 1")]
    [InlineData("A6", "Unknown opcode 0xA6 at offset 0")]
    public void TryToListing_And_TryWildcard_RejectMasksThatAreNotInstructions_WithReason(string text, string reason)
    {
        IlMask mask = Parse(text);
        string? listing = null;
        IlMask? wildcarded = null;
        string? listingError = null;
        string? wildcardError = null;

        Assert.False(mask.TryToListing(out listing, out listingError));
        Assert.False(mask.TryWildcard(IlOperands.Constants, out wildcarded, out wildcardError));
        Assert.Null(listing);
        Assert.Null(wildcarded);
        Assert.StartsWith(reason, listingError, StringComparison.Ordinal);
        Assert.Equal(listingError, wildcardError);
    }

    [Fact]
    public void FindIn_ReturnsMatchesWithTheirMethod()
    {
        IlMask ret = Parse("2A");

        Assert.Contains(ret.FindIn(typeof(IlMaskTests)), found => found.Method.Name == nameof(Parse) && found.Match.Offset > 0);
        Assert.Contains(ret.FindIn(typeof(IlMask).Assembly), found => found.Method.DeclaringType == typeof(IlMask));
        Assert.Empty(ret.FindIn((Type?)null));
        Assert.Empty(ret.FindIn((Assembly?)null));
    }

    private static IlMask Parse(string text)
    {
        IlMask? mask = null;

        Assert.True(IlMask.TryParse(text, out mask));
        return mask;
    }
}
