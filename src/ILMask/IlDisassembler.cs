using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection.Emit;

namespace ILMask;

/// <summary>Reads a mask pattern back as IL instructions.</summary>
internal static class IlDisassembler
{
    private const string WildcardText = "??";

    /// <summary>Tries to split a pattern into instructions; wildcards may only cover operands.</summary>
    public static bool TryDecode(
        byte?[] pattern,
        [NotNullWhen(true)] out List<(int Offset, IlInstruction Instruction)>? instructions,
        [NotNullWhen(false)] out string? error)
    {
        byte[] il = [.. pattern.Select(b => b.GetValueOrDefault())];
        List<(int Offset, IlInstruction Instruction)> decoded = [];
        int offset = 0;
        IlInstruction instruction = default;

        instructions = null;
        while (offset < il.Length)
        {
            if (!IlInstruction.TryRead(il, offset, out instruction, out error))
            {
                return false;
            }

            if (pattern[offset..(offset + instruction.FixedSize)].Any(b => b is null))
            {
                error = $"Wildcard in the opcode or switch count at offset {offset}: the mask cannot be read as instructions.";
                return false;
            }

            decoded.Add((offset, instruction));
            offset += instruction.Size;
        }

        instructions = decoded;
        error = null;
        return true;
    }

    /// <summary>Tries to write a pattern as a listing, one <c>IL_xxxx: mnemonic operand</c> line per instruction.</summary>
    public static bool TryDisassemble(byte?[] pattern, [NotNullWhen(true)] out string? listing, [NotNullWhen(false)] out string? error)
    {
        List<(int Offset, IlInstruction Instruction)>? instructions = null;

        listing = null;
        if (!TryDecode(pattern, out instructions, out error))
        {
            return false;
        }

        listing = string.Join('\n', instructions.Select(decoded => Line(pattern, decoded.Offset, decoded.Instruction)));
        return true;
    }

    private static string Line(byte?[] pattern, int offset, IlInstruction instruction)
    {
        int end = offset + instruction.Size;
        byte?[] operand = pattern[(offset + instruction.OpCode.Size)..end];
        bool isExact = operand.All(b => b.HasValue);
        string text = isExact ? Operand(instruction.OpCode, [.. operand.Select(b => b.GetValueOrDefault())], end) : WildcardText;

        return string.Create(CultureInfo.InvariantCulture, $"IL_{offset:x4}: {instruction.OpCode.Name} {text}").TrimEnd();
    }

    // The text IlListingOperand reads back to the same bytes; next is the offset of the next instruction.
    private static string Operand(OpCode opCode, byte[] bytes, int next) => opCode.OperandType switch
    {
        OperandType.InlineNone => string.Empty,
        OperandType.ShortInlineI => Text((sbyte)bytes[0]),
        OperandType.InlineI => Text(BinaryPrimitives.ReadInt32LittleEndian(bytes)),
        OperandType.InlineI8 => Text(BinaryPrimitives.ReadInt64LittleEndian(bytes)),
        OperandType.ShortInlineR => Single(bytes),
        OperandType.InlineR => Double(bytes),
        OperandType.ShortInlineVar => Text(bytes[0]),
        OperandType.InlineVar => Text(BinaryPrimitives.ReadUInt16LittleEndian(bytes)),
        OperandType.ShortInlineBrTarget => Label(next + (sbyte)bytes[0]),
        OperandType.InlineBrTarget => Label(next + BinaryPrimitives.ReadInt32LittleEndian(bytes)),
        OperandType.InlineSwitch => $"({string.Join(", ", SwitchTargets(bytes, next))})",
        _ => string.Create(CultureInfo.InvariantCulture, $"0x{BinaryPrimitives.ReadUInt32LittleEndian(bytes):X8}"),
    };

    private static string Single(byte[] bytes)
    {
        float value = BinaryPrimitives.ReadSingleLittleEndian(bytes);

        return float.IsFinite(value) ? Text(value) : Raw(bytes);
    }

    private static string Double(byte[] bytes)
    {
        double value = BinaryPrimitives.ReadDoubleLittleEndian(bytes);

        return double.IsFinite(value) ? Text(value) : Raw(bytes);
    }

    // After the 4-byte count, each target is relative to the end of the switch.
    private static IEnumerable<string> SwitchTargets(byte[] bytes, int next) =>
        Enumerable.Range(1, (bytes.Length / sizeof(int)) - 1)
            .Select(i => Label(next + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(sizeof(int) * i))));

    // NaN and infinities have no exact decimal text: ldc.r4 (00 00 C0 FF).
    private static string Raw(byte[] bytes) => $"({string.Join(' ', bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)))})";

    private static string Label(int offset) => string.Create(CultureInfo.InvariantCulture, $"IL_{offset:x4}");

    private static string Text(IFormattable value) => value.ToString(null, CultureInfo.InvariantCulture);
}
