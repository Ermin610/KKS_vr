using System;
using System.Collections.Generic;
using Studio;
using UnityEngine;
using VRGIN.Core;

namespace KKCharaStudioVR;

/// <summary>
/// Makes a hand-dragged Studio guide stick. Writing the bone transform is not
/// enough: Studio only recomputes the limb when the change amount is published,
/// and the Animator overwrites any limb whose IK (or FK) chain is not active.
/// </summary>
internal static class VRIkPosing
{
    private struct Owner
    {
        public OCIChar Character;
        public GuideObject Guide;
        public OIBoneInfo.BoneGroup Group;
        public VRStudioInteractionPolicy.PoseChain Chain;
    }

    internal struct MmdPoseSanitizeResult
    {
        public OCIChar Character;
        public bool DisabledIk;
        public bool DisabledFk;
        public bool ReseatIkTargets;
        public int ReleasedPoseLocks;

        public bool Changed => DisabledIk || DisabledFk || ReleasedPoseLocks > 0;
    }

    private static readonly Dictionary<int, Owner> Owners = new Dictionary<int, Owner>(256);
    private static readonly Dictionary<int, float> MissRetryAt = new Dictionary<int, float>(64);
    private static bool _commitFailureLogged;
    private static bool _activationFailureLogged;
    private static bool _scanFailureLogged;
    private static bool _mmdSanitizeFailureLogged;

    /// <summary>
    /// Clears the posing state that fights an MMD motion: VR figure pose locks on
    /// the character's IK/FK guides and Studio's IK and FK switches. The per-group
    /// IK/FK flags stay as they are, so switching a mode back on restores the
    /// player's group choice. The root guide (placement and figure scale),
    /// clothing, accessories, and body data are not touched.
    /// </summary>
    internal static MmdPoseSanitizeResult SanitizeCharacterForMmd(OCIChar character, bool beforeBind)
    {
        MmdPoseSanitizeResult result = new MmdPoseSanitizeResult { Character = character };
        if (character == null || character.oiCharInfo == null || character.charInfo == null)
            return result;

        OICharInfo info = character.oiCharInfo;
        try
        {
            result.ReleasedPoseLocks = VRFigurePose.ForgetAll(CollectPoseGuides(character));
            VRStudioInteractionPolicy.MmdPoseSanitizePlan plan = VRStudioInteractionPolicy.PlanMmdPoseSanitize(
                info.enableIK,
                info.enableFK,
                result.ReleasedPoseLocks,
                beforeBind);

            if (plan.DisableIk)
            {
                character.ActiveKinematicMode(OICharInfo.KinematicMode.IK, false, true);
                result.DisabledIk = true;
            }
            if (plan.DisableFk)
            {
                character.ActiveKinematicMode(OICharInfo.KinematicMode.FK, false, true);
                result.DisabledFk = true;
            }
            result.ReseatIkTargets = plan.ReseatIkTargets;

            // FinalIK and FKCtrl leave their last result on the bones. MMDD reads
            // those bones right after this call, so the Animator has to write the
            // clean pose now rather than on the next frame.
            if (plan.SettlePose && character.charInfo.animBody != null)
                character.charInfo.animBody.Update(0f);

            if (result.Changed)
            {
                VRLog.Info("Cleared posing state before MMD for "
                    + VRCharacterClothingService.GetCharacterName(character)
                    + ": IK off=" + result.DisabledIk
                    + ", FK off=" + result.DisabledFk
                    + ", pose locks released=" + result.ReleasedPoseLocks);
            }
        }
        catch (Exception ex)
        {
            if (!_mmdSanitizeFailureLogged)
            {
                _mmdSanitizeFailureLogged = true;
                VRLog.Warn("MMD pose sanitize failed: " + ex.Message);
            }
        }
        return result;
    }

    /// <summary>
    /// Moves every IK target onto its bone, the same as Studio's copy-bone action.
    /// </summary>
    internal static void ReseatIkTargets(OCIChar character)
    {
        if (character == null || character.listIKTarget == null)
            return;
        try
        {
            for (int i = 0; i < character.listIKTarget.Count; i++)
            {
                OCIChar.IKInfo ik = character.listIKTarget[i];
                if (ik != null && ik.guideObject != null && ik.guideObject.changeAmount != null)
                    ik.CopyBone();
            }
        }
        catch (Exception ex)
        {
            if (!_mmdSanitizeFailureLogged)
            {
                _mmdSanitizeFailureLogged = true;
                VRLog.Warn("IK target re-seat failed: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// A rolled-back VMD load hands the character its old kinematic modes back.
    /// The IK targets and FK values were not re-seated, so the old pose returns.
    /// </summary>
    internal static void RestoreAfterFailedMmdLoad(MmdPoseSanitizeResult result)
    {
        OCIChar character = result.Character;
        if (character == null || character.oiCharInfo == null || character.charInfo == null)
            return;
        try
        {
            if (result.DisabledFk)
                character.ActiveKinematicMode(OICharInfo.KinematicMode.FK, true, false);
            if (result.DisabledIk)
                character.ActiveKinematicMode(OICharInfo.KinematicMode.IK, true, false);
        }
        catch (Exception ex)
        {
            VRLog.Warn("Could not restore IK/FK after a failed VMD load: " + ex.Message);
        }
    }

    private static HashSet<GuideObject> CollectPoseGuides(OCIChar character)
    {
        HashSet<GuideObject> guides = new HashSet<GuideObject>();
        if (character.listIKTarget != null)
        {
            for (int i = 0; i < character.listIKTarget.Count; i++)
            {
                OCIChar.IKInfo ik = character.listIKTarget[i];
                if (ik != null && ik.guideObject != null)
                    guides.Add(ik.guideObject);
            }
        }
        if (character.listBones != null)
        {
            for (int i = 0; i < character.listBones.Count; i++)
            {
                OCIChar.BoneInfo bone = character.listBones[i];
                if (bone != null && bone.guideObject != null)
                    guides.Add(bone.guideObject);
            }
        }
        return guides;
    }

    /// <summary>
    /// Publishes the change amount Studio's solvers listen to and re-applies it to
    /// the bone. Both halves are needed; writing only the transform leaves the IK
    /// solver on the previous target.
    /// </summary>
    internal static void Commit(GuideObject guide)
    {
        if (guide == null || guide.changeAmount == null || guide.transformTarget == null)
            return;
        try
        {
            guide.changeAmount.OnChange();
            guide.ForceUpdate();
        }
        catch (Exception ex)
        {
            if (_commitFailureLogged)
                return;
            _commitFailureLogged = true;
            VRLog.Warn("Guide change commit failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Called when a hand takes hold of a guide. Hands the limb to the IK or FK
    /// solver and, in figure posing mode, stops the clip that would fight it.
    /// </summary>
    internal static void BeginDrag(GuideObject guide)
    {
        VRFigurePose.Forget(guide);
        VRFigureScale.Forget(guide);
        EnsureChainActive(guide);
    }

    /// <summary>
    /// Activates the chain that owns <paramref name="guide"/> if Studio still has
    /// it switched off. Safe to call every frame; the owner lookup is cached.
    /// </summary>
    internal static void EnsureChainActive(GuideObject guide)
    {
        Owner owner;
        if (!TryResolveOwner(guide, out owner))
            return;
        OCIChar character = owner.Character;
        OICharInfo info = character.oiCharInfo;
        if (info == null)
            return;

        try
        {
            if (VRStudioInteractionPolicy.NeedsKinematicModeSwitch(owner.Chain, info.enableIK, info.enableFK))
            {
                character.ActiveKinematicMode(
                    owner.Chain == VRStudioInteractionPolicy.PoseChain.Ik
                        ? OICharInfo.KinematicMode.IK
                        : OICharInfo.KinematicMode.FK,
                    true,
                    false);
                VRLog.Info("Enabled " + owner.Chain + " kinematics so a dragged limb is not reset by the Animator");
            }

            if (VRStudioInteractionPolicy.NeedsGroupActivation(owner.Chain, IsGroupActive(character, owner)))
            {
                if (owner.Chain == VRStudioInteractionPolicy.PoseChain.Ik)
                    character.ActiveIK(owner.Group, true, false);
                else
                    character.ActiveFK(owner.Group, true, false);
                VRLog.Info("Activated " + owner.Chain + " group " + owner.Group + " for VR posing");
            }

            FreezeAnimationIfPosing(character);
        }
        catch (Exception ex)
        {
            if (_activationFailureLogged)
                return;
            _activationFailureLogged = true;
            VRLog.Warn("IK/FK activation for VR posing failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Figure posing holds a limb where the hand left it, so a clip that keeps
    /// driving the body has to stop. The character's own speed slider restores it.
    /// </summary>
    internal static void FreezeAnimationIfPosing(OCIChar character)
    {
        if (character == null || character.oiCharInfo == null)
            return;
        if (!VRStudioInteractionPolicy.ShouldFreezeAnimation(
                VRInteractionOptions.FigurePosingMode, character.oiCharInfo.animeSpeed))
            return;
        character.animeSpeed = 0f;
        VRLog.Info("Figure posing froze the character animation so the pose cannot be overwritten");
    }

    internal static void FreezeAnimationIfPosing(GuideObject guide)
    {
        Owner owner;
        if (TryResolveOwner(guide, out owner))
            FreezeAnimationIfPosing(owner.Character);
    }

    internal static void ForgetOwner(GuideObject guide)
    {
        if (guide == null)
            return;
        int id = guide.GetInstanceID();
        Owners.Remove(id);
        MissRetryAt.Remove(id);
    }

    private static bool IsGroupActive(OCIChar character, Owner owner)
    {
        OICharInfo info = character.oiCharInfo;
        bool[] flags = owner.Chain == VRStudioInteractionPolicy.PoseChain.Ik ? info.activeIK : info.activeFK;
        if (flags == null)
            return false;
        int index = GroupIndex(owner.Chain, owner.Group);
        if (index < 0 || index >= flags.Length)
            return false;
        return flags[index];
    }

    private static int GroupIndex(VRStudioInteractionPolicy.PoseChain chain, OIBoneInfo.BoneGroup group)
    {
        if (chain == VRStudioInteractionPolicy.PoseChain.Ik)
        {
            // Studio indexes the IK flags by bit position: Body, RightLeg, LeftLeg,
            // RightArm, LeftArm.
            for (int i = 0; i < 5; i++)
            {
                if ((int)group == 1 << i)
                    return i;
            }
            return -1;
        }

        OIBoneInfo.BoneGroup[] parts = FKCtrl.parts;
        if (parts == null)
            return -1;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == group)
                return i;
        }
        return -1;
    }

    private static bool TryResolveOwner(GuideObject guide, out Owner owner)
    {
        owner = default(Owner);
        if (guide == null || guide.transformTarget == null)
            return false;

        int id = guide.GetInstanceID();
        Owner cached;
        if (Owners.TryGetValue(id, out cached))
        {
            if (cached.Character != null && cached.Guide == guide)
            {
                owner = cached;
                return true;
            }
            Owners.Remove(id);
        }

        // A guide that belongs to an item, a camera, or a folder never resolves.
        // Rescanning every character for it on every frame of a drag is wasted work.
        float retryAt;
        if (MissRetryAt.TryGetValue(id, out retryAt) && Time.unscaledTime < retryAt)
            return false;

        if (!Scan(guide, out owner))
        {
            MissRetryAt[id] = Time.unscaledTime + 2f;
            return false;
        }

        MissRetryAt.Remove(id);
        Owners[id] = owner;
        return true;
    }

    private static bool Scan(GuideObject guide, out Owner owner)
    {
        owner = default(Owner);
        Studio.Studio studio = Singleton<Studio.Studio>.Instance;
        if (studio == null || studio.dicObjectCtrl == null)
            return false;

        try
        {
            foreach (KeyValuePair<int, ObjectCtrlInfo> pair in studio.dicObjectCtrl)
            {
                OCIChar character = pair.Value as OCIChar;
                if (character == null)
                    continue;

                if (character.listIKTarget != null)
                {
                    for (int i = 0; i < character.listIKTarget.Count; i++)
                    {
                        OCIChar.IKInfo ik = character.listIKTarget[i];
                        if (ik == null || ik.guideObject != guide)
                            continue;
                        owner = new Owner
                        {
                            Character = character,
                            Guide = guide,
                            Group = ik.boneGroup,
                            Chain = VRStudioInteractionPolicy.PlanPoseChain(true, false)
                        };
                        return true;
                    }
                }

                if (character.listBones == null)
                    continue;
                for (int i = 0; i < character.listBones.Count; i++)
                {
                    OCIChar.BoneInfo bone = character.listBones[i];
                    if (bone == null || bone.guideObject != guide)
                        continue;
                    owner = new Owner
                    {
                        Character = character,
                        Guide = guide,
                        Group = bone.boneGroup,
                        Chain = VRStudioInteractionPolicy.PlanPoseChain(false, true)
                    };
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            if (!_scanFailureLogged)
            {
                _scanFailureLogged = true;
                VRLog.Warn("Guide owner scan failed: " + ex.Message);
            }
            return false;
        }
        return false;
    }
}
