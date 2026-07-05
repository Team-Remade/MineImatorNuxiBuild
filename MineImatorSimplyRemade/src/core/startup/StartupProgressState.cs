namespace MineImatorSimplyRemade.core.startup;

public sealed class StartupProgressState
{
    public string Title { get; set; } = "Preparing Mine Imator Simply Remade";
    public string Phase { get; set; } = "Starting up";
    public string Status { get; set; } = "Preparing startup...";
    public string Detail { get; set; } = "";
    public float Progress { get; set; }
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
}