namespace ILMask.Tests;

public sealed class IlMaskFromListingTests
{
    [Fact]
    public void TryFromListing_ReadsIlSpyOutput_ResolvingDeclaredNames()
    {
        const string listing = """
            .method public hidebysig
                instance int32 Add (
                    int32 'object',
                    class [System.Runtime]System.Collections.Generic.Dictionary`2<string, int32> map,
                    int32 b
                ) cil managed
            {
                .maxstack 8
                .locals init (
                    [0] int32 total,
                    [1] int32
                )

                IL_0000: ldarg.s 'object'
                IL_0002: ldarg.s b
                IL_0004: stloc.s total
                IL_0006: ldfld int32 Foo::_value
                IL_000b: ret
            } // end of method Foo::Add
            """;
        IlMask mask = FromListing(listing);

        Assert.Equal("0E 01 0E 03 13 00 7B ?? ?? ?? ?? 2A", mask.ToString());
    }

    [Fact]
    public void TryFromListing_ReadmeExample()
    {
        const string listing = """
            .method public hidebysig instance int32 Get (int32 index) cil managed
            {
                .locals init ([0] int32 result)

                IL_0000: ldarg.s index
                IL_0002: ldc.i4.s 42
                IL_0004: stloc.s result
                IL_0006: ldfld int32 Foo::_value
                IL_000b: br.s IL_0000
            }
            """;
        IlMask mask = FromListing(listing);

        Assert.Equal("0E 01 1F 2A 13 00 7B ?? ?? ?? ?? 2B F3", mask.ToString());
    }

    [Fact]
    public void TryFromListing_ReadsDnSpyOutputWithBytes()
    {
        const string listing = """
            /* 0x00000250 02           */ IL_0000: ldarg.0
            /* 0x00000251 7B01000004   */ IL_0001: ldfld     int32 Foo::_value
            /* 0x00000256 2A           */ IL_0006: ret
            """;
        IlMask mask = FromListing(listing);

        Assert.Equal("02 7B ?? ?? ?? ?? 2A", mask.ToString());
    }

    [Fact]
    public void TryFromListing_ReadsIldasmOutputWithBytes()
    {
        const string listing = """
            IL_0000:  /* 02   |                  */ ldarg.0
            IL_0001:  /* 7B   | (04)000001       */ ldfld      int32 Foo::_value
            IL_0006:  /* 2A   |                  */ ret
            """;
        IlMask mask = FromListing(listing);

        Assert.Equal("02 7B ?? ?? ?? ?? 2A", mask.ToString());
    }

    [Fact]
    public void TryFromListing_IgnoresLabelsInsideStrings()
    {
        const string listing = """
            IL_0000: ldstr "IL_0001: ret"
            IL_0005: ret
            """;
        IlMask mask = FromListing(listing);

        Assert.Equal("72 ?? ?? ?? ?? 2A", mask.ToString());
    }

    [Fact]
    public void TryFromListing_ResolvesNamesPerPastedMethod()
    {
        const string listing = """
            .method static void A (int32 x, int32 y) cil managed
            {
                IL_0000: ldarg.s y
            }
            .method static void B (int32 y) cil managed
            {
                .locals init ([0] int32 y)
                IL_0000: ldarg.s y
                IL_0002: ldloc.s y
            }
            """;
        IlMask mask = FromListing(listing);

        Assert.Equal("0E 01 0E 00 11 00", mask.ToString());
    }

    [Theory]
    [InlineData("IL_0000: ldc.i4.s -5", "1F FB")]
    [InlineData("IL_0000: ldc.i4.s 0x7f", "1F 7F")]
    [InlineData("IL_0000: unaligned. 4", "FE 12 04")]
    [InlineData("IL_0000: ldc.i4 -1000000 // 0xfff0bdc0", "20 C0 BD F0 FF")]
    [InlineData("IL_0000: ldc.i4 0xfff0bdc0", "20 C0 BD F0 FF")]
    [InlineData("IL_0000: ldc.i8 123456789012345", "21 79 DF 0D 86 48 70 00 00")]
    [InlineData("IL_0000: ldc.i8 0xFFFFFFFFFFFFFFFF", "21 FF FF FF FF FF FF FF FF")]
    [InlineData("IL_0000: ldc.r4 0.1", "22 CD CC CC 3D")]
    [InlineData("IL_0000: ldc.r4 (00 00 C0 FF)", "22 00 00 C0 FF")]
    [InlineData("IL_0000: ldc.r8 1E-300", "23 59 F3 F8 C2 1F 6E A5 01")]
    [InlineData("IL_0000: ldc.r8 (00 00 00 00 00 00 F0 FF)", "23 00 00 00 00 00 00 F0 FF")]
    [InlineData("IL_0000: ldloc.s 4", "11 04")]
    [InlineData("IL_0000: ldloc.s V_4", "11 04")]
    [InlineData("IL_0000: ldarg.s A_4", "0E 04")]
    [InlineData("IL_0000: ldarg.s undeclared", "0E ??")]
    [InlineData("IL_0000: ldarg.s V_4", "0E ??")]
    [InlineData("IL_0000: ldloc V_300", "FE 0C 2C 01")]
    [InlineData("IL_0010: br.s IL_0000", "2B EE")]
    [InlineData("IL_0000: br.s IL_0081", "2B 7F")]
    [InlineData("IL_0000: br IL_1000", "38 FB 0F 00 00")]
    [InlineData("IL_0000: switch (IL_000D, IL_0011)", "45 02 00 00 00 00 00 00 00 04 00 00 00")]
    [InlineData("IL_0000: switch (\n    IL_000D,\n    IL_0011)", "45 02 00 00 00 00 00 00 00 04 00 00 00")]
    [InlineData("IL_0000: switch ()", "45 00 00 00 00")]
    [InlineData("IL_0000: ceq", "FE 01")]
    [InlineData("IL_0000: LDARG.0", "02")]
    [InlineData("IL_0000: constrained. [System.Runtime]System.Object\nIL_0006: callvirt instance string Object::ToString()", "FE 16 ?? ?? ?? ?? 6F ?? ?? ?? ??")]
    public void TryFromListing_EncodesOperandsExactly(string listing, string expected)
    {
        IlMask mask = FromListing(listing);

        Assert.Equal(expected, mask.ToString());
    }

    [Theory]
    [InlineData(null, "No IL instruction found")]
    [InlineData("", "No IL instruction found")]
    [InlineData("ldarg.0", "No IL instruction found")]
    [InlineData("IL_0000:", "unknown instruction ''")]
    [InlineData("IL_0000: notanopcode", "unknown instruction 'notanopcode'")]
    [InlineData("IL_FFFFFFFFF: nop", "IL_FFFFFFFFF: invalid label")]
    [InlineData("IL_0000: ldc.i4.s 300", "invalid operand '300' for ldc.i4.s")]
    [InlineData("IL_0000: ldc.i4.s 0x100", "invalid operand")]
    [InlineData("IL_0000: ldc.i4 abc", "invalid operand")]
    [InlineData("IL_0000: ldc.i4 0xZZ", "invalid operand")]
    [InlineData("IL_0000: ldc.r4 abc", "invalid operand")]
    [InlineData("IL_0000: ldc.r8 abc", "invalid operand")]
    [InlineData("IL_0000: ldc.r4 (00 00)", "invalid operand")]
    [InlineData("IL_0000: ldc.r4 (00 00 C0 GG)", "invalid operand")]
    [InlineData("IL_0000: ldc.r4 (00 00 C0 F)", "invalid operand")]
    [InlineData("IL_0000: ldloc.s", "invalid operand '' for ldloc.s")]
    [InlineData("IL_0000: ldloc.s 256", "invalid operand")]
    [InlineData("IL_0000: br.s somewhere", "invalid operand")]
    [InlineData("IL_0000: br.s IL_0082", "invalid operand")]
    [InlineData("IL_0082: br.s IL_0000", "invalid operand")]
    [InlineData("IL_0000: switch IL_0010", "invalid operand")]
    [InlineData("IL_0000: switch (IL_0010, nowhere)", "invalid operand")]
    public void TryFromListing_RejectsInvalidListing_WithReason(string? listing, string reason)
    {
        IlMask? mask = null;
        string? error = null;

        Assert.False(IlMask.TryFromListing(listing, out mask, out error));
        Assert.Null(mask);
        Assert.Contains(reason, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".method static void A cil managed {\n IL_0000: ldarg.s x\n}")]
    [InlineData(".method static void A (int32 x cil managed {\n IL_0000: ldarg.s x\n}")]
    [InlineData(".locals init ([0] int32 x\n IL_0000: ldloc.s x")]
    [InlineData(".locals init (garbage)\n IL_0000: ldloc.s x")]
    public void TryFromListing_WildcardsNamesItCannotResolve(string listing)
    {
        IlMask mask = FromListing(listing);

        Assert.EndsWith("??", mask.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TryFromListing_WithoutErrorOverload_RejectsInvalidListing()
    {
        IlMask? mask = null;

        Assert.False(IlMask.TryFromListing("IL_0000: notanopcode", out mask));
    }

    private static IlMask FromListing(string listing)
    {
        IlMask? mask = null;
        string? error = null;

        Assert.True(IlMask.TryFromListing(listing, out mask, out error), error);
        return mask;
    }
}
