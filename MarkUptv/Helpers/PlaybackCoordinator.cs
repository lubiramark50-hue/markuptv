using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Maui.Views;

namespace MarkUptv.Helpers;

/// <summary>
/// App-wide rule: only ONE TV / stream may play at a time.
///
/// Every page that hosts a <see cref="MediaElement"/> registers its player
/// here (idempotent). The coordinator watches the <c>Source</c> property of
/// every registered player: as soon as a player receives a new non-null
/// source (which is what makes it start playing), any other player that is
/// currently active is stopped first. Pages additionally release their own
/// player when they disappear so hidden pages never keep streaming audio.
/// </summary>
public static class PlaybackCoordinator
{
    private static readonly ConditionalWeakTable<MediaElement, object>
        RegisteredPlayers = new();

    private static readonly object Sync = new();

    private static MediaElement? _activePlayer;

    /// <summary>
    /// Registers a page's player once. Safe to call many times.
    /// </summary>
    public static void Register(
        MediaElement player)
    {
        if (player is null)
        {
            return;
        }

        lock (Sync)
        {
            if (RegisteredPlayers.TryGetValue(
                    player,
                    out _))
            {
                return;
            }

            RegisteredPlayers.Add(
                player,
                new object());
        }

        player.PropertyChanged +=
            OnPlayerPropertyChanged;
    }

    /// <summary>
    /// Called when a page disappears. Stops the player if it is the
    /// currently active one so hidden pages never stream in the background.
    /// </summary>
    public static void Release(
        MediaElement player)
    {
        if (player is null)
        {
            return;
        }

        bool shouldStop = false;

        lock (Sync)
        {
            if (ReferenceEquals(
                    _activePlayer,
                    player))
            {
                _activePlayer = null;
                shouldStop = true;
            }
        }

        if (shouldStop)
        {
            SafeStop(player);
        }
    }

    /// <summary>
    /// The player that currently owns playback, if any. Read-only; used by
    /// diagnostics to observe the real media pipeline.
    /// </summary>
    public static MediaElement? ActivePlayer
    {
        get
        {
            lock (Sync)
            {
                return _activePlayer;
            }
        }
    }

    /// <summary>
    /// Immediately stops whatever single player is currently active.
    /// </summary>
    public static void StopActive()
    {
        MediaElement? toStop = null;

        lock (Sync)
        {
            toStop = _activePlayer;
            _activePlayer = null;
        }

        if (toStop is not null)
        {
            SafeStop(toStop);
        }
    }

    private static void OnPlayerPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (sender is not MediaElement player)
        {
            return;
        }

        if (!string.Equals(
                e.PropertyName,
                nameof(MediaElement.Source),
                StringComparison.Ordinal))
        {
            return;
        }

        // A null source means "stop", not "start playing".
        if (player.Source is null)
        {
            return;
        }

        Claim(player);
    }

    private static void Claim(
        MediaElement player)
    {
        MediaElement? previous = null;

        lock (Sync)
        {
            if (ReferenceEquals(
                    _activePlayer,
                    player))
            {
                return;
            }

            previous = _activePlayer;
            _activePlayer = player;
        }

        if (previous is not null)
        {
            SafeStop(previous);
        }
    }

    private static void SafeStop(
        MediaElement player)
    {
        try
        {
            if (Microsoft.Maui.ApplicationModel.MainThread.IsMainThread)
            {
                player.Stop();
            }
            else
            {
                Microsoft.Maui.ApplicationModel.MainThread
                    .BeginInvokeOnMainThread(player.Stop);
            }
        }
        catch (ObjectDisposedException)
        {
            // The player has already been torn down.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PlaybackCoordinator] Failed to stop a player: " +
                $"{exception.Message}");
        }
    }
}
