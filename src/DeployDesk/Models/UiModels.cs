using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeployDesk.Models;

public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ProjectListItem(DeployLink config, string initialStatus = "Loading …") : ObservableModel
{
    private string _status = initialStatus;

    public DeployLink Config { get; } = config;
    public string Name => Config.Project.Name;
    public string Description => Config.Project.Description;

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }
}

public sealed class OptionItem(DeployOptionDefinition definition) : ObservableModel
{
    private bool _isSelected = definition.Default;

    public DeployOptionDefinition Definition { get; } = definition;
    public string Label => Definition.Label;
    public string Description => Definition.Description;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

public sealed record CommitItem(string Hash, string Subject, string RelativeTime)
{
    public string Display => $"{Hash}  {Subject}";
}
