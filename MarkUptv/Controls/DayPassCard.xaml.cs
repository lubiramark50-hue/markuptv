using System.Windows.Input;
using Microsoft.Maui.Controls;

namespace MarkUptv.Controls;

/// <summary>
/// The day-pass offer / confirmation card. All money figures are pushed in by
/// the page from the server's status payload, so the card itself never invents
/// a price, and it renders the same whether it is shown inside the film
/// section or the 18+ shelf.
/// </summary>
public partial class DayPassCard : ContentView
{
    public static readonly BindableProperty IsActiveProperty =
        BindableProperty.Create(nameof(IsActive), typeof(bool), typeof(DayPassCard), false);

    public static readonly BindableProperty IsBusyProperty =
        BindableProperty.Create(nameof(IsBusy), typeof(bool), typeof(DayPassCard), false);

    public static readonly BindableProperty PriceTextProperty =
        BindableProperty.Create(nameof(PriceText), typeof(string), typeof(DayPassCard), "1,000 UGX");

    public static readonly BindableProperty DurationTextProperty =
        BindableProperty.Create(nameof(DurationText), typeof(string), typeof(DayPassCard), "24 hours");

    public static readonly BindableProperty ExpiryTextProperty =
        BindableProperty.Create(nameof(ExpiryText), typeof(string), typeof(DayPassCard), string.Empty);

    public static readonly BindableProperty EmailProperty =
        BindableProperty.Create(
            nameof(Email),
            typeof(string),
            typeof(DayPassCard),
            string.Empty,
            BindingMode.TwoWay);

    public static readonly BindableProperty MessageProperty =
        BindableProperty.Create(
            nameof(Message),
            typeof(string),
            typeof(DayPassCard),
            string.Empty,
            propertyChanged: OnMessageChanged);

    public static readonly BindableProperty PayCommandProperty =
        BindableProperty.Create(nameof(PayCommand), typeof(ICommand), typeof(DayPassCard));

    public DayPassCard()
    {
        InitializeComponent();
    }

    /// <summary>True while the viewer's pass is running — switches to the confirmation strip.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>True while a checkout is being created.</summary>
    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    /// <summary>Server-priced headline, e.g. "1,000 UGX".</summary>
    public string PriceText
    {
        get => (string)GetValue(PriceTextProperty);
        set => SetValue(PriceTextProperty, value);
    }

    /// <summary>Server-declared pass length, e.g. "24 hours".</summary>
    public string DurationText
    {
        get => (string)GetValue(DurationTextProperty);
        set => SetValue(DurationTextProperty, value);
    }

    /// <summary>Countdown shown once the pass is active.</summary>
    public string ExpiryText
    {
        get => (string)GetValue(ExpiryTextProperty);
        set => SetValue(ExpiryTextProperty, value);
    }

    /// <summary>Buyer email, required by the payment gateway.</summary>
    public string Email
    {
        get => (string)GetValue(EmailProperty);
        set => SetValue(EmailProperty, value);
    }

    /// <summary>Inline feedback, e.g. a validation or gateway message.</summary>
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    /// <summary>
    /// Inverse of <see cref="IsActive"/>, computed here rather than through a
    /// converter: this control builds before it is attached, and an early
    /// resource lookup during construction can blank the page that hosts it.
    /// </summary>
    public bool IsLocked => !IsActive;

    /// <summary>Inverse of <see cref="IsBusy"/> for the pay button's enabled state.</summary>
    public bool IsNotBusy => !IsBusy;

    public ICommand? PayCommand
    {
        get => (ICommand?)GetValue(PayCommandProperty);
        set => SetValue(PayCommandProperty, value);
    }

    /// <summary>Button caption that names the amount the viewer is about to pay.</summary>
    public string PayButtonText =>
        !string.IsNullOrWhiteSpace(PriceText) && PriceText != "1,000 UGX"
            ? $"Continue — {PriceText}"
            : "Continue to payment";

    private static void OnMessageChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is DayPassCard card)
        {
            card.OnPropertyChanged(nameof(HasMessage));
        }
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        // The caption embeds the price, so it has to follow it.
        if (propertyName == PriceTextProperty.PropertyName)
        {
            OnPropertyChanged(nameof(PayButtonText));
        }

        // Derived state the template binds to directly.
        if (propertyName == IsActiveProperty.PropertyName)
        {
            OnPropertyChanged(nameof(IsLocked));
        }
        else if (propertyName == IsBusyProperty.PropertyName)
        {
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }
}
