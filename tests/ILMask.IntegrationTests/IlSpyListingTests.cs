using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;

namespace ILMask.IntegrationTests;

/// <summary>Compares masks read from real ILSpy output with masks built from the compiled bytes.</summary>
public sealed class IlSpyListingTests
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Theory]
    [InlineData(typeof(OperandSamples))]
    [InlineData(typeof(string))]
    [InlineData(typeof(List<>))]
    [InlineData(typeof(Dictionary<,>))]
    [InlineData(typeof(Enumerable))]
    [InlineData(typeof(Math))]
    public void TryFromListing_OnIlSpyOutput_GivesTheSameMaskAsTryFromIl(Type type)
    {
        using PEFile file = new(type.Assembly.Location);
        List<MethodInfo> methods = [.. type.GetMethods(AllDeclared).Where(method => method.GetMethodBody() is not null)];

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            bool decoded = IlMask.TryFromIl(method.GetMethodBody()!.GetILAsByteArray(), out IlMask? expected);
            bool read = IlMask.TryFromListing(Disassemble(file, method), out IlMask? actual, out string? error);

            Assert.True(decoded);
            Assert.True(read, error);
            Assert.Equal(expected!.ToString(), actual!.ToString());
        });
    }

    [Fact]
    public void TryFromListing_OnSeveralIlSpyMethodsPastedTogether_ResolvesEachMethodsNames()
    {
        using PEFile file = new(typeof(OperandSamples).Assembly.Location);
        MethodInfo[] methods =
        [
            typeof(OperandSamples).GetMethod(nameof(OperandSamples.Variables))!,
            typeof(OperandSamples).GetMethod(nameof(OperandSamples.Constants))!,
        ];
        byte[] both = [.. methods.SelectMany(m => m.GetMethodBody()!.GetILAsByteArray()!)];
        bool read = IlMask.TryFromListing(string.Join('\n', methods.Select(m => Disassemble(file, m))), out IlMask? actual);
        bool decoded = IlMask.TryFromIl(both, out IlMask? expected);

        Assert.True(read);
        Assert.True(decoded);
        Assert.Equal(expected!.ToString(), actual!.ToString());
    }

    private static string Disassemble(PEFile file, MethodInfo method)
    {
        PlainTextOutput output = new();

        new ReflectionDisassembler(output, CancellationToken.None)
            .DisassembleMethod(file, (MethodDefinitionHandle)MetadataTokens.EntityHandle(method.MetadataToken));
        return output.ToString();
    }

    /// <summary>Methods whose IL uses every kind of operand.</summary>
    private static class OperandSamples
    {
        public static double Constants(float a, double @object, long big)
        {
            float nan = float.NaN;
            double negativeInfinity = double.NegativeInfinity;
            double tiny = 1e-300;
            float tenth = 0.1f;
            long large = 123456789012345;
            int negative = -1000000;
            sbyte small = -5;

            return a + @object + big + nan + negativeInfinity + tiny + tenth + large + negative + small;
        }

        public static int Branches(int x)
        {
            switch (x)
            {
                case 0: return 10;
                case 1: return 20;
                case 2: return 30;
                case 3: return 40;
            }

            for (int i = 0; i < 300; i++)
            {
                x += i;
            }

            try
            {
                x = int.Parse("IL_0001: not a label", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                x = 5;
            }

            return x > 1000 ? LongBranch(x) : x;
        }

        public static int Variables(int a, int b, int c, int d, int e, ref int f)
        {
            int v0 = a, v1 = b, v2 = c, v3 = d, v4 = e;

            f = v4;
            e = v0 + v1 + v2 + v3 + v4;
            return e + f;
        }

        private static int LongBranch(int x)
        {
            if (x > 0)
            {
                x = (x * 31) + (x / 7) + (x % 13) + (x * 17) + (x / 3) + (x % 11) + (x * 19) + (x / 5) + (x % 23) + (x * 29);
                x = (x * 31) + (x / 7) + (x % 13) + (x * 17) + (x / 3) + (x % 11) + (x * 19) + (x / 5) + (x % 23) + (x * 29);
                x = (x * 31) + (x / 7) + (x % 13) + (x * 17) + (x / 3) + (x % 11) + (x * 19) + (x / 5) + (x % 23) + (x * 29);
                x = (x * 31) + (x / 7) + (x % 13) + (x * 17) + (x / 3) + (x % 11) + (x * 19) + (x / 5) + (x % 23) + (x * 29);
            }

            return x;
        }
    }
}
