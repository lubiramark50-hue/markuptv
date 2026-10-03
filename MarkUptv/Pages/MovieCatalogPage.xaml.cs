using MarkUptv.Models;
using MarkUptv.ViewModels;
using Microsoft.Maui.Controls;

namespace MarkUptv.Pages;

/// <summary>
/// Free-film catalogue: search, shelves and the offline library entry point.
///
/// Taps inside the card and chip templates are wired through these handlers
/// rather than RelativeSource-bound commands: a gesture whose command is
/// resolved via <c>{Binding Source={RelativeSource AncestorType=…}}</c> inside
/// a DataTemplate never fires on Android, which made every film card and every
/// shelf chip silently dead. Code-behind reads the item straight off the
/// sender's BindingContext, so nothing depends on ancestor binding resolution.
/// </summary>
public partial class MovieCatalogPage : ContentPage
{
    private readonly MovieCatalogViewModel _viewModel;

    public MovieCatalogPage(MovieCatalogViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.InitializeCommand.ExecuteAsync(null);
    }

    /// <summary>Opens the tapped film's detail page.</summary>
    private void OnMovieCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Element { BindingContext: MovieCatalogItem movie })
        {
            _viewModel.OpenMovieCommand.Execute(movie);
        }
    }

    /// <summary>Switches the shelf (Newest / Most watched / Translated).</summary>
    private void OnShelfTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Element { BindingContext: ShelfOption shelf })
        {
            _viewModel.SelectShelfCommand.Execute(shelf.Name);
        }
    }

    /// <summary>Applies a language filter (or clears it with the "All" chip).</summary>
    private void OnLanguageTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Element { BindingContext: MovieLanguage language })
        {
            _viewModel.SelectLanguageCommand.Execute(language);
        }
    }
}
