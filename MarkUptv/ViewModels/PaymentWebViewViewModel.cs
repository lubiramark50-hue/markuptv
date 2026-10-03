using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MarkUptv.ViewModels;

public partial class PaymentWebViewViewModel : ObservableObject, IQueryAttributable
{
    private readonly PaymentService _paymentService;
    private Func<Task>? _onSuccess;
    private Func<Task>? _onFailure;

    [ObservableProperty]
    private string _paymentUrl = string.Empty;

    [ObservableProperty]
    private bool _isLoading = true;

    public PaymentWebViewViewModel(PaymentService paymentService)
    {
        _paymentService = paymentService;
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

        // Support both direct custom app schemas and live server API webhooks.
        // The TV unlock checkout lands on api/device/payment-callback, while
        // donations land on api/donation/callback — both are gateway callbacks
        // that complete the transaction on the server side.
        if (url.StartsWith("markuptv://payment-success", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("/payment-success") ||
            url.Contains("api/device/payment-callback") ||
            url.Contains("api/donation/callback"))
        {
            // Give server architecture a tiny window to complete distributed transactional logging
            await Task.Delay(1500);
            await HandleSuccess();
        }
        else if (url.StartsWith("markuptv://payment-failed", StringComparison.OrdinalIgnoreCase) ||
                 url.Contains("/payment-failed"))
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