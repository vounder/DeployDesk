using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
    private const int MaximumPendingLogLines = 10_000;
    private readonly ProcessService _processService = new();
    private readonly GitService _gitService;
    private readonly DeployLinkService _deployLinkService;
    private readonly string _powerShellExecutable;
    private readonly StateService _stateService = new();
    private readonly ConcurrentQueue<(string Line, bool IsError)> _pendingLogLines = new();
    private readonly StringBuilder _logBuilder = new();
    private readonly DispatcherTimer _logFlushTimer;
    private readonly DispatcherTimer _autoRefreshTimer;

    private AppState _appState = new();
    private bool _isRefreshing;
    private ProjectListItem? _selectedProject;
    private CancellationTokenSource? _deployCancellation;
    private Task? _initializationTask;
    private bool _isBusy;
    private bool _isSettingsOpen;
    private bool _loadingSettings;
    private string _languageCode = "en";
    private bool _autoRefreshEnabled = true;
    private int _autoRefreshIntervalSeconds = 5;
    private bool _confirmBeforeDeploy = true;
    private bool _clearLogBeforeDeploy = true;
    private bool _autoScrollLog = true;
    private int _pendingLogLineCount;
    private int _runnerCompletedEventReceived;
    private int _runnerErrorEventReceived;
    private int _undeployedCount;
    private string _projectName = "No project selected";
    private string _projectDescription = "Add a .deploylink file to get started.";
    private string _serverEnvironment = "NO TARGET";
    private string _serverSummary = "Server not configured";
    private string _branch = "–";
    private string _dirtySummary = "–";
    private string _undeployedSummary = "–";
    private string _lastDeploySummary = "Never";
    private string _deployStatus = "Ready.";
    private string _logText = "No deployment has been started yet.";
    private string _commitMessage = string.Empty;
    private bool _autoCommit;
    private Brush _statusBrush;

    public MainWindow()
    {
        InitializeComponent();
        _gitService = new GitService(_processService);
        _deployLinkService = new DeployLinkService(_gitService);
        _powerShellExecutable = ExecutableLocator.FindWindowsPowerShellExecutable();
        _statusBrush = (Brush)FindResource("MutedBrush");
        _logFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _logFlushTimer.Tick += FlushLogLines;
        _logFlushTimer.Start();
        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
        Activated += (_, _) => UpdateAutoRefreshTimer();
        Deactivated += (_, _) => _autoRefreshTimer.Stop();
        DataContext = this;
        Loaded += async (_, _) => await EnsureInitializedAsync();
        Closed += (_, _) =>
        {
            _logFlushTimer.Stop();
            _autoRefreshTimer.Stop();
        };
    }

    public ObservableCollection<ProjectListItem> Projects { get; } = [];
    public ObservableCollection<OptionItem> Options { get; } = [];
    public ObservableCollection<string> Changes { get; } = [];
    public ObservableCollection<CommitItem> Commits { get; } = [];
    public IReadOnlyList<int> RefreshIntervalOptions { get; } = [5, 15, 30, 60];
    public LocalizationService Texts { get; } = new();

    private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        // Ein bereits eingeplanter Tick darf nach dem Fokusverlust keinen Refresh mehr starten.
        if (!IsActive)
        {
            return;
        }

        await RefreshAsync();
    }

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
    public bool CanOpenWebsite => SelectedProject?.Config.Links.Any(link => TryGetWebUri(link.Url, out _)) == true;
    public Visibility ProgressVisibility => _isBusy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsVisibility => _isSettingsOpen ? Visibility.Visible : Visibility.Collapsed;

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
    public string DeployActionLabel => _isBusy ? Texts["DeployRunning"] : Texts["DeployNow"];
    public string DeployActionHint => _isBusy ? Texts["RunnerExecuting"] : Texts["UpdateProduction"];

    public string LanguageCode
    {
        get => _languageCode;
        set
        {
            var normalized = string.Equals(value, "de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
            if (_languageCode == normalized)
            {
                return;
            }

            _languageCode = normalized;
            Texts.SetLanguage(normalized);
            _deployLinkService.LanguageCode = normalized;
            _processService.LanguageCode = normalized;
            OnPropertyChanged(nameof(LanguageCode));
            RelocalizeDynamicContent();
            PersistSettings();
        }
    }

    public bool AutoRefreshEnabled
    {
        get => _autoRefreshEnabled;
        set
        {
            if (_autoRefreshEnabled == value) return;
            _autoRefreshEnabled = value;
            OnPropertyChanged(nameof(AutoRefreshEnabled));
            UpdateAutoRefreshTimer();
            PersistSettings();
        }
    }

    public int AutoRefreshIntervalSeconds
    {
        get => _autoRefreshIntervalSeconds;
        set
        {
            var normalized = RefreshIntervalOptions.Contains(value) ? value : 5;
            if (_autoRefreshIntervalSeconds == normalized) return;
            _autoRefreshIntervalSeconds = normalized;
            _autoRefreshTimer.Interval = TimeSpan.FromSeconds(normalized);
            OnPropertyChanged(nameof(AutoRefreshIntervalSeconds));
            PersistSettings();
        }
    }

    public bool ConfirmBeforeDeploy
    {
        get => _confirmBeforeDeploy;
        set => SetSetting(ref _confirmBeforeDeploy, value, nameof(ConfirmBeforeDeploy));
    }

    public bool ClearLogBeforeDeploy
    {
        get => _clearLogBeforeDeploy;
        set => SetSetting(ref _clearLogBeforeDeploy, value, nameof(ClearLogBeforeDeploy));
    }

    public bool AutoScrollLog
    {
        get => _autoScrollLog;
        set => SetSetting(ref _autoScrollLog, value, nameof(AutoScrollLog));
    }

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
        _appState.Settings ??= new ApplicationSettings();
        LoadSettings(_appState.Settings);

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

    private void LoadSettings(ApplicationSettings settings)
    {
        _loadingSettings = true;
        try
        {
            LanguageCode = settings.Language;
            AutoRefreshEnabled = settings.AutoRefreshEnabled;
            AutoRefreshIntervalSeconds = settings.AutoRefreshIntervalSeconds;
            ConfirmBeforeDeploy = settings.ConfirmBeforeDeploy;
            ClearLogBeforeDeploy = settings.ClearLogBeforeDeploy;
            AutoScrollLog = settings.AutoScrollLog;
        }
        finally
        {
            _loadingSettings = false;
        }

        settings.Language = LanguageCode;
        settings.AutoRefreshEnabled = AutoRefreshEnabled;
        settings.AutoRefreshIntervalSeconds = AutoRefreshIntervalSeconds;
        settings.ConfirmBeforeDeploy = ConfirmBeforeDeploy;
        settings.ClearLogBeforeDeploy = ClearLogBeforeDeploy;
        settings.AutoScrollLog = AutoScrollLog;
        UpdateAutoRefreshTimer();
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

            if (Projects.Any(item => item.Config.Project.Id.Equals(config.Project.Id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(Texts.Format("DuplicateProjectId", config.Project.Id));
            }

            if (askForTrust)
            {
                var deploymentHash = ComputeDeploymentHash(config.RunnerPath, config.SourcePath);
                var target = $"{config.Server.User}@{config.Server.Host}:{config.Server.SshPort}";
                var answer = MessageBox.Show(
                    Texts.Format("TrustProjectMessage", config.Project.Name, config.RepositoryRoot, target,
                        config.Server.RemotePath, config.RunnerPath, deploymentHash),
                    Texts["TrustProjectTitle"],
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

            var item = new ProjectListItem(config, Texts["Loading"]);
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
            MessageBox.Show(exception.Message, Texts["ProjectOpenError"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task AddProjectAsync(string path)
    {
        var config = await _deployLinkService.LoadAsync(path);
        if (Projects.Any(item => item.Config.Project.Id.Equals(config.Project.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(Texts.Format("DuplicateProjectId", config.Project.Id));
        }
        var currentHash = ComputeDeploymentHash(config.RunnerPath, config.SourcePath);
        if (!_appState.Projects.TryGetValue(config.Project.Id, out var state) ||
            !string.Equals(state.TrustedDeploymentHash, currentHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(Texts["ConfigChangedReimport"]);
        }
        Projects.Add(new ProjectListItem(config, Texts["Loading"]));
    }

    private void ApplySelectedProject()
    {
        Options.Clear();
        Changes.Clear();
        Commits.Clear();

        if (SelectedProject is null)
        {
            ProjectName = Texts["NoProject"];
            ProjectDescription = Texts["NoProjectDescription"];
            ServerEnvironment = Texts["NoTarget"];
            ServerSummary = Texts["ServerNotConfigured"];
            Branch = DirtySummary = UndeployedSummary = "–";
            LastDeploySummary = Texts["Never"];
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
        if (selected is null || _isBusy || _isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
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

            _undeployedCount = undeployedTask.Result;
            DirtySummary = Changes.Count == 0 ? Texts["Clean"] : Texts.Format("ChangesCount", Changes.Count);
            UndeployedSummary = _undeployedCount == 0 ? Texts["EverythingDeployed"] : Texts.Format("PendingCount", _undeployedCount);
            selected.Status = Changes.Count == 0
                ? Texts.Format("ProjectCleanStatus", Branch)
                : Texts.Format("ProjectChangedStatus", Branch, Changes.Count);
        }
        catch (Exception exception)
        {
            selected.Status = Texts["StatusUnavailable"];
            SetStatus(exception.Message, "DangerBrush");
        }
        finally
        {
            _isRefreshing = false;
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
                throw new InvalidOperationException(Texts["ConfigChanged"]);
            }

            SetBusy(true);
            SetStatus(Texts["PreparingDeployment"], "TextBrush");

            var currentBranch = await _gitService.GetBranchAsync(config.RepositoryRoot);
            if (!string.Equals(currentBranch, config.Repository.Branch, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(Texts.Format("WrongBranch", currentBranch, config.Repository.Branch));
            }

            var changes = await _gitService.GetChangesAsync(config.RepositoryRoot);
            Changes.Clear();
            foreach (var change in changes)
            {
                Changes.Add(change);
            }

            if (changes.Count > 0)
            {
                if (!AutoCommit)
                {
                    throw new InvalidOperationException(Texts["DirtyWorktree"]);
                }

                var potentialSecretPaths = changes.Where(IsPotentialSecretChange).Take(5).ToList();
                if (potentialSecretPaths.Count > 0)
                {
                    throw new InvalidOperationException(Texts.Format(
                        "PotentialSecretChanges",
                        string.Join(Environment.NewLine, potentialSecretPaths)));
                }
            }

            if (ConfirmBeforeDeploy && MessageBox.Show(
                    BuildDeploymentConfirmation(config, changes),
                    Texts["ConfirmDeployTitle"],
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                SetStatus(Texts["DeploymentCanceled"], "WarningBrush");
                return;
            }

            if (ClearLogBeforeDeploy)
            {
                while (_pendingLogLines.TryDequeue(out _))
                {
                    Interlocked.Decrement(ref _pendingLogLineCount);
                }
                _logBuilder.Clear();
                LogText = string.Empty;
            }

            if (changes.Count > 0)
            {
                var message = string.IsNullOrWhiteSpace(CommitMessage)
                    ? $"deploy: {DateTime.Now:yyyy-MM-dd HH:mm}"
                    : CommitMessage.Trim();
                SetStatus(Texts["CommittingChanges"], "WarningBrush");
                await _gitService.CommitAllAsync(config.RepositoryRoot, message);
                CommitMessage = string.Empty;
                EnqueueLog(Texts.Format("CommitCreated", message));
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
            Volatile.Write(ref _runnerCompletedEventReceived, 0);
            Volatile.Write(ref _runnerErrorEventReceived, 0);
            EnqueueLog(Texts.Format("TargetLog", $"{config.Server.User}@{config.Server.Host}:{config.Server.SshPort} · {config.Server.RemotePath}"));
            EnqueueLog(Texts.Format("RunnerLog", config.RunnerPath));
            var result = await _processService.RunAsync(
                _powerShellExecutable,
                arguments,
                config.RepositoryRoot,
                line => EnqueueLog(line),
                line => EnqueueLog(line, isError: true),
                _deployCancellation.Token);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(Texts.Format("DeploymentFailed", result.ExitCode));
            }
            if (Volatile.Read(ref _runnerErrorEventReceived) != 0)
            {
                throw new InvalidOperationException(Texts["RunnerReportedError"]);
            }
            if (Volatile.Read(ref _runnerCompletedEventReceived) == 0)
            {
                throw new InvalidOperationException(Texts["MissingCompletedEvent"]);
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
            SetStatus(Texts.Format("DeploymentSuccessful", DateTime.Now), "AccentBrush");
        }
        catch (OperationCanceledException)
        {
            SetStatus(Texts["DeploymentCanceled"], "WarningBrush");
            EnqueueLog(Texts["DeploymentCanceledLog"], isError: true);
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

    private string BuildDeploymentConfirmation(DeployLink config, IReadOnlyList<string> changes)
    {
        var target = $"{config.Server.User}@{config.Server.Host}:{config.Server.SshPort}";
        var builder = new StringBuilder(Texts.Format(
            "ConfirmDeployMessage",
            config.Project.Name,
            target,
            config.Server.RemotePath,
            config.RunnerPath));
        builder.AppendLine().AppendLine();

        if (changes.Count == 0)
        {
            builder.Append(Texts["NoLocalChanges"]);
            return builder.ToString();
        }

        builder.AppendLine(Texts.Format("ChangesToCommit", changes.Count));
        foreach (var change in changes.Take(8))
        {
            builder.Append("  ").AppendLine(change);
        }

        if (changes.Count > 8)
        {
            builder.AppendLine(Texts.Format("MoreChanges", changes.Count - 8));
        }

        builder.AppendLine().Append(Texts["CommitAllWarning"]);
        return builder.ToString();
    }

    private static bool IsPotentialSecretChange(string statusLine)
    {
        var pathSection = statusLine.Length > 3 ? statusLine[3..] : statusLine;
        foreach (var candidate in pathSection.Split(" -> ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fileName = Path.GetFileName(candidate.Trim('"')).ToLowerInvariant();
            if (fileName is ".env.example" or ".env.sample" or ".env.template" or "credentials.example.json")
            {
                continue;
            }

            if (fileName == ".env" || fileName.StartsWith(".env.", StringComparison.Ordinal) ||
                fileName is "secrets.json" or "credentials.json" or "id_rsa" or "id_dsa" or "id_ecdsa" or "id_ed25519" ||
                fileName.EndsWith(".pem", StringComparison.Ordinal) ||
                fileName.EndsWith(".key", StringComparison.Ordinal) ||
                fileName.EndsWith(".p12", StringComparison.Ordinal) ||
                fileName.EndsWith(".pfx", StringComparison.Ordinal) ||
                fileName.EndsWith(".kdbx", StringComparison.Ordinal) ||
                fileName.EndsWith(".ovpn", StringComparison.Ordinal) ||
                fileName.EndsWith(".publishsettings", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeDeploymentHash(string runnerPath, string deployLinkPath)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFileToHash(hash, runnerPath);
        hash.AppendData(new byte[] { 0 });
        AppendFileToHash(hash, deployLinkPath);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendFileToHash(IncrementalHash hash, string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[81_920];
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, bytesRead);
        }
    }

    private void EnqueueLog(string line, bool isError = false)
    {
        ObserveRunnerProtocolEvent(line);
        if (Interlocked.Increment(ref _pendingLogLineCount) > MaximumPendingLogLines)
        {
            Interlocked.Decrement(ref _pendingLogLineCount);
            return;
        }

        _pendingLogLines.Enqueue((line, isError));
    }

    private void ObserveRunnerProtocolEvent(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            var type = typeElement.GetString();
            if (string.Equals(type, "completed", StringComparison.OrdinalIgnoreCase))
            {
                Volatile.Write(ref _runnerCompletedEventReceived, 1);
            }
            else if (string.Equals(type, "error", StringComparison.OrdinalIgnoreCase))
            {
                Volatile.Write(ref _runnerErrorEventReceived, 1);
            }
        }
        catch (JsonException)
        {
            // Plain runner output is not part of the JSONL protocol.
        }
    }

    private void FlushLogLines(object? sender, EventArgs e)
    {
        var changed = false;
        var count = 0;
        while (count++ < 250 && _pendingLogLines.TryDequeue(out var entry))
        {
            Interlocked.Decrement(ref _pendingLogLineCount);
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
        if (AutoScrollLog)
        {
            LogBox.ScrollToEnd();
        }
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
            return Texts["Never"];
        }

        var localTime = deployedAt.ToLocalTime();
        return localTime.Date == DateTimeOffset.Now.Date
            ? Texts.Format("TodayAt", localTime)
            : localTime.ToString(
                LanguageCode == "de" ? "dd.MM.yyyy, HH:mm" : "MMM d, yyyy, HH:mm",
                CultureInfo.GetCultureInfo(LanguageCode == "de" ? "de-DE" : "en-US"));
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
            Title = Texts["SelectDeployLink"],
            Filter = Texts["DeployLinkFilter"],
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
        _appState.Projects.Remove(selected.Config.Project.Id);
        await _stateService.SaveAsync(_appState);
        SelectedProject = Projects.FirstOrDefault();
    }

    private void OpenRepository_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is { } selected)
        {
            Process.Start(new ProcessStartInfo(selected.Config.RepositoryRoot) { UseShellExecute = true });
        }
    }

    private void OpenWebsite_Click(object sender, RoutedEventArgs e)
    {
        var website = SelectedProject?.Config.Links.FirstOrDefault(link =>
                link.Label.Equals("Website", StringComparison.OrdinalIgnoreCase) && TryGetWebUri(link.Url, out _))
            ?? SelectedProject?.Config.Links.FirstOrDefault(link => TryGetWebUri(link.Url, out _));
        if (website is not null && TryGetWebUri(website.Url, out var uri))
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
        Clipboard.SetText($"DeployDesk · {ProjectName} · {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{Texts["CopiedStatus"]}: {DeployStatus}\n\n{LogText}");
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

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        _isSettingsOpen = !_isSettingsOpen;
        OnPropertyChanged(nameof(SettingsVisibility));
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        _isSettingsOpen = false;
        OnPropertyChanged(nameof(SettingsVisibility));
    }

    private static bool TryGetWebUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
            (candidate.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             candidate.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(candidate.Host) &&
            string.IsNullOrEmpty(candidate.UserInfo))
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }

    private void SetSetting(ref bool field, bool value, string propertyName)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        PersistSettings();
    }

    private void PersistSettings()
    {
        if (_loadingSettings)
        {
            return;
        }

        var settings = _appState.Settings ??= new ApplicationSettings();
        settings.Language = LanguageCode;
        settings.AutoRefreshEnabled = AutoRefreshEnabled;
        settings.AutoRefreshIntervalSeconds = AutoRefreshIntervalSeconds;
        settings.ConfirmBeforeDeploy = ConfirmBeforeDeploy;
        settings.ClearLogBeforeDeploy = ClearLogBeforeDeploy;
        settings.AutoScrollLog = AutoScrollLog;
        _ = SaveSettingsAsync();
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _stateService.SaveAsync(_appState);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus(exception.Message, "DangerBrush");
        }
    }

    private void UpdateAutoRefreshTimer()
    {
        _autoRefreshTimer.Interval = TimeSpan.FromSeconds(AutoRefreshIntervalSeconds);
        if (AutoRefreshEnabled && IsActive)
        {
            _autoRefreshTimer.Start();
        }
        else
        {
            _autoRefreshTimer.Stop();
        }
    }

    private void RelocalizeDynamicContent()
    {
        OnPropertyChanged(nameof(DeployActionLabel));
        OnPropertyChanged(nameof(DeployActionHint));

        if (SelectedProject is null)
        {
            ApplySelectedProject();
        }
        else
        {
            LastDeploySummary = FormatLastDeploy(SelectedProject.Config.Project.Id);
            DirtySummary = Changes.Count == 0 ? Texts["Clean"] : Texts.Format("ChangesCount", Changes.Count);
            UndeployedSummary = _undeployedCount == 0
                ? Texts["EverythingDeployed"]
                : Texts.Format("PendingCount", _undeployedCount);
            _ = RefreshAsync();
        }

        if (!_isBusy)
        {
            SetStatus(Texts["Ready"], "MutedBrush");
        }

        if (_logBuilder.Length == 0)
        {
            LogText = Texts["NoDeploymentStarted"];
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
