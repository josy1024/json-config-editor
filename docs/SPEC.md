# json-config-editor – Spec

Stand 21.11.2026. v0.1.0 gebaut: alles aus Abschnitt 1 bis 12, Abschnitt 13 (Später) noch offen. v0.2.0: Startseite ohne Parameter, App-Listen, Robustheit (8a, 8b). Bedienung siehe [README](../README.md).

Kernfrage: Wie macht man eine `appsettings.json` für normale Anwender übersichtlich editierbar, ohne dass sie JSON anfassen müssen und ohne dass ein Update ihre Einstellungen überschreibt?

Antwort: Ein generischer Editor, der die Werks-Config einer App liest, Schalter, Zahlen, Texte und Pfade als richtige Bedienelemente zeigt und **nur die Abweichungen** in eine User-Datei unter `%APPDATA%` schreibt. Erste Anwendung: [PraxisRecorder](https://github.com/josy1024/audiorecord2text) (siehe dort `docs/SPEC-ConfigEditor.md`).

## 1. Ziel und Nicht-Ziel

Ziel:

* appsettings-artige JSON-Dateien (Format von `Microsoft.Extensions.Configuration`) übersichtlich bearbeiten.
* Layer-bewusst: Werks-Defaults, IT-Vorgaben, eigene Werte. Man sieht immer, woher ein Wert kommt.
* Geschrieben wird nur die User-Datei, und nur das, was vom Default abweicht.
* Generisch: jede App kann den Editor nutzen, mit oder ohne eigene Beschreibung (Descriptor).

Nicht-Ziel in v1:

* Die Basisdatei (`appsettings.json` neben der exe) ändern.
* ProgramData schreiben (IT-Admin-Modus kommt später).
* Beliebiges JSON-Schema, beliebige JSON-Dateien.
* Cross-Platform. Windows only.

## 2. Architektur

Solution `JsonConfigEditor.sln`, .NET 10.

| Projekt | Ziel | Inhalt |
| --- | --- | --- |
| `JsonConfigEditor.Core` | net10.0 | Layer laden, Auto-Erkennung, Descriptor, Feldmodell, Validierung, Sparse-Write, atomares Speichern. Keine UI. |
| `JsonConfigEditor.Wpf` | net10.0-windows (DLL) | `ConfigEditorView` (UserControl), `ConfigEditorWindow`, Fluent-Styles, Feld-Controls |
| `JsonConfigEditor` | net10.0-windows (WinExe) | dünner Host: Kommandozeile lesen, Fenster zeigen |
| `JsonConfigEditor.Tests` | net10.0 | xUnit für Core |

UI-Technik: WPF mit dem Fluent-Theme von .NET (hell/dunkel folgt Windows). Optik orientiert sich an Karten mit Schaltern rechts, Navigation links, Suche oben (Vorbild: Hex "Labs"-Seite).

## 3. Layer-Modell

Drei Ebenen, die untere überschreibt die obere:

| # | Ebene | Pfad (Beispiel) | Im Editor |
| --- | --- | --- | --- |
| 1 | **Base** | `appsettings.json` neben der App | nur lesen |
| 2 | **Machine** | `%ProgramData%\<App>\appsettings.json` | nur lesen |
| 3 | **User** | `%APPDATA%\<App>\appsettings.local.json` | **einzige Datei, die geschrieben wird** |

Gesperrte Werte: Steht in der Machine-Datei ein Abschnitt `"Locked"`, gelten diese Werte über allem und sind im Editor gesperrt (🔒, Quelle "IT").

```json
{
  "Output": { "HistoryCount": 20 },
  "Locked": { "Output": { "RootPath": "D:\\Aufnahmen" } }
}
```

* `Output:HistoryCount` = IT-Vorgabe: User darf's ändern, der Editor zeigt "IT-Vorgabe".
* `Output:RootPath` = gesperrt: Feld read-only, Schreiben in die User-Datei unmöglich.
* Wichtig: Der Editor zeigt die Sperre nur an. Die App muss `Locked` beim Laden selbst als letzte Ebene drüberlegen, sonst kann jemand die User-Datei von Hand ändern.
* **Listen**: `IConfiguration` führt JSON-Arrays über die Ebenen **nach Index** zusammen (Basis `["a","b","c"]` + User `["x"]` = `["x","b","c"]`). Der Editor bearbeitet Arrays deshalb nicht selbst (Knopf "Im Texteditor öffnen"). Empfehlung an App-Entwickler: Listen, die ein User ganz ersetzen soll, als Komma-getrennten String ablegen und in der App aufteilen. In PraxisRecorder so umgesetzt (`Text:FillerWords`).

Pro Feld zeigt der Editor:

* den **wirksamen Wert**,
* die **Herkunft**: Standard / IT-Vorgabe / IT gesperrt / eigener Wert,
* ↺ **Zurücksetzen**: nimmt den Key aus der User-Datei, dann gilt wieder der Wert darunter.

## 4. Auto-Erkennung (ohne Descriptor)

Ohne Descriptor baut der Editor die Oberfläche aus der Base-Datei.

Beschreibungen aus Kommentaren:

* Base wird zusätzlich mit `Utf8JsonReader` und `JsonCommentHandling.Allow` gelesen.
* `//`-Zeilen direkt vor einem Key werden zur Beschreibung des Felds. Viele Apps (z. B. PraxisRecorder) haben ihre Hilfetexte so schon in der `appsettings.json` stehen.

Typen:

| JSON | Feldtyp | Bedienelement |
| --- | --- | --- |
| `true` / `false` | bool | Schalter (Toggle) |
| Zahl ohne Punkt | int | Zahlenfeld, nur ganze Zahlen |
| Zahl mit Punkt | number | Zahlenfeld, Dezimal (in JSON immer Punkt, Anzeige nach Windows-Kultur) |
| String | string | Textfeld |
| String, Pfad-artig | path | Textfeld + Durchsuchen + Prüfung |
| Objekt | Abschnitt | Eintrag in der Navigation links |
| Array | – | v1 nur Anzeige "N Einträge" + Button "Im Texteditor öffnen" |

Pfad-artig heißt: Key endet auf `Path`, `Dir`, `Directory`, `Folder` oder `File`, **oder** der Wert enthält `\`, `/`, `%VAR%` oder einen Laufwerksbuchstaben (`C:`).

Sonderfälle:

* Keys mit `_` vorne (`_hinweis`, `_verfuegbar`, `_autoconfig`, `_jsonConfigEditor`) sind Metadaten: kein Feld. `_hinweis` wird als Hinweistext über dem Abschnitt gezeigt. `_jsonConfigEditor.favorites` speichert persönliche Favoriten. Beim Schreiben bleiben Metadaten erhalten.
* Keys, die nur in der User-Datei stehen (nicht in Base): Abschnitt "Sonstige", anzeigen und editierbar, nie löschen.

## 5. Descriptor `appsettings.editor.json`

Optional. Die App legt ihn neben ihre `appsettings.json` und liefert ihn mit aus. Er ergänzt oder überschreibt die Auto-Erkennung **feldweise**, alles ist optional.

```json
{
  "app": {
    "name": "PraxisRecorder",
    "title": "PraxisRecorder Einstellungen",
    "base": "appsettings.json",
    "userFile": "%APPDATA%\\PraxisRecorder\\appsettings.local.json",
    "machineFile": "%ProgramData%\\PraxisRecorder\\appsettings.json"
  },
  "favorites": ["Output:RootPath", "Retention:Days", "Live:ChunkSeconds", "Recording:SegmentMinSeconds"],
  "sections": {
    "Live": { "label": "Live-Text", "order": 2, "icon": "waveform" }
  },
  "fields": {
    "Output:RootPath": { "type": "path", "pathKind": "folder", "mustExist": false,
                          "emptyMeans": "Dokumente\\PraxisRecorder", "warnCloudFolder": true },
    "Recording:MaxMinutes": { "min": 0, "unit": "min", "zeroMeans": "unbegrenzt" },
    "Models:Backend": { "restartRequired": true },
    "Recording:FlushSeconds": { "advanced": true }
  }
}
```

### Block `app`

* `base`: Pfad zur Basisdatei, **relativ zum Ordner des Descriptors** (Default `appsettings.json`). Damit klappt's egal, wohin die App installiert ist.
* `userFile`, `machineFile`: mit Umgebungsvariablen. Fehlen sie, gilt `%APPDATA%\<name>\appsettings.local.json` bzw. `%ProgramData%\<name>\appsettings.json`.
* `title`: Fenstertitel.

### Block `sections`

Key = Abschnittsname aus der JSON. Attribute: `label`, `order`, `icon`, `description`, `hidden`.

### Block `fields`

Keys im Format `Abschnitt:Key` wie bei `IConfiguration` (verschachtelt: `A:B:C`).

| Attribut | Bedeutung |
| --- | --- |
| `label` | Anzeigename (Default: Key, CamelCase aufgetrennt) |
| `description` | Hilfetext (Default: Kommentar aus Base) |
| `type` | `bool`, `int`, `number`, `string`, `path` |
| `readonly` | anzeigen, nicht änderbar |
| `hidden` | gar nicht anzeigen |
| `advanced` | nur sichtbar mit "Erweitert anzeigen" |
| `min`, `max` | Grenzen für Zahlen |
| `unit` | Einheit hinter dem Feld (`min`, `s`, `dB`, `GB` …) |
| `zeroMeans` | Text, wenn Wert 0 ist (z. B. "unbegrenzt", "automatisch") |
| `emptyMeans` | Text, wenn String leer ist (z. B. Default-Pfad) |
| `restartRequired` | Hinweis ⟳ "gilt nach Neustart der App" |
| `pathKind` | `folder` oder `file` |
| `mustExist` | Pfad muss existieren, sonst Fehler statt Warnung |
| `expandEnv` | `%VAR%` auflösen für Prüfung und Anzeige (Default true) |
| `relativeTo` | `base` = relativ zum Ordner der Basisdatei auflösen |
| `warnCloudFolder` | warnen, wenn der Pfad in OneDrive/SharePoint liegt |

Reserviert für später (v1 ignoriert sie mit Warnung im Log): `options`, `optionsFromGlob`, `list`, `provider`.

### Favoriten

* `favorites` = Liste von Keys, in der gewünschten Reihenfolge.
* Werden als eigener Block **"Favoriten" ganz oben** gezeigt (wie "Neu in Labs" im Vorbild). Das Feld bleibt zusätzlich in seinem Abschnitt, beide zeigen denselben Wert.
* Favorit schlägt `hidden` und `advanced` (Widerspruch → Warnung im Log, Favorit gewinnt).
* Eigene Favoriten stehen in der User-Datei unter `_jsonConfigEditor.favorites`. Fehlt dieser Eintrag, gelten die Favoriten des Descriptors; eine vorhandene leere Liste entfernt auch alle Descriptor-Favoriten für diesen User.
* Ohne Descriptor erscheint der Favoriten-Block, sobald mindestens ein Feld als Favorit markiert wurde.

### Validierung des Descriptors

* Key im Descriptor, den es in Base nicht gibt → Warnung im Log, Feld wird trotzdem gezeigt (Typ aus Descriptor, sonst string).
* Falscher Typ an einer Stelle (`"min": "5"`, Feld ist kein Objekt, `favorites` keine Liste …) → nur dieser Eintrag wird ignoriert, Hinweis im Editor. Kaputtes JSON (Syntax) → Descriptor nicht ladbar.
* `--validate` meldet solche Fehler mit Exit-Code (für CI der App).

## 6. Oberfläche

```
┌──────────────┬─────────────────────────────────────────────────────────────┐
│ PraxisRec.   │  Einstellungen   [🔍 24 Einstellungen durchsuchen]  [Alle | Geändert · 3] │
│              │  (Alle 24) (Audio 3) (Ausgabe 4) (Aufnahme 8) ...           │
│ ★ Favoriten  │                                                             │
│ Audio        │  Favoriten                                                  │
│ Ausgabe      │  ┌───────────────────────────┐ ┌──────────────────────────┐ │
│ Aufnahme     │  │ Speicherort               │ │ Löschen nach (Tagen)     │ │
│ Datenschutz  │  │ D:\Aufnahmen       [ … ]  │ │ [ 30 ] Tage   🔒 IT      │ │
│ Modelle      │  │ ✓ existiert, beschreibbar │ │                          │ │
│ Live         │  └───────────────────────────┘ └──────────────────────────┘ │
│ ...          │                                                             │
│              │  Live  ⌄                                                    │
│ ☐ Erweitert  │  ┌───────────────────────────┐ ┌──────────────────────────┐ │
│              │  │ Live aktiv          [●──] │ │ Chunk-Länge  [ 7 ] s  ↺  │ │
│              │  │ Live-Text beim Diktieren  │ │ eigener Wert             │ │
│              │  └───────────────────────────┘ └──────────────────────────┘ │
└──────────────┴─────────────────────────────────────────────────────────────┘
```

* Startansicht: Favoriten oben, darunter alle Abschnitte mit einklappbarem Titel und Anzahl.
* Links: Navigation, springt zum Abschnitt. Unten "Erweitert anzeigen".
* Oben: Suche über Label, Beschreibung und Key. Filter-Chips pro Abschnitt mit Anzahl. Umschalter "Alle | Geändert · N" (Geändert = alles mit Herkunft "eigener Wert").
* Karten zweispaltig, ab schmalem Fenster einspaltig.
* Karte: Label (fett), Beschreibung, Bedienelement rechts, Herkunfts-Badge, ↺ (nur bei eigenem Wert), 🔒 bei readonly/gesperrt mit Tooltip "gesperrt durch IT", ⟳ bei `restartRequired`.
* Favoriten: Konturierter Stern = kein Favorit, gefüllter goldener Stern = Favorit. Nur das Symbol bekommt den Farbakzent; keine gelbe Kartenfläche.
* Erweiterte Einstellungen: Wenn sichtbar, tragen sie das Text-Badge "Erweitert" und eine dezente neutrale Akzentlinie. Die Karte bleibt gleich hell und kontrastreich; Farbe ist nicht das einzige Erkennungsmerkmal.

Bedienelemente:

* **Bool**: Toggle, wirkt sofort.
* **Zahl**: NumberBox. Grenzen aus `min`/`max`. Ungültig → roter Rahmen + Text, wird **nicht** gespeichert. `zeroMeans` steht als graue Info daneben, wenn 0.
* **Text**: TextBox, gespeichert beim Verlassen des Felds oder nach Debounce.
* **Pfad**: TextBox + "…" (Ordner- oder Dateidialog je nach `pathKind`). Darunter der expandierte Pfad und ein Status:
  * ✓ existiert (und beschreibbar bei Ordnern)
  * ⚠ fehlt, wird von der App angelegt (bei `mustExist: false`)
  * ✗ ungültiger Pfad (Zeichen, Format)
  * ⚠ nicht beschreibbar
  * ⚠ liegt in OneDrive/SharePoint (bei `warnCloudFolder`). Erkennung über `%OneDrive%`, `%OneDriveCommercial%`, `%OneDriveConsumer%` und Registry `HKCU\Software\Microsoft\OneDrive\Accounts\*\UserFolder`.
  * Leer + `emptyMeans` → graue Info "leer = Dokumente\PraxisRecorder".
* Barrierefreiheit: alles per Tastatur, `AutomationProperties.Name` = Label, Status nicht nur über Farbe (Symbol + Text).

## 7. Speichern

* **Sofort**: jede gültige Änderung wird nach ~300 ms Debounce geschrieben. Kein Speichern-Button.
* **Atomar**: in Temp-Datei schreiben, mit `Flush(true)` auf die Platte zwingen, dann umbenennen. Schlägt etwas fehl, wird die Temp-Datei gelöscht. Beim Öffnen werden eigene `*.tmp` älter als 1 Tag weggeräumt. Ordner der User-Datei wird bei Bedarf angelegt.
* **Letzter guter Stand**: vor jedem Speichern kommt die bisherige (gültige) Fassung nach `appsettings.local.json.bak`.
* **Datei gesperrt**: Wert bleibt im Feld, neuer Versuch alle 3 s (bis 20×), dann beim Verlassen des Felds. Externes Neuladen überschreibt so ein Feld nicht. Beim Schließen Rückfrage, wenn noch Werte offen sind.
* **Sparse**: in die User-Datei kommt ein Key nur, wenn sein Wert vom wirksamen Wert aus Base+Machine abweicht. Wird ein Wert auf den Default zurückgestellt, fliegt der Key raus. Leere Abschnitte fliegen raus.
* **Patchen statt neu schreiben**: die User-Datei wird als `JsonNode` geladen und nur an den geänderten Stellen angefasst. Unbekannte Keys, `_`-Metadaten, Arrays und Reihenfolge bleiben erhalten. Kommentare in der User-Datei gehen beim Schreiben verloren (steht so in der Doku).
* Zahlen werden als JSON-Zahl geschrieben, Bools als `true/false`, nie als String.
* **Externe Änderung** (App schreibt z. B. per Auto-Config): FileSystemWatcher → neu laden, Hinweis "Datei wurde von außen geändert, neu geladen". Feld, das gerade bearbeitet wird, behält den Fokus.
* **Kaputte User-Datei**: nie überschreiben. Banner oben mit Fehlermeldung + "Letzten guten Stand wiederherstellen" (nur wenn `.bak` gültig ist; kaputte Fassung wird zu `.kaputt-<datum>`) + "Im Texteditor öffnen" + "Sicherung anlegen und neu beginnen" (benennt um in `.kaputt-<datum>`).
* **Gesperrte Felder** werden nie in die User-Datei geschrieben.

## 8. Kommandozeile (EXE)

Hauptweg: **ein Parameter = Pfad zum Descriptor**. Alles andere steht drin.

```
JsonConfigEditor.exe "C:\Program Files\PraxisRecorder\appsettings.editor.json" [--section Live] [--owner-pid 1234]
```

Ohne Descriptor, nur Auto-Erkennung (für beliebige Apps):

```
JsonConfigEditor.exe --base <appsettings.json> [--user <pfad>] [--machine <pfad>] [--app <Name>]
```

| Parameter | Bedeutung |
| --- | --- |
| `<pfad>` (positional) | `*.editor.json` (oder JSON mit `formatVersion`), `appsettings.json`, `*.local.json` (Basis daneben, diese Datei wird beschrieben) oder Ordner einer App |
| `--descriptor <pfad>` | Descriptor ausdrücklich angeben |
| `--base` | Basisdatei (überschreibt Descriptor) |
| `--user` | User-Datei (überschreibt Descriptor) |
| `--machine` | Machine-Datei (überschreibt Descriptor) |
| `--app <Name>` | leitet Default-Pfade ab: `%APPDATA%\<Name>\appsettings.local.json`, `%ProgramData%\<Name>\appsettings.json` |
| `--section <Name>` | öffnet direkt bei diesem Abschnitt |
| `--owner-pid <pid>` | Editor schließt sich, wenn dieser Prozess endet |
| `--readonly` | alles nur anzeigen |
| `--validate` | ohne UI: Descriptor + Dateien prüfen, Fehler auf stdout |
| `--version` | Version auf stdout, Ende (für Updater der App) |
| `--pick` | immer die Startseite zeigen |
| `--list` | bekannte Apps auf stdout (Name, Quelle, Pfad, Problem) |

* **Single-Instance pro User-Datei**: zweiter Aufruf mit derselben User-Datei holt das vorhandene Fenster nach vorne (Named Mutex + Named Pipe für "aktivieren"), wechselt ggf. zu `--section`.
* Exit-Codes: `0` OK, `1` unerwarteter Fehler (Details im Log), `2` Datei kaputt, `3` Parameterfehler, `4` Validierung fehlgeschlagen.

### 8a. Ohne Parameter (Doppelklick): Auto-Öffnen und Startseite

1. Liegt **genau eine** `appsettings.editor.json` neben der EXE oder im aktuellen Ordner, wird sie direkt geöffnet.
2. Sonst kommt die **Startseite** mit allen bekannten Apps, aus diesen Quellen (bei Doppelten gewinnt die obere):

| Quelle | Datei | Badge |
| --- | --- | --- |
| IT | `%ProgramData%\JsonConfigEditor\apps.json` | IT |
| Paket | `apps.json` neben `JsonConfigEditor.exe` | Mitgeliefert |
| User | `%APPDATA%\JsonConfigEditor\apps.json` | Meine Liste |
| Suche | `appsettings.editor.json` neben der EXE / im aktuellen Ordner | Gefunden |
| automatisch | `%APPDATA%\JsonConfigEditor\recent.json`, max. 10 | Zuletzt |

Format `apps.json` (Kommentare erlaubt, `%VARS%`, relative Pfade gelten ab dem Ordner der `apps.json`):

```json
{
  "apps": [
    { "name": "PraxisRecorder", "descriptor": "%ProgramFiles%\\PraxisRecorder\\appsettings.editor.json" },
    { "name": "MeineApp", "base": "C:\\Tools\\MeineApp\\appsettings.json", "user": "%APPDATA%\\MeineApp\\appsettings.local.json" }
  ]
}
```

* Pro Eintrag `descriptor` **oder** `base`, optional `user`, `machine`, `name`. Ohne `name`: `app.name` aus dem Descriptor, sonst Ordnername (Build-Ordner wie `bin`, `Release`, `net10.0` werden übersprungen). Ohne Descriptor und ohne `user` leitet `name` die Default-Pfade ab wie `--app`.
* Sortierung: zuletzt geöffnete oben, dann in Quellen-Reihenfolge. Fehlt die Datei, bleibt der Eintrag sichtbar, ausgegraut mit "Datei fehlt" (z. B. Netzlaufwerk gerade weg). "Zuletzt"-Einträge lassen sich mit × oder Entf entfernen.
* "Andere Datei öffnen…" (Dateidialog) und Drag & Drop einer Datei oder eines App-Ordners. Die Datei wird wie der Positionsparameter erkannt. Passt sie nicht, steht der Grund oben in Rot und die Startseite bleibt offen.
* "Eigene Liste bearbeiten" legt `%APPDATA%\JsonConfigEditor\apps.json` mit kommentierter Vorlage an und öffnet sie im Texteditor.
* Kaputte `apps.json` wird übersprungen, Hinweis unten auf der Startseite. Jede erfolgreich geöffnete App landet in "Zuletzt".
* Im Editor-Fenster führt "Andere App öffnen…" (links unten, nur in der EXE) zur Startseite in einem neuen Prozess.

### 8b. Robustheit

* **Dateien lesen**: Ordner statt Datei, über 5 MB, Binärdatei, kein Zugriff → klare Meldung statt Absturz. Lesen mit `FileShare.ReadWrite | Delete`, die App darf die Datei gleichzeitig offen haben. Gesperrt (Virenscanner) → bis zu 3× kurz nochmal.
* **Pfade**: ungültige Zeichen (`< > " | ? *`, `:` mitten im Pfad, Steuerzeichen) in Parametern, Descriptor oder `apps.json` → Parameterfehler (Exit 3) mit Angabe, welcher Pfad. User-Datei = Basis, Machine oder Descriptor → Fehler. `app.base` zeigt auf den Descriptor selbst → Fehler mit Hinweis.
* **JSON**: doppelte Keys, die sich nur in Groß-/Kleinschreibung unterscheiden, gelten als kaputte Datei (IConfiguration würde einen stillschweigend gewinnen lassen).
* **Kodierung**: BOM wird beachtet (UTF-8, UTF-16). Ohne BOM streng UTF-8, bei ungültigen Bytes Windows-1252 mit Hinweis. Die User-Datei wird dann beim nächsten Speichern UTF-8; Basis- oder Machine-Datei in ANSI ergibt einen Hinweis (die App liest sie als UTF-8).
* **Nicht beschreibbar**: beim Öffnen wird geprüft, ob die User-Datei geschrieben werden kann (Ordner anlegbar, Datei nicht schreibgeschützt, Probe-Datei im Ordner). Wenn nicht: Editor öffnet **nur zur Ansicht** mit Banner und Grund, alle Felder gesperrt. `--validate` prüft das nicht (legt nichts an).
* **Zweiter Aufruf**: schickt dem offenen Editor per Named Pipe den Abschnitt und wartet auf "ok" (Fenster wirklich nach vorne geholt). Keine Antwort in 3 s (Editor hängt) → Meldung "reagiert nicht", Exit 1. Fehler beim Nach-vorne-Holen beenden das Zuhören nicht. Ältere Editoren (≤ 0.3, Pipe nur in eine Richtung) werden wie früher nur benachrichtigt.
* **Eigene Metadaten**: `_jsonConfigEditor` in der User-Datei, das kein Objekt ist, wird beim Lesen mit Hinweis ignoriert und beim nächsten Speichern der Favoriten ersetzt.
* **Unerwartete Fehler**: im Fenster → Meldung mit Pfad zum Log, Fenster bleibt offen (gespeichert wird atomar, nichts ist halb). Außerhalb → Meldung (bzw. stderr), Exit 1.
* **Log**: `%LOCALAPPDATA%\JsonConfigEditor\logs\editor-yyyyMMdd.log`, 7 Tage. Start, geöffnete Dateien, Lade- und Schreibfehler (Key + Grund, **keine Werte**), Abstürze mit Stacktrace. Nur die EXE loggt, eine App mit eingebetteter DLL nicht (`Log.Enabled`).

## 9. DLL-API

Für Apps, die den Editor im eigenen Prozess zeigen wollen (WPF):

```csharp
var opts = new ConfigEditorOptions
{
    DescriptorPath = Path.Combine(AppContext.BaseDirectory, "appsettings.editor.json"),
    InitialSection = "Live",
};
ConfigEditorWindow.Show(opts, owner);        // oder ShowDialog
var view = new ConfigEditorView(opts);       // zum Einbetten in ein eigenes Fenster
view.Saved += (_, e) => { /* e.ChangedKeys, e.RestartRequired */ };
```

Core ohne UI, z. B. für eine Auto-Config der App:

```csharp
var layers = ConfigLayers.Load(opts);
ConfigWriter.Set(layers, "Live:Model", "ggml-large-v3-turbo-q5_0.bin");   // gleiche Sparse-Regeln
```

Lizenz beachten (Abschnitt 11): DLL einbetten = abgeleitetes Werk.

## 10. Release und Verteilung

* GitHub Actions bei Tag `v*`:
  * `dotnet test`
  * `dotnet publish src/JsonConfigEditor -c Release -r win-x64 --self-contained false`
  * Release mit `JsonConfigEditor-<ver>-win-x64.zip` und `SHA256SUMS.txt`.
* **Framework-dependent**: braucht die .NET 10 Desktop Runtime. Dafür ist das Zip nur wenige MB. Die aufrufende App prüft die Runtime vor dem Start (siehe PraxisRecorder-Spec).
* Asset-Namen bleiben stabil, damit Apps die URL aus Version zusammenbauen können: `https://github.com/josy1024/json-config-editor/releases/download/v<ver>/JsonConfigEditor-<ver>-win-x64.zip`.
* Repo ist **public**, damit der Download ohne Token klappt.
* Semantische Versionen. Descriptor-Format bekommt ein Feld `"formatVersion": 1`; neue Attribute nur additiv.

## 11. Lizenz

AGPL-3.0-or-later (LICENSE im Repo).

* Aufruf als **separate EXE** (Prozess starten, Pfad übergeben): eigenständiges Programm. Die aufrufende App muss deswegen nicht AGPL sein.
* **DLL einbetten**: abgeleitetes Werk, AGPL-Pflichten gelten für die einbettende App.
* Dual-Licensing (kommerzielle Lizenz auf Anfrage) wie bei PraxisRecorder möglich.

## 12. Akzeptanz und Tests

Unit-Tests (Core):

* Auto-Erkennung mit der echten `appsettings.json` von PraxisRecorder: alle Abschnitte da, `//`-Kommentare als Beschreibung, `Output:RootPath`, `Models:Directory`, `Models:VocabularyFile` als Pfad, Bools als Schalter, `PreferredDevices` als Array-Anzeige.
* Layer: Base < Machine < User < Machine:Locked.
* Sparse-Write: Toggle ändern → User-Datei enthält genau diesen Key. Zurückstellen → Key weg, leerer Abschnitt weg.
* Vorhandene User-Datei mit `_hinweis`, `_verfuegbar`, `PreferredDevices`, unbekannten Keys → bleiben nach dem Schreiben inhaltlich gleich.
* Gesperrtes Feld: `Set` wirft, Datei unverändert.
* Kaputte User-Datei: wird nie überschrieben.
* Descriptor: Favoriten in Reihenfolge, Favorit schlägt hidden, unbekannter Key → Warnung.
* Kommandozeile: `--validate`, `--version`, Exit-Codes.

Manuell:

* UI hell/dunkel, Fenster schmal/breit, Tastaturbedienung, Screenreader liest Labels.
* App läuft parallel und lädt nach dem Speichern neu (PraxisRecorder-Watcher).

## 13. Später

* ✅ Eigene Favoriten des Users (Stern am Feld, in `_jsonConfigEditor.favorites` der User-Datei gespeichert).
* Enum-Dropdown (`options`), z. B. `auto | vulkan | cpu`.
* Datei-aus-Ordner (`optionsFromGlob`), z. B. Whisper-Modelle aus dem Modellordner.
* Sortierbare Objekt-Listen (`list`) mit Provider der App, z. B. Mikro-Prioliste.
* IT-Admin-Modus: Machine-Datei schreiben, Werte sperren.
* Registry/ADMX-Policies (`HKLM\Software\Policies\<App>`).
* NuGet-Paket für die DLL, winget-Paket für die EXE.
* Mehrsprachigkeit (UI-Texte, Descriptor-Labels pro Sprache).
* Developermodus: später die `advanced`-Einordnung pro Feld anpassen können. Diese Editor-Metadaten bleiben getrennt von den eigentlichen Einstellungswerten; IT-Admin-Modus und Developermodus bleiben getrennte Funktionen.

## 14. BUGnFeatures

* ✅ Favoritenstern an jedem Feld: Outline = kein Favorit, Solid = Favorit
* ✅ Favoriten werden in der lokalen Config der App gespeichert
* ✅ Wenn ich in ein Einstellungen Scrolle, werden Die Abschnitte auch vom aktuellen Abschnitt markiert
* ✅ Abschnitte Links lösen kurze "Bewegung" aus (damit der zusammenhang besser / klarer wird)
* ✅ Favoriten: Stern konturiert = kein Favorit, gefüllt goldfarben = Favorit. Nur das Symbol bekommt den Farbakzent; keine gelbe Kartenfläche.
* ✅ Erweiterte Einstellungen: Badge "Erweitert" und dezente neutrale Akzentlinie; Karten bleiben gleich hell und kontrastreich.

* ✅ Favoriten sind kein eigener Abschnitt mehr: Ein Favoriten-Filter sitzt neben "Alle" und "Geändert"; Favoriten erscheinen in ihrer jeweiligen Sektion.
* ✅ Bei der Suche zeigt die Kopfzeile live die Anzahl der passenden sichtbaren Einstellungen.
* ✅ Abschnitte und ihre Filter werden ausgeblendet, wenn sie nach Suche, Änderungs- oder Erweitert-Filter keine sichtbaren Felder enthalten.
