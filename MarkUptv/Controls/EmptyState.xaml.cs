using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace MarkUptv.Controls;

/// <summary>
/// A single, themed empty/idle state: icon, title, one line of guidance and an
/// optional action. Screens used to hand-roll this, so the same situation read
/// as a status on one page ("NO HISTORY FOUND") and as an error on another
/// ("No local channels found - pull to refresh!").
///
/// Usage:
/// <code>
/// &lt;controls:EmptyState
///     Icon="{StaticResource IconHistory}"
///     Title="No history yet"
///     Hint="Channels you watch will appear here."
///     Accent="{StaticResource AccentPink}" /&gt;
/// </code>
/// </summary>
public partial class EmptyState : ContentView
{
    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(Geometry), typeof(EmptyState));

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(EmptyState), string.Empty,
            propertyChanged: OnTextChanged);

    public static readonly BindableProperty HintProperty =
        BindableProperty.Create(nameof(Hint), typeof(string), typeof(EmptyState), string.Empty,
            propertyChanged: OnTextChanged);

    public static readonly BindableProperty AccentProperty =
        BindableProperty.Create(nameof(Accent), typeof(Color), typeof(EmptyState),
            Color.FromArgb("#FF4D8D"),
            propertyChanged: OnAccentChanged);

    public static readonly BindableProperty ActionTextProperty =
        BindableProperty.Create(nameof(ActionText), typeof(string), typeof(EmptyState), string.Empty,
            propertyChanged: OnTextChanged);

    public static readonly BindableProperty ActionCommandProperty =
        BindableProperty.Create(nameof(ActionCommand), typeof(ICommand), typeof(EmptyState));

    // Derived, so a screen never has to pass the same colour twice.
    private static readonly BindablePropertyKey AccentTintPropertyKey =
        BindableProperty.CreateReadOnly(nameof(AccentTint), typeof(Color), typeof(EmptyState),
            Color.FromArgb("#22FF4D8D"));

    public static readonly BindableProperty AccentTintProperty = AccentTintPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey HasHintPropertyKey =
        BindableProperty.CreateReadOnly(nameof(HasHint), typeof(bool), typeof(EmptyState), false);

    public static readonly BindableProperty HasHintProperty = HasHintPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey HasActionPropertyKey =
        BindableProperty.CreateReadOnly(nameof(HasAction), typeof(bool), typeof(EmptyState), false);

    public static readonly BindableProperty HasActionProperty = HasActionPropertyKey.BindableProperty;

    public EmptyState()
    {
        InitializeComponent();
    }

    /// <summary>Vector icon drawn in the accent disc. Use a key from Icons.xaml.</summary>
    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Short headline, e.g. "No history yet".</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>One line of guidance telling the user what to do next.</summary>
    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <summary>Accent colour for the disc, icon and action button.</summary>
    public Color Accent
    {
        get => (Color)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <summary>Optional action button label. The button hides when this is blank.</summary>
    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    /// <summary>Accent at low opacity, for the disc behind the icon.</summary>
    public Color AccentTint => (Color)GetValue(AccentTintProperty);

    public bool HasHint => (bool)GetValue(HasHintProperty);

    public bool HasAction => (bool)GetValue(HasActionProperty);

    private static void OnTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (EmptyState)bindable;
        control.SetValue(HasHintPropertyKey, !string.IsNullOrWhiteSpace(control.Hint));
        control.SetValue(HasActionPropertyKey, !string.IsNullOrWhiteSpace(control.ActionText));
    }

    private static void OnAccentChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (EmptyState)bindable;
        var accent = control.Accent;
        control.SetValue(AccentTintPropertyKey, accent.WithAlpha(0.14f));
    }
}
