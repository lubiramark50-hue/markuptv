using MarkUptv.Models;
using MarkUptv.ViewModels;

namespace MarkUptv.Pages;

public partial class MatchThreadPage : ContentPage
{
    private readonly MatchThreadViewModel _viewModel;

    public MatchThreadPage(MatchThreadViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));

        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.ReloadCommand.Execute(null);
    }

    private void OnReactClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject bindable && bindable.BindingContext is ThreadPost post)
        {
            _viewModel.ReactCommand.Execute(post);
        }
    }

    private void OnReplyClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject bindable && bindable.BindingContext is ThreadPost post)
        {
            _viewModel.ReplyCommand.Execute(post);
        }
    }

    private async void OnReportClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject bindable && bindable.BindingContext is ThreadPost post)
        {
            await _viewModel.ReportCommand.ExecuteAsync(post);
        }
    }
}
