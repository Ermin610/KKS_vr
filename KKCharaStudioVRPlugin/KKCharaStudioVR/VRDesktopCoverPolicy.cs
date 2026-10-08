namespace KKCharaStudioVR;

/// <summary>
/// Pure decisions for the desktop cover ("privacy mode"). The cover only blanks
/// the desktop window; it must never change what the headset or the GUI quads
/// show, and its on/off state is a saved setting rather than a per-session flag.
/// </summary>
internal static class VRDesktopCoverPolicy
{
    internal const string ToggleKeyName = "Space";

    /// <summary>
    /// Space toggles the cover, except while text is typed into an input field.
    /// Otherwise typing a name with a space in it silently flips (and saves)
    /// privacy mode.
    /// </summary>
    internal static bool ShouldToggleFromKey(bool toggleKeyDown, bool textInputFocused)
    {
        return toggleKeyDown && !textInputFocused;
    }

    /// <summary>
    /// The saved setting decides the state after a restart. Without settings
    /// (not initialized yet) the cover stays off so nothing is hidden by surprise.
    /// </summary>
    internal static bool ResolveInitialState(bool settingsAvailable, bool savedEnabled)
    {
        return settingsAvailable && savedEnabled;
    }

    /// <summary>Unity's desktop mirror of the headset is off while covered.</summary>
    internal static bool ShowDeviceView(bool coverEnabled)
    {
        return !coverEnabled;
    }

    internal static string ButtonLabel(bool coverEnabled)
    {
        return coverEnabled
            ? "Restore Desktop View (" + ToggleKeyName + ")"
            : "Cover Desktop View (" + ToggleKeyName + ")";
    }
}
