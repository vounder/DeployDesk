using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DeployDesk.Models;
using DeployDesk.Services;
using Microsoft.Win32;

namespace DeployDesk;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ProcessService _processService = new();
    private readonly GitService _gitService;
    private readonly DeployLinkService _deployLinkService;
    private readonly StateService _stateService = new();
    private readonly ConcurrentQueue<(string Line, bool IsError)> _pendingLogLines = new();
    private readonly StringBuilder _logBuilder = new();
    private readonly DispatcherTimer _logFlushTimer;

    private AppState _appState = new();
    private ProjectListItem? _selectedProject;
    private CancellationTokenSource? _deployCancellation;
    private Task? _initializationTask;
    private bool _isBusy;
    private string _projectName = "Kein Projekt ausgewählt";
    private string _projectDescription = "Füge eine .deploylink-Datei hinzu, um zu beginnen.";
    private string _serverEnvironment = "NO TARGET";
    private string _serverSummary = "Server nicht konfiguriert";
    private string _branch = "–";
    private string _dirtySummary = "–";
    private string _undeployedSummary = "–";
    private string _lastDeploySummary = "Noch nie";
    private string _deployStatus = "Bereit.";
    private string _logText = "Noch kein Deploy gestartet.";
    private string _commitMessage = string.Empty;
    private bool _autoCommit = true;
    private Brush _statusBrush;

    public MainWindow()
    {
        InitializeComponent();
        _gitService = new GitService(_processService);
        _deployLinkService = new DeployLinkService(_gitService);
        _statusBrush = (Brush)FindResource("MutedBrush");
        _logFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _logFlushTimer.Tick += FlushLogLines;
        _logFlushTimer.Start();
        DataContext = this;
        Loaded += async (_, _) => await EnsureInitializedAsync();
        Closed += (_, _) => _logFlushTimer.Stop();
    }

    public ObservableCollection<ProjectListItem> Projects { get; } = [];
    public ObservableCollection<OptionItem> Options { get; } = [];
    public ObservableCollection<string> Changes { get; } = [];
    public ObservableCollection<CommitItem> Commits { get; } = [];

    public ProjectListItem? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (ReferenceEquals(_selectedProject, value))
            {
                return;
            }

            _selectedProject = value;
            OnPropertyChanged(nameof(SelectedProject));
            OnPropertyChanged(nameof(HasProject));
            OnPropertyChanged(nameof(CanDeploy));
            OnPropertyChanged(nameof(CanRefresh));
            OnPropertyChanged(nameof(CanOpenWebsite));
            ApplySelectedProject();
            _ = RefreshAsync();
        }
    }

    public bool HasProject => SelectedProject is not null;
    public bool CanDeploy => HasProject && !_isBusy;
    public bool CanRefresh => HasProject && !_isBusy;
    public bool CanCancel => _isBusy;
    public bool CanOpenWebsite => SelectedProject?.Config.Links.Any(link => Uri.TryCreate(link.Url, UriKind.Absolute, out _)) == true;
    public Visibility ProgressVisibility => _isBusy ? Visibility.Visible : Visibility.Collapsed;

    public string ProjectName { get => _projectName; private set => Set(ref _projectName, value); }
    public string ProjectDescription { get => _projectDescription; private set => Set(ref _projectDescription, value); }
    public string ServerEnvironment { get => _serverEnvironment; private set => Set(ref _serverEnvironment, value); }
    public string ServerSummary { get => _serverSummary; private set => Set(ref _serverSummary, value); }
    public string Branch { get => _branch; private set => Set(ref _branch, value); }
    public string DirtySummary { get => _dirtySummary; private set => Set(ref _dirtySummary, value); }
    public string UndeployedSummary { get => _undeployedSummary; private set => Set(ref _undeployedSummary, value); }
    public string LastDeploySummary { get => _lastDeploySummary; private set => Set(ref _lastDeploySummary, value); }
    public string DeployStatus { get => _deployStatus; private set => Set(ref _deployStatus, value); }
    public string LogText { get => _logText; private set => Set(ref _logText, value); }
    public Brush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }
    public string DeployActionLabel => _isBusy ? "DEPLOY LÄUFT" : "JETZT DEPLOYEN";
    public string DeployActionHint => _isBusy ? "Runner wird ausgeführt" : "Produktionsumgebung aktualisieren";

    public string CommitMessage
    {
        get => _commitMessage;
        set => Set(ref _commitMessage, value);
    }

    public bool AutoCommit
    {
        get => _autoCommit;
        set => Set(ref _autoCommit, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task ImportFromCommandLineAsync(string path)
    {
        await EnsureInitializedAsync();
        await ImportProjectAsync(path, askForTrust: true);
    }

    private Task EnsureInitializedAsync()
    {
        return _initializationTask ??= InitializeCoreAsync();
    }

    private async Task InitializeCoreAsync()
    {
        _appState = await _stateService.LoadAsync();

        foreach (var projectFile in _appState.ProjectFiles.ToArray())
        {
            try
            {
                await AddProjectAsync(projectFile);
            }
            catch
            {
                _appState.ProjectFiles.Remove(projectFile);
            }
        }

        await _stateService.SaveAsync(_appState);
        SelectedProject ??= Projects.FirstOrDefault();
    }

    private async Task ImportProjectAsync(string path, bool askForTrust)
    {
        try
        {
            var config = await _deployLinkService.LoadAsync(path);
            var existing = Projects.FirstOrDefault(item =>
                item.Config.SourcePath.Equals(config.SourcePath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                SelectedProject = existing;
                return;
            }

            if (askForTrust)
            {
                var deploymentHash = ComputeDeploymentHash(config.RunnerPath, config.SourcePath);
                var target = $"{config.Server.User}@{config.Server.Host}:{config.Server.SshPort}";
                var answer = MessageBox.Show(
                    $"Projekt: {config.Project.Name}\n\nRepository:\n{config.RepositoryRoot}\n\nZielserver:\n{target}\n{config.Server.RemotePath}\n\nAusgeführter Runner:\n{config.RunnerPath}\n\nDeployment-SHA-256:\n{deploymentHash}\n\nMöchtest du diesem Projekt und Ziel vertrauen?",
                    "Deploy-Projekt hinzufügen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }

                if (!_appState.Projects.TryGetValue(config.Project.Id, out var state))
                {
                    state = new ProjectState();
                    _appState.Projects[config.Project.Id] = state;
                }
                state.TrustedDeploymentHash = deploymentHash;
            }

            var item = new ProjectListItem(config);
            Projects.Add(item);
            if (!_appState.ProjectFiles.Contains(config.SourcePath, StringComparer.OrdinalIgnoreCase))
            {
                _appState.ProjectFiles.Add(config.SourcePath);
            }

            await _stateService.SaveAsync(_appState);
            SelectedProject = item;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Projekt konnte nicht geöffnet werden", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task AddProjectAsync(string path)
    {
        var config = await _deployLinkService.LoadAsync(path);
        var currentHash = ComputeDeploymentHash(config.RunnerPath, config.SourcePath);
        if (!_appState.Projects.TryGetValue(config.Project.Id, out var state) ||
            !string.Equals(state.TrustedDeploymentHash, currentHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Deployment-Konfiguration oder Runner wurde geändert. Bitte das Projekt erneut hinzufügen und bestätigen.");
        }
        Projects.Add(new ProjectListItem(config));
    }

    private void ApplySelectedProject()
    {
        Options.Clear();
        Changes.Clear();
        Commits.Clear();

        if (SelectedProject is null)
        {
            ProjectName = "Kein Projekt ausgewählt";
            ProjectDescription = "Füge eine .deploylink-Datei hinzu, um zu beginnen.";
            ServerEnvironment = "NO TARGET";
            ServerSummary = "Server nicht konfiguriert";
            Branch = DirtySummary = UndeployedSummary = "–";
            LastDeploySummary = "Noch nie";
            return;
        }

        var config = SelectedProject.Config;
        ProjectName = config.Project.Name;
        ProjectDescription = string.IsNullOrWhiteSpace(config.Project.Description)
            ? config.RepositoryRoot
            : config.Project.Description;
        ServerEnvironment = string.IsNullOrWhiteSpace(config.Server.Name)
            ? "SERVER"
            : config.Server.Name.ToUpperInvariant();
        ServerSummary = $"{config.Server.User}@{config.Server.Host}:{config.Server.SshPort} · {config.Server.RemotePath}";
        LastDeploySummary = FormatLastDeploy(config.Project.Id);
        foreach (var option in config.Options)
        {
            Options.Add(new OptionItem(option));
        }

        AnimateContentTransition();
    }

    private async Task RefreshAsync()
    {
        var selected = SelectedProject;
        if (selected is null || _isBusy)
        {
            return;
        }

        try
        {
            var config = selected.Config;
            var branchTask = _gitService.GetBranchAsync(config.RepositoryRoot);
            var changesTask = _gitService.GetChangesAsync(config.RepositoryRoot);
            var commitsTask = _gitService.GetCommitsAsync(config.RepositoryRoot);
            _appState.Projects.TryGetValue(config.Project.Id, out var projectState);
            var undeployedTask = _gitService.GetUndeployedCountAsync(
                config.RepositoryRoot,
                projectState?.LastDeployedCommit,
                config.Repository.Remote,
                config.Repository.Branch);

            await Task.WhenAll(branchTask, changesTask, commitsTask, undeployedTask);
            if (!ReferenceEquals(selected, SelectedProject))
            {
                return;
            }

            Branch = branchTask.Result;
            Changes.Clear();
            foreach (var change in changesTask.Result)
            {
                Changes.Add(change);
            }

            Commits.Clear();
            foreach (var commit in commitsTask.Result)
            {
                Commits.Add(commit);
            }

            DirtySummary = Changes.Count == 0 ? "Sauber" : $"{Changes.Count} Änderung(en)";
            UndeployedSummary = undeployedTask.Result == 0 ? "Alles deployt" : $"{undeployedTask.Result} ausstehend";
            selected.Status = $"{Branch} · {(Changes.Count == 0 ? "sauber" : $"{Changes.Count} geändert")}";
        }
        catch (Exception exception)
        {
            selected.Status = "Status nicht verfügbar";
            SetStatus(exception.Message, "DangerBrush");
        }
    }

    private async Task StartDeployAsync()
    {
        var selected = SelectedProject;
        if (selected is null || _isBusy)
        {
            return;
        }

        var config = selected.Config;
        try
        {
            var currentRunnerHash = ComputeDeploymentHash(config.RunnerPath, config.SourcePath);
            if (!_appState.Projects.TryGetValue(config.Project.Id, out var trustedState) ||
                !string.Equals(trustedState.TrustedDeploymentHash, currentRunnerHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Deployment-Konfiguration oder Runner wurde seit dem Import geändert. Entferne das Projekt und füge es erneut hinzu, um das neue Ziel zu bestätigen.");
            }

            SetBusy(true);
            _logBuilder.Clear();
            LogText = string.Empty;
            SetStatus("Deployment wird vorbereitet …", "TextBrush");

            var changes = await _gitService.GetChangesAsync(config.RepositoryRoot);
            if (changes.Count > 0)
            {
                if (!AutoCommit)
                {
                    throw new InvalidOperationException("Das Arbeitsverzeichnis enthält Änderungen. Aktiviere den Commit vor dem Deploy oder committe manuell.");
                }

                var message = string.IsNullOrWhiteSpace(CommitMessage)
                    ? $"deploy: {DateTime.Now:yyyy-MM-dd HH:mm}"
                    : CommitMessage.Trim();
                SetStatus("Lokale Änderungen werden committed …", "WarningBrush");
                await _gitService.CommitAllAsync(config.RepositoryRoot, message);
                CommitMessage = string.Empty;
                EnqueueLog($"Commit erstellt: {message}");
            }

            var deployedHead = await _gitService.GetHeadAsync(config.RepositoryRoot);
            var arguments = new List<string>
            {
                "-NoProfile",
                "-ExecutionPolicy", "Bypass",
                "-File", config.RunnerPath
            };
            arguments.AddRange(config.Runner.Arguments);
            arguments.AddRange(["-DeployLinkPath", config.SourcePath]);
            AddRunnerArgument(arguments, "-NonInteractive");
            AddRunnerArgument(arguments, "-SkipLocalGit");
            if (!arguments.Contains("-OutputFormat", StringComparer.OrdinalIgnoreCase))
            {
                arguments.AddRange(["-OutputFormat", "JsonLines"]);
            }

            foreach (var option in Options.Where(option => option.IsSelected))
            {
                arguments.Add(option.Definition.Argument);
            }

            _deployCancellation = new CancellationTokenSource();
            EnqueueLog($"Ziel: {config.Server.User}@{config.Server.Host}:{config.Server.SshPort} · {config.Server.RemotePath}");
            EnqueueLog($"Runner: {config.RunnerPath}");
            var result = await _processService.RunAsync(
                "powershell.exe",
                arguments,
                config.RepositoryRoot,
                line => EnqueueLog(line),
                line => EnqueueLog(line, isError: true),
                _deployCancellation.Token);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"Deployment fehlgeschlagen (Exit {result.ExitCode}).");
            }

            if (!_appState.Projects.TryGetValue(config.Project.Id, out var state))
            {
                state = new ProjectState();
                _appState.Projects[config.Project.Id] = state;
            }

            state.LastDeployedCommit = deployedHead;
            state.LastDeployedAt = DateTimeOffset.Now;
            await _stateService.SaveAsync(_appState);
            LastDeploySummary = FormatLastDeploy(config.Project.Id);
            SetStatus($"Deployment erfolgreich · {DateTime.Now:HH:mm:ss}", "AccentBrush");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Deployment abgebrochen.", "WarningBrush");
            EnqueueLog("Deployment wurde durch den Benutzer abgebrochen.", isError: true);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, "DangerBrush");
            EnqueueLog(exception.Message, isError: true);
        }
        finally
        {
            _deployCancellation?.Dispose();
            _deployCancellation = null;
            SetBusy(false);
            await RefreshAsync();
        }
    }

    private static void AddRunnerArgument(List<string> arguments, string argument)
    {
        if (!arguments.Contains(argument, StringComparer.OrdinalIgnoreCase))
        {
            arguments.Add(argument);
        }
    }

    private static string ComputeDeploymentHash(string runnerPath, string deployLinkPath)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(File.ReadAllBytes(runnerPath));
        hash.AppendData(new byte[] { 0 });
        hash.AppendData(File.ReadAllBytes(deployLinkPath));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private void EnqueueLog(string line, bool isError = false) => _pendingLogLines.Enqueue((line, isError));

    private void FlushLogLines(object? sender, EventArgs e)
    {
        var changed = false;
        var count = 0;
        while (count++ < 250 && _pendingLogLines.TryDequeue(out var entry))
        {
            changed = true;
            HandleStructuredLine(entry.Line, entry.IsError);
        }

        if (!changed)
        {
            return;
        }

        const int maximumLogLength = 200_000;
        if (_logBuilder.Length > maximumLogLength)
        {
            _logBuilder.Remove(0, _logBuilder.Length - maximumLogLength);
        }

        LogText = _logBuilder.ToString();
        LogBox.ScrollToEnd();
    }

    private void HandleStructuredLine(string line, bool isError)
    {
        var displayLine = line;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var typeElement))
            {
                var type = typeElement.GetString();
                var message = root.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : root.TryGetProperty("label", out var labelElement) ? labelElement.GetString() : null;
                if (!string.IsNullOrWhiteSpace(message))
                {
                    displayLine = $"[{type?.ToUpperInvariant()}] {message}";
                    if (type is "step") SetStatus(message, "TextBrush");
                    if (type is "warning") SetStatus(message, "WarningBrush");
                    if (type is "error") SetStatus(message, "DangerBrush");
                }
            }
        }
        catch (JsonException)
        {
            // Normale Prozessausgabe bleibt unverändert sichtbar.
        }

        _logBuilder.AppendLine(isError ? $"[STDERR] {displayLine}" : displayLine);
    }

    private void SetBusy(bool value)
    {
        _isBusy = value;
        OnPropertyChanged(nameof(CanDeploy));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(ProgressVisibility));
        OnPropertyChanged(nameof(DeployActionLabel));
        OnPropertyChanged(nameof(DeployActionHint));

        if (FindResource("RunningStoryboard") is Storyboard storyboard)
        {
            if (value)
            {
                storyboard.Begin(this, true);
            }
            else
            {
                storyboard.Remove(this);
                DeployGlow.Opacity = 0;
                ActivityPulse.Opacity = 1;
                ProgressTranslate.X = -190;
            }
        }
    }

    private void SetStatus(string message, string brushResource)
    {
        DeployStatus = message;
        StatusBrush = (Brush)FindResource(brushResource);
    }

    private string FormatLastDeploy(string projectId)
    {
        if (!_appState.Projects.TryGetValue(projectId, out var state) || state.LastDeployedAt is not { } deployedAt)
        {
            return "Noch nie";
        }

        var localTime = deployedAt.ToLocalTime();
        return localTime.Date == DateTimeOffset.Now.Date
            ? $"Heute, {localTime:HH:mm}"
            : $"{localTime:dd.MM.yyyy, HH:mm}";
    }

    private void AnimateContentTransition()
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        ContentHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = easing
        });
        ContentTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = easing
        });
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        WindowFrame.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(10);
        WindowFrame.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
        MaximizeGlyph.Text = maximized ? "❐" : "□";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Deploy-Verknüpfung auswählen",
            Filter = "DeployDesk-Verknüpfung (*.deploylink)|*.deploylink|Alle Dateien (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            _ = ImportProjectAsync(dialog.FileName, askForTrust: true);
        }
    }

    private async void RemoveProject_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedProject;
        if (selected is null || _isBusy)
        {
            return;
        }

        Projects.Remove(selected);
        _appState.ProjectFiles.RemoveAll(path => path.Equals(selected.Config.SourcePath, StringComparison.OrdinalIgnoreCase));
        await _stateService.SaveAsync(_appState);
        SelectedProject = Projects.FirstOrDefault();
    }

    private void OpenRepository_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is { } selected)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", selected.Config.RepositoryRoot) { UseShellExecute = true });
        }
    }

    private void OpenWebsite_Click(object sender, RoutedEventArgs e)
    {
        var website = SelectedProject?.Config.Links.FirstOrDefault(link =>
            link.Label.Equals("Website", StringComparison.OrdinalIgnoreCase))
            ?? SelectedProject?.Config.Links.FirstOrDefault();
        if (website is not null && Uri.TryCreate(website.Url, UriKind.Absolute, out var uri))
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void Deploy_Click(object sender, RoutedEventArgs e) => await StartDeployAsync();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _deployCancellation?.Cancel();
        _processService.CancelActive();
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText($"DeployDesk · {ProjectName} · {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nStatus: {DeployStatus}\n\n{LogText}");
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.FirstOrDefault(path => path.EndsWith(".deploylink", StringComparison.OrdinalIgnoreCase)) is { } path)
        {
            _ = ImportProjectAsync(path, askForTrust: true);
        }
    }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
