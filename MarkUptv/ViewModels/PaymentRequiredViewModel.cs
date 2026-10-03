using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Models;
using MarkUptv.Pages;
using MarkUptv.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MarkUptv.ViewModels;

public partial class PaymentRequiredViewModel : ObservableObject
{
    private readonly PaymentService _paymentService;
    private readonly StatusCacheService _cacheService;

    private static readonly Regex EmailValidator = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _phoneNumber = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    [NotifyPropertyChangedFor(nameof(PayButtonText))]
    private bool _isLoading;

    /// <summary>True while the checkout is being created - used to disable the CTA.</summary>
    public bool IsNotLoading => !IsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _titleText = "Choose Premium Access";

    [ObservableProperty]
    private string _messageText = "Your free trial has ended. Upgrade to keep enjoying premium features.";

    [ObservableProperty]
    private string _daysLeftText = string.Empty;

    [ObservableProperty]
    private bool _hasDaysLeftText;

    /// <summary>
    /// Unlock price, server-owned. Starts on the known default so the screen
    /// is never blank, and is replaced by whatever the backend reports.
    /// </summary>
    [ObservableProperty]
    private string _priceText = "5,000 UGX";

    /// <summary>Label for the pay button, kept in sync with <see cref="PriceText"/>.</summary>
    public string PayButtonText => IsLoading ? "CONNECTING TO GATEWAY…" : $"PAY {PriceText} · UNLOCK TV";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public PaymentRequiredViewModel(PaymentService paymentService, StatusCacheService cacheService)
    {
        _paymentService = paymentService;
        _cacheService = cacheService;
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task InitializeAsync()
    {
        await LoadStatusAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoadStatusAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            var status = await _paymentService.GetStatusAsync(useCache: true);
            if (status == null) return;

            if (status.CanWatch)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Shell.Current.GoToAsync("///MainPage");
                });
                return;
            }

            // Price comes from the same response as the entitlement, so the
            // headline and the button can never advertise different amounts.
            PriceText = status.PriceText;
            OnPropertyChanged(nameof(PayButtonText));

            if (status.IsTrial && status.DaysRemaining > 0)
            {
                TitleText = "⏳ TRIAL ACTIVE";
                MessageText = $"Your free trial ends in {status.DaysRemaining} day(s).";
                DaysLeftText = $"Unlock permanently now for a one-time {PriceText} payment.";
                HasDaysLeftText = true;
            }
            else
            {
                TitleText = "⚠️ ACCESS EXPIRED";
                MessageText = "Your trial has ended. Unlock this device permanently.";
                DaysLeftText = $"One-time payment of {PriceText} unlocks TV forever.";
                HasDaysLeftText = true;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = "Failed to synchronize status with server connection gates.";
            System.Diagnostics.Debug.WriteLine($"LoadStatusAsync Structural Error: {ex}");
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task PaymentAsync()
    {
        var sanitizedEmail = Email?.Trim();
        if (string.IsNullOrWhiteSpace(sanitizedEmail) || !EmailValidator.IsMatch(sanitizedEmail))
        {
            ErrorMessage = "Please enter a valid email address.";
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var paymentDetails = new PaymentDetails
            {
                Email = sanitizedEmail,
                PhoneNumber = PhoneNumber?.Trim() ?? string.Empty,
                FirstName = "Premium",
                LastName = "User"
            };

            // The service hands back either a checkout URL or the gateway's own
            // reason for refusing, so the screen never dead-ends with "empty
            // redirect URL" when the real cause is, say, bad credentials.
            var result = await _paymentService.InitiatePaymentAsync(paymentDetails);

            if (result.Success && !string.IsNullOrEmpty(result.RedirectUrl))
            {
                ConfigureWebViewCallbacks();

                var navigationParams = new Dictionary<string, object>
                {
                    { "url", result.RedirectUrl }
                };

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    // PaymentWebViewPage is a dynamic push route, so keep it relative.
                    await Shell.Current.GoToAsync("PaymentWebViewPage", navigationParams);
                });
            }
            else
            {
                ErrorMessage = string.IsNullOrWhiteSpace(result.Error)
                    ? "Could not start the payment. Please try again."
                    : result.Error;
            }
        }
        catch (HttpRequestException httpEx)
        {
            ErrorMessage = $"Network Error ({httpEx.StatusCode}): Please check your connection.";
            System.Diagnostics.Debug.WriteLine($"[PAYMENT API ERROR]: {httpEx.Message} | Status: {httpEx.StatusCode}");
        }
        catch (Exception ex)
        {
            // LOG THE FULL STACK TRACE
            ErrorMessage = $"Unexpected error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[PAYMENT CRITICAL FAILURE]: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ConfigureWebViewCallbacks()
    {
        PaymentWebViewCallbackHolder.SuccessCallback = async () =>
        {
            CleanupWebViewCallbacks();
            _cacheService.Invalidate();

            var status = await _paymentService.GetFreshStatusAsync();

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (status?.CanWatch == true)
                    await Shell.Current.GoToAsync("///MainPage");
                else
                    await LoadStatusAsync();
            });
        };

        PaymentWebViewCallbackHolder.FailureCallback = async () =>
        {
            CleanupWebViewCallbacks();

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                ErrorMessage = "Payment was cancelled or rejected by gateway provider.";
                await LoadStatusAsync();
            });
        };
    }

    private void CleanupWebViewCallbacks()
    {
        PaymentWebViewCallbackHolder.SuccessCallback = null;
        PaymentWebViewCallbackHolder.FailureCallback = null;
    }
}
