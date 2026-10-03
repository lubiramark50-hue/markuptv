using MarkUptv.Services;
using MarkUptv.ViewModels;

namespace MarkUptv.Pages;

public partial class DonationPage : ContentPage
{
    private readonly DonationViewModel _viewModel;
    private readonly PaymentService _paymentService;

    public DonationPage(DonationViewModel viewModel, PaymentService paymentService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _paymentService = paymentService;
        BindingContext = _viewModel;
    }

    private async void OnDonateClicked(object sender, EventArgs e)
    {
        var redirectUrl = await _viewModel.InitiatePaymentAsync();
        if (!string.IsNullOrEmpty(redirectUrl))
        {
            // Set static callbacks for the WebView page
            PaymentWebViewCallbackHolder.SuccessCallback = async () =>
            {
                await DisplayAlertAsync("Success", "Thank you for your donation!", "OK");
                await Shell.Current.GoToAsync("///MainPage");
            };
            PaymentWebViewCallbackHolder.FailureCallback = async () =>
            {
                await DisplayAlertAsync("Error", "Donation failed. Please try again.", "OK");
            };

            await Shell.Current.GoToAsync($"PaymentWebViewPage?url={Uri.EscapeDataString(redirectUrl)}");
        }
        else if (!string.IsNullOrEmpty(_viewModel.ErrorMessage))
        {
            await DisplayAlertAsync("Error", _viewModel.ErrorMessage, "OK");
        }
    }
}