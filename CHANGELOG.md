# Changelog · json-config-editor

Was sich pro Version geändert hat. Neueste oben. Releases mit Zip und SHA256: [GitHub Releases](https://github.com/josy1024/json-config-editor/releases).

## Unveröffentlicht

* Build: Die Version im exe-Manifest kommt jetzt automatisch aus der App-Version (`Directory.Build.props` bzw. Release-Tag). Bisher stand dort fix 1.0.0.0

## v0.4.3 – 25.11.2026

Neue App-Favoriten erreichen auch User mit eigenen Favoriten.

* Neue App-Favoriten kommen auch bei eigenen Favoriten dazu. Bisher galt: Hat der User einmal einen Stern geklickt, sieht er keine neuen Descriptor-Favoriten mehr (z. B. die Mikro-Prioliste in PraxisRecorder). Jetzt merkt sich der Editor die App-Favoriten (`_jsonConfigEditor.descriptorFavorites`), neue erscheinen an ihrer Stelle, selbst abgewählte bleiben weg
* Beispiele: TestApp zeigt sortierbare Listen (Tags, Endpoints mit `itemLabel`/`itemDetail`, Regions mit fester Reihenfolge), das PraxisRecorder-Beispiel die Mikro-Prioliste als Favorit
* README: Abschnitt Autor

## v0.4.2 – 25.11.2026

Listen sortieren, z. B. die Mikro-Prioliste von PraxisRecorder.

* Listen sortieren: Listen zeigen eine nummerierte Zeile pro Eintrag (Text = erste Text-Eigenschaft, z. B. `Name`), ▲ ▼ oder Alt+↑ ↓ ändern die Reihenfolge. Der Editor schreibt die ganze Liste in die User-Datei, jeder Eintrag bleibt unverändert (`Id`, `LastSeen` …). Hat die App die Liste inzwischen selbst geändert, wird der Eintrag per Inhalt wiedergefunden oder es kommt "bitte nochmal" statt einer falschen Reihenfolge
* Descriptor: neues Feldattribut `list` mit `reorder`, `itemLabel`, `itemDetail`. `list` ist damit nicht mehr "reserviert"
* Anzahl der Listeneinträge zählt jetzt wie `IConfiguration` (nach Index über alle Ebenen zusammengeführt)
* Suche findet auch Listeneinträge (z. B. "Jabra")

## v0.4.1 – 21.11.2026

Selbstheilung und tolerantes Verhalten, damit keine Einstellungen still verloren gehen.

* Kaputte User-Datei: Vor jedem Speichern liegt der letzte gültige Stand als `appsettings.local.json.bak` daneben. Neuer Knopf "Letzten guten Stand wiederherstellen", die kaputte Fassung bleibt als `.kaputt-<Datum>`
* Datei gerade gesperrt (Virenscanner, App schreibt): Wert bleibt im Feld, neuer Versuch alle 3 s. Beim Schließen Rückfrage, wenn noch Werte offen sind. Vorher war der Wert nur im Feld zu sehen, aber nicht gespeichert
* Descriptor: Falscher Typ an einer Stelle (`"min": "5"` …) kostet nur diesen Eintrag statt den ganzen Editor. `--validate` meldet es weiterhin
* Dateien in ANSI (Windows-1252) werden mit richtigen Umlauten gelesen, die User-Datei wird beim Speichern UTF-8. Hinweis, wenn die `appsettings.json` der App nicht UTF-8 ist
* Speichern auf die Platte gezwungen (sicher bei Stromausfall), keine halben tmp-Dateien mehr, alte eigene `*.tmp` werden beim Öffnen weggeräumt
* Zweiter Aufruf bei hängendem Editor: Meldung "reagiert nicht" (Exit 1) statt still zu beenden
* Kaputtes `_jsonConfigEditor` in der User-Datei wird beim Speichern der Favoriten ersetzt statt mit Fehler abzubrechen
* Doku: Exit-Codes mit Ursachen, Beispiel für IT-Sperren (`Locked`), eigene Favoriten, Hinweis zu Listen in `IConfiguration`

## v0.4.0 – 21.11.2026

* Eigene Favoriten: Stern an jedem Feld, gespeichert in der User-Datei (`_jsonConfigEditor.favorites`)
* Favoriten-Filter neben "Alle" und "Geändert", Trefferzahl bei der Suche
* Navigation links folgt dem Scrollen, sanfte Bewegung beim Springen zu einem Abschnitt
* Test-Configs unter `samples/testapp`

Hinweis: Die EXE aus diesem Tag meldet sich noch als 0.3.0 (Version in `Directory.Build.props` nicht angehoben).

## v0.3.0 – 21.11.2026

* F3: Debug-Infos (Aufruf, Version, verwendete Dateien, Inhalt der User-Datei) mit "Kopieren", Versionszeile verlinkt das GitHub-Projekt
* Pfadfelder: 📂 öffnet den Ordner im Explorer, Rechtsklick-Menü mit Ordner öffnen, Durchsuchen, Pfad kopieren
* App-Icon für EXE und Fenster

## v0.2.0 – 21.11.2026

* Ohne Parameter: öffnet die einzige `appsettings.editor.json` daneben direkt, sonst Startseite mit bekannten Apps (`apps.json` von IT, Paket und User, zuletzt geöffnet), Drag & Drop
* Nur zur Ansicht, wenn die User-Datei nicht beschreibbar ist
* Klare Meldungen statt Absturz: Dateien über 5 MB, Binärdateien, Ordner statt Datei, ungültige Pfade, doppelte Keys
* Log unter `%LOCALAPPDATA%\JsonConfigEditor\logs`

## v0.1.0 – 21.11.2026

Erste Version: Core, WPF-Oberfläche mit Fluent-Theme, EXE mit `--validate`, drei Ebenen mit IT-Sperren, Descriptor, sofortiges atomares Speichern, Tests.
