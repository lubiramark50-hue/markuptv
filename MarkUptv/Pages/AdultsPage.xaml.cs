using System;
using System.Threading.Tasks;
using MarkUptv.Helpers;
using Microsoft.Maui.Controls;

namespace MarkUptv.Pages;

/// <summary>
/// Age-gated entry to the Adults (18+) channel section. Confirms the user is
/// of legal age, unlocks the session flag consumed by the playback guard,
/// then hands off to the consolidated category channel page pre-configured
/// for the "adult" category.
/// </summary>
public partial class AdultsPage : ContentPage
{
    public AdultsPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Returning visitors who already accepted on this device skip the
        // prompt — the gate is about not surprising anyone, not about
        // re-asking every launch. A fresh install (or an explicit Lock)
        // still lands on the confirmation card, and the session flag is
        // always required before the channel list will load.
        if (!AdultsAccess.IsGranted &&
            AdultsAccess.HasPersistedConsent)
        {
            AdultsAccess.Grant();
        }

        RefreshGateState();
    }

    private void RefreshGateState()
    {
        bool granted = AdultsAccess.IsGranted;

        GateCard.IsVisible = !granted;
        GateActions.IsVisible = !granted;
        UnlockedCard.IsVisible = granted;
    }

    private async void OnAcceptClicked(object? sender, EventArgs e)
    {
        if (sender is VisualElement button)
        {
            await AnimateTapAsync(button);
        }

        AdultsAccess.Grant();
        RefreshGateState();
    }

    private async void OnBrowseClicked(object? sender, EventArgs e)
    {
        if (sender is VisualElement button)
        {
            await AnimateTapAsync(button);
        }

        if (!AdultsAccess.IsGranted)
        {
            RefreshGateState();
            return;
        }

        await Shell.Current.GoToAsync(
            "CategoryChannelPage?category=adult&title=Adults%2018%2B&accent=%23B31847");
    }

    private async void OnLockClicked(object? sender, EventArgs e)
    {
        if (sender is VisualElement button)
        {
            await AnimateTapAsync(button);
        }

        AdultsAccess.Revoke();
        RefreshGateState();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (sender is VisualElement button)
        {
            await AnimateTapAsync(button);
        }

        await Shell.Current.GoToAsync("..");
    }

    private static async Task AnimateTapAsync(VisualElement element)
    {
        element.CancelAnimations();

        try
        {
            await element.ScaleToAsync(0.94, 60, Easing.CubicOut);
            await element.ScaleToAsync(1, 140, Easing.SpringOut);
        }
        finally
        {
            element.Scale = 1;
        }
    }
}
