using System.Text;

namespace QuickMenu.Services.GameSources;

/// <summary>
/// Reads Valve's text KeyValues format (libraryfolders.vdf, appmanifest_*.acf): quoted keys followed by a quoted
/// value or a { … } block. Keys compare case-insensitively, as Steam treats them.
/// </summary>
public static class Vdf
{
    public sealed class Node : Dictionary<string, object>
    {
        public Node() : base(StringComparer.OrdinalIgnoreCase) { }
        public string? Text(string key) => TryGetValue(key, out var value) ? value as string : null;
        public Node? Child(string key) => TryGetValue(key, out var value) ? value as Node : null;
    }

    public static Node Parse(string text)
    {
        var position = 0;
        return Block(text, ref position, topLevel: true);
    }

    private static Node Block(string text, ref int position, bool topLevel)
    {
        var node = new Node();
        while (true)
        {
            var key = Token(text, ref position);
            if (key is null || (key == "}" && !topLevel)) return node;
            if (key == "}") continue;
            var value = Token(text, ref position);
            if (value is null) return node;
            if (value == "{") node[key] = Block(text, ref position, topLevel: false);
            else node[key] = value;
        }
    }

    // A quoted string (with \" \\ \n \t escapes), a brace, or a bare word; // comments are skipped.
    private static string? Token(string text, ref int position)
    {
        while (position < text.Length)
        {
            var c = text[position];
            if (char.IsWhiteSpace(c)) { position++; continue; }
            if (c == '/' && position + 1 < text.Length && text[position + 1] == '/') { while (position < text.Length && text[position] != '\n') position++; continue; }
            if (c is '{' or '}') { position++; return c.ToString(); }
            if (c == '"')
            {
                var builder = new StringBuilder();
                position++;
                while (position < text.Length && text[position] != '"')
                {
                    if (text[position] == '\\' && position + 1 < text.Length)
                    {
                        position++;
                        builder.Append(text[position] switch { 'n' => '\n', 't' => '\t', var other => other });
                    }
                    else builder.Append(text[position]);
                    position++;
                }
                position++;
                return builder.ToString();
            }
            var start = position;
            while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not ('{' or '}' or '"')) position++;
            return text[start..position];
        }
        return null;
    }
}
