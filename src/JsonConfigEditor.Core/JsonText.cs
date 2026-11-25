using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonConfigEditor.Core;

/// <summary>Gemeinsame JSON-Einstellungen. Keys sind wie bei IConfiguration case-insensitive.</summary>
public static class JsonText
{
    public static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };

    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Datei als JSON-Objekt lesen. Wirft <see cref="ConfigEditorException"/> (Exit 2) bei kaputtem JSON.</summary>
    public static JsonObject ParseObject(string text, string what)
    {
        if (string.IsNullOrWhiteSpace(text)) return NewObject();
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text, NodeOptions, DocumentOptions);
            // JsonObject liest lazy: doppelte Keys ("a" und "A") fallen erst beim Zugriff auf, hier gleich prüfen
            if (node is JsonObject o) Touch(o);
        }
        catch (JsonException ex)
        {
            throw new ConfigEditorException($"{what} ist kein gültiges JSON: {ex.Message}", ExitCodes.BrokenFile, ex);
        }
        catch (ArgumentException ex)
        {
            throw new ConfigEditorException($"{what}: Key doppelt (Groß-/Kleinschreibung zählt nicht): {ex.Message}", ExitCodes.BrokenFile, ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConfigEditorException($"{what} ist ungültig: {ex.Message}", ExitCodes.BrokenFile, ex);
        }
        return node as JsonObject ?? throw new ConfigEditorException($"{what} ist kein JSON-Objekt", ExitCodes.BrokenFile);
    }

    private static void Touch(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var kv in o) Touch(kv.Value);
                break;
            case JsonArray a:
                foreach (var item in a) Touch(item);
                break;
        }
    }

    public static JsonObject NewObject() => new(NodeOptions);

    public static string Serialize(JsonNode node) => node.ToJsonString(WriteOptions);

    /// <summary>Wie JsonNode.DeepEquals, aber Zahlen werden nach Wert verglichen (7 == 7.0).</summary>
    public static bool ValuesEqual(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.GetValueKind() == JsonValueKind.Number && b.GetValueKind() == JsonValueKind.Number)
        {
            if (TryDecimal(a, out var da) && TryDecimal(b, out var db)) return da == db;
            return double.Parse(a.ToJsonString(), CultureInfo.InvariantCulture) == double.Parse(b.ToJsonString(), CultureInfo.InvariantCulture);
        }
        return JsonNode.DeepEquals(a, b);
    }

    private static bool TryDecimal(JsonNode n, out decimal d) =>
        decimal.TryParse(n.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d);

    /// <summary>
    /// Obere Ebene über die untere legen wie IConfiguration: Objekte Key für Key (case-insensitive),
    /// Arrays nach Index, sonst gewinnt oben. Eine leere obere Liste ändert nichts. Liefert immer eine Kopie.
    /// </summary>
    public static JsonNode? MergeLikeConfiguration(JsonNode? lower, JsonNode? upper)
    {
        if (upper is null) return lower?.DeepClone();
        switch (lower, upper)
        {
            case (JsonObject lo, JsonObject uo):
            {
                var result = (JsonObject)lo.DeepClone();
                foreach (var (k, v) in uo)
                {
                    var existing = result.TryGetPropertyValue(k, out var e) ? e : null;
                    var merged = MergeLikeConfiguration(existing, v);
                    if (result.ContainsKey(k)) result[k] = merged;
                    else result.Add(k, merged);
                }
                return result;
            }
            case (JsonArray la, JsonArray ua):
            {
                var result = (JsonArray)la.DeepClone();
                for (var i = 0; i < ua.Count; i++)
                {
                    if (i < result.Count) result[i] = MergeLikeConfiguration(result[i], ua[i]);
                    else result.Add(ua[i]?.DeepClone());
                }
                return result;
            }
            default:
                return upper.DeepClone();
        }
    }

    /// <summary>Wert unter "A:B:C" holen, null wenn nicht da.</summary>
    public static JsonNode? GetPath(JsonObject root, string key)
    {
        JsonNode? cur = root;
        foreach (var part in key.Split(':'))
        {
            if (cur is not JsonObject o || !o.TryGetPropertyValue(part, out cur)) return null;
        }
        return cur;
    }

    public static bool HasPath(JsonObject root, string key)
    {
        JsonNode? cur = root;
        var parts = key.Split(':');
        for (var i = 0; i < parts.Length; i++)
        {
            if (cur is not JsonObject o || !o.TryGetPropertyValue(parts[i], out cur)) return false;
        }
        return true;
    }

    /// <summary>Wert unter "A:B:C" setzen, Zwischenobjekte anlegen.</summary>
    public static void SetPath(JsonObject root, string key, JsonNode? value)
    {
        var parts = key.Split(':');
        var cur = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (cur[parts[i]] is not JsonObject next)
            {
                next = NewObject();
                cur[parts[i]] = next;
            }
            cur = next;
        }
        cur[parts[^1]] = value;
    }

    /// <summary>Key entfernen und dabei leer gewordene Objekte darüber aufräumen (nie die Wurzel).</summary>
    public static bool RemovePath(JsonObject root, string key)
    {
        var parts = key.Split(':');
        var chain = new List<JsonObject> { root };
        var cur = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (cur[parts[i]] is not JsonObject next) return false;
            chain.Add(next);
            cur = next;
        }
        if (!cur.Remove(parts[^1])) return false;
        for (var i = chain.Count - 1; i > 0; i--)
        {
            if (chain[i].Count > 0) break;
            chain[i - 1].Remove(parts[i - 1]);
        }
        return true;
    }
}
