using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace MarkUptv.ViewModels;

/// <summary>
/// Shared ViewModel foundation for loading, refreshing, errors,
/// cancellation and safe Shell navigation.
/// </summary>
public abstract partial class BaseViewModel : ObservableObject
{
    private readonly SemaphoreSlim _navigationGate =
        new(1, 1);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    public bool HasError =>
        !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsNotLoading =>
        !IsLoading;

    public virtual Task RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    protected async Task ExecuteWithLoadingAsync(
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default,
        string fallbackErrorMessage = "Something went wrong.")
    {
        ArgumentNullException.ThrowIfNull(work);

        await SetLoadingStateAsync(
            isLoading: true);

        ClearError();

        try
        {
            await work(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Normal command or navigation cancellation.
        }
        catch (Exception exception)
        {
            SetError(
                string.IsNullOrWhiteSpace(exception.Message)
                    ? fallbackErrorMessage
                    : exception.Message);

            System.Diagnostics.Debug.WriteLine(
                $"[{GetType().Name}] {exception}");
        }
        finally
        {
            await SetLoadingStateAsync(
                isLoading: false);
        }
    }

    protected async Task ExecuteWithRefreshingAsync(
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default,
        string fallbackErrorMessage = "Refresh failed.")
    {
        ArgumentNullException.ThrowIfNull(work);

        await SetRefreshingStateAsync(
            isRefreshing: true);

        ClearError();

        try
        {
            await work(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Normal refresh cancellation.
        }
        catch (Exception exception)
        {
            SetError(
                string.IsNullOrWhiteSpace(exception.Message)
                    ? fallbackErrorMessage
                    : exception.Message);

            System.Diagnostics.Debug.WriteLine(
                $"[{GetType().Name}] {exception}");
        }
        finally
        {
            await SetRefreshingStateAsync(
                isRefreshing: false);
        }
    }

    protected void SetError(string? message)
    {
        ErrorMessage =
            string.IsNullOrWhiteSpace(message)
                ? "Something went wrong."
                : message.Trim();
    }

    protected void ClearError()
    {
        ErrorMessage = string.Empty;
    }

    /// <summary>
    /// Performs Shell navigation on the UI thread and prevents
    /// duplicate navigation requests from running simultaneously.
    /// </summary>
    protected async Task SafeNavigateAsync(
        string route,
        IDictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default,
        bool animate = true)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return;
        }

        await _navigationGate.WaitAsync(
            cancellationToken);

        try
        {
            async Task NavigateCoreAsync()
            {
                Shell? shell =
                    Shell.Current;

                if (shell is null)
                {
                    throw new InvalidOperationException(
                        "Shell.Current is unavailable.");
                }

                var state =
                    new ShellNavigationState(
                        route.Trim());

                if (parameters is null ||
                    parameters.Count == 0)
                {
                    await shell.GoToAsync(
                        state,
                        animate);
                }
                else
                {
                    await shell.GoToAsync(
                        state,
                        animate,
                        parameters);
                }
            }

            if (MainThread.IsMainThread)
            {
                await NavigateCoreAsync();
            }
            else
            {
                await MainThread.InvokeOnMainThreadAsync(
                    NavigateCoreAsync);
            }
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    private Task SetLoadingStateAsync(
        bool isLoading)
    {
        if (MainThread.IsMainThread)
        {
            IsLoading = isLoading;
            return Task.CompletedTask;
        }

        return MainThread.InvokeOnMainThreadAsync(
            () => IsLoading = isLoading);
    }

    private Task SetRefreshingStateAsync(
        bool isRefreshing)
    {
        if (MainThread.IsMainThread)
        {
            IsRefreshing = isRefreshing;
            return Task.CompletedTask;
        }

        return MainThread.InvokeOnMainThreadAsync(
            () => IsRefreshing = isRefreshing);
    }
}