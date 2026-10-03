using CommunityToolkit.Maui.Core.Handlers;
using MarkUptv.Helpers;

namespace MarkUptv.Handlers;

/// <summary>
/// Injects Referer / User-Agent / extra headers into the native media players
/// so anti-hotlink CDNs serve the manifest AND every HLS segment.
///
/// Wired in MauiProgram with:
///   MediaElementHandler.PropertyMapper.AppendToMapping(
///       nameof(MediaElement.Source),
///       (handler, _) => StreamPlayerHeaderHandler.Apply(handler));
///
/// Ordering contract: the page calls StreamHeaderProvider.Set(...) BEFORE
/// assigning MediaElement.Source, so the headers are in place when this runs.
/// The mapping runs after the toolkit's own MapSource, so the platform view
/// already exists when the headers are applied.
/// </summary>
public static partial class StreamPlayerHeaderHandler
{
    public static void Apply(MediaElementHandler handler)
    {
        try
        {
            if (handler.PlatformView is null ||
                handler.VirtualView.Source is null)
            {
                return;
            }

            ApplyPlatform(handler);
        }
        catch (Exception ex)
        {
            // Header injection must never take playback down with it.
            System.Diagnostics.Debug.WriteLine(
                $"[StreamPlayerHeaderHandler] {ex}");
        }
    }

    static partial void ApplyPlatform(MediaElementHandler handler);

    /// <summary>
    /// Finds the first object in the toolkit's internal platform view that is
    /// an instance of the given native player type (the toolkit does not
    /// expose the player through documented APIs, so we discover it once by
    /// walking the property/field graph). Returns null when not found.
    /// </summary>
    private static object? FindPlayer(object platformView, Func<Type, bool> isMatch)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

        object? Visit(object current, int depth)
        {
            if (depth > 6 || !seen.Add(current))
            {
                return null;
            }

            var type = current.GetType();

            if (isMatch(type))
            {
                return current;
            }

            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            foreach (var property in type.GetProperties(flags))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                object? value;
                try
                {
                    value = property.GetValue(current);
                }
                catch
                {
                    continue;
                }

                if (value is null || value is string || value.GetType().IsPrimitive)
                {
                    continue;
                }

                if (isMatch(value.GetType()))
                {
                    return value;
                }
            }

            foreach (var field in type.GetFields(flags))
            {
                object? value;
                try
                {
                    value = field.GetValue(current);
                }
                catch
                {
                    continue;
                }

                if (value is null || value is string || value.GetType().IsPrimitive)
                {
                    continue;
                }

                if (isMatch(value.GetType()))
                {
                    return value;
                }

                var nested = Visit(value, depth + 1);
                if (nested is not null)
                {
                    return nested;
                }
            }

            return null;
        }

        // Also probe direct children (e.g. Windows MediaPlayerElement) since a
        // single-level property scan may miss controls deeper in the tree.
        foreach (var property in platformView.GetType().GetProperties(
                     System.Reflection.BindingFlags.Instance |
                     System.Reflection.BindingFlags.Public |
                     System.Reflection.BindingFlags.NonPublic))
        {
            if (!property.CanRead)
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(platformView);
            }
            catch
            {
                continue;
            }

            if (value is not null && isMatch(value.GetType()))
            {
                return value;
            }
        }

        return Visit(platformView, 0);
    }
}
