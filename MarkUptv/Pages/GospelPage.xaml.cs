using MarkUptv.Models;
using MarkUptv.ViewModels;

namespace MarkUptv.Pages;

public partial class GospelPage : ContentPage
{
    private GospelViewModel _viewModel;

    public GospelPage(GospelViewModel viewModel)
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


    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel.Channels.Count == 0 && !_viewModel.IsLoading)
            await _viewModel.LoadChannelsCommand.ExecuteAsync(null);
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

    private void OnMediaOpened(object? sender, EventArgs e) => _viewModel.MediaReadyCommand.Execute(null);
    private void OnMediaFailed(object? sender, EventArgs e) => _viewModel.MediaFailedCommand.Execute(null);

    private async void OnLogoClicked(object? sender, EventArgs e)
    {
        if (sender is View view)
        {
            await view.ScaleToAsync(1.2, 100, Easing.CubicOut);
            await view.ScaleToAsync(1.0, 100, Easing.CubicIn);
        }
    }
}