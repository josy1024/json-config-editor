# json-config-editor

Einstellungen in `appsettings.json` übersichtlich bearbeiten, ohne JSON anzufassen. Schalter als Schalter, Zahlen als Zahlen, Pfade mit Durchsuchen und Prüfung. Geschrieben wird nur eine User-Datei unter `%APPDATA%`, und nur das, was vom Standard abweicht. Updates der App überschreiben also nie die Einstellungen des Users.

Windows, WPF mit Fluent-Theme (hell/dunkel folgt Windows). Spec: [docs/SPEC.md](docs/SPEC.md).

## Was es kann

* **Drei Ebenen**: Werks-Defaults (`appsettings.json` neben der App) < IT-Vorgaben (`%ProgramData%\<App>\appsettings.json`) < eigene Werte (`%APPDATA%\<App>\appsettings.local.json`). Was die IT unter `"Locked"` einträgt, ist gesperrt 🔒.
* Pro Feld sichtbar, woher der Wert kommt (Standard / IT-Vorgabe / IT gesperrt / eigener Wert), ↺ setzt zurück.
* **Auto-Erkennung**: `//`-Kommentare in der `appsettings.json` werden zu Beschreibungen, Pfade werden am Key oder Wert erkannt.
* **Descriptor** `appsettings.editor.json` (optional): Labels, Favoriten oben, Einheiten, Grenzen, read-only, versteckt, erweitert, Neustart-Hinweis, Pfadprüfung inkl. OneDrive-Warnung.
* **Sofort speichern**, atomar. Unbekannte Keys, `_hinweis`-Metadaten und Listen in der User-Datei bleiben erhalten.
* Suche, Filter pro Abschnitt, "Geändert"-Ansicht, Strg+F.
* Ändert die App die Datei selbst, lädt der Editor neu.
* Ohne Parameter: Startseite mit bekannten Apps (IT-Liste, eigene Liste, zuletzt geöffnet).

## Aufruf

Hauptweg, alles steht im Descriptor der App:

```powershell
JsonConfigEditor.exe "C:\Program Files\PraxisRecorder\appsettings.editor.json" --owner-pid 1234
```

Ohne Descriptor:

```powershell
JsonConfigEditor.exe --base "C:\Program Files\MeineApp\appsettings.json" --app MeineApp
```

| Option | Bedeutung |
|---|---|
| `<pfad>` | `*.editor.json`, `appsettings.json`, `*.local.json` oder Ordner einer App, wird erkannt |
| `--base`, `--user`, `--machine` | Dateien direkt angeben (überschreibt Descriptor) |
| `--app <Name>` | Default-Pfade unter `%APPDATA%` und `%ProgramData%` |
| `--section <Name>` | bei diesem Abschnitt öffnen |
| `--owner-pid <pid>` | schließt sich, wenn die aufrufende App endet |
| `--readonly` | nur anzeigen |
| `--validate` | ohne Fenster prüfen (CI, IT) |
| `--pick` | immer die Startseite zeigen |
| `--list` | bekannte Apps ausgeben |
| `--version`, `--help` | |

Exit-Codes: `0` OK, `1` unerwarteter Fehler, `2` Datei kaputt, `3` Parameterfehler, `4` Validierung fehlgeschlagen. Pro User-Datei läuft nur ein Fenster, ein zweiter Aufruf holt es nach vorne.

### Ohne Parameter (Doppelklick)

Liegt genau eine `appsettings.editor.json` neben der EXE oder im aktuellen Ordner, öffnet der Editor sie direkt. Sonst kommt eine Startseite mit allen bekannten Apps, dazu "Andere Datei öffnen…" und Drag & Drop.

Die Liste kommt aus `apps.json`-Dateien und den zuletzt geöffneten Apps:

| Datei | Für |
|---|---|
| `%ProgramData%\JsonConfigEditor\apps.json` | IT, für alle User am Rechner |
| `apps.json` neben `JsonConfigEditor.exe` | Firmen- oder Portable-Paket |
| `%APPDATA%\JsonConfigEditor\apps.json` | eigene Liste ("Eigene Liste bearbeiten" legt sie an) |
| `%APPDATA%\JsonConfigEditor\recent.json` | automatisch, die letzten 10 |

```json
{
  "apps": [
    { "name": "PraxisRecorder", "descriptor": "%ProgramFiles%\\PraxisRecorder\\appsettings.editor.json" },
    { "name": "MeineApp", "base": "C:\\Tools\\MeineApp\\appsettings.json" }
  ]
}
```

`descriptor` oder `base`, optional `user`, `machine`. Relative Pfade gelten ab dem Ordner der `apps.json`. Fehlt eine Datei, bleibt der Eintrag ausgegraut sichtbar.

### Wenn etwas nicht passt

* Kann die User-Datei nicht geschrieben werden (Ordner gesperrt, Datei schreibgeschützt), öffnet der Editor **nur zur Ansicht** und sagt warum.
* Dateien über 5 MB, Binärdateien, Ordner statt Datei, ungültige Zeichen im Pfad: klare Meldung statt Absturz.
* Log: `%LOCALAPPDATA%\JsonConfigEditor\logs`, 7 Tage. Enthält Pfade und Keys, keine Werte.

PowerShell wartet bei GUI-Programmen nicht auf den Exit-Code. Für `--validate` deshalb pipen:

```powershell
.\JsonConfigEditor.exe .\appsettings.editor.json --validate | Out-Host; $LASTEXITCODE
```

## Descriptor

Liegt neben der `appsettings.json` der App und wird mit ihr ausgeliefert. Alles optional. Beispiel: [samples/praxisrecorder/appsettings.editor.json](samples/praxisrecorder/appsettings.editor.json).

```json
{
  "formatVersion": 1,
  "app": {
    "name": "PraxisRecorder",
    "base": "appsettings.json",
    "userFile": "%APPDATA%\\PraxisRecorder\\appsettings.local.json",
    "machineFile": "%ProgramData%\\PraxisRecorder\\appsettings.json"
  },
  "favorites": ["Output:RootPath", "Live:ChunkSeconds"],
  "sections": { "Live": { "label": "Live-Text", "order": 2, "icon": "waveform" } },
  "fields": {
    "Output:RootPath": { "type": "path", "pathKind": "folder", "emptyMeans": "Dokumente\\PraxisRecorder", "warnCloudFolder": true },
    "Recording:MaxMinutes": { "type": "number", "unit": "min", "min": 0, "zeroMeans": "unbegrenzt" },
    "Models:Backend": { "restartRequired": true },
    "Recording:FlushSeconds": { "advanced": true }
  }
}
```

Feldattribute: `label`, `description`, `type` (`bool`, `int`, `number`, `string`, `path`), `readonly`, `hidden`, `advanced`, `min`, `max`, `unit`, `zeroMeans`, `emptyMeans`, `restartRequired`, `pathKind` (`file`, `folder`), `mustExist`, `expandEnv`, `relativeTo` (`base`), `warnCloudFolder`. Abschnitt-Icons: `mic`, `folder`, `record`, `shield`, `model`, `waveform`, `quality`, `archive`, `tools`, `clock`, `globe`, `user` …

Tipp: Zahlen ohne Punkt in der `appsettings.json` (z. B. `60`) erkennt der Editor als ganze Zahl. Ist in der App ein `double`, im Descriptor `"type": "number"` setzen.

## In eigene App einbinden

Als EXE (empfohlen, eigene App muss dafür nicht AGPL sein):

```csharp
Process.Start(new ProcessStartInfo(editorExe)
{
    ArgumentList = { Path.Combine(AppContext.BaseDirectory, "appsettings.editor.json"), "--owner-pid", Environment.ProcessId.ToString() },
});
```

Die App lädt ihre Config per `FileSystemWatcher` neu, wenn der Editor speichert. Mehr braucht's nicht.

Als DLL in einer WPF-App (abgeleitetes Werk, AGPL gilt):

```csharp
var w = ConfigEditorWindow.Show(new ConfigEditorOptions { DescriptorPath = "appsettings.editor.json" }, owner);
w.Saved += (_, e) => { /* e.ChangedKeys, e.RestartRequired */ };
```

Wichtig für die App: `Locked` aus der Machine-Datei beim Laden als **letzte** Ebene drüberlegen, sonst kann man die Sperre in der User-Datei von Hand aushebeln.

## Bauen

```powershell
dotnet test tests/JsonConfigEditor.Tests
```

```powershell
dotnet run --project src/JsonConfigEditor.App -- samples/praxisrecorder/appsettings.editor.json --user "$env:TEMP\jce-test.json"
```

Release: Tag `v*` pushen, GitHub Actions baut `JsonConfigEditor-<ver>-win-x64.zip` (framework-dependent, .NET 10 Desktop Runtime nötig) und `SHA256SUMS.txt`.

## Lizenz

AGPL-3.0-or-later, siehe [LICENSE](LICENSE). Aufruf als separate EXE macht die aufrufende App nicht zum abgeleiteten Werk. Kommerzielle Lizenz auf Anfrage.
