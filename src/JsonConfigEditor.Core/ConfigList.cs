using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonConfigEditor.Core;

/// <summary>Eine Zeile einer Liste: Nummer = Priorität, Text aus dem Eintrag.</summary>
public sealed record ConfigListItem(int Index, string Label, string? Detail, string ToolTip, JsonNode? Node);

/// <summary>Zeilentexte für Listen (siehe SPEC "Listen sortieren").</summary>
public static class ConfigList
{
    public static IReadOnlyList<ConfigListItem> Items(ConfigField field)
    {
        var list = field.EffectiveList;
        if (list == null) return [];
        var items = new List<ConfigListItem>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            var node = list[i];
            items.Add(new ConfigListItem(i, LabelOf(node, field.ItemLabel, i), DetailOf(node, field.ItemDetail), ToolTipOf(node), node));
        }
        return items;
    }

    /// <summary>
    /// Objekt: Wert von itemLabel, sonst der ersten String-Eigenschaft. String/Zahl: der Wert selbst. Sonst "Eintrag N".
    /// </summary>
    public static string LabelOf(JsonNode? node, string? itemLabel, int index)
    {
        switch (node)
        {
            case JsonObject o:
                if (itemLabel != null && Find(o, itemLabel) is { } wanted && Scalar(wanted) is { Length: > 0 } w) return w;
                foreach (var (_, v) in o)
                    if (v is JsonValue sv && sv.GetValueKind() == JsonValueKind.String && sv.GetValue<string>() is { Length: > 0 } s) return s;
                break;
            case JsonValue v when Scalar(v) is { Length: > 0 } s:
                return s;
        }
        return $"Eintrag {index + 1}";
    }

    private static string? DetailOf(JsonNode? node, List<string> names)
    {
        if (node is not JsonObject o || names.Count == 0) return null;
        var parts = names
            .Select(n => (Name: n, Value: Find(o, n) is { } v ? Scalar(v) : null))
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .Select(p => $"{p.Name}: {p.Value}")
            .ToList();
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string ToolTipOf(JsonNode? node) => node switch
    {
        JsonObject o => string.Join(Environment.NewLine, o.Select(kv => $"{kv.Key}: {Scalar(kv.Value) ?? kv.Value?.ToJsonString() ?? "null"}")),
        _ => Scalar(node) ?? node?.ToJsonString() ?? "null",
    };

    private static JsonNode? Find(JsonObject o, string name)
    {
        foreach (var (k, v) in o)
            if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) return v;
        return null;
    }

    private static string? Scalar(JsonNode? n) => n is JsonValue v
        ? v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : v.ToJsonString()
        : null;

    /// <summary>
    /// Wo steht <paramref name="item"/> jetzt in <paramref name="list"/>? Erst exakt gleich, sonst eindeutig gleicher Zeilentext
    /// (die App kann z. B. LastSeen inzwischen aufgefrischt haben). -1 = nicht (eindeutig) gefunden.
    /// </summary>
    internal static int Locate(JsonArray list, JsonNode? item, string? itemLabel, int hint)
    {
        if (hint >= 0 && hint < list.Count && JsonNode.DeepEquals(list[hint], item)) return hint;
        for (var i = 0; i < list.Count; i++)
            if (JsonNode.DeepEquals(list[i], item)) return i;

        var label = LabelOf(item, itemLabel, -1);
        if (label == LabelOf(null, null, -1)) return -1; // kein Text, an dem man ihn erkennt
        var hits = Enumerable.Range(0, list.Count).Where(i => LabelOf(list[i], itemLabel, i) == label).ToList();
        return hits.Count == 1 ? hits[0] : -1;
    }
}
