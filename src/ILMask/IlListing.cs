using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection.Emit;
using System.Text.RegularExpressions;

namespace ILMask;

/// <summary>
/// Reads IL listings as shown by ILSpy, dnSpy or ildasm, where each instruction
/// starts a line with a label such as <c>IL_0001: ldfld int32 Foo::_bar</c>.
/// </summary>
internal static partial class IlListing
{
    /// <summary>
    /// Tries to turn a listing into a pattern: opcodes and operands are exact,
    /// metadata tokens are wildcards.
    /// </summary>
    public static bool TryParse(string listing, [NotNullWhen(true)] out byte?[]? pattern, [NotNullWhen(false)] out string? error)
    {
        List<byte?> bytes = [];
        string[] methods = MethodStartRegex().Split(listing);

        pattern = null;
        foreach (string method in methods)
        {
            if (!TryAppendMethod(bytes, method, out error))
            {
                return false;
            }
        }

        error = bytes.Count == 0 ? "No IL instruction found; expected lines such as 'IL_0000: ldarg.0'." : null;
        pattern = error is null ? [.. bytes] : null;
        return error is null;
    }

    private static bool TryAppendMethod(List<byte?> pattern, string method, [NotNullWhen(false)] out string? error)
    {
        IlListingSymbols symbols = IlListingSymbols.Read(method);
        MatchCollection labels = InstructionStartRegex().Matches(method);

        error = null;
        for (int i = 0; i < labels.Count; i++)
        {
            if (!TryAppendInstruction(pattern, labels[i].Groups[1].Value, InstructionText(method, labels, i), symbols, out error))
            {
                return false;
            }
        }

        return true;
    }

    private static string InstructionText(string method, MatchCollection labels, int index)
    {
        int start = labels[index].Index + labels[index].Length;
        int end = index + 1 < labels.Count ? labels[index + 1].Index : method.Length;

        return method[start..end];
    }

    private static bool TryAppendInstruction(
        List<byte?> pattern, string label, string instruction, IlListingSymbols symbols, [NotNullWhen(false)] out string? error)
    {
        string[] parts = LeadingCommentRegex().Replace(instruction, string.Empty)
            .Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        string mnemonic = parts.Length == 0 ? string.Empty : parts[0];
        string operandText = parts.Length == 2 ? parts[1] : string.Empty;
        int offset = 0;
        OpCode opCode = default;
        byte?[]? operand = null;

        if (!int.TryParse(label, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out offset))
        {
            error = $"IL_{label}: invalid label.";
            return false;
        }

        if (!IlOpCodes.ByName.TryGetValue(mnemonic, out opCode))
        {
            error = $"IL_{label}: unknown instruction '{mnemonic}'.";
            return false;
        }

        if (!IlListingOperand.TryEncode(opCode, offset, operandText, symbols, out operand))
        {
            error = $"IL_{label}: invalid operand '{FirstLine(operandText)}' for {opCode.Name}.";
            return false;
        }

        if (opCode.Size == 2)
        {
            pattern.Add((byte)(opCode.Value >> 8));
        }

        pattern.Add((byte)opCode.Value);
        pattern.AddRange(operand);
        error = null;
        return true;
    }

    private static string FirstLine(string text) => text.Split('\n')[0].Trim();

    // A label only counts at the start of a line (after dnSpy's byte comment), never inside a string such as ldstr "IL_0001:".
    [GeneratedRegex(@"^[ \t]*(?:/\*[^\n]*?\*/[ \t]*)?IL_([0-9A-Fa-f]+):", RegexOptions.Multiline)]
    private static partial Regex InstructionStartRegex();

    // Each pasted method has its own argument and local names.
    [GeneratedRegex(@"(?=^[ \t]*\.method\b)", RegexOptions.Multiline)]
    private static partial Regex MethodStartRegex();

    // ildasm /bytes writes the raw bytes between the label and the mnemonic: "IL_0000:  /* 02   | */ ldarg.0".
    [GeneratedRegex(@"^[ \t]*/\*.*?\*/")]
    private static partial Regex LeadingCommentRegex();
}
