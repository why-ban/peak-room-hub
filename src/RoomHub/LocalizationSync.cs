using System;
using System.Collections.Generic;

namespace RoomHub;

/// <summary>
/// Bridges the game's language-switch event to the mod's static texts.
///
/// The game fires <c>LocalizedText.OnLangugageChanged</c> (note the game's own
/// misspelling) after <c>RefreshAllText</c>; only components carrying the game's
/// <c>LocalizedText</c> component update themselves. Our texts are plain TMP labels
/// written once at creation, so without this bridge a language switch leaves the
/// ribbon button, panel titles and page buttons in the old language until the page
/// happens to be rebuilt. Handlers run only while the owning element exists.
/// </summary>
internal static class LocalizationSync
{
    private static readonly HashSet<Action> Handlers = new();
    private static bool _hooked;

    /// <summary>Register a refresh callback; must be paired with <see cref="Unregister"/>.</summary>
    public static void Register(Action handler)
    {
        if (handler == null)
            return;
        lock (Handlers)
        {
            Handlers.Add(handler);
            if (!_hooked)
            {
                try
                {
                    LocalizedText.OnLangugageChanged += OnLanguageChanged;
                    _hooked = true;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"LocalizationSync hook failed: {ex.Message}");
                }
            }
        }
    }

    public static void Unregister(Action handler)
    {
        if (handler == null)
            return;
        lock (Handlers)
        {
            Handlers.Remove(handler);
            if (_hooked && Handlers.Count == 0)
            {
                try
                {
                    LocalizedText.OnLangugageChanged -= OnLanguageChanged;
                }
                catch { /* ignore */ }
                _hooked = false;
            }
        }
    }

    private static void OnLanguageChanged()
    {
        Action[] snapshot;
        lock (Handlers)
        {
            snapshot = new Action[Handlers.Count];
            Handlers.CopyTo(snapshot);
        }
        foreach (var h in snapshot)
        {
            try { h(); }
            catch { /* one bad handler must not break the rest */ }
        }
    }
}
