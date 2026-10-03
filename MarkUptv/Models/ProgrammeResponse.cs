namespace MarkUptv.Models;

/// <summary>
/// Supports backend endpoints that wrap EPG entries inside
/// a programmes property.
/// </summary>
public sealed class ProgrammeResponse
{
    public List<TvProgramme> Programmes { get; set; } = [];
}