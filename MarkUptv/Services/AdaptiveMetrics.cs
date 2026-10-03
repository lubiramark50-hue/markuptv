using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;

namespace MarkUptv.Services;

/// <summary>
/// Viewport width class, measured in device-independent pixels.
/// </summary>
public enum ScreenBreakpoint
{
    /// <summary>Phones in portrait - the tightest layout the app must survive.</summary>
    Compact,

    /// <summary>Large phones and small tablets.</summary>
    Medium,

    /// <summary>Tablets and resized desktop windows.</summary>
    Expanded,

    /// <summary>Android TV, large monitors, full-screen desktop.</summary>
    Wide
}

/// <summary>
/// The single source of truth for how big the app thinks the screen is, and
/// what that means for spacing, type and content width.
///
/// Before this existed every page hard-coded pixel values, so a layout tuned on
/// a 1080x2400 phone simply clipped on a TV and looked lost on a tablet.
/// Pages ask this type (or the tokens in Resources/Styles/Sizes.xaml) instead
/// of guessing.
///
/// Measurement prefers the live window size so desktop users resizing the
/// window get a real re-layout, and falls back to the physical display on
/// phones and TV, where the window is always full-screen.
/// </summary>
public static class AdaptiveMetrics
{
    /// <summary>Raised when the breakpoint changes (rotation, resize, TV dock).</summary>
    public static event EventHandler? Changed;

    private static ScreenBreakpoint _current = ScreenBreakpoint.Medium;

    /// <summary>Current width in device-independent pixels.</summary>
    public static double Width { get; private set; } = 400;

    /// <summary>Current height in device-independent pixels.</summary>
    public static double Height { get; private set; } = 800;

    public static ScreenBreakpoint Current => _current;

    public static bool IsCompact => _current == ScreenBreakpoint.Compact;

    /// <summary>True on tablets, desktop and TV - anywhere with room to breathe.</summary>
    public static bool IsWide => _current is ScreenBreakpoint.Expanded or ScreenBreakpoint.Wide;

    /// <summary>
    /// Re-measures the viewport and raises <see cref="Changed"/> if the
    /// breakpoint moved. Safe to call often - it is cheap and never throws.
    /// </summary>
    public static void Refresh()
    {
        double width = 0;
        double height = 0;

        try
        {
            // Live window size first: on desktop and resizable windows this is
            // the only value that reflects what the user actually sees.
            var window = Application.Current?.Windows.Count > 0
                ? Application.Current.Windows[0]
                : null;

            if (window is not null && window.Width > 0 && window.Height > 0)
            {
                width = window.Width;
                height = window.Height;
            }
        }
        catch
        {
            // Fall through to the display measurement.
        }

        if (width <= 0 || height <= 0)
        {
            try
            {
                var display = DeviceDisplay.Current.MainDisplayInfo;
                var density = display.Density <= 0 ? 1d : display.Density;

                width = display.Width / density;
                height = display.Height / density;
            }
            catch
            {
                // Keep the last known values.
            }
        }

        if (width > 0)
        {
            Width = width;
        }

        if (height > 0)
        {
            Height = height;
        }

        var next = Resolve(Width);

        if (next == _current)
        {
            return;
        }

        _current = next;

        try
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
        catch
        {
            // A listener must never take the app down on rotation.
        }
    }

    private static ScreenBreakpoint Resolve(double width) => width switch
    {
        < 420 => ScreenBreakpoint.Compact,
        < 600 => ScreenBreakpoint.Medium,
        < 900 => ScreenBreakpoint.Expanded,
        _ => ScreenBreakpoint.Wide
    };

    // ─── Layout rhythm ──────────────────────────────────────────────────────

    /// <summary>Horizontal page gutter.</summary>
    public static double PagePadding => _current switch
    {
        ScreenBreakpoint.Compact => 16,
        ScreenBreakpoint.Medium => 20,
        ScreenBreakpoint.Expanded => 28,
        _ => 36
    };

    /// <summary>Gap between major sections.</summary>
    public static double SectionSpacing => _current switch
    {
        ScreenBreakpoint.Compact => 18,
        ScreenBreakpoint.Medium => 22,
        ScreenBreakpoint.Expanded => 26,
        _ => 32
    };

    /// <summary>Gap between cards in a rail or grid.</summary>
    public static double Gutter => _current switch
    {
        ScreenBreakpoint.Compact => 12,
        ScreenBreakpoint.Medium => 14,
        ScreenBreakpoint.Expanded => 18,
        _ => 22
    };

    /// <summary>
    /// Widest a column of text or a single-column form should get. Beyond this
    /// the content is centred instead of stretched, which is what makes the app
    /// readable on a TV rather than a wall of edge-to-edge text.
    /// </summary>
    public static double ContentMaxWidth => _current switch
    {
        ScreenBreakpoint.Compact => double.PositiveInfinity,
        ScreenBreakpoint.Medium => 720,
        ScreenBreakpoint.Expanded => 980,
        _ => 1200
    };

    /// <summary>How many cards fit comfortably across the viewport.</summary>
    public static int GridColumns => _current switch
    {
        ScreenBreakpoint.Compact => 2,
        ScreenBreakpoint.Medium => 3,
        ScreenBreakpoint.Expanded => 4,
        _ => 6
    };

    // ─── Type ramp ──────────────────────────────────────────────────────────

    /// <summary>Multiplier applied to a page's base font sizes.</summary>
    public static double TypeScale => _current switch
    {
        ScreenBreakpoint.Compact => 0.94,
        ScreenBreakpoint.Medium => 1.0,
        ScreenBreakpoint.Expanded => 1.06,
        _ => 1.15
    };

    /// <summary>Scales a design-time font size to the current breakpoint.</summary>
    public static double Font(double baseSize) => Math.Round(baseSize * TypeScale, 2);

    /// <summary>Scales a design-time length to the current breakpoint.</summary>
    public static double Size(double baseSize) => Math.Round(baseSize * TypeScale, 2);
}
