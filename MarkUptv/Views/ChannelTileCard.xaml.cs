using System;
using Microsoft.Maui.Controls;

namespace MarkUptv.Views;

/// <summary>
/// The app's one dense channel tile (see the XAML header for the contract).
/// Host pages wire <see cref="WatchClicked"/>/<see cref="LogoClicked"/>/<see cref="CardTapped"/>
/// to their existing handlers; the events are raised with the tapped element as
/// sender so those handlers keep reading the channel off BindingContext.
/// </summary>
public partial class ChannelTileCard : ContentView
{
    private EventHandler<TappedEventArgs>? _cardTapped;
    private TapGestureRecognizer? _cardTapGesture;

    /// <summary>Tune-in request from the play chip.</summary>
    public event EventHandler? WatchClicked;

    /// <summary>Logo tapped (used by pages for their press animation).</summary>
    public event EventHandler? LogoClicked;

    /// <summary>
    /// Whole-tile tune-in, for pages that are not selection-driven. This is
    /// opt-in on purpose: subscribing installs a tap gesture on the tile, which
    /// then swallows the tap — so pages that play through the CollectionView's
    /// SelectionChanged must leave it unwired.
    /// </summary>
    public event EventHandler<TappedEventArgs>? CardTapped
    {
        add
        {
            _cardTapped += value;
            EnsureCardTapGesture();
        }
        remove => _cardTapped -= value;
    }

    public ChannelTileCard()
    {
        InitializeComponent();
    }

    private void EnsureCardTapGesture()
    {
        if (_cardTapGesture is not null)
        {
            return;
        }

        _cardTapGesture = new TapGestureRecognizer();
        _cardTapGesture.Tapped += (_, e) => _cardTapped?.Invoke(this, e);
        TileRoot.GestureRecognizers.Add(_cardTapGesture);
    }

    private void OnWatchTapped(object? sender, TappedEventArgs e)
    {
        if (WatchClicked is not null)
        {
            WatchClicked.Invoke(sender, EventArgs.Empty);
            return;
        }

        // Pages whose tune-in is a whole-row tap get that same behaviour from
        // the chip, so the chip is never a dead control.
        _cardTapped?.Invoke(this, e);
    }

    private void OnLogoTapped(object? sender, TappedEventArgs e)
    {
        LogoClicked?.Invoke(sender, EventArgs.Empty);

        // The artwork is part of the tile, so a whole-row-tap page tunes in from
        // it too — otherwise the upper half of every card would do nothing.
        if (_cardTapped is not null)
        {
            _cardTapped.Invoke(this, e);
        }
    }
}
