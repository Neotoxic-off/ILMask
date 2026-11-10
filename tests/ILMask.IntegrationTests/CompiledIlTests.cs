using System.Buffers.Binary;
using System.Reflection;

namespace ILMask.IntegrationTests;

public sealed class CompiledIlTests
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void MaskFromOneMethod_FindsSameShapeMethod_AndExtractsItsFieldToken()
    {
        bool created = IlMask.TryFromIl(IlOf(nameof(Sample.GetFirst)), out IlMask? mask);
        IlMaskMatch? match = mask?.Find(IlOf(nameof(Sample.GetSecond))).SingleOrDefault();
        int token = match is null ? 0 : BinaryPrimitives.ReadInt32LittleEndian(match.Wildcards.Span);

        Assert.True(created);
        Assert.NotNull(match);
        Assert.Equal("_second", typeof(Sample).Module.ResolveField(token)!.Name);
    }

    [Fact]
    public void HandWrittenMask_FindsStringLoadedByCompiledMethod()
    {
        bool parsed = IlMask.TryParse("72 ?? ?? ?? ??", out IlMask? mask); // ldstr <token>
        IlMaskMatch? match = mask?.Find(IlOf(nameof(Sample.Greet))).SingleOrDefault();
        int token = match is null ? 0 : BinaryPrimitives.ReadInt32LittleEndian(match.Wildcards.Span);

        Assert.True(parsed);
        Assert.NotNull(match);
        Assert.Equal("hello", typeof(Sample).Module.ResolveString(token));
    }

    [Fact]
    public void MaskCopiedFromIlSpy_FindsCompiledMethod_AndExtractsItsFieldToken()
    {
        const string ilSpyListing = """
            IL_0000: ldarg.0
            IL_0001: ldfld int32 ILMask.IntegrationTests.CompiledIlTests/Sample::_first
            IL_0006: ret
            """;
        bool created = IlMask.TryFromListing(ilSpyListing, out IlMask? mask);
        IlMaskMatch? match = mask?.Find(IlOf(nameof(Sample.GetSecond))).SingleOrDefault();
        int token = match is null ? 0 : BinaryPrimitives.ReadInt32LittleEndian(match.Wildcards.Span);

        Assert.True(created);
        Assert.NotNull(match);
        Assert.Equal("_second", typeof(Sample).Module.ResolveField(token)!.Name);
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(List<>))]
    [InlineData(typeof(Dictionary<,>))]
    [InlineData(typeof(Enumerable))]
    [InlineData(typeof(IlMask))]
    public void TryFromIl_DecodesRealMethods_AndMatchesThemAtOffsetZero(Type type)
    {
        List<byte[]> bodies = type.GetMethods(AllDeclared)
            .Select(method => method.GetMethodBody()?.GetILAsByteArray())
            .OfType<byte[]>()
            .ToList();

        Assert.NotEmpty(bodies);
        Assert.All(bodies, il =>
        {
            bool created = IlMask.TryFromIl(il, out IlMask? mask);

            Assert.True(created);
            Assert.Equal(0, mask!.Find(il).First().Offset);
        });
    }

    private static byte[] IlOf(string methodName) =>
        typeof(Sample).GetMethod(methodName)!.GetMethodBody()!.GetILAsByteArray()!;

    private sealed class Sample
    {
        private readonly int _first = 1;
        private readonly int _second = 2;

        public int GetFirst() => _first;

        public int GetSecond() => _second;

        public static string Greet() => "hello";
    }
}
