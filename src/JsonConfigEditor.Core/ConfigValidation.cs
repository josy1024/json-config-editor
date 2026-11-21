namespace JsonConfigEditor.Core;

public sealed record ValidationResult(int ExitCode, IReadOnlyList<string> Problems);

/// <summary>Prüft Descriptor und Dateien ohne UI (für <c>--validate</c>, CI der App, IT).</summary>
public static class ConfigValidation
{
    public static ValidationResult Run(ConfigEditorOptions options)
    {
        ConfigDocument doc;
        try { doc = ConfigDocument.Load(options); }
        catch (ConfigEditorException ex) { return new(ex.ExitCode, [ex.Message]); }

        if (doc.UserFileError != null) return new(ExitCodes.BrokenFile, [doc.UserFileError]);

        var problems = new List<string>(doc.Warnings);
        foreach (var f in doc.Fields)
        {
            if (f.Kind is FieldKind.Array or FieldKind.Object) continue;
            if (f.HasBase && FieldValidator.Check(f, f.BaseValue) is { } b) problems.Add("Basis: " + b);
            if (f.HasMachine && FieldValidator.Check(f, f.MachineValue) is { } m) problems.Add("Machine: " + m);
            if (f.HasLocked && FieldValidator.Check(f, f.LockedValue) is { } l) problems.Add("Locked: " + l);
            if (f.HasUser && FieldValidator.Check(f, f.UserValue) is { } u) problems.Add("User: " + u);
        }
        return new(problems.Count == 0 ? ExitCodes.Ok : ExitCodes.ValidationFailed, problems);
    }
}

/// <summary>Kurzform aus der Spec: <c>ConfigLayers.Load(opts)</c>.</summary>
public static class ConfigLayers
{
    public static ConfigDocument Load(ConfigEditorOptions options) => ConfigDocument.Load(options);
}
