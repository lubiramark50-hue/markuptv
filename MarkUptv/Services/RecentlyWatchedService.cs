using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Maui.Storage;
using MarkUptv.Models;

namespace MarkUptv.Services
{
    /// <summary>
    /// Thread-safe implementation tracking recently watched tv channels.
    /// Completely optimized for Native AOT execution models.
    /// </summary>
    public class RecentlyWatchedService
    {
        private const string PrefsKey = "RecentChannels_V2";
        private const int MaxHistoryCount = 15;
        private readonly List<TvChannel> _recentChannels = new();
        private readonly object _syncLock = new();
        public event Action? RecentlyWatchedChanged;

        public RecentlyWatchedService()
        {
            LoadFromStorage();
        }

        /// <summary>
        /// Retrieves an isolated snapshots array of all recorded history items.
        /// </summary>
        public List<TvChannel> GetRecentChannels()
        {
            lock (_syncLock)
            {
                return new List<TvChannel>(_recentChannels);
            }
        }

        public List<TvChannel> GetRecent(int limit = MaxHistoryCount)
        {
            lock (_syncLock)
            {
                return _recentChannels.Take(limit).ToList();
            }
        }

        /// <summary>
        /// Registers a viewed channel. Moves the channel to the top of the history 
        /// list and drops historical records that exceed structural layout limits.
        /// </summary>
        public void AddChannelToHistory(TvChannel channel)
        {
            if (channel == null) return;

            lock (_syncLock)
            {
                // Remove existing instances of the same channel ID to prevent duplicates
                _recentChannels.RemoveAll(c => c.Id == channel.Id);

                // Insert into position zero (the top item)
                _recentChannels.Insert(0, channel);

                // Discard entries trailing beyond max capability profiles
                while (_recentChannels.Count > MaxHistoryCount)
                {
                    _recentChannels.RemoveAt(_recentChannels.Count - 1);
                }

                SaveToStorage();
            }

            RecentlyWatchedChanged?.Invoke();
        }

        public void Add(TvChannel channel) => AddChannelToHistory(channel);

        public void AddOrUpdate(TvChannel channel) => AddChannelToHistory(channel);

        /// <summary>
        /// Explicitly removes an item from history tracking records.
        /// </summary>
        public void RemoveChannelFromHistory(int channelId)
        {
            lock (_syncLock)
            {
                int removedCount = _recentChannels.RemoveAll(c => c.Id == channelId);
                if (removedCount > 0)
                {
                    SaveToStorage();
                }
            }

            RecentlyWatchedChanged?.Invoke();
        }

        /// <summary>
        /// Wipes all elements from persistence storage models cleanly.
        /// </summary>
        public void ClearHistory()
        {
            lock (_syncLock)
            {
                _recentChannels.Clear();
                Preferences.Remove(PrefsKey);
            }

            RecentlyWatchedChanged?.Invoke();
        }

        private void LoadFromStorage()
        {
            lock (_syncLock)
            {
                try
                {
                    var serializedData = Preferences.Get(PrefsKey, string.Empty);
                    if (string.IsNullOrEmpty(serializedData)) return;

                    // Fixed: Leverages the source generator context metadata maps instead of reflection lookup
                    var parsedList = JsonSerializer.Deserialize(serializedData, NewsContextContainer.Default.ListTvChannel);
                    if (parsedList != null)
                    {
                        _recentChannels.Clear();
                        _recentChannels.AddRange(parsedList);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RecentlyWatchedService] Load failure fallback: {ex.Message}");
                }
            }
        }

        private void SaveToStorage()
        {
            try
            {
                // Fixed: Explicit serialization metadata resolution passes compile constraints safely
                var serializedData = JsonSerializer.Serialize(_recentChannels, NewsContextContainer.Default.ListTvChannel);
                Preferences.Set(PrefsKey, serializedData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecentlyWatchedService] Save operation failed: {ex.Message}");
            }
        }
    }
}
