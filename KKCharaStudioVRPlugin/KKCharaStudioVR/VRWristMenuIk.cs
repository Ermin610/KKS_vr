using UnityEngine;

namespace KKCharaStudioVR;

public sealed partial class VRWristMenuController
{
    private const float IkButtonX = 24f;
    private const float IkButtonWidth = 512f;
    private const float IkButtonHeight = 54f;
    private const float IkButtonGap = 6f;
    private const float IkButtonStartY = 56f;

    private GameObject _ikPage;
    private VRWristMenuButtonTarget _ikGuideButton;
    private VRWristMenuButtonTarget _kneadTouchButton;
    private VRWristMenuButtonTarget _physicalUndressButton;
    private VRWristMenuButtonTarget _figurePoseButton;

    private void BuildIkPage()
    {
        CreateButton(
            "IkBack",
            "<",
            18f,
            10f,
            new Color(1f, 1f, 1f, 0.08f),
            new Color(1f, 1f, 1f, 0.2f),
            HandleBackToRoot,
            _ikPage.transform,
            58f,
            38f,
            28);
        CreateText(
            "IkTitle",
            _ikPage.transform,
            L("IK 互动", "IK インタラクト", "IK interact"),
            92f,
            10f,
            440f,
            40f,
            28,
            TextAnchor.MiddleLeft,
            Color.white);

        float y = IkButtonStartY;
        _ikGuideButton = CreateButton(
            "IkGuideVisibility",
            L("IK 小绿球显示：开", "IK ガイド表示：オン", "IK guides: on"),
            IkButtonX,
            y,
            new Color(0.075f, 0.24f, 0.14f, 0.5f),
            new Color(0.11f, 0.46f, 0.24f, 0.78f),
            HandleToggleIkVisibility,
            _ikPage.transform,
            IkButtonWidth,
            IkButtonHeight,
            16);
        y += IkButtonHeight + IkButtonGap;
        _kneadTouchButton = CreateButton(
            "KneadTouch",
            L("抓取揉捏：开", "揉み掴み：オン", "Grab knead: on"),
            IkButtonX,
            y,
            new Color(0.18f, 0.1f, 0.22f, 0.52f),
            new Color(0.46f, 0.18f, 0.42f, 0.78f),
            HandleToggleKneadTouch,
            _ikPage.transform,
            IkButtonWidth,
            IkButtonHeight,
            16);
        y += IkButtonHeight + IkButtonGap;
        _physicalUndressButton = CreateButton(
            "PhysicalUndress",
            L("物理脱衣：开", "物理脱衣：オン", "Physical undress: on"),
            IkButtonX,
            y,
            new Color(0.28f, 0.1f, 0.12f, 0.52f),
            new Color(0.62f, 0.18f, 0.22f, 0.78f),
            HandleTogglePhysicalUndress,
            _ikPage.transform,
            IkButtonWidth,
            IkButtonHeight,
            16);
        y += IkButtonHeight + IkButtonGap;
        _figurePoseButton = CreateButton(
            "FigurePosing",
            L("手办定型模式：关", "フィギュア固定：オフ", "Figure posing: off"),
            IkButtonX,
            y,
            new Color(0.28f, 0.2f, 0.07f, 0.5f),
            new Color(0.55f, 0.36f, 0.1f, 0.76f),
            HandleToggleFigurePosing,
            _ikPage.transform,
            IkButtonWidth,
            IkButtonHeight,
            16);
        y += IkButtonHeight + IkButtonGap;
        CreateButton(
            "OpenFigureScale",
            L(
                "手办缩放与尺寸  ›\n滑条与 1/1、1/4、1/6、1/7、1/12 预设",
                "フィギュアサイズ  ›\nスライダーと 1/1・1/4・1/6・1/7・1/12",
                "Figure scale and size  ›\nSlider and 1/1, 1/4, 1/6, 1/7, 1/12 presets"),
            IkButtonX,
            y,
            new Color(0.12f, 0.17f, 0.3f, 0.5f),
            new Color(0.18f, 0.36f, 0.62f, 0.8f),
            HandleOpenFigureScalePage,
            _ikPage.transform,
            IkButtonWidth,
            IkButtonHeight,
            16);
        y += IkButtonHeight + 4f;
        CreateText(
            "IkHint",
            _ikPage.transform,
            L(
                "小绿球关闭只隐藏外观，关节仍可抓取。抓取揉捏管胸臀揉捏、抚摸和拍打。物理脱衣单独控制拉扯脱衣，关闭后怎么揉都不会掉衣服。手办定型让松手姿势固定，软体不再回弹。",
                "ガイドを消しても関節は掴めます。揉み掴みは胸と尻の揉む・撫でる・叩くです。物理脱衣は引っ張って脱ぐ操作で、オフなら揉んでも服は落ちません。フィギュア固定は離した姿勢を維持します。",
                "Hiding guides only hides the spheres. Grab knead covers chest and hip kneading, petting, and slaps. Physical undress is separate: when it is off, pulls never remove clothes. Figure posing keeps the released pose."),
            IkButtonX,
            y,
            IkButtonWidth,
            408f - y,
            12,
            TextAnchor.UpperLeft,
            SecondaryTextColor);
    }

    private void ClearIkMenuReferences()
    {
        _ikPage = null;
        _ikGuideButton = null;
        _kneadTouchButton = null;
        _physicalUndressButton = null;
        _figurePoseButton = null;
    }

    private void HandleOpenIkPage()
    {
        ShowPage(WristMenuPage.Ik);
        SetStatus(L("IK 互动", "IK インタラクト", "IK interact"), new Color(0.47f, 0.9f, 0.55f, 1f), 0f);
    }

    private void RefreshIkPage()
    {
        VRQuickActions.PullIkGuideVisible();
        if (_ikGuideButton != null)
        {
            _ikGuideButton.SetLabel(VRQuickActions.ikVisible
                ? L("IK 小绿球显示：开\n只隐藏外观，抓取仍然有效", "IK ガイド表示：オン\n非表示でも掴める", "IK guides: on\nHiding them still allows grabs")
                : L("IK 小绿球显示：关\n关节和骨骼仍可抓取摆放", "IK ガイド表示：オフ\n関節は掴んだまま置ける", "IK guides: off\nJoints can still be grabbed"));
        }
        if (_kneadTouchButton != null)
        {
            _kneadTouchButton.SetLabel(VRInteractionOptions.DynamicTouchEnabled
                ? L("抓取揉捏：开\n胸臀揉捏、抚摸与拍打", "揉み掴み：オン\n胸と尻を揉む・撫でる・叩く", "Grab knead: on\nKnead, pet, and slap")
                : L("抓取揉捏：关\n揉捏、抚摸与拍打停止", "揉み掴み：オフ\n揉む・撫でる・叩くを停止", "Grab knead: off\nKnead, pet, and slap stop"));
        }
        if (_physicalUndressButton != null)
        {
            _physicalUndressButton.SetLabel(VRInteractionOptions.PhysicalUndressEnabled
                ? L("物理脱衣：开\n拉扯衣物可以脱衣", "物理脱衣：オン\n服を引っ張ると脱げる", "Physical undress: on\nPulling clothes removes them")
                : L("物理脱衣：关\n揉捏拉扯都不会掉衣服", "物理脱衣：オフ\n揉んでも服は落ちない", "Physical undress: off\nPulls never remove clothes"));
        }
        if (_figurePoseButton != null)
        {
            _figurePoseButton.SetLabel(VRInteractionOptions.FigurePosingMode
                ? L("手办定型模式：开\n松手后保持姿势，不再回弹", "フィギュア固定：オン\n離してもポーズを維持", "Figure posing: on\nThe released pose stays")
                : L("手办定型模式：关\n胸部和臀部松手后柔和回弹", "フィギュア固定：オフ\n胸と尻は柔らかく戻る", "Figure posing: off\nChest and hip spring back"));
        }
    }

    // These toggles used to change the in-memory settings only, so they were
    // lost on restart unless another menu happened to save the file later.
    private static void TrySaveToggledSettings(KKCharaStudioVRSettings settings)
    {
        try
        {
            settings.Save();
        }
        catch (System.Exception exception)
        {
            VRGIN.Core.VRLog.Error("Unable to save VR settings: " + exception.Message);
        }
    }

    private void HandleToggleKneadTouch()
    {
        KKCharaStudioVRSettings settings = VRInteractionOptions.Settings;
        if (settings == null)
        {
            SetStatus(L("设置尚未就绪", "設定がまだ準備できていません", "Settings are not ready"), new Color(1f, 0.38f, 0.34f, 1f), 4f);
            return;
        }
        settings.DynamicTouchEnabled = !settings.DynamicTouchEnabled;
        TrySaveToggledSettings(settings);
        RefreshIkPage();
        SetStatus(
            settings.DynamicTouchEnabled
                ? L("抓取揉捏已开启", "揉み掴みをオンにしました", "Grab knead is on")
                : L("抓取揉捏已关闭", "揉み掴みをオフにしました", "Grab knead is off"),
            new Color(0.35f, 1f, 0.62f, 1f),
            4f);
    }

    private void HandleTogglePhysicalUndress()
    {
        KKCharaStudioVRSettings settings = VRInteractionOptions.Settings;
        if (settings == null)
        {
            SetStatus(L("设置尚未就绪", "設定がまだ準備できていません", "Settings are not ready"), new Color(1f, 0.38f, 0.34f, 1f), 4f);
            return;
        }
        settings.PhysicalUndressEnabled = !settings.PhysicalUndressEnabled;
        TrySaveToggledSettings(settings);
        RefreshIkPage();
        SetStatus(
            settings.PhysicalUndressEnabled
                ? L("物理脱衣已开启", "物理脱衣をオンにしました", "Physical undress is on")
                : L("物理脱衣已关闭，揉捏不会掉衣服", "物理脱衣をオフにしました。揉んでも服は落ちません", "Physical undress is off; kneading will not remove clothes"),
            new Color(0.35f, 1f, 0.62f, 1f),
            4f);
    }

    private void HandleToggleFigurePosing()
    {
        KKCharaStudioVRSettings settings = VRInteractionOptions.Settings;
        if (settings == null)
        {
            SetStatus(L("设置尚未就绪", "設定がまだ準備できていません", "Settings are not ready"), new Color(1f, 0.38f, 0.34f, 1f), 4f);
            return;
        }
        settings.FigurePosingMode = !settings.FigurePosingMode;
        TrySaveToggledSettings(settings);
        RefreshIkPage();
        SetStatus(
            settings.FigurePosingMode
                ? L("手办定型已开启，松手后保持姿势", "フィギュア固定をオンにしました", "Figure posing is on")
                : L("手办定型已关闭，软体恢复回弹", "フィギュア固定をオフにしました", "Figure posing is off"),
            new Color(0.35f, 1f, 0.62f, 1f),
            4f);
    }
}
