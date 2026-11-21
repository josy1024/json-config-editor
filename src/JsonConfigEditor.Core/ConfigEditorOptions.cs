namespace JsonConfigEditor.Core;

/// <summary>
/// Was der Editor öffnen soll. Hauptweg: nur <see cref="DescriptorPath"/>, der Rest steht im Descriptor.
/// Explizit gesetzte Pfade überschreiben den Descriptor.
/// </summary>
public sealed class ConfigEditorOptions
{
    /// <summary>Pfad zur <c>*.editor.json</c> der App.</summary>
    public string? DescriptorPath { get; set; }

    /// <summary>Basisdatei (Werks-Defaults, wird nie geschrieben).</summary>
    public string? BasePath { get; set; }

    /// <summary>User-Datei, die einzige Datei, die geschrieben wird.</summary>
    public string? UserPath { get; set; }

    /// <summary>Vorgaben für alle User am Rechner (IT), optional mit Abschnitt <c>Locked</c>.</summary>
    public string? MachinePath { get; set; }

    /// <summary>Leitet Default-Pfade ab: %APPDATA%\&lt;App&gt;\appsettings.local.json und %ProgramData%\&lt;App&gt;\appsettings.json.</summary>
    public string? AppName { get; set; }

    /// <summary>Abschnitt, bei dem der Editor startet.</summary>
    public string? InitialSection { get; set; }

    /// <summary>Alles nur anzeigen.</summary>
    public bool ReadOnly { get; set; }
}

/// <summary>Fehler mit Exit-Code für die Kommandozeile.</summary>
public class ConfigEditorException(string message, int exitCode, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>2 = Datei kaputt, 3 = Parameterfehler, 4 = Validierung fehlgeschlagen.</summary>
    public int ExitCode { get; } = exitCode;
}

public static class ExitCodes
{
    public const int Ok = 0;
    /// <summary>Unerwarteter Fehler, Details im Log.</summary>
    public const int Crash = 1;
    public const int BrokenFile = 2;
    public const int BadArguments = 3;
    public const int ValidationFailed = 4;
}
