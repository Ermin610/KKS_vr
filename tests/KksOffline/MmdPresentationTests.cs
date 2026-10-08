using KKCharaStudioVR;

static partial class Program
{
    private static void MmdPresentationPolicyChecks()
    {
        // hideSetting, stateReadable, reported, fresh, available, playing, keepUi
        Check(VRMmdPresentationPolicy.Decide(true, true, true, true, true, true, false) == VRMmdPresentationPolicy.Block.None,
            "fresh playing report with hide setting on must present");
        Check(VRMmdPresentationPolicy.Decide(false, true, true, true, true, true, false) == VRMmdPresentationPolicy.Block.SettingOff,
            "hide setting off must keep hands and UI");
        Check(VRMmdPresentationPolicy.Decide(true, true, false, false, false, false, false) == VRMmdPresentationPolicy.Block.NoReport,
            "no report yet is its own reason");
        Check(VRMmdPresentationPolicy.Decide(true, true, true, false, true, true, false) == VRMmdPresentationPolicy.Block.ReportStale,
            "stale report must not present even if the last flag said playing");
        Check(VRMmdPresentationPolicy.Decide(true, true, true, true, false, false, false) == VRMmdPresentationPolicy.Block.NotAvailable,
            "missing director is reported as unavailable");
        Check(VRMmdPresentationPolicy.Decide(true, true, true, true, true, false, false) == VRMmdPresentationPolicy.Block.NotPlaying,
            "paused playback must not present");
        Check(VRMmdPresentationPolicy.Decide(true, false, true, true, true, true, false) == VRMmdPresentationPolicy.Block.StateUnreadable,
            "an unreadable bridge fails open");
        Check(VRMmdPresentationPolicy.Decide(true, true, true, true, true, true, true) == VRMmdPresentationPolicy.Block.KeptUiAfterSummon,
            "UI summoned during playback stays");

        // Same truth table as the old inline expression:
        // stateReadable && fresh && available && playing && hide && !keepUi.
        for (int mask = 0; mask < 128; mask++)
        {
            bool hide = (mask & 1) != 0, readable = (mask & 2) != 0, reported = (mask & 4) != 0,
                fresh = (mask & 8) != 0, available = (mask & 16) != 0, playing = (mask & 32) != 0, keep = (mask & 64) != 0;
            bool old = readable && (reported && available && fresh && playing) && hide && !keep;
            bool now = VRMmdPresentationPolicy.Decide(hide, readable, reported, fresh, available, playing, keep)
                == VRMmdPresentationPolicy.Block.None;
            Check(old == now, "presentation decision changed for mask " + mask);
        }

        Check(!VRMmdPresentationPolicy.ShouldLogTransition(VRMmdPresentationPolicy.Block.NotPlaying, VRMmdPresentationPolicy.Block.NotPlaying, true),
            "an unchanged reason does not log");
        Check(VRMmdPresentationPolicy.ShouldLogTransition(VRMmdPresentationPolicy.Block.None, VRMmdPresentationPolicy.Block.NotPlaying, false),
            "leaving presentation always logs");
        Check(VRMmdPresentationPolicy.ShouldLogTransition(VRMmdPresentationPolicy.Block.ReportStale, VRMmdPresentationPolicy.Block.None, true),
            "entering presentation always logs");
        Check(!VRMmdPresentationPolicy.ShouldLogTransition(VRMmdPresentationPolicy.Block.SettingOff, VRMmdPresentationPolicy.Block.NoReport, false),
            "idle reason changes do not spam the log");
        Check(VRMmdPresentationPolicy.ShouldLogTransition(VRMmdPresentationPolicy.Block.NotPlaying, VRMmdPresentationPolicy.Block.ReportStale, true),
            "a reason change while MMDD says playing is logged");

        Check(VRMmdPresentationPolicy.BlocksUiButtons(true, false), "hidden UI blocks B/Y recall without a camera VMD");
        Check(VRMmdPresentationPolicy.BlocksUiButtons(false, true), "a locked rig blocks UI buttons");
        Check(!VRMmdPresentationPolicy.BlocksUiButtons(false, false), "normal play leaves UI buttons live");

        int count = 0;
        for (int i = 0; i < VRMmdPresentationPolicy.SilentListenerWarnAfter; i++)
            count = VRMmdPresentationPolicy.NextSilentListenerCount(count, true, true);
        Check(VRMmdPresentationPolicy.ShouldWarnSilentListener(count), "repeated stale-after-install warns once");
        Check(!VRMmdPresentationPolicy.ShouldWarnSilentListener(VRMmdPresentationPolicy.NextSilentListenerCount(count, true, true)),
            "the silent-listener warning is not repeated every retry");
        Check(VRMmdPresentationPolicy.NextSilentListenerCount(count, false, true) == 0, "a fresh report resets the silence count");
        Check(VRMmdPresentationPolicy.NextSilentListenerCount(count, true, false) == 0,
            "a failed install or missing director is not listener silence");
        Check(VRMmdPresentationPolicy.Describe(VRMmdPresentationPolicy.Block.ReportStale).Contains("stale"),
            "stale reason is readable in the log");
    }
}
