using System.Text;
using System.Text.Json;

namespace JsonConfigEditor.Core;

/// <summary>
/// Holt <c>//</c>- und <c>/* */</c>-Kommentare aus einer appsettings.json als Beschreibung.
/// Kommentar in den Zeilen direkt vor einem Key gehört zu dem Key. Kommentar in derselben Zeile
/// hinter einem Wert gehört zum Key davor. Kommentare in Arrays werden ignoriert.
/// </summary>
public static class CommentScanner
{
    /// <returns>Key im Format "A:B" → Beschreibung. Case-insensitive.</returns>
    public static Dictionary<string, string> Scan(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bytes = Encoding.UTF8.GetBytes(json);
        var span = bytes.AsSpan();
        if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) span = span[3..];

        var reader = new Utf8JsonReader(span, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Allow,
            AllowTrailingCommas = true,
        });

        var stack = new List<(string? Name, bool IsArray)>();
        string? pendingProperty = null;
        string? lastKey = null;
        var pending = new List<string>();
        long lastTokenEnd = 0;

        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.Comment:
                    {
                        var text = Clean(reader.GetComment());
                        var sameLine = !span[(int)lastTokenEnd..(int)reader.TokenStartIndex].Contains((byte)'\n');
                        if (sameLine && lastKey != null && lastTokenEnd > 0)
                        {
                            if (text.Length > 0)
                                result[lastKey] = result.TryGetValue(lastKey, out var existing) ? existing + " " + text : text;
                        }
                        else if (text.Length > 0)
                        {
                            pending.Add(text);
                        }
                        break;
                    }
                    case JsonTokenType.PropertyName:
                    {
                        var name = reader.GetString()!;
                        pendingProperty = name;
                        if (InArray(stack))
                        {
                            lastKey = null;
                        }
                        else
                        {
                            var key = string.Join(':', stack.Where(s => s.Name != null).Select(s => s.Name).Append(name));
                            if (pending.Count > 0) result[key] = string.Join(" ", pending);
                            lastKey = key;
                        }
                        pending.Clear();
                        break;
                    }
                    case JsonTokenType.StartObject:
                        stack.Add((pendingProperty, false));
                        pendingProperty = null;
                        pending.Clear();
                        lastKey = null;
                        break;
                    case JsonTokenType.StartArray:
                        stack.Add((pendingProperty, true));
                        pendingProperty = null;
                        pending.Clear();
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        if (stack.Count > 0)
                        {
                            var closed = stack[^1];
                            stack.RemoveAt(stack.Count - 1);
                            // Kommentar in derselben Zeile hinter "]" gehört zum Array-Key
                            lastKey = closed.IsArray && closed.Name != null && !InArray(stack)
                                ? string.Join(':', stack.Where(s => s.Name != null).Select(s => s.Name).Append(closed.Name))
                                : null;
                        }
                        pending.Clear();
                        break;
                    default:
                        // Wert: lastKey bleibt, damit ein Kommentar dahinter in derselben Zeile zum Key passt
                        pendingProperty = null;
                        pending.Clear();
                        if (InArray(stack)) lastKey = null;
                        break;
                }
                if (reader.TokenType != JsonTokenType.Comment) lastTokenEnd = reader.BytesConsumed;
            }
        }
        catch (JsonException)
        {
            // Kaputtes JSON: was bis dahin da ist, reicht für Beschreibungen. Den Fehler meldet das Laden selbst.
        }
        return result;
    }

    private static bool InArray(List<(string? Name, bool IsArray)> stack) => stack.Any(s => s.IsArray);

    private static string Clean(string comment)
    {
        var lines = comment.Split('\n')
            .Select(l => l.Trim().TrimStart('*').Trim())
            .Where(l => l.Length > 0);
        return string.Join(" ", lines);
    }
}
