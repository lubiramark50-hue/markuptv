using MarkUptv.Models;
using MarkUptv.ViewModels;

namespace MarkUptv.Pages;

public partial class WorldChannelsPage : ContentPage
{
    private readonly WorldChannelsViewModel _viewModel;

    public WorldChannelsPage(WorldChannelsViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));

        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await _viewModel.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"[WorldChannelsPage] load failed: {exception}");
        }
    }

    private async void OnChannelTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject bindable &&
            bindable.BindingContext is LiveSource source)
        {
            await _viewModel.OpenChannelAsync(source);
        }
    }
}
