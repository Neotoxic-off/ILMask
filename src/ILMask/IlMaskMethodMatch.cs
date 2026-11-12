using System.Reflection;

namespace ILMask;

/// <summary>A match of an <see cref="IlMask"/> in the IL body of a method.</summary>
/// <param name="Method">The method or constructor whose body matched.</param>
/// <param name="Match">Where it matched, and the bytes under the wildcards.</param>
public sealed record IlMaskMethodMatch(MethodBase Method, IlMaskMatch Match);
