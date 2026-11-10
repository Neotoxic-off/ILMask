namespace ILMask;

/// <summary>A place in IL code where an <see cref="IlMask"/> matched.</summary>
/// <param name="Offset">The offset of the first matched byte.</param>
/// <param name="Bytes">All matched bytes.</param>
/// <param name="Wildcards">The bytes under the mask's wildcards, in order: the content the mask extracts.</param>
public sealed record IlMaskMatch(int Offset, ReadOnlyMemory<byte> Bytes, ReadOnlyMemory<byte> Wildcards);
