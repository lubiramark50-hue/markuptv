using MarkUptv.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MarkUptv.Services;

public interface INotificationService
{
    Task<List<Notification>> GetNotificationsAsync();
    Task MarkAllAsReadAsync();
    Task MarkReadAsync(int notificationId);
    Task DeleteAsync(int notificationId);
    Task<int> GetUnreadCountAsync();
}