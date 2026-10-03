using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkUptv.Models;
using MarkUptv.Services;
using System.Collections.ObjectModel;

namespace MarkUptv.ViewModels;

public partial class NotificationsViewModel : ObservableObject
{
    private readonly INotificationService _notificationService;

    public ObservableCollection<Notification> Notifications { get; } = new();

    [ObservableProperty]
    private bool _isLoading;

    public NotificationsViewModel(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var items = await _notificationService.GetNotificationsAsync();
            Notifications.Clear();
            foreach (var item in items)
                Notifications.Add(item);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        await _notificationService.MarkAllAsReadAsync();
        await LoadAsync();
    }
}
