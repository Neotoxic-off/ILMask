using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection.Emit;
using System.Text.RegularExpressions;

namespace ILMask;

/// <summary>
/// Encodes the operand of one listing instruction exactly, as the compiler did.
/// Only metadata tokens, whose values the text does not show, become wildcards.
/// </summary>
internal static partial class IlListingOperand
{
    /// <summary>Tries to encode the operand text of <paramref name="opCode"/> at <paramref name="offset"/>.</summary>
    /// <param name="opCode">The instruction opcode.</param>
    /// <param name="offset">The instruction offset, from its <c>IL_xxxx</c> label; branch targets are relative to it.</param>
    /// <param name="text">The text after the mnemonic, up to the next instruction.</param>
    /// <param name="symbols">The argument and local names of the method.</param>
    /// <param name="operand">The encoded operand; <see langword="null"/> entries are wildcards.</param>
    public static bool TryEncode(
        OpCode opCode, int offset, string text, IlListingSymbols symbols, [NotNullWhen(true)] out byte?[]? operand)
    {
        string token = FirstToken(text);
        int next = offset + opCode.Size + IlOpCodes.OperandSize(opCode.OperandType);

        operand = opCode.OperandType switch
        {
            OperandType.InlineNone => [],
            OperandType.ShortInlineI => Integer(token, sbyte.MinValue, byte.MaxValue, sizeof(byte)),
            OperandType.InlineI => Integer(token, int.MinValue, uint.MaxValue, sizeof(int)),
            OperandType.InlineI8 => Integer(token, long.MinValue, long.MaxValue, sizeof(long)),
            OperandType.ShortInlineR => Real(text, token, sizeof(float)),
            OperandType.InlineR => Real(text, token, sizeof(double)),
            OperandType.ShortInlineVar => Variable(opCode, token, symbols, byte.MaxValue, sizeof(byte)),
            OperandType.InlineVar => Variable(opCode, token, symbols, ushort.MaxValue, sizeof(ushort)),
            OperandType.ShortInlineBrTarget => Branch(token, next, sizeof(byte)),
            OperandType.InlineBrTarget => Branch(token, next, sizeof(int)),
            OperandType.InlineSwitch => Switch(text, offset + opCode.Size),
            _ => Wildcards(sizeof(int)),
        };
        return operand is not null;
    }

    /// <summary>Removes the quotes ILSpy and ildasm put around names such as <c>'object'</c>.</summary>
    public static string Unquote(string name) => name.Trim('\'');

    // ldc.i4 -1000000 // 0xfff0bdc0 -> -1000000; ildasm writes raw bits in hex: ldc.i4 0xfff0bdc0.
    private static byte?[]? Integer(string token, long min, long max, int size)
    {
        bool isHex = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        ulong bits = 0;
        long value = 0;
        bool validHex = isHex
            && ulong.TryParse(token.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bits)
            && (size == sizeof(long) || bits >> (8 * size) == 0);
        bool validDecimal = !isHex
            && long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
            && value >= min && value <= max;

        if (validHex)
        {
            return LittleEndian((long)bits, size);
        }

        return validDecimal ? LittleEndian(value, size) : null;
    }

    // ldc.r8 1E-300, or raw bytes for NaN and infinities: ldc.r4 (00 00 C0 FF).
    private static byte?[]? Real(string text, string token, int size)
    {
        bool isRaw = token.StartsWith('(');
        byte[]? raw = isRaw ? HexBytes(ParenthesesRegex().Match(text).Groups[1].Value) : null;
        float single = 0;
        double dbl = 0;
        bool parsed = size == sizeof(float)
            ? float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out single)
            : double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out dbl);
        long bits = size == sizeof(float) ? BitConverter.SingleToInt32Bits(single) : BitConverter.DoubleToInt64Bits(dbl);

        if (isRaw)
        {
            return raw?.Length == size ? [.. raw.Select(b => (byte?)b)] : null;
        }

        return parsed ? LittleEndian(bits, size) : null;
    }

    // ldloc.s 4, ldloc.s V_4, ldarg.s A_4 or ldarg.s name; a name the listing does not declare becomes a wildcard.
    private static byte?[]? Variable(OpCode opCode, string token, IlListingSymbols symbols, int max, int size)
    {
        string name = Unquote(token);
        bool isLocal = opCode.Name!.Contains("loc", StringComparison.Ordinal);
        int index = 0;
        bool known = int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out index)
            || symbols.TryGetIndex(name, isLocal, out index)
            || (name.StartsWith(isLocal ? "V_" : "A_", StringComparison.Ordinal)
                && int.TryParse(name.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out index));

        if (name.Length == 0)
        {
            return null;
        }

        if (!known)
        {
            return Wildcards(size);
        }

        return index <= max ? LittleEndian(index, size) : null;
    }

    // br.s IL_0010: the operand is the target minus the offset of the next instruction.
    private static byte?[]? Branch(string token, int next, int size)
    {
        bool isLabel = TryParseLabel(token, out int target);
        long delta = (long)target - next;
        long limit = size == sizeof(byte) ? sbyte.MaxValue : int.MaxValue;

        return isLabel && delta >= -limit - 1 && delta <= limit ? LittleEndian(delta, size) : null;
    }

    // switch (IL_0010, IL_0020): a target count, then each target relative to the end of the switch.
    private static byte?[]? Switch(string text, int operandOffset)
    {
        Match targets = ParenthesesRegex().Match(text);
        string[] labels = targets.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int next = operandOffset + (sizeof(int) * (labels.Length + 1));
        List<byte?> operand = [.. LittleEndian(labels.Length, sizeof(int))];
        int target = 0;

        if (!targets.Success)
        {
            return null;
        }

        foreach (string label in labels)
        {
            if (!TryParseLabel(label, out target))
            {
                return null;
            }

            operand.AddRange(LittleEndian((long)target - next, sizeof(int)));
        }

        return [.. operand];
    }

    private static bool TryParseLabel(string token, out int offset)
    {
        offset = 0;
        return token.StartsWith("IL_", StringComparison.Ordinal)
            && int.TryParse(token.AsSpan(3), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out offset);
    }

    private static byte[]? HexBytes(string text)
    {
        string[] tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        byte[] bytes = new byte[tokens.Length];

        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Length != 2 || !byte.TryParse(tokens[i], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[i]))
            {
                return null;
            }
        }

        return bytes;
    }

    private static byte?[] LittleEndian(long value, int size)
    {
        byte[] bytes = new byte[sizeof(long)];

        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return [.. bytes.Take(size).Select(b => (byte?)b)];
    }

    private static byte?[] Wildcards(int size) => new byte?[size];

    private static string FirstToken(string text)
    {
        string[] tokens = text.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);

        return tokens.Length == 0 ? string.Empty : tokens[0];
    }

    [GeneratedRegex(@"\(([^)]*)\)")]
    private static partial Regex ParenthesesRegex();
}
