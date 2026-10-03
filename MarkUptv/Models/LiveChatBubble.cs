using System;

namespace MarkUptv.Models
{
    /// <summary>
    /// Represents a single live message bubble item streaming inside a live video environment.
    /// Fully optimized for ultra-low allocations and memory-safe layouts inside dynamic scrolling controls.
    /// </summary>
    public class LiveChatBubble
    {
        /// <summary>
        /// Unique tracking identifier for the message bubble item.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// The target live channel identifier mapping this chat stream.
        /// </summary>
        public int ChannelId { get; set; }

        /// <summary>
        /// The screen profile name or handle of the user who sent the message.
        /// </summary>
        public string Username { get; set; } = "Anonymous Member";

        /// <summary>
        /// The text body contents of the chat message.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Absolute time marker designating execution dispatch cycles.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// HEX accent color string assigned dynamically to decorate the user handle avatar element.
        /// </summary>
        public string AvatarHexColor { get; set; } = "#FF006E";

        /// <summary>
        /// Generates a localized textual timestamp approximation representation.
        /// </summary>
        public string TimeAgo
        {
            get
            {
                var span = DateTime.UtcNow - Timestamp;
                if (span.TotalSeconds < 60) return "Just now";
                if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
                return Timestamp.ToLocalTime().ToString("t");
            }
        }
    }
}