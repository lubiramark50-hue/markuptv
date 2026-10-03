using System;
using System.Runtime.CompilerServices;
using MarkUptv.Services;
using Microsoft.Maui.Controls;

namespace MarkUptv.Helpers;

/// <summary>
/// Grows a page's typography on larger viewports.
///
/// The app has ~55 pages and thousands of explicit FontSize values written for
/// a phone. Converting every one of them to a token by hand is a long campaign;
/// this layer makes all of them adapt immediately, and the token migration can
/// then happen page by page without any page regressing in the meantime.
///
/// Rules:
///   - only ever scales *up*, never down, so a small phone keeps the type size
///     its designer chose;
///   - each element's original size is remembered, so repeated re-layouts
///     (rotation, window resize, TV mode) cannot compound the scaling;
///   - elements that never set a size (-1) are left to the style system.
/// </summary>
internal static class AdaptiveTypeScaler
{
    private static readonly ConditionalWeakTable<Element, StrongBox<double>> BaseSizes = new();

    public static void Apply(Page? page)
    {
        if (page is null)
        {
            return;
        }

        double factor = Math.Max(1.0, AdaptiveMetrics.TypeScale);

        try
        {
            foreach (var element in Descendants(page))
            {
                switch (element)
                {
                    case Label label:
                        label.FontSize = Scaled(label, label.FontSize, factor);
                        break;

                    case Button button:
                        button.FontSize = Scaled(button, button.FontSize, factor);
                        break;

                    case Entry entry:
                        entry.FontSize = Scaled(entry, entry.FontSize, factor);
                        break;

                    case Editor editor:
                        editor.FontSize = Scaled(editor, editor.FontSize, factor);
                        break;

                    case Picker picker:
                        picker.FontSize = Scaled(picker, picker.FontSize, factor);
                        break;

                    case DatePicker datePicker:
                        datePicker.FontSize = Scaled(datePicker, datePicker.FontSize, factor);
                        break;

                    case SearchBar searchBar:
                        searchBar.FontSize = Scaled(searchBar, searchBar.FontSize, factor);
                        break;
                }
            }
        }
        catch
        {
            // Typography is cosmetic: never let it break a page.
        }
    }

    private static double Scaled(Element element, double current, double factor)
    {
        // -1 means "not set", so the implicit style decides and we stay out of it.
        if (current <= 0)
        {
            return current;
        }

        if (!BaseSizes.TryGetValue(element, out var baseline))
        {
            baseline = new StrongBox<double>(current);
            BaseSizes.Add(element, baseline);
        }

        double target = Math.Round(baseline.Value * factor, 2);

        return Math.Abs(target - current) < 0.01 ? current : target;
    }

    private static System.Collections.Generic.IEnumerable<Element> Descendants(Element root)
    {
        if (root is IVisualTreeElement treeElement)
        {
            foreach (var descendant in treeElement.GetVisualTreeDescendants())
            {
                if (descendant is Element element)
                {
                    yield return element;
                }
            }
        }
    }
}
