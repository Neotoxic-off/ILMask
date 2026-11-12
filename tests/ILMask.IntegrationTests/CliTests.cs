using ILMask.Cli;

namespace ILMask.IntegrationTests;

public sealed class CliTests
{
    private static readonly string Library = typeof(IlMask).Assembly.Location;

    [Fact]
    public void Find_ListsTheMethodsThatMatch()
    {
        (int exitCode, string output, string _) = Run("find", Library, "02", "7B", "?", "?", "?", "?", "2A");

        Assert.Equal(0, exitCode);
        Assert.Contains("ILMask.IlMask::get_Pattern IL_0000: 02 7B ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void From_WritesTheMaskOfAMethod_ThatShowReadsBack()
    {
        (int exitCode, string mask, string _) = Run("from", Library, "ILMask.IlMask::get_Pattern");
        (int showExitCode, string listing, string _) = Run("show", mask.Trim());

        Assert.Equal(0, exitCode);
        Assert.Equal("02 7B ?? ?? ?? ?? 2A", mask.Trim());
        Assert.Equal(0, showExitCode);
        Assert.Equal("IL_0000: ldarg.0\nIL_0001: ldfld ??\nIL_0006: ret", listing.Trim());
    }

    [Theory]
    [InlineData("Usage:")]
    [InlineData("Invalid mask byte 'GG'", "show", "GG")]
    [InlineData("No method 'Nope::Nope'", "from", "<library>", "Nope::Nope")]
    [InlineData("Could not find file", "find", "missing.dll", "2A")]
    [InlineData("", "find", "<text file>", "2A")]
    public void Errors_ReturnOne_WithReason(string reason, params string[] args)
    {
        string textFile = Path.Combine(AppContext.BaseDirectory, "ILMask.IntegrationTests.deps.json");
        string[] resolved = [.. args.Select(arg => arg.Replace("<library>", Library, StringComparison.Ordinal)
            .Replace("<text file>", textFile, StringComparison.Ordinal))];
        (int exitCode, string output, string error) = Run(resolved);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.NotEmpty(error);
        Assert.Contains(reason, error, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        int exitCode = Program.Run(args, output, errors);

        return (exitCode, output.ToString(), errors.ToString());
    }
}
