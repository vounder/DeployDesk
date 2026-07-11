# DeployDesk

DeployDesk ist eine schlanke Windows-Oberfläche für Deployments aus kompatiblen Git-Repositories. Ein Repository beschreibt sich über eine `*.deploylink`-Datei und stellt einen nicht-interaktiven PowerShell-Runner bereit.

## Funktionen

- mehrere lokale Projekte verwalten
- `.deploylink` per Dateidialog, Doppelklick oder Drag-and-drop importieren
- Branch, lokale Änderungen und letzte Commits anzeigen
- lokale Änderungen vor dem Deploy bewusst committen
- projektspezifische Optionen wie Seeds auswählen
- Deploy-Runner ohne Konsolenfenster starten
- strukturierte JSON-Lines und normale Prozessausgabe live anzeigen
- erfolgreichen Commit pro Projekt unter `%LOCALAPPDATA%\DeployDesk` merken
- laufenden Prozess inklusive Kindprozessen abbrechen

## Voraussetzungen

- Windows 10 oder Windows 11 (x64)
- Git for Windows
- Windows OpenSSH Client
- ein Repository mit einer gültigen `.deploylink` und einem kompatiblen Runner

Die veröffentlichte EXE enthält die benötigte .NET-Laufzeit selbst.

## Entwicklung

```powershell
dotnet build DeployDesk.sln
dotnet run --project src/DeployDesk/DeployDesk.csproj
```

Portable Einzeldatei erzeugen:

```powershell
.\scripts\publish.ps1
```

Das Ergebnis liegt anschließend unter `artifacts\publish\DeployDesk.exe`.

Den veröffentlichten Build inklusive echtem WPF-Fenster prüfen:

```powershell
.\scripts\smoke-start.ps1
```

## Deploy-Verknüpfung

Die Datei liegt im Root des Website-Repositories, zum Beispiel `meine-website.deploylink`. Das Schema befindet sich unter `docs/deploylink-v1.schema.json`.

Der Runner muss folgende Parameter akzeptieren:

- `-NonInteractive`: keine Eingabeaufforderungen anzeigen
- `-SkipLocalGit`: Committen wird von DeployDesk übernommen
- `-OutputFormat JsonLines`: strukturierte Ereignisse zeilenweise ausgeben

Unterstützte Ereignistypen sind `step`, `success`, `warning`, `error` und `completed`. Jede Zeile ist ein eigenständiges JSON-Objekt:

```json
{"type":"step","message":"Container bauen und starten"}
```

Normale stdout-/stderr-Zeilen sind weiterhin erlaubt und erscheinen unverändert im Log.

## Vertrauen und Geheimnisse

DeployDesk zeigt beim ersten Import Repository und Runner an und verlangt eine Bestätigung. Eine `.deploylink` darf keine Kennwörter, Tokens oder privaten SSH-Schlüssel enthalten. Dafür werden die normale SSH-Konfiguration und lokale Betriebssystemspeicher verwendet.

## Installer

`installer/DeployDesk.iss` erzeugt mit Inno Setup einen benutzerlokalen Installer. Er legt Startmenüeinträge an und registriert `.deploylink` für DeployDesk. Die portable EXE funktioniert auch ohne Installation über Dateidialog oder Drag-and-drop.
