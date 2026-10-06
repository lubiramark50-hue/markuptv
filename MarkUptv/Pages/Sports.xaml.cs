using CommunityToolkit.Maui.Views;
using MarkUptv.Models;
using MarkUptv.ViewModels;

namespace MarkUptv.Pages;

public partial class Sports : ContentPage
{
    private SportsViewModel _viewModel;

    public Sports(SportsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        MarkUptv.Helpers.PlaybackCoordinator.Register(ChannelPlayer);
    }
    private void OnWatchClicked(object? sender, EventArgs e)
    {
        if (sender is VisualElement element &&
            element.BindingContext is Models.TvChannel channel)
        {
            _viewModel.PlayChannelCommand.Execute(channel);
        }
    }

    private async void OnAnimatedButtonClicked(object? sender, EventArgs e)
    {
        try
        {
            if (sender is VisualElement element)
            {
                await AnimateElementAsync(element);
            }
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnAnimatedButtonClicked: " + exception.Message);
        }
    }

    private async void OnChannelCardTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (sender is not VisualElement element ||
                element.BindingContext is not TvChannel channel)
            {
                return;
            }

            await AnimateElementAsync(element);

            _viewModel.PlayChannelCommand.Execute(channel);
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnChannelCardTapped: " + exception.Message);
        }
    }

    private static async Task AnimateElementAsync(VisualElement element)
    {
        element.CancelAnimations();

        try
        {
            await Task.WhenAll(
                element.ScaleToAsync(0.96, 75, Easing.CubicOut),
                element.FadeToAsync(0.88, 75, Easing.CubicOut));

            await Task.WhenAll(
                element.ScaleToAsync(1, 150, Easing.SpringOut),
                element.FadeToAsync(1, 130, Easing.CubicOut));
        }
        finally
        {
            element.Scale = 1;
            element.Opacity = 1;
        }
    }


    protected override async void OnAppearing()
    {
        try
        {
            base.OnAppearing();
            if (_viewModel.Channels.Count == 0 && !_viewModel.IsLoading)
                await _viewModel.LoadChannelsCommand.ExecuteAsync(null);
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnAppearing: " + exception.Message);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        MarkUptv.Helpers.PlaybackCoordinator.Release(ChannelPlayer);
    }

    private void OnChannelSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is TvChannel channel)
        {
            _viewModel.PlayChannelCommand.Execute(channel);
            ((CollectionView)sender!).SelectedItem = null;
        }
    }

    private void OnGroupSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is string group)
            _viewModel.SelectedGroup = group;
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        _viewModel.MediaReadyCommand.Execute(null);
        if (sender is MediaElement player)
        {
            player.Volume = 1.0;
        }
    }

    private void OnMediaFailed(object? sender, EventArgs e)
    {
        _viewModel.MediaFailedCommand.Execute(null);
    }

    private async void OnLogoClicked(object? sender, EventArgs e)
    {
        try
        {
            if (sender is View view)
            {
                await view.ScaleToAsync(1.2, 100, Easing.CubicOut);
                await view.ScaleToAsync(1.0, 100, Easing.CubicIn);
            }
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnLogoClicked: " + exception.Message);
        }
    }
}