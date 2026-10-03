using MarkUptv.Models;

namespace MarkUptv.Services;

public sealed class LocalNotificationService : INotificationService
{
    private readonly List<Notification> _notifications = new()
    {
        new Notification
        {
            Id = 1,
            Title = "Welcome to MarkUp TV",
            Body = "Live channels, news, community, and support are ready.",
            Timestamp = DateTime.UtcNow,
            IsRead = false
        }
    };

    public Task<List<Notification>> GetNotificationsAsync() =>
        Task.FromResult(_notifications.OrderByDescending(n => n.Timestamp).ToList());

    public Task MarkAllAsReadAsync()
    {
        foreach (var item in _notifications) item.IsRead = true;
        return Task.CompletedTask;
    }

    public Task MarkReadAsync(int notificationId)
    {
        var item = _notifications.FirstOrDefault(n => n.Id == notificationId);
        if (item != null) item.IsRead = true;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int notificationId)
    {
        _notifications.RemoveAll(n => n.Id == notificationId);
        return Task.CompletedTask;
    }

    public Task<int> GetUnreadCountAsync() =>
        Task.FromResult(_notifications.Count(n => !n.IsRead));
}
