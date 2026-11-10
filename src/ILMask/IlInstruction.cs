using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;

namespace ILMask;

/// <summary>One decoded IL instruction: its opcode and total size in bytes.</summary>
internal readonly record struct IlInstruction(OpCode OpCode, int Size)
{
    private const byte TwoByteOpCodePrefix = 0xFE;

    /// <summary>Gets a value indicating whether the operand is a metadata token.</summary>
    public bool HasTokenOperand => IlOpCodes.HasTokenOperand(OpCode.OperandType);

    /// <summary>Tries to decode the instruction starting at <paramref name="offset"/>.</summary>
    /// <returns><see langword="false"/> when the IL is truncated or has an unknown opcode.</returns>
    public static bool TryRead(ReadOnlySpan<byte> il, int offset, out IlInstruction instruction, [NotNullWhen(false)] out string? error)
    {
        ReadOnlySpan<byte> code = il[offset..];
        short value = code[0] == TwoByteOpCodePrefix && code.Length > 1 ? (short)(TwoByteOpCodePrefix << 8 | code[1]) : code[0];
        OpCode opCode = default;
        long size = 0;

        instruction = default;
        if (!IlOpCodes.ByValue.TryGetValue(value, out opCode))
        {
            error = $"Unknown opcode 0x{(ushort)value:X2} at offset {offset}.";
            return false;
        }

        size = opCode.Size + IlOpCodes.OperandSize(opCode.OperandType) + SwitchTargetsSize(code, opCode);
        if (size > code.Length)
        {
            error = $"Truncated {opCode.Name} at offset {offset}: needs {size} bytes, {code.Length} left.";
            return false;
        }

        error = null;
        instruction = new IlInstruction(opCode, (int)size);
        return true;
    }

    // A switch operand is a 4-byte target count followed by that many 4-byte targets.
    private static long SwitchTargetsSize(ReadOnlySpan<byte> code, OpCode opCode)
    {
        bool hasCount = opCode.OperandType == OperandType.InlineSwitch && code.Length >= opCode.Size + sizeof(uint);

        return hasCount ? sizeof(uint) * (long)BinaryPrimitives.ReadUInt32LittleEndian(code[opCode.Size..]) : 0;
    }
}
