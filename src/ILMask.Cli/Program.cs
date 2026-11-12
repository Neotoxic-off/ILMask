using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILMask.Cli;

/// <summary>The <c>ilmask</c> command. It reads assemblies as files and never loads or runs them.</summary>
internal static class Program
{
    private const string Usage = """
        Usage:
          ilmask find <assembly> <mask>           find a mask in every method body
          ilmask from <assembly> <Type::Method>   write the mask of a method
          ilmask show <mask>                      read a mask as IL instructions
        """;

    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Runs one command; returns 0, or 1 after writing the reason to <paramref name="errors"/>.</summary>
    internal static int Run(string[] args, TextWriter output, TextWriter errors)
    {
        string? error = null;

        // The only try/catch of the repository: a file given by the user is a trust boundary, and the readers throw.
        try
        {
            error = args switch
            {
                ["find", string file, _, ..] => Find(file, string.Join(' ', args[2..]), output),
                ["from", string file, string name] => From(file, name, output),
                ["show", _, ..] => Show(string.Join(' ', args[1..]), output),
                _ => Usage,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            error = exception.Message;
        }

        if (error is not null)
        {
            errors.WriteLine(error);
        }

        return error is null ? 0 : 1;
    }

    private static string? Find(string file, string text, TextWriter output)
    {
        IlMask? mask = null;
        List<(string Name, byte[] Il)>? methods = null;
        string? error = null;

        if (!IlMask.TryParse(text, out mask, out error) || !TryReadMethods(file, out methods, out error))
        {
            return error;
        }

        foreach ((string name, byte[] il) in methods)
        {
            foreach (IlMaskMatch match in mask.Find(il))
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name} IL_{match.Offset:x4}: {Spaced(match.Bytes)}"));
            }
        }

        return null;
    }

    private static string? From(string file, string name, TextWriter output)
    {
        List<(string Name, byte[] Il)>? methods = null;
        IlMask? mask = null;
        string? error = null;
        bool found = false;

        if (!TryReadMethods(file, out methods, out error))
        {
            return error;
        }

        foreach ((string _, byte[] il) in methods.Where(method => method.Name == name))
        {
            if (!IlMask.TryFromIl(il, out mask, out error))
            {
                return error;
            }

            found = true;
            output.WriteLine(mask.ToString());
        }

        return found ? null : $"No method '{name}' with an IL body in '{file}'.";
    }

    private static string? Show(string text, TextWriter output)
    {
        IlMask? mask = null;
        string? listing = null;
        string? error = null;

        if (!IlMask.TryParse(text, out mask, out error) || !mask.TryToListing(out listing, out error))
        {
            return error;
        }

        output.WriteLine(listing);
        return null;
    }

    // Every method with an IL body, named Namespace.Type::Method (Outer/Nested for nested types).
    private static bool TryReadMethods(
        string file, [NotNullWhen(true)] out List<(string Name, byte[] Il)>? methods, [NotNullWhen(false)] out string? error)
    {
        using PEReader pe = new(File.OpenRead(file));
        MetadataReader? reader = pe.HasMetadata ? pe.GetMetadataReader() : null;
        List<(string Name, byte[] Il)> found = [];

        methods = null;
        if (reader is null)
        {
            error = $"'{file}' is not a .NET assembly.";
            return false;
        }

        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            AddMethods(found, pe, reader, reader.GetTypeDefinition(handle));
        }

        methods = found;
        error = null;
        return true;
    }

    private static void AddMethods(List<(string Name, byte[] Il)> found, PEReader pe, MetadataReader reader, TypeDefinition type)
    {
        string typeName = TypeName(reader, type);
        MethodDefinition method = default;
        byte[]? il = null;

        foreach (MethodDefinitionHandle handle in type.GetMethods())
        {
            method = reader.GetMethodDefinition(handle);
            il = HasIlBody(method) ? pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes() : null;
            if (il is not null)
            {
                found.Add(($"{typeName}::{reader.GetString(method.Name)}", il));
            }
        }
    }

    private static bool HasIlBody(MethodDefinition method) =>
        method.RelativeVirtualAddress != 0 && (method.ImplAttributes & MethodImplAttributes.CodeTypeMask) == MethodImplAttributes.IL;

    private static string TypeName(MetadataReader reader, TypeDefinition type)
    {
        TypeDefinitionHandle declaring = type.GetDeclaringType();
        string name = reader.GetString(type.Name);
        string ns = reader.GetString(type.Namespace);

        if (!declaring.IsNil)
        {
            return $"{TypeName(reader, reader.GetTypeDefinition(declaring))}/{name}";
        }

        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    private static string Spaced(ReadOnlyMemory<byte> bytes) =>
        string.Join(' ', bytes.ToArray().Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
}
