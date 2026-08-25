namespace TowerFoundation.Optimization;

public sealed class FoundationOptimizationException : InvalidOperationException
{
    public FoundationOptimizationException(
        string dialogTitle,
        string message,
        string statusSummary)
        : base(message)
    {
        DialogTitle = dialogTitle;
        StatusSummary = statusSummary;
    }

    public string DialogTitle { get; }

    public string StatusSummary { get; }
}
