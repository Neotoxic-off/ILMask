using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ILMask;

/// <summary>
/// An IDA-style byte pattern over IL code, such as <c>02 7B ?? ?? ?? ?? 2A</c>,
/// where <c>??</c> (or <c>?</c>) matches any byte.
/// </summary>
/// <remarks>
/// Every factory follows the <c>Try</c> pattern and never throws: it returns <see langword="false"/>,
/// a <see langword="null"/> mask and, through its <c>error</c> overload, the reason.
/// </remarks>
public sealed class IlMask
{
    private const string WildcardText = "??";

    private readonly byte?[] _pattern;

    private IlMask(byte?[] pattern)
    {
        _pattern = pattern;
        Pattern = Array.AsReadOnly(pattern);
    }

    /// <summary>Gets the pattern; a <see langword="null"/> entry is a wildcard.</summary>
    public IReadOnlyList<byte?> Pattern { get; }

    /// <inheritdoc cref="TryCreate(IEnumerable{byte?}, out IlMask, out string)"/>
    public static bool TryCreate(IEnumerable<byte?>? pattern, [NotNullWhen(true)] out IlMask? mask) =>
        TryCreate(pattern, out mask, out _);

    /// <summary>Tries to create a mask where a <see langword="null"/> entry is a wildcard.</summary>
    /// <param name="pattern">The bytes to match; <see langword="null"/> matches any byte.</param>
    /// <param name="mask">The mask, or <see langword="null"/> when the pattern is null or empty.</param>
    /// <param name="error">Why the mask could not be created, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the mask was created.</returns>
    public static bool TryCreate(
        IEnumerable<byte?>? pattern, [NotNullWhen(true)] out IlMask? mask, [NotNullWhen(false)] out string? error)
    {
        byte?[] bytes = pattern is null ? [] : [.. pattern];

        mask = bytes.Length == 0 ? null : new IlMask(bytes);
        error = mask is null ? "A mask needs at least one byte." : null;
        return mask is not null;
    }

    /// <inheritdoc cref="TryParse(string, out IlMask, out string)"/>
    public static bool TryParse(string? text, [NotNullWhen(true)] out IlMask? mask) => TryParse(text, out mask, out _);

    /// <summary>Tries to parse a mask such as <c>02 7B ?? ?? ?? ?? 2A</c>.</summary>
    /// <param name="text">Whitespace-separated two-digit hex bytes or <c>?</c>/<c>??</c> wildcards.</param>
    /// <param name="mask">The parsed mask, or <see langword="null"/> when the text is not a valid mask.</param>
    /// <param name="error">Why the text is not a valid mask, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text is a valid mask.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out IlMask? mask, [NotNullWhen(false)] out string? error)
    {
        string[] tokens = text?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];
        byte?[] pattern = new byte?[tokens.Length];

        mask = null;
        for (int i = 0; i < tokens.Length; i++)
        {
            if (!TryParseToken(tokens[i], out pattern[i]))
            {
                error = $"Invalid mask byte '{tokens[i]}' at position {i}: expected two hex digits, '?' or '??'.";
                return false;
            }
        }

        return TryCreate(pattern, out mask, out error);
    }

    /// <inheritdoc cref="TryFromIl(ReadOnlySpan{byte}, out IlMask, out string)"/>
    public static bool TryFromIl(ReadOnlySpan<byte> il, [NotNullWhen(true)] out IlMask? mask) => TryFromIl(il, out mask, out _);

    /// <summary>
    /// Tries to create a mask from a method body, wildcarding metadata token operands
    /// (fields, methods, types, strings, signatures) since they change between builds.
    /// </summary>
    /// <param name="il">The IL bytes, e.g. from <c>MethodBody.GetILAsByteArray()</c>.</param>
    /// <param name="mask">The mask, or <see langword="null"/> when the IL is empty, truncated or has an unknown opcode.</param>
    /// <param name="error">Why the IL could not be decoded, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the IL was decoded.</returns>
    public static bool TryFromIl(ReadOnlySpan<byte> il, [NotNullWhen(true)] out IlMask? mask, [NotNullWhen(false)] out string? error)
    {
        byte?[] pattern = new byte?[il.Length];
        int offset = 0;
        IlInstruction instruction = default;

        mask = null;
        while (offset < il.Length)
        {
            if (!IlInstruction.TryRead(il, offset, out instruction, out error))
            {
                return false;
            }

            CopyInstruction(il, offset, instruction, pattern);
            offset += instruction.Size;
        }

        return TryCreate(pattern, out mask, out error);
    }

    /// <inheritdoc cref="TryFromListing(string, out IlMask, out string)"/>
    public static bool TryFromListing(string? listing, [NotNullWhen(true)] out IlMask? mask) =>
        TryFromListing(listing, out mask, out _);

    /// <summary>
    /// Tries to create a mask from IL text copied from ILSpy, dnSpy or ildasm, such as
    /// <c>IL_0000: ldarg.0</c> / <c>IL_0001: ldfld int32 Foo::_bar</c>.
    /// Opcodes and operands (constants, branch targets, locals, arguments) are exact and
    /// metadata tokens are wildcards, as with <see cref="TryFromIl(ReadOnlySpan{byte}, out IlMask)"/>.
    /// Text outside instructions (method headers, <c>.locals</c>, braces, comments) is ignored.
    /// </summary>
    /// <param name="listing">The IL text; each instruction starts a line with an <c>IL_xxxx:</c> label.</param>
    /// <param name="mask">The mask, or <see langword="null"/> when the text has no instruction or an invalid one.</param>
    /// <param name="error">Which instruction could not be read and why, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the listing was read.</returns>
    public static bool TryFromListing(string? listing, [NotNullWhen(true)] out IlMask? mask, [NotNullWhen(false)] out string? error)
    {
        byte?[]? pattern = null;

        mask = null;
        return IlListing.TryParse(listing ?? string.Empty, out pattern, out error) && TryCreate(pattern, out mask, out error);
    }

    /// <summary>
    /// Finds every match that starts on an instruction, as when searching a method body.
    /// Bytes that do not decode as an instruction are skipped one at a time.
    /// </summary>
    /// <param name="il">The IL bytes to search, e.g. from <c>MethodBody.GetILAsByteArray()</c>.</param>
    /// <returns>The matches, in offset order, evaluated lazily.</returns>
    public IEnumerable<IlMaskMatch> Find(ReadOnlyMemory<byte> il)
    {
        for (int offset = 0; offset < il.Length; offset += InstructionSize(il.Span, offset))
        {
            if (IsMatchAt(il.Span, offset))
            {
                yield return CreateMatch(il.Slice(offset, _pattern.Length), offset);
            }
        }
    }

    /// <summary>
    /// Finds every (possibly overlapping) match at any byte offset, as when searching a whole file.
    /// </summary>
    /// <param name="bytes">The bytes to search.</param>
    /// <returns>The matches, in offset order, evaluated lazily.</returns>
    public IEnumerable<IlMaskMatch> FindBytes(ReadOnlyMemory<byte> bytes)
    {
        for (int offset = 0; offset < bytes.Length; offset++)
        {
            if (IsMatchAt(bytes.Span, offset))
            {
                yield return CreateMatch(bytes.Slice(offset, _pattern.Length), offset);
            }
        }
    }

    /// <summary>Writes the mask as text, such as <c>02 7B ?? ?? ?? ?? 2A</c>.</summary>
    /// <returns>Text that <see cref="TryParse(string, out IlMask)"/> reads back to an equal mask.</returns>
    public override string ToString() =>
        string.Join(' ', _pattern.Select(b => b?.ToString("X2", CultureInfo.InvariantCulture) ?? WildcardText));

    private static bool TryParseToken(string token, out byte? value)
    {
        value = null;
        if (token is "?" or WildcardText)
        {
            return true;
        }

        if (token.Length == 2 && byte.TryParse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte b))
        {
            value = b;
            return true;
        }

        return false;
    }

    // Copies one instruction into the pattern, with its metadata token (if any) as wildcards.
    private static void CopyInstruction(ReadOnlySpan<byte> il, int offset, IlInstruction instruction, byte?[] pattern)
    {
        int operandStart = offset + instruction.OpCode.Size;
        int end = offset + instruction.Size;

        for (int i = offset; i < end; i++)
        {
            pattern[i] = instruction.HasTokenOperand && i >= operandStart ? null : il[i];
        }
    }

    private static int InstructionSize(ReadOnlySpan<byte> il, int offset) =>
        IlInstruction.TryRead(il, offset, out IlInstruction instruction, out _) ? instruction.Size : 1;

    private bool IsMatchAt(ReadOnlySpan<byte> bytes, int offset)
    {
        if (offset > bytes.Length - _pattern.Length)
        {
            return false;
        }

        for (int i = 0; i < _pattern.Length; i++)
        {
            if (_pattern[i].HasValue && bytes[offset + i] != _pattern[i].GetValueOrDefault())
            {
                return false;
            }
        }

        return true;
    }

    private IlMaskMatch CreateMatch(ReadOnlyMemory<byte> bytes, int offset)
    {
        ReadOnlySpan<byte> span = bytes.Span;
        List<byte> wildcards = [];

        for (int i = 0; i < _pattern.Length; i++)
        {
            if (_pattern[i] is null)
            {
                wildcards.Add(span[i]);
            }
        }

        return new IlMaskMatch(offset, bytes, wildcards.ToArray());
    }
}
