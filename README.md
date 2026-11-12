<p align="center">
  <img src="assets/logo.svg" alt="ILMask: IL code on one side, the IDA-style mask on the other, split by 🎭" width="360">
</p>

# ILMask

<p align="center">
  <a href="https://github.com/Neotoxic-off/ILMask/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/Neotoxic-off/ILMask/ci.yml?branch=master&label=CI" alt="CI"></a>
  <a href="https://github.com/Neotoxic-off/ILMask/releases"><img src="https://img.shields.io/github/v/release/Neotoxic-off/ILMask?include_prereleases" alt="Release"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <img src="https://img.shields.io/badge/coverage-100%25-brightgreen" alt="Coverage: 100%">
  <img src="https://img.shields.io/badge/dependencies-0-brightgreen" alt="No dependencies">
</p>

IDA-style masks for .NET IL code. A mask is a byte pattern where `??` matches any byte:

```
02 7B ?? ?? ?? ?? 2A      ldarg.0; ldfld <any field>; ret
```

You can **write** masks (by hand, from a method body, or from ILSpy / dnSpy / ildasm text),
**read** them back from text, and **find** them in IL to pull out the bytes under the wildcards.

- [`docs/instructions.md`](docs/instructions.md): how every IL instruction maps to mask bytes, and which operands are wildcards.
- [Complete example](#complete-example): every feature in one program.
- [Command line](#command-line): the `ilmask` tool, to do the same on assembly files.

## Requirements

.NET 10 SDK.

## Install

Download `ILMask.<version>.nupkg` from the [Releases](https://github.com/Neotoxic-off/ILMask/releases)
into a folder, then add it to your project:

```bash
dotnet add package ILMask --source ./path/to/that/folder
```

Each release also has `ILMask.dll` (with `ILMask.xml` for IntelliSense) if you prefer a plain reference,
and `ILMask.Cli.<version>.nupkg`, the [command-line tool](#command-line).

## Release

The CI (`.github/workflows/ci.yml`) lints, tests and packs every push and pull request; the package is
attached to each run. Pushing a `v` tag publishes a GitHub release with that version:

```bash
git tag v1.0.0
git push origin v1.0.0
```

## No exceptions

Every factory follows the `Try` pattern and never throws, even on `null`:

```csharp
IlMask? mask = null;
string? error = null;

if (!IlMask.TryParse("02 7B ?? ?? ?? ?? 2A", out mask, out error))
{
    Console.WriteLine(error);   // e.g. "Invalid mask byte 'GG' at position 1: expected two hex digits, '?' or '??'."
}
```

Each `Try...` method has two overloads: `(input, out mask)` and `(input, out mask, out error)`.

## Usage

### Read a mask

```csharp
IlMask? mask = null;

IlMask.TryParse("02 7B ?? ?? ?? ?? 2A", out mask);
```

Tokens are separated by whitespace. Each one is a two-digit hex byte (`7B`, `7b`) or a wildcard (`??` or `?`).

### Write a mask

```csharp
byte[] il = typeof(Foo).GetMethod("Bar")!.GetMethodBody()!.GetILAsByteArray()!;
IlMask? fromCode = null;
IlMask? fromIl = null;

// From code: null is a wildcard.
IlMask.TryCreate([0x02, 0x7B, null, null, null, null, 0x2A], out fromCode);

// From a compiled method: metadata tokens (fields, methods, types, strings, signatures)
// are wildcarded, because they change from one build to another. Everything else is exact.
IlMask.TryFromIl(il, out fromIl);

// Same, straight from reflection; fails with a reason when the method has no IL body.
IlMask.TryFromMethod(typeof(Foo).GetMethod("Bar"), out fromIl);

// To text, readable again by IlMask.TryParse.
Console.WriteLine(fromIl!.ToString());   // "02 7B ?? ?? ?? ?? 2A"
```

### Write a mask from ILSpy, dnSpy or ildasm

Copy the IL of a method (or just a few instructions) from your decompiler and paste it:

```csharp
const string listing = """
    .method public hidebysig instance int32 Get (int32 index) cil managed
    {
        .locals init ([0] int32 result)

        IL_0000: ldarg.s index
        IL_0002: ldc.i4.s 42
        IL_0004: stloc.s result
        IL_0006: ldfld int32 Foo::_value
        IL_000b: br.s IL_0000
    }
    """;
IlMask? mask = null;

IlMask.TryFromListing(listing, out mask);
Console.WriteLine(mask!.ToString());   // "0E 01 1F 2A 13 00 7B ?? ?? ?? ?? 2B F3"
```

The result is the same mask as `TryFromIl` on the compiled method: the integration tests check this
on every method of `string`, `List<T>`, `Dictionary<TKey,TValue>`, `Enumerable` and `Math`,
disassembled by the real ILSpy engine.

- **Instructions**: every line that starts with an `IL_xxxx:` label, as all three tools print it.
  dnSpy's `/* 0x... */` byte comments and ildasm's `/bytes` comments are understood. A label inside
  a string (`ldstr "IL_0001:"`) is not an instruction.
- **Ignored text**: method headers, `.maxstack`, `.try` / `catch` blocks, braces, comments.
- **Exact operands**:
  - constants: decimal or hex (`ldc.i4 0x3e8`), floats (`1E-300`), and raw bytes for NaN and
    infinities (`ldc.r4 (00 00 C0 FF)`);
  - branch and `switch` targets, computed from the `IL_xxxx` labels;
  - arguments and locals, by number (`ldloc.s 4`), generated name (`V_4`, `A_4`) or declared name
    (`ldarg.s index`, read from the `.method` header and `.locals` of each pasted method,
    `instance` methods counting `this` as argument 0).
- **Wildcards**: metadata tokens, whose values the text does not show, and argument or local names
  that the pasted text does not declare.

### Find a mask and extract its content

```csharp
foreach (IlMaskMatch match in mask.Find(il))
{
    int offset = match.Offset;                       // where the match starts
    ReadOnlyMemory<byte> bytes = match.Bytes;        // every matched byte
    ReadOnlyMemory<byte> content = match.Wildcards;  // only the bytes under the ?? wildcards
    IReadOnlyList<ReadOnlyMemory<byte>> groups = match.Groups;  // the same bytes, one entry per run of ??
}
```

`Groups` splits the content per run of consecutive wildcards: for `7B ?? ?? ?? ?? 28 ?? ?? ?? ??`,
`Groups[0]` is the field token and `Groups[1]` the method token.

`mask.Find(method)` takes a `MethodBase` directly, and two masks with the same pattern are equal
(`Equals`, `GetHashCode`), so masks work as dictionary keys.

| Method      | Matches start                      | Use it on                    |
|-------------|------------------------------------|------------------------------|
| `Find`      | on an instruction                  | a method body                |
| `FindBytes` | at any byte offset (overlapping)   | a whole file or section      |

Both are lazy and return matches in offset order; `Find(il).FirstOrDefault()` stops at the first one.
`Find` skips, one byte at a time, any bytes that do not decode as an instruction.

Wildcards over a metadata token give you the token, which you can resolve back:

```csharp
IlMask? ldstr = null;

IlMask.TryParse("72 ?? ?? ?? ??", out ldstr);
foreach (IlMaskMatch match in ldstr!.Find(il))
{
    int token = BinaryPrimitives.ReadInt32LittleEndian(match.Wildcards.Span);

    Console.WriteLine(method.Module.ResolveString(token));
}
```

### Find in a type or an assembly

```csharp
foreach (IlMaskMethodMatch found in mask.FindIn(typeof(Foo).Assembly))   // or mask.FindIn(typeof(Foo))
{
    Console.WriteLine($"{found.Method.DeclaringType}::{found.Method.Name} at {found.Match.Offset}");
}
```

`FindIn(Assembly)` reads `assembly.GetTypes()`, which throws when a type cannot be loaded;
in that case call `FindIn(Type)` on the types that did load.

### Loosen a mask

A mask from `TryFromIl` only wildcards metadata tokens. To keep matching after constants change
or code moves, wildcard those operands too:

```csharp
IlMask? loose = null;

mask.TryWildcard(IlOperands.Constants | IlOperands.Branches, out loose);
// "1F 2A 2B 05"  ->  "1F ?? 2B ??"     (ldc.i4.s 42; br.s IL_0009)
```

`Constants` are the operands of `ldc.i4`, `ldc.i4.s`, `ldc.i8`, `ldc.r4`, `ldc.r8` (not the short
forms such as `ldc.i4.1`, which have no operand). `Branches` are branch and `switch` targets; the
`switch` target count stays exact.

### Read a mask as instructions

```csharp
string? listing = null;

mask.TryToListing(out listing);
// IL_0000: ldarg.0
// IL_0001: ldfld ??
// IL_0006: ret
```

It fails, with a reason, when the mask is not a sequence of whole instructions (a wildcard on an
opcode, a cut operand). `TryFromListing` reads the text back to the same mask when the wildcards
cover exactly the metadata tokens.

### Complete example

Every feature in one program (`Program.cs` of a console app referencing ILMask):

```csharp
using System.Buffers.Binary;
using System.Reflection;
using ILMask;

const string Listing = """
    .method public hidebysig instance string Greet () cil managed
    {
        IL_0000: ldstr "Hello, "
        IL_0005: ldarg.0
        IL_0006: ldfld string Greeter::_name
        IL_000b: call string [System.Runtime]System.String::Concat(string, string)
        IL_0010: ret
    }
    """;
MethodInfo method = typeof(Greeter).GetMethod(nameof(Greeter.Greet))!;
byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
byte[] dll = File.ReadAllBytes(typeof(IlMask).Assembly.Location);
IlMask? getter = null;
IlMask? fromIl = null;
IlMask? fromListing = null;
IlMask? reread = null;
IlMask? ldstr = null;
string? error = null;

// 1. Read a mask from text. Failures return false with a reason; nothing throws.
if (!IlMask.TryParse("02 GG", out _, out error))
{
    Console.WriteLine($"TryParse error:  {error}");
}

// 2. Write a mask by hand: null is a wildcard. Same as TryParse("02 7B ?? ?? ?? ?? 2A").
if (IlMask.TryCreate([0x02, 0x7B, null, null, null, null, 0x2A], out getter))
{
    Console.WriteLine($"TryCreate:       {getter}");
}

// 3. Write a mask from a compiled method: metadata tokens become wildcards.
if (IlMask.TryFromIl(il, out fromIl, out error))
{
    Console.WriteLine($"TryFromIl:       {fromIl}");
}

// 4. Write a mask from ILSpy / dnSpy / ildasm text: the same mask as TryFromIl.
if (IlMask.TryFromListing(Listing, out fromListing, out error))
{
    Console.WriteLine($"TryFromListing:  {fromListing}");
}

// 5. ToString is the text form, readable again by TryParse.
if (fromIl is not null && IlMask.TryParse(fromIl.ToString(), out reread))
{
    Console.WriteLine($"Round trip:      {reread.Pattern.SequenceEqual(fromIl.Pattern)}");
}

// 6. Find in a method body (matches start on instructions), then decode the bytes under the wildcards.
if (IlMask.TryParse("72 ?? ?? ?? ??", out ldstr))
{
    foreach (IlMaskMatch match in ldstr.Find(il))
    {
        int token = BinaryPrimitives.ReadInt32LittleEndian(match.Wildcards.Span);

        Console.WriteLine($"Find ldstr:      offset {match.Offset}, \"{method.Module.ResolveString(token)}\"");
    }
}

// 7. FindBytes searches raw bytes at every offset, e.g. a whole .dll.
if (getter is not null)
{
    Console.WriteLine($"FindBytes:       {getter.FindBytes(dll).Count()} field getters in ILMask.dll");
}

internal sealed class Greeter(string name)
{
    private readonly string _name = name;

    public string Greet() => "Hello, " + _name;
}
```

Output:

```
TryParse error:  Invalid mask byte 'GG' at position 1: expected two hex digits, '?' or '??'.
TryCreate:       02 7B ?? ?? ?? ?? 2A
TryFromIl:       72 ?? ?? ?? ?? 02 7B ?? ?? ?? ?? 28 ?? ?? ?? ?? 2A
TryFromListing:  72 ?? ?? ?? ?? 02 7B ?? ?? ?? ?? 28 ?? ?? ?? ?? 2A
Round trip:      True
Find ldstr:      offset 0, "Hello, "
FindBytes:       10 field getters in ILMask.dll
```

## Command line

`ilmask` reads assemblies as files: it never loads or runs them.

```bash
dotnet tool install --global ILMask.Cli --add-source ./path/to/the/folder/with/the/nupkg

ilmask find Foo.dll "02 7B ?? ?? ?? ?? 2A"     # every match, in every method body
# Foo.Bar::get_Value IL_0000: 02 7B 01 00 00 04 2A

ilmask from Foo.dll Foo.Bar::get_Value        # the mask of a method (one line per overload)
# 02 7B ?? ?? ?? ?? 2A

ilmask show "02 7B ?? ?? ?? ?? 2A"            # a mask as IL instructions
# IL_0000: ldarg.0
# IL_0001: ldfld ??
# IL_0006: ret
```

Methods are named `Namespace.Type::Method`, with `Outer/Nested` for nested types. The exit code
is 0, or 1 with the reason on the error output.

## Development

```bash
dotnet build                          # analyzers run as errors (latest-recommended + .editorconfig)
dotnet format --verify-no-changes     # linter
dotnet test                           # unit + integration tests
```

- `tests/ILMask.Tests`: unit tests. `dotnet test` fails if line, branch or method coverage of
  the library drops below 100%.
- `tests/ILMask.IntegrationTests`: runs masks against IL compiled by the C# compiler, against
  methods of the BCL, and against listings produced by ILSpy's disassembler (`ICSharpCode.Decompiler`).

### Code style

- Explicit types everywhere, never `var` (enforced by the linter).
- No `throw` and no `try`/`catch`: failures are `Try...` methods returning `false` and an error.
  The one exception is `ilmask`'s `Program.Run`, which catches the file and image errors of the file it is given.
- In every function, local variables are declared and initialized first, then a blank line,
  then the code.
