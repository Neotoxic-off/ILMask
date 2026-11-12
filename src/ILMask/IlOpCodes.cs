using System.Collections.Frozen;
using System.Reflection;
using System.Reflection.Emit;

namespace ILMask;

/// <summary>The IL opcode table, built from <see cref="OpCodes"/>.</summary>
internal static class IlOpCodes
{
    private static readonly OpCode[] All = [.. typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .Where(opCode => opCode.OpCodeType != OpCodeType.Nternal)];

    /// <summary>Gets the opcodes by encoded value (two-byte opcodes are <c>0xFExx</c>).</summary>
    public static FrozenDictionary<short, OpCode> ByValue { get; } = All.ToFrozenDictionary(opCode => opCode.Value);

    /// <summary>Gets the opcodes by mnemonic, such as <c>ldarg.0</c>, ignoring case.</summary>
    public static FrozenDictionary<string, OpCode> ByName { get; } =
        All.ToFrozenDictionary(opCode => opCode.Name!, StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the operand size in bytes; for <c>switch</c>, only its 4-byte target count.</summary>
    public static int OperandSize(OperandType type) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        _ => 4,
    };

    /// <summary>Gets the kind of operand that <see cref="IlMask.TryWildcard(IlOperands, out IlMask)"/> can wildcard.</summary>
    public static IlOperands OperandKind(OperandType type) => type switch
    {
        OperandType.ShortInlineI or OperandType.InlineI or OperandType.InlineI8
            or OperandType.ShortInlineR or OperandType.InlineR => IlOperands.Constants,
        OperandType.ShortInlineBrTarget or OperandType.InlineBrTarget or OperandType.InlineSwitch => IlOperands.Branches,
        _ => IlOperands.None,
    };

    /// <summary>Gets a value indicating whether the operand is a metadata token.</summary>
    public static bool HasTokenOperand(OperandType type) => type is OperandType.InlineField or OperandType.InlineMethod
        or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType;
}
