using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MarkUptv.Models;

/// <summary>
/// Represents a browsable category tile on the main dashboard.
/// </summary>
public class Category
{
    /// <summary>Display name shown below the icon, e.g. "Sports".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Emoji icon rendered large inside the tile.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Diagonal LinearGradientBrush applied as the tile background.
    /// Generated per-category by MainPageViewModel.CreateCategory().
    /// </summary>
    public LinearGradientBrush? Gradient { get; set; }

    /// <summary>
    /// Dominant hue of this category used for the per-tile drop shadow glow.
    /// Bound to Border.Shadow.Brush in XAML for a coloured ambient glow.
    /// </summary>
    public Color ShadowColor { get; set; } = Colors.Black;

    /// <summary>
    /// Navigation command pre-bound in the ViewModel so the DataTemplate
    /// does not need ancestor binding for the command itself.
    /// CommandParameter is the category Name string.
    /// </summary>
    public IRelayCommand<string>? Command { get; set; }
    public string CommandParameter { get; set; } = string.Empty;

  
}
