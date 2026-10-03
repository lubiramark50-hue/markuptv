using MarkUptv.ViewModels;

namespace MarkUptv.Pages;

/// <summary>
/// The films saved inside the app, playable without a connection.
/// </summary>
public partial class MovieDownloadsPage : ContentPage
{
    private readonly MovieDownloadsViewModel _viewModel;

    public MovieDownloadsPage(MovieDownloadsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _viewModel.LoadCommand.Execute(null);
    }
}
