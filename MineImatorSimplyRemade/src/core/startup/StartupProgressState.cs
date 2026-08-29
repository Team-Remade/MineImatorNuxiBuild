using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MineImatorSimplyRemade.core.startup;

public sealed class StartupProgressState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private string _title = "Preparing Mine Imator Simply Remade";
    public string Title { get => _title; set { _title = value; Raise(); } }

    private string _phase = "Starting up";
    public string Phase { get => _phase; set { _phase = value; Raise(); } }

    private string _status = "Preparing startup...";
    public string Status { get => _status; set { _status = value; Raise(); } }

    private string _detail = "";
    public string Detail { get => _detail; set { _detail = value; Raise(); Raise(nameof(HasDetail)); } }

    private float _progress;
    public float Progress { get => _progress; set { _progress = value; Raise(); } }

    private int _currentStep;
    public int CurrentStep { get => _currentStep; set { _currentStep = value; Raise(); Raise(nameof(StepLabel)); } }

    private int _totalSteps;
    public int TotalSteps { get => _totalSteps; set { _totalSteps = value; Raise(); Raise(nameof(StepLabel)); } }

    public string StepLabel => TotalSteps > 0
        ? $"Step {Math.Clamp(CurrentStep, 1, TotalSteps)}/{TotalSteps}"
        : "Startup";

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    /// <summary>Copies every field from <paramref name="other"/>, raising one
    /// change notification per property (mirrors the old struct-copy update
    /// pattern from main.cs's UpdateStartupWindow).</summary>
    public void CopyFrom(StartupProgressState other)
    {
        Title = other.Title;
        CurrentStep = other.CurrentStep;
        TotalSteps = other.TotalSteps;
        Phase = other.Phase;
        Status = other.Status;
        Detail = other.Detail;
        Progress = other.Progress;
    }
}
