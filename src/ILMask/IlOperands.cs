namespace ILMask;

/// <summary>The kinds of operands that <see cref="IlMask.TryWildcard(IlOperands, out IlMask)"/> turns into wildcards.</summary>
[Flags]
public enum IlOperands
{
    /// <summary>No operand.</summary>
    None = 0,

    /// <summary>Integer and floating-point constants, such as the operand of <c>ldc.i4</c> or <c>ldc.r8</c>.</summary>
    Constants = 1,

    /// <summary>Branch and <c>switch</c> targets, which move when the code between them changes.</summary>
    Branches = 2,
}
