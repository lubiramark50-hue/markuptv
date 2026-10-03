using System.Windows.Input;
using Microsoft.Maui.Graphics;

namespace MarkUptv.Models
{
    /// <summary>
    /// Represents a high-performance configuration item for rendering visual category cards.
    /// Fully optimized for layout distribution and Native AOT initialization standards.
    /// </summary>
    public class CategoryCardItem
    {
        /// <summary>
        /// The display emoji icon representing the media category section.
        /// </summary>
        public string Emoji { get; set; } = string.Empty;

        /// <summary>
        /// The readable label or title text displayed on the card interface.
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// Starting point background color definition for linear color gradients.
        /// </summary>
        public Color BackgroundStartColor { get; set; } = Colors.Transparent;

        /// <summary>
        /// Terminal boundary color configuration for unified rendering frames.
        /// </summary>
        public Color BackgroundEndColor { get; set; } = Colors.Transparent;

        /// <summary>
        /// Boundary accent stroke visualization entry vector start.
        /// </summary>
        public Color StrokeStartColor { get; set; } = Colors.Transparent;

        /// <summary>
        /// Accent framework frame termination coloration parameter.
        /// </summary>
        public Color StrokeEndColor { get; set; } = Colors.Transparent;

        /// <summary>
        /// The programmatic interactive gesture execution entry trigger hook.
        /// </summary>
        public ICommand? TapCommand { get; set; }
    }
}