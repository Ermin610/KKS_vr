using System;
using System.Collections.Generic;

namespace KKCharaStudioVR;

/// <summary>
/// Pure decisions for MMD stick transport, IK guide visibility, knead/touch,
/// physical undress, and figure posing. Playback code and the offline tests
/// both call this so a paused stick click cannot be treated as a view turn.
/// Knead/touch and physical undress are separate switches.
/// </summary>
internal static class VRStudioInteractionPolicy
{
    internal enum TransportOp
    {
        Ignore = 0,
        Pause = 1,
        Start = 2,
        ToggleLive = 3
    }

    internal struct StickPlan
    {
        public TransportOp Operation;
        public bool SwallowLocomotion;
        public bool HoldUntilRelease;
    }

    internal struct MmdMovementPlan
    {
        public bool LockRig;
        public bool StickTrimsCamera;
        public bool AllowLocomotion;
        public bool AllowWorldGrab;
        public bool AllowObjectGrab;
    }

    /// <summary>
    /// Only a camera motion that writes the VR rig may take the player's view.
    /// A dance without a camera VMD leaves stick locomotion, snap turn, and grip
    /// world-move live. Object grabs stay off while any dance plays, because the
    /// motion would fight a dragged limb.
    /// </summary>
    internal static MmdMovementPlan PlanMmdMovement(bool playbackActive, bool cameraMotionActive)
    {
        bool locked = playbackActive && cameraMotionActive;
        return new MmdMovementPlan
        {
            LockRig = locked,
            StickTrimsCamera = locked,
            AllowLocomotion = !locked,
            AllowWorldGrab = !locked,
            AllowObjectGrab = !playbackActive
        };
    }

    internal static bool CanToggle(
        bool sessionLatched,
        bool reportFresh,
        bool playbackAvailable,
        float startFrame,
        float endFrame,
        bool reportedPlaying)
    {
        if (reportFresh && reportedPlaying)
            return true;
        if (sessionLatched)
            return true;
        return reportFresh
            && playbackAvailable
            && endFrame > startFrame + 0.5f;
    }

    internal static TransportOp ChooseTransport(bool canToggle, bool reportFresh, bool reportedPlaying)
    {
        if (!canToggle)
            return TransportOp.Ignore;
        // A stale C# flag must not choose Pause while MMDD is already stopped.
        // startStop reads the live interpreter flag.
        if (!reportFresh)
            return TransportOp.ToggleLive;
        return reportedPlaying ? TransportOp.Pause : TransportOp.Start;
    }

    internal static StickPlan PlanStick(
        bool unchordedClick,
        bool canToggle,
        bool reportFresh,
        bool reportedPlaying,
        bool alreadyConsumed,
        bool transportHold)
    {
        TransportOp operation = TransportOp.Ignore;
        if (unchordedClick && !alreadyConsumed)
            operation = ChooseTransport(canToggle, reportFresh, reportedPlaying);

        bool owned = operation != TransportOp.Ignore || transportHold;
        return new StickPlan
        {
            Operation = operation,
            SwallowLocomotion = unchordedClick || alreadyConsumed || transportHold,
            HoldUntilRelease = owned
        };
    }

    internal static bool TransportOwnsHeldPress(StickPlan plan, bool transportHold)
    {
        return plan.Operation == TransportOp.Ignore && transportHold && plan.HoldUntilRelease;
    }

    internal static bool IkRendererEnabled(bool guidesVisible)
    {
        return guidesVisible;
    }

    internal static bool IkColliderEnabled(bool guidesVisible)
    {
        return true;
    }

    /// <summary>
    /// Value passed to OCIChar.VisibleIKGuide / VisibleFKGuide. Studio turns the
    /// guide colliders off along with the renderers, so the caller has to put the
    /// colliders back afterwards; <see cref="IkColliderEnabled"/> stays true.
    /// </summary>
    internal static bool NativeGuideVisible(bool guidesVisible)
    {
        return guidesVisible;
    }

    /// <summary>
    /// Studio deactivates an unselected guide's axis gizmo on purpose. Re-activating
    /// it would paint arrows on every bone, so visibility only drives renderers.
    /// </summary>
    internal static bool MayActivateHiddenGizmo(bool guidesVisible)
    {
        _ = guidesVisible;
        return false;
    }

    /// <summary>
    /// Renderer state for one guide. Turning the VR switch back on must not reveal
    /// guides Studio itself is hiding, such as those of a hidden character.
    /// </summary>
    internal static bool GuideRendererEnabled(bool guidesVisible, bool studioVisible, bool studioVisibleOutside)
    {
        return guidesVisible && studioVisible && studioVisibleOutside;
    }

    internal enum PoseChain
    {
        None = 0,
        Ik = 1,
        Fk = 2
    }

    /// <summary>
    /// Which kinematic chain has to own a guide before a hand drags it. Without
    /// this the Animator re-evaluates the limb and the bone snaps back next frame.
    /// </summary>
    internal static PoseChain PlanPoseChain(bool isIkGuide, bool isFkGuide)
    {
        if (isIkGuide)
            return PoseChain.Ik;
        if (isFkGuide)
            return PoseChain.Fk;
        return PoseChain.None;
    }

    /// <summary>
    /// True when Studio's whole-character kinematic switch still has to be flipped.
    /// IK and FK are mutually exclusive in Studio, so turning one on tears the other
    /// down. Grabbing an IK target is an explicit request for IK, but a bone the hand
    /// brushed past must never destroy an IK pose the player already built.
    /// </summary>
    internal static bool NeedsKinematicModeSwitch(PoseChain chain, bool ikEnabled, bool fkEnabled)
    {
        if (chain == PoseChain.Ik)
            return !ikEnabled;
        if (chain == PoseChain.Fk)
            return !fkEnabled && !ikEnabled;
        return false;
    }

    /// <summary>
    /// True when the limb's own group flag (LeftLeg, RightArm, ...) is still off.
    /// </summary>
    internal static bool NeedsGroupActivation(PoseChain chain, bool groupActive)
    {
        return chain != PoseChain.None && !groupActive;
    }

    /// <summary>
    /// Figure posing freezes a running clip so a dragged limb is not overwritten
    /// by the next animation frame. A character that is already still is left alone.
    /// </summary>
    internal static bool ShouldFreezeAnimation(bool figurePosing, float animeSpeed)
    {
        return figurePosing && animeSpeed > 0.0001f;
    }

    /// <summary>
    /// Figure posing keeps re-asserting the released pose every frame instead of
    /// writing it once, which is what let DynamicBone and the Animator drag it back.
    /// </summary>
    internal static bool HoldReleasedPose(bool figurePosing)
    {
        return figurePosing;
    }

    /// <summary>
    /// A held pose is dropped the moment the same guide is grabbed again, so the
    /// hold cannot fight the hand that is moving it.
    /// </summary>
    internal static bool KeepHeldPose(bool figurePosing, bool guideGrabbed)
    {
        return figurePosing && !guideGrabbed;
    }

    internal static bool DynamicTouchAllowed(bool enabled)
    {
        return enabled;
    }

    /// <summary>
    /// Chest/hip knead, petting, and slap. Closing physical undress does not stop this.
    /// </summary>
    internal static bool KneadTouchAllowed(bool kneadTouchEnabled, bool physicalUndressEnabled)
    {
        _ = physicalUndressEnabled;
        return kneadTouchEnabled;
    }

    /// <summary>
    /// Hand-pull undress. Closing knead/touch does not stop this, and kneading never removes clothes when this is off.
    /// </summary>
    internal static bool PhysicalUndressAllowed(bool kneadTouchEnabled, bool physicalUndressEnabled)
    {
        _ = kneadTouchEnabled;
        return physicalUndressEnabled;
    }

    internal static bool BakePoseOnRelease(bool figurePosing)
    {
        return figurePosing;
    }

    internal static bool RestituteSoftBody(bool figurePosing, bool dynamicTouch)
    {
        return dynamicTouch && !figurePosing;
    }

    /// <summary>
    /// What has to change on a character before MMDD drives it. Only the IK/FK
    /// switches and the IK targets are covered; clothing, accessories, hair,
    /// heels, and body shape have no field here on purpose.
    /// </summary>
    internal struct MmdPoseSanitizePlan
    {
        public bool DisableIk;
        public bool DisableFk;
        public bool SettlePose;
        public bool ReseatIkTargets;
    }

    /// <summary>
    /// MMDD builds its VMD skeleton from the live bone positions at bind time and
    /// syncs bones in LateUpdate, where Studio's FinalIK and FKCtrl also write.
    /// Both switches therefore go off. Before a bind the Animator pose is settled
    /// so a posed limb is not baked into the dance rest pose. IK targets are
    /// re-seated when IK was on or a VR pose lock may have moved them, so turning
    /// IK back on later starts from the bones instead of the old pose.
    /// </summary>
    internal static MmdPoseSanitizePlan PlanMmdPoseSanitize(
        bool ikEnabled,
        bool fkEnabled,
        int releasedPoseLocks,
        bool beforeBind)
    {
        bool releasedLocks = releasedPoseLocks > 0;
        return new MmdPoseSanitizePlan
        {
            DisableIk = ikEnabled,
            DisableFk = fkEnabled,
            SettlePose = beforeBind && (ikEnabled || fkEnabled || releasedLocks),
            ReseatIkTargets = ikEnabled || releasedLocks
        };
    }

    /// <summary>
    /// A camera-only VMD never binds a character, so nobody's pose is touched.
    /// </summary>
    internal static bool ShouldSanitizeForVmdLoad(bool hasMotionFile)
    {
        return hasMotionFile;
    }

    /// <summary>
    /// Starting playback re-checks the dancers, because a VR drag after the load
    /// turns IK back on. A pause request leaves the paused pose alone.
    /// </summary>
    internal static bool ShouldSanitizeBeforePlayback(bool reportedPlaying)
    {
        return !reportedPlaying;
    }

    /// <summary>
    /// Mirrors the actor selection of the MMDD load script: explicit targets, else
    /// the Studio selection, and a lone character when neither matches.
    /// </summary>
    internal static int[] ResolveMmdMotionTargets(int[] requestedKeys, int[] selectedKeys, int[] liveKeys)
    {
        int[] live = liveKeys ?? new int[0];
        int[] source = requestedKeys != null && requestedKeys.Length > 0
            ? requestedKeys
            : selectedKeys ?? new int[0];
        List<int> result = new List<int>(source.Length);
        foreach (int key in source)
        {
            if (Array.IndexOf(live, key) >= 0 && !result.Contains(key))
                result.Add(key);
        }
        if (result.Count == 0 && live.Length == 1)
            result.Add(live[0]);
        return result.ToArray();
    }
}
