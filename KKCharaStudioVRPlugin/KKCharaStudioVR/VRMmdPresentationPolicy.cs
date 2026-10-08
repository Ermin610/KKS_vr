namespace KKCharaStudioVR;

/// <summary>
/// Pure decisions for MMD presentation ("hide hands and UI during playback").
/// The playback controller, the grip tool, the UI pointer, and the offline
/// tests share these so the reason presentation is off can be logged.
/// </summary>
internal static class VRMmdPresentationPolicy
{
    internal enum Block
    {
        None = 0,
        SettingOff = 1,
        NoReport = 2,
        ReportStale = 3,
        NotAvailable = 4,
        NotPlaying = 5,
        StateUnreadable = 6,
        KeptUiAfterSummon = 7
    }

    /// <summary>
    /// Returns Block.None when presentation should be active, otherwise the
    /// first reason it is not. Order matters: a stale report hides the
    /// playing flag, so staleness is reported before NotPlaying.
    /// </summary>
    internal static Block Decide(
        bool hideSetting,
        bool stateReadable,
        bool reported,
        bool reportFresh,
        bool available,
        bool playing,
        bool keepUiAfterSummon)
    {
        if (!hideSetting)
            return Block.SettingOff;
        if (!reported)
            return Block.NoReport;
        if (!reportFresh)
            return Block.ReportStale;
        if (!available)
            return Block.NotAvailable;
        if (!playing)
            return Block.NotPlaying;
        if (!stateReadable)
            return Block.StateUnreadable;
        if (keepUiAfterSummon)
            return Block.KeptUiAfterSummon;
        return Block.None;
    }

    /// <summary>
    /// A reason is worth a log line only while MMDD itself says it is playing
    /// (or the reason changes away from "active"). Idle reasons such as
    /// NotPlaying would otherwise log on every pause.
    /// </summary>
    internal static bool ShouldLogTransition(Block previous, Block next, bool lastKnownPlaying)
    {
        if (previous == next)
            return false;
        if (previous == Block.None || next == Block.None)
            return true;
        return lastKnownPlaying;
    }

    /// <summary>
    /// While hands and UI are hidden, buttons must not bring UI back: B/Y
    /// long-press (GUI recall), trigger selection, and the UI laser. KK
    /// returned from the grip tool for the whole presentation; KKS keeps
    /// locomotion free without a camera VMD, so only the UI paths are gated.
    /// </summary>
    internal static bool BlocksUiButtons(bool presentationActive, bool rigLocked)
    {
        return presentationActive || rigLocked;
    }

    internal const int SilentListenerWarnAfter = 3;

    /// <summary>
    /// The reporter install itself writes one report. If every later check
    /// still finds the report stale, VNGE's "update" event is not delivering
    /// to the listener, which keeps presentation from ever starting.
    /// </summary>
    internal static int NextSilentListenerCount(int current, bool staleNow, bool lastInstallSucceeded)
    {
        if (!staleNow)
            return 0;
        return lastInstallSucceeded ? current + 1 : 0;
    }

    internal static bool ShouldWarnSilentListener(int count)
    {
        return count == SilentListenerWarnAfter;
    }

    internal static string Describe(Block block)
    {
        switch (block)
        {
            case Block.None: return "active";
            case Block.SettingOff: return "HideHandsAndUiDuringMmd is off";
            case Block.NoReport: return "no MMDD playback report yet";
            case Block.ReportStale: return "MMDD playback report is stale (>1s)";
            case Block.NotAvailable: return "MMDD reported no active director (gdata.mmdd missing or read failed)";
            case Block.NotPlaying: return "MMDD reports not playing";
            case Block.StateUnreadable: return "MMDD state bridge failed";
            case Block.KeptUiAfterSummon: return "UI was summoned during playback";
            default: return block.ToString();
        }
    }
}
