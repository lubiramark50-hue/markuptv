using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MarkUptv.Models;

namespace MarkUptv.Services
{
    /// <summary>
    /// Contract defining interactions for community feeds, comments, and real-time live channel streams.
    /// </summary>
    public interface ISocialService
    {
        // --- 🌐 Community Feed & Timeline Operations ---
        Task<FeedResponse?> GetFeedAsync(string deviceId, string? cursor = null, int limit = 20);
        Task<SocialPost?> CreatePostAsync(string deviceId, string authorName, string content, string? mediaUrl = null);
        Task<bool> LikePostAsync(int postId, string deviceId);
        Task LogViewAsync(string deviceId, string contentType, int contentId);
        Task<int> GetReactionCountAsync(string contentType, int contentId);
        Task<bool> ToggleReactionAsync(string deviceId, string contentType, int contentId);

        // --- 💬 On-Demand Post Comments Operations ---
        Task<List<SocialComment>> GetCommentsAsync(int postId);
        Task<List<UserComment>> GetCommentsAsync(string contentType, int contentId);
        Task<SocialComment?> CreateCommentAsync(int postId, string deviceId, string authorName, string content);
        Task<UserComment?> AddCommentAsync(string deviceId, string contentType, int contentId, string text);

        // --- 📺 Real-Time Interactive Live Player Chat Engine ---
        Task<bool> SubmitLiveChatMessageAsync(int channelId, string username, string message);
        void SubscribeToLiveChat(int channelId, Action<LiveChatBubble> onNewMessageReceived);
        void UnsubscribeFromLiveChat(int channelId);
    }
}
