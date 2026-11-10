using System.Text.RegularExpressions;

namespace ILMask;

/// <summary>
/// The parameter and local names of one method in an IL listing, read from its
/// <c>.method</c> header and <c>.locals</c> block, so that <c>ldarg.s x</c> can be encoded.
/// </summary>
internal sealed partial class IlListingSymbols
{
    private readonly Dictionary<string, int> _arguments;
    private readonly Dictionary<string, int> _locals;

    private IlListingSymbols(Dictionary<string, int> arguments, Dictionary<string, int> locals)
    {
        _arguments = arguments;
        _locals = locals;
    }

    /// <summary>Reads the symbols declared in the listing of one method.</summary>
    public static IlListingSymbols Read(string method) => new(ReadArguments(method), ReadLocals(method));

    /// <summary>Tries to find the index of an argument or a local by name.</summary>
    public bool TryGetIndex(string name, bool isLocal, out int index) =>
        (isLocal ? _locals : _arguments).TryGetValue(name, out index);

    // ".method public hidebysig instance int32 Add (int32 a, int32 b) cil managed {": a is 1, b is 2.
    private static Dictionary<string, int> ReadArguments(string method)
    {
        Dictionary<string, int> arguments = [];
        string text = MethodHeaderRegex().Match(method).Value;
        int close = text.LastIndexOf(')');
        int open = MatchingOpen(text, close);
        int first = open >= 0 && InstanceRegex().IsMatch(text[..open]) ? 1 : 0;
        List<string> parameters = open >= 0 ? SplitTopLevel(text[(open + 1)..close]) : [];

        for (int i = 0; i < parameters.Count; i++)
        {
            arguments.TryAdd(LastToken(parameters[i]), first + i);
        }

        return arguments;
    }

    // ".locals init ([0] int32 count, [1] string)": count is 0; [1] has no name.
    private static Dictionary<string, int> ReadLocals(string method)
    {
        Dictionary<string, int> locals = [];
        Match block = LocalsRegex().Match(method);
        int open = block.Index + block.Length - 1;
        int close = block.Success ? MatchingClose(method, open) : -1;
        List<string> entries = close >= 0 ? SplitTopLevel(method[(open + 1)..close]) : [];

        foreach (string entry in entries)
        {
            AddLocal(locals, entry);
        }

        return locals;
    }

    private static void AddLocal(Dictionary<string, int> locals, string entry)
    {
        Match local = LocalEntryRegex().Match(entry);
        int index = 0;

        if (local.Success && int.TryParse(local.Groups[1].ValueSpan, out index))
        {
            locals.TryAdd(LastToken(local.Groups[2].Value), index);
        }
    }

    private static int MatchingOpen(string text, int close)
    {
        int depth = 0;

        for (int i = close; i >= 0; i--)
        {
            depth += text[i] switch { ')' => 1, '(' => -1, _ => 0 };
            if (depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static int MatchingClose(string text, int open)
    {
        int depth = 0;

        for (int i = open; i < text.Length; i++)
        {
            depth += text[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    // Splits on commas outside brackets, since generic types such as Dictionary`2<string, int32> contain commas.
    private static List<string> SplitTopLevel(string text)
    {
        List<string> parts = [];
        int depth = 0;
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            depth += text[i] switch { '(' or '<' or '[' => 1, ')' or '>' or ']' => -1, _ => 0 };
            if (text[i] == ',' && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        return [.. parts.Where(part => !string.IsNullOrWhiteSpace(part))];
    }

    private static string LastToken(string text) =>
        IlListingOperand.Unquote(text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[^1]);

    [GeneratedRegex(@"\.method\b[^{]*")]
    private static partial Regex MethodHeaderRegex();

    [GeneratedRegex(@"\binstance\b")]
    private static partial Regex InstanceRegex();

    [GeneratedRegex(@"\.locals\b[^(]*\(")]
    private static partial Regex LocalsRegex();

    [GeneratedRegex(@"^\s*\[(\d+)\]\s*(\S.*)$", RegexOptions.Singleline)]
    private static partial Regex LocalEntryRegex();
}
