using KKCharaStudioVR;

static partial class Program
{
    // Saved VR background colour (wrist menu presets, 5 = green screen).
    private static void BackgroundPresetPolicyChecks()
    {
        const int count = 6;
        Check(!VRBackgroundPresetPolicy.HasSavedPreset(VRBackgroundPresetPolicy.Unset, count), "Unset keeps Studio's background");
        Check(VRBackgroundPresetPolicy.HasSavedPreset(0, count) && VRBackgroundPresetPolicy.HasSavedPreset(5, count), "First and green-screen presets are valid");
        Check(!VRBackgroundPresetPolicy.HasSavedPreset(6, count), "Index past the last preset is ignored");
        Check(!VRBackgroundPresetPolicy.HasSavedPreset(0, 0), "No presets means nothing saved");
        Check(VRBackgroundPresetPolicy.NormalizeSaved(5, count) == 5, "Green screen is stored as 5");
        Check(VRBackgroundPresetPolicy.NormalizeSaved(9, count) == VRBackgroundPresetPolicy.Unset, "Hand-edited out-of-range index becomes Unset");
        Check(VRBackgroundPresetPolicy.NormalizeSaved(-7, count) == VRBackgroundPresetPolicy.Unset, "Negative index becomes Unset");

        int pair = VRBackgroundPresetPolicy.CameraSignature(101, 202);
        Check(pair != VRBackgroundPresetPolicy.CameraSignature(101, 0), "VR camera arriving later changes the signature");
        Check(pair != VRBackgroundPresetPolicy.CameraSignature(303, 202), "A rebuilt Studio camera changes the signature");
        Check(pair == VRBackgroundPresetPolicy.CameraSignature(101, 202), "Same cameras keep the signature");

        Check(VRBackgroundPresetPolicy.ShouldReapply(5, count, true, pair, 0, 0, 0), "First camera pair after start gets the saved colour");
        Check(!VRBackgroundPresetPolicy.ShouldReapply(5, count, true, pair, pair, 0, 0), "Already applied to these cameras: no per-frame fight");
        Check(VRBackgroundPresetPolicy.ShouldReapply(5, count, true, pair, pair, 1, 0), "Scene load request re-applies once");
        Check(!VRBackgroundPresetPolicy.ShouldReapply(5, count, false, pair, 0, 1, 0), "Nothing to apply before Studio's camera exists");
        Check(!VRBackgroundPresetPolicy.ShouldReapply(VRBackgroundPresetPolicy.Unset, count, true, pair, 0, 1, 0), "Unset never overrides Studio");
    }
}
