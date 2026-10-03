using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace MarkUptv.Converters;

/// <summary>
/// Turns a dashboard feature's icon token into the flat vector icon set the
/// quick-explore chips already use, so the feature cards stop rendering
/// full-colour emoji next to them.
///
/// Geometries are written in a 24x24 viewport and prefixed with "F0" (even-odd
/// fill): the sub-paths then read as cut-outs — a play hole in the TV, the
/// sprocket holes in the film strip, the pentagon on the football, the ring in
/// the magnifier.
/// </summary>
public sealed class FeatureIconConverter : IValueConverter
{
    private static readonly PathGeometryConverter GeometryFactory = new();
    private static readonly Dictionary<string, Geometry> Parsed = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> Tokens = new(StringComparer.Ordinal)
    {
        // Live TV
        ["◆"] = "F0 M4,6.2 H20 V17.8 H4 Z M9.8,9.4 L15.4,12 L9.8,14.6 Z",

        // Sports
        ["🏆"] = "F0 M7,3.6 H17 V9 A5,5 0 0,1 7,9 Z M10.2,14.6 H13.8 V16.6 H16.6 V20 H7.4 V16.6 H10.2 Z",

        // News
        ["📰"] = "F0 M5.4,3 H13.6 L18.6,8 V21 H5.4 Z M8.4,11.6 H15.6 V13.2 H8.4 Z M8.4,15 H15.6 V16.6 H8.4 Z",

        // Movies
        ["🎬"] = "F0 M3.4,5 H20.6 V19 H3.4 Z M5.6,6.9 H7.6 V8.9 H5.6 Z M5.6,10.6 H7.6 V12.6 H5.6 Z " +
                 "M5.6,14.3 H7.6 V16.3 H5.6 Z M16.4,6.9 H18.4 V8.9 H16.4 Z M16.4,10.6 H18.4 V12.6 H16.4 Z " +
                 "M16.4,14.3 H18.4 V16.3 H16.4 Z M9.4,7.9 H14.6 V16 H9.4 Z",

        // Music
        ["🎵"] = "M19,2.6 V15.4 A2.9,2.9 0 1,1 16.9,12.7 V7.3 L10,8.9 V17.4 A2.9,2.9 0 1,1 7.9,14.7 V5 Z",

        // Football
        ["⚽"] = "F0 M12,2.4 A9.6,9.6 0 1,1 11.99,2.4 Z M12,8.4 L15.4,10.9 L14.1,14.9 H9.9 L8.6,10.9 Z",

        // Cartoons
        ["🎈"] = "F0 M12,2.6 A5.4,5.4 0 1,1 11.99,2.6 Z M11.2,13.1 H12.8 L12,14.7 Z M11.6,14.9 H12.4 V20 H11.6 Z",

        // Discovery
        ["🧭"] = "F0 M12,2.4 A9.6,9.6 0 1,1 11.99,2.4 Z M12,5.6 A6.4,6.4 0 1,0 12.01,5.6 Z " +
                 "M12,7.6 L14.6,16.4 L12,15.2 L9.4,16.4 Z",

        // Community
        ["💬"] = "F0 M4,4.4 H20 V15.4 H11.4 L6.4,19.6 V15.4 H4 Z",

        // Recently watched
        ["🕘"] = "F0 M12,2.4 A9.6,9.6 0 1,1 11.99,2.4 Z M12,5.6 A6.4,6.4 0 1,0 12.01,5.6 Z " +
                 "M11.5,7 H12.5 V12.4 H11.5 Z M12.2,11.9 H16.4 V12.9 H12.2 Z",

        // Search
        ["🔍"] = "F0 M10.5,3.2 A7.3,7.3 0 1,1 10.49,3.2 Z M10.5,5.6 A4.9,4.9 0 1,0 10.51,5.6 Z " +
                 "M15.6,14.2 L20.4,19 L19,20.4 L14.2,15.6 Z",

        // Support
        ["💎"] = "F0 M12,2.6 L20.4,9.6 L12,21.4 L3.6,9.6 Z M12,5.8 L6.9,9.6 L12,17.2 L17.1,9.6 Z",

        // ── Shell flyout items ────────────────────────────────────────────
        // The flyout binds its item titles through this converter, so every
        // menu entry carries the same flat vector mark as the dashboard.

        ["Dashboard"] = "F0 M4,6.2 H20 V17.8 H4 Z M9.8,9.4 L15.4,12 L9.8,14.6 Z",
        ["Home"] = "F0 M12,3 L21,11 H18.6 V20.4 H5.4 V11 H3 Z",
        ["Local TV"] = "F0 M4,6.2 H20 V17.8 H4 Z M9.8,9.4 L15.4,12 L9.8,14.6 Z",
        ["World Channels"] = "F0 M12,2.2 A9.8,9.8 0 1,1 11.99,2.2 Z " +
                             "M12,2.2 C15.7,5.8 15.7,18.2 12,21.8 C8.3,18.2 8.3,5.8 12,2.2 Z " +
                             "M2.6,9 H21.4 M2.6,15 H21.4",
        ["Football"] = "F0 M12,2.4 A9.6,9.6 0 1,1 11.99,2.4 Z M12,8.4 L15.4,10.9 L14.1,14.9 H9.9 L8.6,10.9 Z",
        ["European Football"] = "F0 M12,2.4 A9.6,9.6 0 1,1 11.99,2.4 Z " +
                               "M12,8.4 L15.4,10.9 L14.1,14.9 H9.9 L8.6,10.9 Z",
        ["Sports"] = "F0 M7,3.6 H17 V9 A5,5 0 0,1 7,9 Z M10.2,14.6 H13.8 V16.6 H16.6 V20 H7.4 V16.6 H10.2 Z",
        ["News"] = "F0 M5.4,3 H13.6 L18.6,8 V21 H5.4 Z M8.4,11.6 H15.6 V13.2 H8.4 Z M8.4,15 H15.6 V16.6 H8.4 Z",
        ["Movies"] = "F0 M3.4,5 H20.6 V19 H3.4 Z M5.6,6.9 H7.6 V8.9 H5.6 Z M5.6,10.6 H7.6 V12.6 H5.6 Z " +
                     "M5.6,14.3 H7.6 V16.3 H5.6 Z M16.4,6.9 H18.4 V8.9 H16.4 Z M16.4,10.6 H18.4 V12.6 H16.4 Z " +
                     "M16.4,14.3 H18.4 V16.3 H16.4 Z M9.4,7.9 H14.6 V16 H9.4 Z",
        ["Movies & Films"] = "F0 M3.4,5 H20.6 V19 H3.4 Z M5.6,6.9 H7.6 V8.9 H5.6 Z M5.6,10.6 H7.6 V12.6 H5.6 Z " +
                            "M5.6,14.3 H7.6 V16.3 H5.6 Z M16.4,6.9 H18.4 V8.9 H16.4 Z M16.4,10.6 H18.4 V12.6 H16.4 Z " +
                            "M16.4,14.3 H18.4 V16.3 H16.4 Z M9.4,7.9 H14.6 V16 H9.4 Z",
        ["Music"] = "M19,2.6 V15.4 A2.9,2.9 0 1,1 16.9,12.7 V7.3 L10,8.9 V17.4 A2.9,2.9 0 1,1 7.9,14.7 V5 Z",
        ["Cartoons"] = "F0 M12,2.6 A5.4,5.4 0 1,1 11.99,2.6 Z M11.2,13.1 H12.8 L12,14.7 Z M11.6,14.9 H12.4 V20 H11.6 Z",
        ["Discovery"] = "F0 M12,2.4 A9.6,9.6 0 1,1 11.99,2.4 Z M12,5.6 A6.4,6.4 0 1,0 12.01,5.6 Z " +
                       "M12,7.6 L14.6,16.4 L12,15.2 L9.4,16.4 Z",
        ["Community"] = "F0 M4,4.4 H20 V15.4 H11.4 L6.4,19.6 V15.4 H4 Z",
        ["Community Hub"] = "F0 M4,4.4 H20 V15.4 H11.4 L6.4,19.6 V15.4 H4 Z",
        ["Recently Watched"] = "F0 M12,2.4 A9.6,9.6 0 1,1 11.99,2.4 Z M12,5.6 A6.4,6.4 0 1,0 12.01,5.6 Z " +
                              "M11.5,7 H12.5 V12.4 H11.5 Z M12.2,11.9 H16.4 V12.9 H12.2 Z",
        ["Support"] = "F0 M12,2.6 L20.4,9.6 L12,21.4 L3.6,9.6 Z M12,5.8 L6.9,9.6 L12,17.2 L17.1,9.6 Z",
        ["Adults 18+"] = "F0 M12,2.6 L20,5.8 V11.6 C20,16.8 16.6,20.2 12,21.8 C7.4,20.2 4,16.8 4,11.6 V5.8 Z",
        ["Religious"] = "M12,2.6 L14.8,9.2 L21.8,9.9 L16.6,14.4 L18.1,21.4 L12,17.6 L5.9,21.4 L7.4,14.4 " +
                       "L2.2,9.9 L9.2,9.2 Z",
        ["Gospel"] = "M12,2.6 L14.8,9.2 L21.8,9.9 L16.6,14.4 L18.1,21.4 L12,17.6 L5.9,21.4 L7.4,14.4 " +
                     "L2.2,9.9 L9.2,9.2 Z",
        ["Fashion"] = "M12,2 L14.3,9.7 L22,12 L14.3,14.3 L12,22 L9.7,14.3 L2,12 L9.7,9.7 Z",
        ["Lifestyle"] = "M12,3 C18,6.4 19,13.4 12,21 C5,13.4 6,6.4 12,3 Z",
        ["Wildlife"] = "F0 M12,13.8 A3.4,3.4 0 1,1 11.99,13.8 Z M6.6,8.4 A2.2,2.2 0 1,1 6.59,8.4 Z " +
                       "M10.4,6.2 A2.2,2.2 0 1,1 10.39,6.2 Z M14.6,6.2 A2.2,2.2 0 1,1 14.59,6.2 Z " +
                       "M18,8.6 A2.2,2.2 0 1,1 17.99,8.6 Z",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string token = value as string ?? string.Empty;

        if (!Tokens.TryGetValue(token, out string? data))
        {
            // An unknown token still gets a mark rather than an empty box.
            data = Tokens["◆"];
        }

        lock (Parsed)
        {
            if (!Parsed.TryGetValue(data, out Geometry? geometry))
            {
                geometry = (Geometry)GeometryFactory.ConvertFromInvariantString(data)!;
                Parsed[data] = geometry;
            }

            return geometry;
        }
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotSupportedException("Feature icons are one-way.");
}
