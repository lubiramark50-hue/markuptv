using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Extensions.Options;

namespace MarkUptv.ViewModels;

public partial class PaymentWebViewViewModel : ObservableObject, IQueryAttributable
{
    private readonly PaymentService _paymentService;
    private readonly Uri _backendUri;
    private Func<Task>? _onSuccess;
    private Func<Task>? _onFailure;

    [ObservableProperty]
    private string _paymentUrl = string.Empty;

    [ObservableProperty]
    private bool _isLoading = true;

    public PaymentWebViewViewModel(PaymentService paymentService, IOptions<PaymentSettings> settings)
    {
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        ArgumentNullException.ThrowIfNull(settings);
        _backendUri = new Uri(settings.Value.BaseUrl, UriKind.Absolute);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("url", out var url) && url != null)
        {
            PaymentUrl = Uri.UnescapeDataString(url.ToString()!);
        }

        if (PaymentWebViewCallbackHolder.SuccessCallback != null)
            _onSuccess = PaymentWebViewCallbackHolder.SuccessCallback;

        if (PaymentWebViewCallbackHolder.FailureCallback != null)
            _onFailure = PaymentWebViewCallbackHolder.FailureCallback;
    }

    [RelayCommand]
    private void WebViewNavigated()
    {
        IsLoading = false;
    }

    public async Task HandleNavigationUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        // Never interpret a URL from an unrelated payment page as confirmation.
        // Entitlements are still verified server-side by PaymentRequiredViewModel.
        PaymentNavigationOutcome outcome = PaymentNavigationGuard.Classify(url, _backendUri);
        if (outcome == PaymentNavigationOutcome.Success)
        {
            await Task.Delay(1500);
            await HandleSuccess();
        }
        else if (outcome == PaymentNavigationOutcome.Failure)
        {
            await HandleFailure();
        }
    }

    private async Task HandleSuccess()
    {
        if (_onSuccess != null)
        {
            await _onSuccess.Invoke();
        }
        await GoBackAsync();
    }

    private async Task HandleFailure()
    {
        if (_onFailure != null)
        {
            await _onFailure.Invoke();
        }
        await GoBackAsync();
    }

    private async Task GoBackAsync()
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            // Clean backward mapping to prevent navigation stack thread crashes
            await Shell.Current.GoToAsync("..");
        });
    }
}

public static class PaymentWebViewCallbackHolder
{
    public static Func<Task>? SuccessCallback { get; set; }
    public static Func<Task>? FailureCallback { get; set; }
}