using JsonConfigEditor.Core;

namespace JsonConfigEditor.App;

public sealed class CommandLine
{
    public ConfigEditorOptions Options { get; } = new();
    public int? OwnerPid { get; private set; }
    public bool Validate { get; private set; }
    public bool Version { get; private set; }
    public bool Help { get; private set; }
    public bool Pick { get; private set; }
    public bool List { get; private set; }
    /// <summary>Positionsparameter, roh.</summary>
    public string? Target { get; private set; }

    public const string Usage = """
        json-config-editor – appsettings.json übersichtlich bearbeiten

        Hauptweg (alles steht im Descriptor der App):
          JsonConfigEditor.exe "<pfad>\appsettings.editor.json" [--section <Name>] [--owner-pid <pid>]

        Ohne Descriptor (Auto-Erkennung):
          JsonConfigEditor.exe --base <appsettings.json> [--user <pfad>] [--machine <pfad>] [--app <Name>]

        Ohne Parameter: liegt genau eine appsettings.editor.json neben der EXE oder im aktuellen
        Ordner, wird sie geöffnet. Sonst kommt die Startseite mit den bekannten Apps aus
          %ProgramData%\JsonConfigEditor\apps.json (IT), apps.json neben der EXE,
          %APPDATA%\JsonConfigEditor\apps.json (eigene) und den zuletzt geöffneten.

        Optionen:
          <pfad>              *.editor.json, appsettings.json, *.local.json oder Ordner einer App
          --base <pfad>       Basisdatei, wird nie geschrieben
          --user <pfad>       User-Datei, die einzige Datei, die geschrieben wird
          --machine <pfad>    Vorgaben der IT, Abschnitt "Locked" = gesperrt
          --app <Name>        Default-Pfade: %APPDATA%\<Name>\appsettings.local.json, %ProgramData%\<Name>\appsettings.json
          --section <Name>    direkt bei diesem Abschnitt öffnen
          --owner-pid <pid>   Editor schließt sich, wenn dieser Prozess endet
          --readonly          nur anzeigen
          --descriptor <pfad> Descriptor ausdrücklich angeben
          --validate          ohne Fenster prüfen, Fehler auf stdout
          --pick              immer die Startseite zeigen
          --list              bekannte Apps auf stdout ausgeben
          --version           Version ausgeben
          --help              diese Hilfe

        Exit-Codes: 0 OK, 1 unerwarteter Fehler, 2 Datei kaputt, 3 Parameterfehler, 4 Validierung fehlgeschlagen
        Log: %LOCALAPPDATA%\JsonConfigEditor\logs
        """;

    /// <summary>Wirft <see cref="ConfigEditorException"/> mit Exit-Code 3 bei falschen Parametern.</summary>
    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        var c = new CommandLine();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string? inline = null;
            if (arg.StartsWith("--", StringComparison.Ordinal) && arg.IndexOf('=') is var eq and > 2)
            {
                inline = arg[(eq + 1)..];
                arg = arg[..eq];
            }

            string Value()
            {
                if (inline != null) return inline;
                if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ConfigEditorException($"{arg} braucht einen Wert.", ExitCodes.BadArguments);
                return args[++i];
            }

            switch (arg.ToLowerInvariant())
            {
                case "--base": c.Options.BasePath = Value(); break;
                case "--user": c.Options.UserPath = Value(); break;
                case "--machine": c.Options.MachinePath = Value(); break;
                case "--descriptor": c.Options.DescriptorPath = Value(); break;
                case "--app": c.Options.AppName = Value(); break;
                case "--section": c.Options.InitialSection = Value(); break;
                case "--owner-pid":
                    var v = Value();
                    c.OwnerPid = int.TryParse(v, out var pid) && pid > 0
                        ? pid
                        : throw new ConfigEditorException($"--owner-pid: \"{v}\" ist keine Prozess-ID.", ExitCodes.BadArguments);
                    break;
                case "--readonly": c.Options.ReadOnly = true; break;
                case "--validate": c.Validate = true; break;
                case "--version": c.Version = true; break;
                case "--pick": c.Pick = true; break;
                case "--list": c.List = true; break;
                case "--help" or "-h" or "-?" or "/?": c.Help = true; break;
                default:
                    if (arg.StartsWith('-'))
                        throw new ConfigEditorException($"Unbekannte Option {arg}. --help zeigt alle.", ExitCodes.BadArguments);
                    if (c.Target != null)
                        throw new ConfigEditorException($"Zu viele Pfade: {arg}", ExitCodes.BadArguments);
                    c.Target = arg;
                    break;
            }
        }

        // Positionsparameter: Descriptor, appsettings.json, *.local.json oder Ordner, wird hier erkannt
        if (c.Target != null && !c.Help && !c.Version) OpenTarget.Apply(c.Options, c.Target);
        if (c.Pick && c.HasTarget)
            throw new ConfigEditorException("--pick zeigt die Startseite, dazu keinen Pfad angeben.", ExitCodes.BadArguments);
        return c;
    }

    /// <summary>Was geöffnet werden soll, steht fest. Sonst: Auto-Suche bzw. Startseite.</summary>
    public bool HasTarget => Options.DescriptorPath != null || Options.BasePath != null;
}
