namespace MarkUptv.Models;

public sealed class DashboardFeature
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string Accent { get; init; } = "#FF3D6E";
    public string Route { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Kind { get; init; } = "route";

    public bool IsCategory => Kind == "category";
}
