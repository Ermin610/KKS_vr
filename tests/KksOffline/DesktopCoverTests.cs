using KKCharaStudioVR;

static partial class Program
{
    // Privacy mode (desktop cover): saved state, key handling, mirror toggle.
    private static void DesktopCoverPolicyChecks()
    {
        Check(VRDesktopCoverPolicy.ResolveInitialState(true, true), "Saved privacy mode is restored on start");
        Check(!VRDesktopCoverPolicy.ResolveInitialState(true, false), "Saved off stays off on start");
        Check(!VRDesktopCoverPolicy.ResolveInitialState(false, true), "Without settings the cover starts off");
        Check(!VRDesktopCoverPolicy.ResolveInitialState(false, false), "Without settings and nothing saved the cover starts off");

        Check(VRDesktopCoverPolicy.ShouldToggleFromKey(true, false), "Space toggles the cover");
        Check(!VRDesktopCoverPolicy.ShouldToggleFromKey(true, true), "Space typed into an input field does not toggle the cover");
        Check(!VRDesktopCoverPolicy.ShouldToggleFromKey(false, false), "No key, no toggle");
        Check(!VRDesktopCoverPolicy.ShouldToggleFromKey(false, true), "No key while typing, no toggle");

        Check(!VRDesktopCoverPolicy.ShowDeviceView(true), "Headset mirror is off while covered");
        Check(VRDesktopCoverPolicy.ShowDeviceView(false), "Headset mirror returns when uncovered");

        Check(VRDesktopCoverPolicy.ButtonLabel(true).StartsWith("Restore") && VRDesktopCoverPolicy.ButtonLabel(true).Contains("Space"),
            "Covered button offers restore");
        Check(VRDesktopCoverPolicy.ButtonLabel(false).StartsWith("Cover") && VRDesktopCoverPolicy.ButtonLabel(false).Contains("Space"),
            "Uncovered button offers cover");
    }
}
