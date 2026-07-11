# Arbeitsanweisung für AI-Agenten: Repository in DeployDesk integrieren

Diese Datei ist eine normative Arbeitsanweisung für eine AI, die ein beliebiges Git-Repository DeployDesk-kompatibel machen soll. Das Ergebnis besteht aus einer `*.deploylink` im Repository-Root und einem nicht-interaktiven PowerShell-Runner.

## Ziel und Grenzen

Die Integration soll vorhandene Build- und Deploymentwege kapseln, nicht blind durch den PrüfungsLerner-Deploy ersetzen.

Die AI muss:

- vor Änderungen alle `AGENTS.md`, README-Dateien, CI-Konfigurationen, Dockerfiles, Compose-Dateien und vorhandenen Deploy-Skripte lesen;
- bestehende Nutzeränderungen erhalten;
- genau ermitteln, wie das Projekt gebaut, gestartet und geprüft wird;
- Server- und Git-Ziel ausschließlich aus der Linkdatei lesen;
- einen manuellen Textmodus und den maschinenlesbaren DeployDesk-Modus anbieten;
- alle Projektprüfungen ausführen, aber ohne ausdrückliche Freigabe kein echtes Deployment und kein Server-Provisioning starten.

Fehlende Werte wie Host, SSH-Benutzer oder Remote-Pfad dürfen nicht erfunden werden. Nur wenn sie nicht aus dem Repository oder der Aufgabenbeschreibung hervorgehen, muss die AI nachfragen.

## 1. Repository analysieren

Vor der Implementierung sind mindestens folgende Punkte zu ermitteln und kurz zu dokumentieren:

| Bereich | Zu klärende Werte |
|---|---|
| Git | Repository-Root, Remote, Zielbranch |
| Anwendung | Laufzeit, Frontend/Backend, Build- und Testbefehle |
| Container | Compose-Datei, Services, Ports, eindeutiger Compose-Projektname |
| Server | Host oder SSH-Alias, Benutzer, SSH-Port, absoluter Remote-Pfad |
| Health | interner Port, HTTP-Pfad, erwarteter Statuscode |
| Betrieb | Migrationen, Seeds, optionale Nacharbeiten |
| URLs | öffentliche Website- und Health-URL |
| Geheimnisse | serverseitige `.env`, Secret Store, SSH-Agent oder SSH-Konfiguration |

Bevorzugte Deploymentstrategie für Git-Repositories:

1. lokal prüfen und zum konfigurierten Remote/Branch pushen;
2. auf dem Server Fast-forward-only aktualisieren;
3. projektspezifischen Build beziehungsweise `docker compose up -d --build` ausführen;
4. Health-Check mit begrenzten Wiederholungen durchführen;
5. erst nach erfolgreichem Health-Check Erfolg melden.

## 2. Linkdatei im Repository-Root anlegen

Dateiname: `<projekt-id>.deploylink`, zum Beispiel `meine-app-prod.deploylink`.

Für jedes Ziel wird eine eigene Datei mit eindeutiger Projekt-ID verwendet. Bei mehreren Linkdateien übergibt DeployDesk den ausgewählten Pfad explizit; beim manuellen Runner-Aufruf muss `-DeployLinkPath` angegeben werden.

Vollständiges Beispiel für Schema 2:

```json
{
  "$schema": "https://deploydesk.local/schema/deploylink-v2.json",
  "schemaVersion": 2,
  "project": {
    "id": "meine-app-prod",
    "name": "Meine App",
    "description": "Produktiv-Deployment",
    "accentColor": "#89D7A7"
  },
  "repository": {
    "remote": "origin",
    "branch": "main"
  },
  "server": {
    "name": "Production",
    "host": "deploy.example.com",
    "user": "deploy",
    "sshPort": 22,
    "remotePath": "/srv/apps/meine-app",
    "healthCheck": {
      "port": 3000,
      "path": "/api/health",
      "expectedStatus": 200,
      "attempts": 20,
      "intervalSeconds": 2
    }
  },
  "runner": {
    "type": "powershell",
    "file": "deploy/deploy.ps1",
    "protocol": "deploydesk-jsonl-v1",
    "arguments": []
  },
  "options": [
    {
      "id": "seed",
      "label": "Seeds ausführen",
      "description": "Optionale Seed-Daten nach dem Deployment einspielen.",
      "type": "boolean",
      "default": false,
      "argument": "-Seed"
    }
  ],
  "links": [
    { "label": "Website", "url": "https://example.com" },
    { "label": "Health", "url": "https://example.com/api/health" }
  ]
}
```

### Bedeutung des Serverblocks

- `name`: Anzeigename der Umgebung, beispielsweise `Production` oder `Staging`.
- `host`: DNS-Name, IP-Adresse oder bewusst gewählter SSH-Alias; kein `https://` und kein Benutzerpräfix.
- `user`: SSH-Benutzer. Ein unprivilegierter Deploy-Benutzer ist `root` vorzuziehen.
- `sshPort`: Port der SSH-Verbindung. SSH verwendet `-p`, SCP verwendet `-P`.
- `remotePath`: absoluter Linux-Pfad der Anwendung. Keine `.`- oder `..`-Segmente.
- `healthCheck.port`: Port, den der Runner auf dem Server über `127.0.0.1` prüft.
- `healthCheck.path`: HTTP-Pfad einschließlich führendem `/`.
- `expectedStatus`, `attempts`, `intervalSeconds`: Erfolgscode und begrenzte Retry-Strategie.

Nicht geheime Zielmetadaten dürfen versioniert werden. In öffentlichen Repositories ist damit auch die Serveradresse öffentlich. Falls das unerwünscht ist, kann ein auflösbarer Deployment-Hostname oder ein SSH-Alias verwendet werden; er ist trotzdem kein Geheimnis.

Das verbindliche JSON-Schema liegt in DeployDesk unter `docs/deploylink-v2.schema.json`.

## 3. PowerShell-Runner implementieren

Empfohlener Pfad: `deploy/deploy.ps1`.

Mindestsignatur:

```powershell
param(
    [string]$DeployLinkPath,
    [switch]$NonInteractive,
    [switch]$SkipLocalGit,
    [switch]$ValidateOnly,
    [string]$CommitMessage,
    [ValidateSet("Text", "JsonLines")]
    [string]$OutputFormat = "Text"
)
```

Projektspezifische, optionale Flags wie `-Seed` werden zusätzlich aufgenommen. DeployDesk reserviert folgende Argumente; sie dürfen nicht in `runner.arguments` stehen:

- `-DeployLinkPath`
- `-NonInteractive`
- `-SkipLocalGit`
- `-ValidateOnly`
- `-OutputFormat`

DeployDesk übergibt immer den kanonischen absoluten Linkpfad. Für den manuellen Aufruf darf der Runner genau eine `*.deploylink` im Repository-Root automatisch erkennen. Bei null oder mehreren Dateien muss er mit einer klaren Meldung abbrechen.

`-ValidateOnly` ist ein empfohlener projektspezifischer Prüfmodus: Er lädt und validiert die Linkdatei und beendet sich vor Git-, SSH- oder Serveraktionen mit einem passenden Exitcode.

### Linkdatei sicher laden

Der Runner muss die Datei erneut validieren. Die UI-Prüfung allein ist keine Sicherheitsgrenze.

```powershell
$json = [System.IO.File]::ReadAllText(
    $DeployLinkPath,
    [System.Text.Encoding]::UTF8
)
$config = $json | ConvertFrom-Json

if ($config.schemaVersion -ne 2) {
    Fail "schemaVersion 2 erforderlich"
}

$serverUser  = [string]$config.server.user
$serverHost  = [string]$config.server.host
$sshPort     = [int]$config.server.sshPort
$remotePath  = [string]$config.server.remotePath
$gitRemote   = [string]$config.repository.remote
$gitBranch   = [string]$config.repository.branch
$healthPort  = [int]$config.server.healthCheck.port
$healthPath  = [string]$config.server.healthCheck.path
```

Mindestens zu validieren:

- Linkpfad und Runnerpfad liegen innerhalb des Repository-Roots;
- Host enthält kein Scheme, Whitespace oder Steuerzeichen;
- Benutzername besitzt nur sichere SSH-Zeichen;
- Ports liegen zwischen 1 und 65535;
- Remote-Pfad ist absolut, enthält keine `.`/`..`-Segmente und nur erlaubte Zeichen;
- Health-Pfad beginnt mit `/` und enthält keine CR/LF- oder Shell-Zeichen;
- Git-Remote beginnt nicht mit `-`;
- Branch besteht `git check-ref-format --branch`.

Konfigurationswerte dürfen niemals ungeprüft als Shellcode eingesetzt werden. Werte entweder strikt beschränken und korrekt quoten oder als Daten, beispielsweise UTF-8/Base64, an ein festes Remote-Skript übertragen.

## 4. Runner-Protokoll einhalten

Der Textmodus bleibt für den manuellen Konsolenaufruf erhalten. Unter `-OutputFormat JsonLines` schreibt jeder Status genau ein JSON-Objekt pro Zeile nach stdout:

```json
{"type":"step","message":"Serververbindung prüfen"}
{"type":"success","message":"Server erreichbar"}
{"type":"warning","message":"Seeds wurden übersprungen"}
{"type":"error","message":"Health-Check fehlgeschlagen"}
{"type":"completed","message":"Deployment abgeschlossen"}
```

Zulässige Ereignistypen:

- `step`
- `success`
- `warning`
- `error`
- `completed`

Regeln:

- `-NonInteractive` darf niemals `Read-Host`, Dialoge oder andere Eingaben auslösen.
- `-SkipLocalGit` überspringt lokales Staging und Committen; DeployDesk übernimmt diesen Schritt.
- Normale Toolausgabe darf zusätzlich im Log erscheinen, aber niemals Geheimnisse enthalten.
- Exitcode `0` gilt ausschließlich für ein wirklich erfolgreiches Deployment.
- Kritische Fehler erzeugen vorher ein `error`-Event und einen Exitcode ungleich `0`.
- `completed` wird erst nach erfolgreichem Health-Check ausgegeben.

## 5. Sicherer Deploymentablauf

Der Runner soll in dieser Reihenfolge arbeiten:

1. lokale Voraussetzungen wie `git` und `ssh` prüfen;
2. Linkdatei lesen und validieren;
3. SSH-Verbindung mit `BatchMode=yes` und normaler `known_hosts`-Prüfung testen;
4. lokalen Branch und Arbeitsbaum prüfen;
5. nur im manuellen Modus optional committen;
6. `git push <remote> HEAD:<branch>` ausführen und Exitcode prüfen;
7. auf dem Server mit Fast-forward-only aktualisieren, niemals ungefragt `reset --hard` verwenden;
8. projektspezifischen Build beziehungsweise Docker Compose ausführen;
9. Migrationen oder Seeds nur bewusst und mit sicheren Defaults starten;
10. Health-Check mit festem Timeout und begrenzten Wiederholungen durchführen;
11. erst danach `completed` ausgeben.

Für Docker Compose:

- einen eindeutigen Compose-Projektnamen verwenden;
- globale `container_name`-Kollisionen vermeiden;
- Ports und Netzwerke für Mehrprojektbetrieb prüfen;
- vor dem Start nach Möglichkeit `docker compose config` ausführen;
- Build- oder Startfehler nicht als Health-Warnung verschlucken.

Ein SCP-Fallback darf keine zufällig zusammengestellte Teildateiliste kopieren. Falls Git auf dem Ziel nicht möglich ist, soll ein reproduzierbares vollständiges Artefaktverfahren mit klaren Ausschlüssen verwendet werden.

## 6. Geheimnisse und einmalige Servervorbereitung

Niemals in `.deploylink`, Git oder Logs speichern:

- Passwörter oder Tokens;
- private SSH-Schlüssel;
- `.env`-Inhalte;
- Datenbank-URLs mit Zugangsdaten;
- Registry- oder Cloud-Credentials.

SSH-Schlüssel kommen aus SSH-Agent, Benutzerprofil oder `~/.ssh/config`. Anwendungsgeheimnisse bleiben in einer serverseitigen `.env`, einem Secret Store oder Docker Secrets.

Einmalige Servervorbereitung wird getrennt dokumentiert und nicht bei jedem Deploy ausgeführt:

- Docker und Compose installieren;
- Deploy-Benutzer und Rechte einrichten;
- Repository und Deploy-Key konfigurieren;
- serverseitige Umgebungsvariablen anlegen;
- DNS, Reverse Proxy und TLS einrichten.

`StrictHostKeyChecking=no` ist kein zulässiger Standard.

## 7. Verifikation ohne Produktionsdeploy

Die AI führt alle repositoryspezifischen Vorgaben aus `AGENTS.md` aus. Zusätzlich:

### JSON prüfen

```powershell
Get-Content -Raw .\meine-app-prod.deploylink | ConvertFrom-Json | Out-Null
```

### PowerShell-Syntax prüfen

```powershell
$tokens = $null
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path .\deploy\deploy.ps1),
    [ref]$tokens,
    [ref]$errors
) | Out-Null
if ($errors.Count) {
    $errors | Format-List
    exit 1
}
```

### Runner-Konfiguration ohne Serverzugriff prüfen

```powershell
.\deploy\deploy.ps1 -DeployLinkPath .\meine-app-prod.deploylink -ValidateOnly
```

### Statische Integrationsprüfung

- Linkdatei besitzt `schemaVersion: 2` und verweist auf das v2-Schema.
- Genau der ausgewählte Server steht im `server`-Block.
- Runnerpfad existiert und bleibt innerhalb des Repositories.
- Jedes `options[].argument` besitzt einen passenden Runnerparameter.
- Host, Benutzer, Ports, Remote-Pfad, Remote und Branch sind nicht im Runner hardcodiert.
- Es gibt keine Serverduplikate in `runner.arguments`.
- Linkdatei und Runner enthalten keine Geheimnisse.
- Der Runner emittiert im JSONL-Modus gültige Einzelzeilen und korrekte Exitcodes.
- `git diff --check` ist fehlerfrei und fremde Änderungen wurden nicht übernommen.

Danach die Linkdatei in DeployDesk importieren. DeployDesk zeigt Server und Remote-Pfad vor der Vertrauensbestätigung. Jede spätere Änderung an Linkdatei oder Runner invalidiert den gespeicherten Trust-Hash und muss neu bestätigt werden.

Reale SSH-, Build-, Health- oder Deploymenttests erfolgen nur mit ausdrücklicher Freigabe. Vor einem autorisierten Lauf ist das Ziel nochmals sichtbar zu nennen.

## Definition of Done

- Eine gültige Schema-v2-Linkdatei liegt im Repository-Root.
- Jedes Deploymentziel kann einen anderen Server, SSH-Port und Remote-Pfad verwenden.
- Alle Zielwerte kommen aus der Linkdatei, nicht aus hardcodierten Runnerwerten.
- Der Runner unterstützt `-DeployLinkPath`, Textmodus und JSONL-Protokoll.
- Optionen besitzen sichere Defaults und passende Runnerparameter.
- Health-Fehler verhindern Exitcode `0` und `completed`.
- Keine Geheimnisse wurden versioniert oder geloggt.
- Einmalige Servervorbereitung ist getrennt dokumentiert.
- Alle Build-, Typ-, Syntax- und Integrationsprüfungen sind grün.
- Es wurde ohne Freigabe kein echtes Deployment ausgeführt.

Der Abschlussbericht der AI nennt die geänderten Dateien, das nicht geheime Ziel, alle ausgeführten Prüfungen und noch offene einmalige Serverarbeiten.
