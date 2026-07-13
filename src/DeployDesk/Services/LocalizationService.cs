using System.ComponentModel;
using System.Globalization;

namespace DeployDesk.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Translations =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["WindowSubtitle"] = "Deployment workspace",
                ["Minimize"] = "Minimize", ["Maximize"] = "Maximize", ["Close"] = "Close",
                ["Settings"] = "Settings", ["SettingsShortcut"] = "Settings (Ctrl+,)", ["Projects"] = "PROJECTS", ["DeploymentTargets"] = "Deployment targets",
                ["AddProject"] = "Add project", ["DropZone"] = "DROP ZONE", ["DropHint"] = "Drop a .deploylink file here",
                ["RemoveProject"] = "Remove project", ["Workspace"] = "WORKSPACE", ["Website"] = "Website",
                ["Repository"] = "Repository", ["Sync"] = "Sync", ["Branch"] = "BRANCH", ["Worktree"] = "WORKTREE",
                ["Pending"] = "PENDING", ["LastDeploy"] = "LAST DEPLOY", ["Deployment"] = "Deployment",
                ["DeploymentSubtitle"] = "Prepare a commit and publish it safely", ["CommitMessage"] = "COMMIT MESSAGE",
                ["CommitMessageHint"] = "Leave empty to automatically use the current date and time.",
                ["AutoCommit"] = "Commit changes before deployment", ["CancelDeployment"] = "Cancel deployment",
                ["LocalChanges"] = "Local changes", ["RecentCommits"] = "Recent commits",
                ["DeploymentActivity"] = "Deployment activity", ["RunnerLiveOutput"] = "Live runner output",
                ["CopyLog"] = "Copy log", ["SettingsTitle"] = "Settings",
                ["SettingsSubtitle"] = "Personalize DeployDesk for your workflow.", ["Language"] = "Language",
                ["LanguageDescription"] = "Changes the visible application language immediately.",
                ["English"] = "English", ["German"] = "German", ["RefreshSection"] = "REPOSITORY STATUS",
                ["AutoRefresh"] = "Refresh automatically",
                ["AutoRefreshDescription"] = "Keep repository status current while DeployDesk is active.",
                ["RefreshInterval"] = "Refresh interval", ["Seconds"] = "seconds",
                ["SafetySection"] = "DEPLOYMENT SAFETY", ["ConfirmDeploy"] = "Confirm before deployment",
                ["ConfirmDeployDescription"] = "Show the target and ask for confirmation before starting a runner.",
                ["ClearLog"] = "Clear log before deployment",
                ["ClearLogDescription"] = "Start each deployment with a clean activity view.",
                ["LogSection"] = "ACTIVITY LOG", ["AutoScrollLog"] = "Follow live output",
                ["AutoScrollLogDescription"] = "Automatically scroll to the newest runner output.",
                ["SettingsSaved"] = "Changes are saved automatically on this device.",
                ["AboutSection"] = "ABOUT", ["ApplicationVersion"] = "Application version",
                ["NoProject"] = "No project selected", ["NoProjectDescription"] = "Add a .deploylink file to get started.",
                ["NoTarget"] = "NO TARGET", ["ServerNotConfigured"] = "Server not configured", ["Never"] = "Never",
                ["Ready"] = "Ready.", ["NoDeploymentStarted"] = "No deployment has been started yet.",
                ["Loading"] = "Loading …", ["Clean"] = "Clean", ["ChangesCount"] = "{0} change(s)",
                ["EverythingDeployed"] = "Everything deployed", ["PendingCount"] = "{0} pending",
                ["ProjectCleanStatus"] = "{0} · clean", ["ProjectChangedStatus"] = "{0} · {1} changed",
                ["StatusUnavailable"] = "Status unavailable", ["DeployRunning"] = "DEPLOYING",
                ["DeployNow"] = "DEPLOY NOW", ["RunnerExecuting"] = "Runner is executing",
                ["UpdateProduction"] = "Update production environment", ["TodayAt"] = "Today, {0:HH:mm}",
                ["ConfirmDeployTitle"] = "Confirm deployment",
                ["ConfirmDeployMessage"] = "Project: {0}\n\nTarget server:\n{1}\n{2}\n\nRunner:\n{3}\n\nStart this deployment now?",
                ["ChangesToCommit"] = "Current worktree changes ({0}):",
                ["MoreChanges"] = "… and {0} more",
                ["CommitAllWarning"] = "WARNING: Auto-commit is enabled. Every current tracked and untracked change listed by Git will be staged and committed before deployment.",
                ["NoLocalChanges"] = "The worktree currently has no local changes.",
                ["TrustProjectTitle"] = "Add deployment project",
                ["TrustProjectMessage"] = "Project: {0}\n\nRepository:\n{1}\n\nTarget server:\n{2}\n{3}\n\nRunner to execute:\n{4}\n\nDeployment SHA-256:\n{5}\n\nDo you trust this project and target?",
                ["ProjectOpenError"] = "Project could not be opened",
                ["DuplicateProjectId"] = "Another imported project already uses the ID '{0}'. Project IDs must be unique on this device.",
                ["ConfigChanged"] = "The deployment configuration or runner has changed. Remove and add the project again to confirm the new target.",
                ["ConfigChangedReimport"] = "The deployment configuration or runner has changed. Add the project again and confirm it.",
                ["PreparingDeployment"] = "Preparing deployment …",
                ["DirtyWorktree"] = "The worktree contains changes. Enable committing before deployment or commit manually.",
                ["PotentialSecretChanges"] = "Automatic commit was blocked because these paths may contain secrets:\n{0}\n\nReview and ignore them, or commit the intended files manually.",
                ["CommittingChanges"] = "Committing local changes …", ["CommitCreated"] = "Commit created: {0}",
                ["TargetLog"] = "Target: {0}", ["RunnerLog"] = "Runner: {0}",
                ["DeploymentFailed"] = "Deployment failed (exit {0}).",
                ["WrongBranch"] = "The current branch is '{0}', but this project requires '{1}'. Switch branches before deploying.",
                ["RunnerReportedError"] = "The runner reported an error event even though the process exited successfully.",
                ["MissingCompletedEvent"] = "The runner exited successfully without the required completed event.",
                ["DeploymentSuccessful"] = "Deployment successful · {0:HH:mm:ss}",
                ["DeploymentCanceled"] = "Deployment canceled.",
                ["DeploymentCanceledLog"] = "The deployment was canceled by the user.",
                ["SelectDeployLink"] = "Select deployment link",
                ["DeployLinkFilter"] = "DeployDesk link (*.deploylink)|*.deploylink|All files (*.*)|*.*",
                ["CopiedStatus"] = "Status"
            },
            ["de"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["WindowSubtitle"] = "Deployment-Arbeitsbereich",
                ["Minimize"] = "Minimieren", ["Maximize"] = "Maximieren", ["Close"] = "Schließen",
                ["Settings"] = "Einstellungen", ["SettingsShortcut"] = "Einstellungen (Strg+,)", ["Projects"] = "PROJEKTE", ["DeploymentTargets"] = "Deployment-Ziele",
                ["AddProject"] = "Projekt hinzufügen", ["DropZone"] = "ABLAGEBEREICH", ["DropHint"] = ".deploylink hier ablegen",
                ["RemoveProject"] = "Projekt entfernen", ["Workspace"] = "ARBEITSBEREICH", ["Website"] = "Website",
                ["Repository"] = "Repository", ["Sync"] = "Synchronisieren", ["Branch"] = "BRANCH",
                ["Worktree"] = "ARBEITSKOPIE", ["Pending"] = "AUSSTEHEND", ["LastDeploy"] = "LETZTES DEPLOYMENT",
                ["Deployment"] = "Deployment", ["DeploymentSubtitle"] = "Commit vorbereiten und sicher veröffentlichen",
                ["CommitMessage"] = "COMMIT-NACHRICHT",
                ["CommitMessageHint"] = "Leer lassen, um Datum und Uhrzeit automatisch zu verwenden.",
                ["AutoCommit"] = "Änderungen vor dem Deployment committen", ["CancelDeployment"] = "Deployment abbrechen",
                ["LocalChanges"] = "Lokale Änderungen", ["RecentCommits"] = "Letzte Commits",
                ["DeploymentActivity"] = "Deployment-Aktivität", ["RunnerLiveOutput"] = "Live-Ausgabe des Runners",
                ["CopyLog"] = "Log kopieren", ["SettingsTitle"] = "Einstellungen",
                ["SettingsSubtitle"] = "Passe DeployDesk an deinen Arbeitsablauf an.", ["Language"] = "Sprache",
                ["LanguageDescription"] = "Ändert die sichtbare Programmsprache sofort.",
                ["English"] = "Englisch", ["German"] = "Deutsch", ["RefreshSection"] = "REPOSITORY-STATUS",
                ["AutoRefresh"] = "Automatisch aktualisieren",
                ["AutoRefreshDescription"] = "Hält den Repository-Status aktuell, solange DeployDesk aktiv ist.",
                ["RefreshInterval"] = "Aktualisierungsintervall", ["Seconds"] = "Sekunden",
                ["SafetySection"] = "DEPLOYMENT-SICHERHEIT", ["ConfirmDeploy"] = "Vor Deployment bestätigen",
                ["ConfirmDeployDescription"] = "Zeigt das Ziel und fragt vor dem Start eines Runners nach.",
                ["ClearLog"] = "Log vor Deployment leeren",
                ["ClearLogDescription"] = "Beginnt jedes Deployment mit einer leeren Aktivitätsansicht.",
                ["LogSection"] = "AKTIVITÄTSLOG", ["AutoScrollLog"] = "Live-Ausgabe verfolgen",
                ["AutoScrollLogDescription"] = "Scrollt automatisch zur neuesten Runner-Ausgabe.",
                ["SettingsSaved"] = "Änderungen werden automatisch auf diesem Gerät gespeichert.",
                ["AboutSection"] = "ÜBER", ["ApplicationVersion"] = "Programmversion",
                ["NoProject"] = "Kein Projekt ausgewählt",
                ["NoProjectDescription"] = "Füge eine .deploylink-Datei hinzu, um zu beginnen.",
                ["NoTarget"] = "KEIN ZIEL", ["ServerNotConfigured"] = "Server nicht konfiguriert", ["Never"] = "Noch nie",
                ["Ready"] = "Bereit.", ["NoDeploymentStarted"] = "Noch kein Deployment gestartet.",
                ["Loading"] = "Wird geladen …", ["Clean"] = "Sauber", ["ChangesCount"] = "{0} Änderung(en)",
                ["EverythingDeployed"] = "Alles deployt", ["PendingCount"] = "{0} ausstehend",
                ["ProjectCleanStatus"] = "{0} · sauber", ["ProjectChangedStatus"] = "{0} · {1} geändert",
                ["StatusUnavailable"] = "Status nicht verfügbar", ["DeployRunning"] = "DEPLOY LÄUFT",
                ["DeployNow"] = "JETZT DEPLOYEN", ["RunnerExecuting"] = "Runner wird ausgeführt",
                ["UpdateProduction"] = "Produktionsumgebung aktualisieren", ["TodayAt"] = "Heute, {0:HH:mm}",
                ["ConfirmDeployTitle"] = "Deployment bestätigen",
                ["ConfirmDeployMessage"] = "Projekt: {0}\n\nZielserver:\n{1}\n{2}\n\nRunner:\n{3}\n\nDieses Deployment jetzt starten?",
                ["ChangesToCommit"] = "Aktuelle Änderungen der Arbeitskopie ({0}):",
                ["MoreChanges"] = "… und {0} weitere",
                ["CommitAllWarning"] = "WARNUNG: Auto-Commit ist aktiviert. Alle aktuell von Git erfassten nachverfolgten und nicht nachverfolgten Änderungen werden vor dem Deployment gestaged und committed.",
                ["NoLocalChanges"] = "Die Arbeitskopie enthält aktuell keine lokalen Änderungen.",
                ["TrustProjectTitle"] = "Deploy-Projekt hinzufügen",
                ["TrustProjectMessage"] = "Projekt: {0}\n\nRepository:\n{1}\n\nZielserver:\n{2}\n{3}\n\nAusgeführter Runner:\n{4}\n\nDeployment-SHA-256:\n{5}\n\nMöchtest du diesem Projekt und Ziel vertrauen?",
                ["ProjectOpenError"] = "Projekt konnte nicht geöffnet werden",
                ["ConfigChanged"] = "Deployment-Konfiguration oder Runner wurde seit dem Import geändert. Entferne das Projekt und füge es erneut hinzu, um das neue Ziel zu bestätigen.",
                ["ConfigChangedReimport"] = "Deployment-Konfiguration oder Runner wurde geändert. Bitte das Projekt erneut hinzufügen und bestätigen.",
                ["PreparingDeployment"] = "Deployment wird vorbereitet …",
                ["DirtyWorktree"] = "Das Arbeitsverzeichnis enthält Änderungen. Aktiviere den Commit vor dem Deploy oder committe manuell.",
                ["CommittingChanges"] = "Lokale Änderungen werden committed …", ["CommitCreated"] = "Commit erstellt: {0}",
                ["TargetLog"] = "Ziel: {0}", ["RunnerLog"] = "Runner: {0}",
                ["DeploymentFailed"] = "Deployment fehlgeschlagen (Exit {0}).",
                ["WrongBranch"] = "Der aktuelle Branch ist '{0}', f\u00FCr dieses Projekt ist jedoch '{1}' erforderlich. Wechsle vor dem Deployment den Branch.",
                ["RunnerReportedError"] = "Der Runner hat trotz erfolgreichem Prozessende ein Fehlerereignis gemeldet.",
                ["MissingCompletedEvent"] = "Der Runner wurde erfolgreich beendet, ohne das erforderliche completed-Ereignis zu senden.",
                ["PotentialSecretChanges"] = "Der automatische Commit wurde blockiert, weil diese Pfade Geheimnisse enthalten k\u00F6nnten:\n{0}\n\nPr\u00FCfe und ignoriere sie oder committe die vorgesehenen Dateien manuell.",
                ["DuplicateProjectId"] = "Ein anderes importiertes Projekt verwendet bereits die ID '{0}'. Projekt-IDs m\u00FCssen auf diesem Ger\u00E4t eindeutig sein.",
                ["DeploymentSuccessful"] = "Deployment erfolgreich · {0:HH:mm:ss}",
                ["DeploymentCanceled"] = "Deployment abgebrochen.",
                ["DeploymentCanceledLog"] = "Deployment wurde durch den Benutzer abgebrochen.",
                ["SelectDeployLink"] = "Deploy-Verknüpfung auswählen",
                ["DeployLinkFilter"] = "DeployDesk-Verknüpfung (*.deploylink)|*.deploylink|Alle Dateien (*.*)|*.*",
                ["CopiedStatus"] = "Status"
            }
        };

    private string _language = "en";

    public string Language => _language;
    public string this[string key] => Translations[_language].TryGetValue(key, out var value)
        ? value
        : Translations["en"].TryGetValue(key, out value) ? value : key;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetLanguage(string? language)
    {
        var normalized = string.Equals(language, "de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
        if (_language == normalized)
        {
            return;
        }

        _language = normalized;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(normalized);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
    }

    public string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, this[key], arguments);
}
