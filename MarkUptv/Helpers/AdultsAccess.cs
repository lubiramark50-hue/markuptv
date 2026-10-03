namespace MarkUptv.Helpers;

/// <summary>
/// Session + device consent state for the Adults (18+) section.
/// The session flag is what actually unlocks the adult category when the
/// app runs; the persisted preference only remembers the choice so the
/// user is not re-prompted on every visit. A fresh app launch starts
/// locked regardless of the persisted flag, so deep links can never land
/// directly inside the adult channel list.
/// </summary>
public static class AdultsAccess
{
    private const string ConsentPreferenceKey = "adults.section.consent";

    private static bool _sessionGranted;

    /// <summary>True once the user passes the 18+ gate in this app session.</summary>
    public static bool IsGranted => _sessionGranted;

    /// <summary>True when the user previously accepted on this device.</summary>
    public static bool HasPersistedConsent =>
        Preferences.Default.Get(ConsentPreferenceKey, false);

    /// <summary>Unlocks the adult category for the rest of this session.</summary>
    public static void Grant()
    {
        _sessionGranted = true;
        Preferences.Default.Set(ConsentPreferenceKey, true);
    }

    /// <summary>Re-arms the gate (for a "Lock section" action or sign-out).</summary>
    public static void Revoke()
    {
        _sessionGranted = false;
        Preferences.Default.Set(ConsentPreferenceKey, false);
    }
}
