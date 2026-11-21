using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonConfigEditor.Core;

/// <summary>Eingabe aus dem Textfeld in einen JSON-Wert verwandeln und prüfen.</summary>
public static class FieldValidator
{
    /// <summary>Text → JSON-Wert. Zahlen in Windows-Kultur oder mit Punkt.</summary>
    public static bool TryParse(ConfigField f, string? text, CultureInfo culture, out JsonNode? value, out string? error)
    {
        value = null;
        error = null;
        text ??= "";
        switch (f.Kind)
        {
            case FieldKind.Bool:
                if (bool.TryParse(text.Trim(), out var b)) { value = JsonValue.Create(b); return true; }
                error = "true oder false erwartet";
                return false;

            case FieldKind.Int:
            {
                var t = text.Trim();
                if (!long.TryParse(t, NumberStyles.Integer, culture, out var l)
                    && !long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out l))
                {
                    error = t.Length == 0 ? "Zahl erwartet" : "ganze Zahl erwartet";
                    return false;
                }
                if (!InRange(f, l, culture, out error)) return false;
                value = JsonValue.Create(l);
                return true;
            }

            case FieldKind.Number:
            {
                var t = text.Trim();
                if (!TryDouble(t, culture, out var d))
                {
                    error = "Zahl erwartet";
                    return false;
                }
                if (!InRange(f, d, culture, out error)) return false;
                value = d == Math.Floor(d) && Math.Abs(d) < 1e15 ? JsonValue.Create((long)d) : JsonValue.Create(d);
                return true;
            }

            case FieldKind.Path:
            {
                var check = PathChecker.Check(f, text, baseDir: null);
                if (check.Severity == PathSeverity.Error)
                {
                    error = check.Message;
                    return false;
                }
                value = JsonValue.Create(text);
                return true;
            }

            case FieldKind.String:
                value = JsonValue.Create(text);
                return true;

            default:
                error = "Im Texteditor bearbeiten";
                return false;
        }
    }

    /// <summary>Wirksamen Wert zum Anzeigen im Textfeld.</summary>
    public static string Format(ConfigField f, CultureInfo culture)
    {
        var v = f.EffectiveValue;
        if (v is null) return "";
        if (v is JsonValue jv)
        {
            switch (jv.GetValueKind())
            {
                case JsonValueKind.String: return jv.GetValue<string>();
                case JsonValueKind.Number:
                    return jv.TryGetValue<long>(out var l)
                        ? l.ToString(culture)
                        : jv.GetValue<double>().ToString("G15", culture);
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
            }
        }
        return v.ToJsonString();
    }

    /// <summary>Gespeicherten Wert gegen Typ und Grenzen prüfen (für --validate). null = OK.</summary>
    public static string? Check(ConfigField f, JsonNode? v)
    {
        var kind = v?.GetValueKind();
        switch (f.Kind)
        {
            case FieldKind.Bool when kind is not (JsonValueKind.True or JsonValueKind.False):
                return $"{f.Key}: true/false erwartet, steht {Show(v)}";
            case FieldKind.Int when kind != JsonValueKind.Number || v!.ToJsonString().IndexOfAny(['.', 'e', 'E']) >= 0:
                return $"{f.Key}: ganze Zahl erwartet, steht {Show(v)}";
            case FieldKind.Number when kind != JsonValueKind.Number:
                return $"{f.Key}: Zahl erwartet, steht {Show(v)}";
            case FieldKind.String or FieldKind.Path when kind is not (JsonValueKind.String or JsonValueKind.Null):
                return $"{f.Key}: Text erwartet, steht {Show(v)}";
        }
        if (f.Kind is FieldKind.Int or FieldKind.Number && kind == JsonValueKind.Number)
        {
            var d = double.Parse(v!.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (!InRange(f, d, CultureInfo.InvariantCulture, out var err)) return $"{f.Key}: {err}";
        }
        return null;
    }

    private static string Show(JsonNode? v) => v?.ToJsonString() ?? "null";

    private static bool TryDouble(string t, CultureInfo culture, out double d)
    {
        // Punkt immer erlauben (JSON-Gewohnheit), sonst Windows-Kultur (Komma)
        if (t.Contains('.') && !t.Contains(',') && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            return double.IsFinite(d);
        if (double.TryParse(t, NumberStyles.Float, culture, out d)) return double.IsFinite(d);
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && double.IsFinite(d);
    }

    private static bool InRange(ConfigField f, double v, CultureInfo culture, out string? error)
    {
        error = null;
        if (f.Min is { } min && v < min) error = $"mindestens {min.ToString(culture)}";
        else if (f.Max is { } max && v > max) error = $"höchstens {max.ToString(culture)}";
        return error == null;
    }
}
