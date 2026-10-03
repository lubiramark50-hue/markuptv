using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Models;
using MarkUptv.Services;
using System.Text.RegularExpressions;

namespace MarkUptv.ViewModels;

public partial class DonationViewModel : ObservableObject
{
    private readonly DonationService _donationService;
    private readonly ConnectivityService _connectivity;

    [ObservableProperty]
    private bool _donating = false;

    [ObservableProperty]
    private string _amount = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _phoneNumber = string.Empty;

    [ObservableProperty]
    private string _firstName = string.Empty;

    [ObservableProperty]
    private string _lastName = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public DonationViewModel(DonationService donationService, ConnectivityService connectivity)
    {
        _donationService = donationService;
        _connectivity = connectivity;
    }

    // This is the method called from the page
    public async Task<string?> InitiatePaymentAsync()
    {
        if (!ValidateInput()) return null;

        if (!_connectivity.IsConnected)
        {
            ErrorMessage = "No internet connection. Please check your network and try again.";
            return null;
        }

        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = "Connecting to payment gateway...";

        var request = new DonationRequest
        {
            Amount = Amount,
            Email = Email,
            PhoneNumber = PhoneNumber,
            FirstName = FirstName,
            LastName = LastName
        };

        var redirectUrl = await _donationService.InitiateDonationAsync(request);

        IsLoading = false;
        StatusMessage = null;

        if (redirectUrl == null)
        {
            ErrorMessage = "Payment service is temporarily unavailable. Please try again later.";
        }

        return redirectUrl;
    }

    [RelayCommand]
    public async Task DonationAsync()
    {
        var redirectUrl = await InitiatePaymentAsync();
        if (redirectUrl != null)
        {
            // This command is used only for external browser; now we use the page's event handler.
            // Keep for compatibility but not used in new flow.
        }
    }

    private bool ValidateInput()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Amount) || !decimal.TryParse(Amount, out var amt) || amt <= 0)
        {
            ErrorMessage = "Please enter a valid donation amount.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Email) || !Regex.IsMatch(Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            ErrorMessage = "Please enter a valid email address.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(PhoneNumber))
        {
            ErrorMessage = "Phone number is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(FirstName))
        {
            ErrorMessage = "First name is required.";
            return false;
        }

        return true;
    }

    [RelayCommand]
    private void ClearError() => ErrorMessage = null;
}