using MarkUptv.ViewModels;

namespace MarkUptv.Pages;

public partial class PaymentWebViewPage : ContentPage
{
    public PaymentWebViewPage(PaymentWebViewViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnWebViewNavigating(object sender, WebNavigatingEventArgs e)
    {
        try
        {
            if (BindingContext is PaymentWebViewViewModel viewModel)
            {
                // Forward raw navigation intercept URL safely to processing view model
                await viewModel.HandleNavigationUrl(e.Url);
            }
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnWebViewNavigating: " + exception.Message);
        }
    }

    private void OnWebViewNavigated(object sender, WebNavigatedEventArgs e)
    {
        if (BindingContext is PaymentWebViewViewModel viewModel)
        {
            if (viewModel.WebViewNavigatedCommand.CanExecute(null))
            {
                viewModel.WebViewNavigatedCommand.Execute(null);
            }
        }
    }
}