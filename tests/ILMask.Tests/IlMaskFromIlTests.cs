namespace ILMask.Tests;

public sealed class IlMaskFromIlTests
{
    [Theory]
    [InlineData("2A", "2A")] // ret
    [InlineData("2B 05", "2B 05")] // br.s
    [InlineData("1F 7F", "1F 7F")] // ldc.i4.s
    [InlineData("0E 01", "0E 01")] // ldarg.s
    [InlineData("FE 09 01 00", "FE 09 01 00")] // ldarg
    [InlineData("20 01 02 03 04", "20 01 02 03 04")] // ldc.i4
    [InlineData("38 01 02 03 04", "38 01 02 03 04")] // br
    [InlineData("22 00 00 80 3F", "22 00 00 80 3F")] // ldc.r4
    [InlineData("21 01 02 03 04 05 06 07 08", "21 01 02 03 04 05 06 07 08")] // ldc.i8
    [InlineData("23 01 02 03 04 05 06 07 08", "23 01 02 03 04 05 06 07 08")] // ldc.r8
    [InlineData("45 02 00 00 00 01 00 00 00 02 00 00 00", "45 02 00 00 00 01 00 00 00 02 00 00 00")] // switch
    [InlineData("7B 01 00 00 04", "7B ?? ?? ?? ??")] // ldfld
    [InlineData("28 01 00 00 0A", "28 ?? ?? ?? ??")] // call
    [InlineData("29 01 00 00 11", "29 ?? ?? ?? ??")] // calli
    [InlineData("72 01 00 00 70", "72 ?? ?? ?? ??")] // ldstr
    [InlineData("D0 01 00 00 02", "D0 ?? ?? ?? ??")] // ldtoken
    [InlineData("8D 01 00 00 01", "8D ?? ?? ?? ??")] // newarr
    [InlineData("FE 15 01 00 00 02", "FE 15 ?? ?? ?? ??")] // initobj
    [InlineData("02 7B 01 00 00 04 2A", "02 7B ?? ?? ?? ?? 2A")] // ldarg.0; ldfld; ret
    public void TryFromIl_WildcardsOnlyMetadataTokens(string il, string expected)
    {
        IlMask? mask = null;

        Assert.True(IlMask.TryFromIl(Bytes(il), out mask));
        Assert.Equal(expected, mask.ToString());
    }

    [Theory]
    [InlineData("A6", "Unknown opcode 0xA6 at offset 0.")] // unused one-byte opcode
    [InlineData("F8", "Unknown opcode 0xF8 at offset 0.")] // reserved prefix
    [InlineData("FE", "Unknown opcode 0xFE at offset 0.")] // two-byte opcode cut short
    [InlineData("FE FF", "Unknown opcode 0xFEFF at offset 0.")] // unused two-byte opcode
    [InlineData("00 20 01 02", "Truncated ldc.i4 at offset 1: needs 5 bytes, 3 left.")] // operand cut short
    [InlineData("45 01 00", "Truncated switch at offset 0: needs 5 bytes, 3 left.")] // switch count cut short
    [InlineData("45 FF FF FF FF", "Truncated switch at offset 0: needs 17179869185 bytes, 5 left.")] // targets missing
    [InlineData("", "A mask needs at least one byte.")] // empty body
    public void TryFromIl_RejectsInvalidIl_WithReason(string il, string reason)
    {
        IlMask? mask = null;
        string? error = null;

        Assert.False(IlMask.TryFromIl(Bytes(il), out mask, out error));
        Assert.Null(mask);
        Assert.Equal(reason, error);
    }

    [Fact]
    public void TryFromIl_WithoutErrorOverload_RejectsInvalidIl()
    {
        IlMask? mask = null;

        Assert.False(IlMask.TryFromIl([0xA6], out mask));
    }

    private static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal));
}
