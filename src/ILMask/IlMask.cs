using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace ILMask;

/// <summary>
/// An IDA-style byte pattern over IL code, such as <c>02 7B ?? ?? ?? ?? 2A</c>,
/// where <c>??</c> (or <c>?</c>) matches any byte.
/// </summary>
/// <remarks>
/// Every factory follows the <c>Try</c> pattern and never throws: it returns <see langword="false"/>,
/// a <see langword="null"/> mask and, through its <c>error</c> overload, the reason.
/// </remarks>
public sealed class IlMask : IEquatable<IlMask>
{
    private const string WildcardText = "??";
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private readonly byte?[] _pattern;
    private readonly Range[] _wildcardRuns;

    private IlMask(byte?[] pattern)
    {
        _pattern = pattern;
        _wildcardRuns = WildcardRuns(pattern);
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

    /// <inheritdoc cref="TryFromMethod(MethodBase, out IlMask, out string)"/>
    public static bool TryFromMethod(MethodBase? method, [NotNullWhen(true)] out IlMask? mask) =>
        TryFromMethod(method, out mask, out _);

    /// <summary>
    /// Tries to create a mask from the IL body of a method, as
    /// <see cref="TryFromIl(ReadOnlySpan{byte}, out IlMask, out string)"/> does from its bytes.
    /// </summary>
    /// <param name="method">The method or constructor, e.g. from <c>typeof(Foo).GetMethod("Bar")</c>.</param>
    /// <param name="mask">The mask, or <see langword="null"/> when the method has no IL body or it cannot be decoded.</param>
    /// <param name="error">Why the mask could not be created, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the mask was created.</returns>
    public static bool TryFromMethod(MethodBase? method, [NotNullWhen(true)] out IlMask? mask, [NotNullWhen(false)] out string? error)
    {
        byte[]? il = method?.GetMethodBody()?.GetILAsByteArray();

        if (il is null)
        {
            mask = null;
            error = "The method has no IL body (null, abstract, extern or runtime-implemented).";
            return false;
        }

        return TryFromIl(il, out mask, out error);
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

    /// <summary>Finds every match that starts on an instruction of the IL body of a method.</summary>
    /// <param name="method">The method or constructor to search; one without an IL body has no match.</param>
    /// <returns>The matches, in offset order, evaluated lazily.</returns>
    public IEnumerable<IlMaskMatch> Find(MethodBase? method) => Find(method?.GetMethodBody()?.GetILAsByteArray() ?? []);

    /// <summary>Finds every match in the methods and constructors that a type declares (not in its nested types).</summary>
    /// <param name="type">The type to search.</param>
    /// <returns>The matches with their method, evaluated lazily.</returns>
    public IEnumerable<IlMaskMethodMatch> FindIn(Type? type) =>
        (type?.GetMembers(AllDeclared) ?? []).OfType<MethodBase>()
            .SelectMany(method => Find(method).Select(match => new IlMaskMethodMatch(method, match)));

    /// <summary>Finds every match in the methods and constructors of every type of an assembly, nested types included.</summary>
    /// <param name="assembly">The assembly to search.</param>
    /// <returns>The matches with their method, evaluated lazily.</returns>
    /// <remarks>
    /// The types come from <see cref="Assembly.GetTypes"/>, which throws when one cannot be loaded;
    /// in that case, call <see cref="FindIn(Type)"/> on the types that did load.
    /// </remarks>
    public IEnumerable<IlMaskMethodMatch> FindIn(Assembly? assembly) =>
        (assembly?.GetTypes() ?? []).SelectMany(type => FindIn(type));

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

    /// <inheritdoc cref="TryToListing(out string, out string)"/>
    public bool TryToListing([NotNullWhen(true)] out string? listing) => TryToListing(out listing, out _);

    /// <summary>
    /// Tries to write the mask as IL instructions, one <c>IL_xxxx: mnemonic operand</c> line each,
    /// such as <c>IL_0001: ldfld ??</c>. An operand with a wildcard is written <c>??</c>.
    /// </summary>
    /// <param name="listing">
    /// The lines, separated by <c>\n</c>. <see cref="TryFromListing(string, out IlMask)"/> reads them back
    /// to an equal mask when the wildcards cover exactly the metadata tokens.
    /// </param>
    /// <param name="error">Why the mask is not a sequence of whole instructions, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the mask is a sequence of whole instructions.</returns>
    public bool TryToListing([NotNullWhen(true)] out string? listing, [NotNullWhen(false)] out string? error) =>
        IlDisassembler.TryDisassemble(_pattern, out listing, out error);

    /// <inheritdoc cref="TryWildcard(IlOperands, out IlMask, out string)"/>
    public bool TryWildcard(IlOperands operands, [NotNullWhen(true)] out IlMask? mask) => TryWildcard(operands, out mask, out _);

    /// <summary>
    /// Tries to create a looser mask, where the chosen kinds of operands are wildcards too,
    /// so that it still matches after constants change or code moves.
    /// </summary>
    /// <param name="operands">The kinds of operands to wildcard.</param>
    /// <param name="mask">The looser mask, or <see langword="null"/> when this mask is not a sequence of whole instructions.</param>
    /// <param name="error">Why the mask is not a sequence of whole instructions, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the mask was created.</returns>
    public bool TryWildcard(IlOperands operands, [NotNullWhen(true)] out IlMask? mask, [NotNullWhen(false)] out string? error)
    {
        byte?[] pattern = [.. _pattern];
        List<(int Offset, IlInstruction Instruction)>? instructions = null;
        int start = 0;

        mask = null;
        if (!IlDisassembler.TryDecode(_pattern, out instructions, out error))
        {
            return false;
        }

        foreach ((int offset, IlInstruction instruction) in instructions)
        {
            if ((operands & IlOpCodes.OperandKind(instruction.OpCode.OperandType)) != IlOperands.None)
            {
                start = offset + instruction.FixedSize;
                Array.Fill(pattern, null, start, offset + instruction.Size - start);
            }
        }

        return TryCreate(pattern, out mask, out error);
    }

    /// <summary>Compares two masks by pattern.</summary>
    /// <param name="other">The mask to compare with.</param>
    /// <returns><see langword="true"/> when both masks have the same bytes and wildcards.</returns>
    public bool Equals(IlMask? other) => other is not null && _pattern.SequenceEqual(other._pattern);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as IlMask);

    /// <inheritdoc/>
    public override int GetHashCode() => _pattern.Aggregate(0, (hash, b) => HashCode.Combine(hash, b));

    // The ranges of consecutive wildcards, e.g. 2..6 for "02 7B ?? ?? ?? ?? 2A".
    private static Range[] WildcardRuns(byte?[] pattern)
    {
        List<Range> runs = [];
        int start = -1;

        for (int i = 0; i <= pattern.Length; i++)
        {
            bool isWildcard = i < pattern.Length && pattern[i] is null;

            if (isWildcard && start < 0)
            {
                start = i;
            }
            else if (!isWildcard && start >= 0)
            {
                runs.Add(start..i);
                start = -1;
            }
        }

        return [.. runs];
    }

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

        return new IlMaskMatch(offset, bytes, wildcards.ToArray()) { Groups = [.. _wildcardRuns.Select(run => bytes[run])] };
    }
}
