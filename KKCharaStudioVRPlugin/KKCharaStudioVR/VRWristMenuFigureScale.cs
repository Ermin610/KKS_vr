using UnityEngine;
using UnityEngine.UI;
using Valve.VR;
using VRGIN.Core;

namespace KKCharaStudioVR;

public sealed partial class VRWristMenuController
{
    private const float FigureScaleSliderX = 24f;
    private const float FigureScaleSliderY = 150f;
    private const float FigureScaleSliderWidth = 512f;
    private const float FigureScaleSliderHeight = 44f;
    private const float FigureScaleTrackInset = 14f;

    private GameObject _figureScalePage;
    private VRWristMenuButtonTarget _figureScaleTargetButton;
    private VRWristMenuButtonTarget _figureScaleSlider;
    private Text _figureScaleValueText;
    private RectTransform _figureScaleFill;
    private RectTransform _figureScaleThumb;
    private readonly VRWristMenuButtonTarget[] _figureScalePresetButtons =
        new VRWristMenuButtonTarget[VRFigureScalePolicy.PresetDenominators.Length];
    private int _figureScaleTargetKey = -1;
    private Studio.OCIChar _figureScaleDragCharacter;
    private bool _figureScaleDragging;
    private float _nextFigureScaleRefresh;

    private void BuildFigureScalePage()
    {
        CreateButton(
            "FigureScaleBack",
            "<",
            18f,
            10f,
            new Color(1f, 1f, 1f, 0.08f),
            new Color(1f, 1f, 1f, 0.2f),
            HandleOpenIkPage,
            _figureScalePage.transform,
            58f,
            38f,
            28);
        CreateText(
            "FigureScaleTitle",
            _figureScalePage.transform,
            L("手办缩放", "フィギュアサイズ", "Figure scale"),
            92f,
            10f,
            440f,
            40f,
            28,
            TextAnchor.MiddleLeft,
            Color.white);

        _figureScaleTargetButton = CreateButton(
            "FigureScaleTarget",
            L("选择场景角色  ›", "シーンのキャラを選択  ›", "Select a scene character  ›"),
            24f,
            64f,
            new Color(0.075f, 0.24f, 0.14f, 0.48f),
            new Color(0.11f, 0.46f, 0.24f, 0.76f),
            HandleChooseFigureScaleTarget,
            _figureScalePage.transform,
            512f,
            40f,
            16);

        _figureScaleValueText = CreateText(
            "FigureScaleValue",
            _figureScalePage.transform,
            "-",
            24f,
            110f,
            512f,
            34f,
            22,
            TextAnchor.MiddleCenter,
            PrimaryTextColor);

        _figureScaleSlider = CreateButton(
            "FigureScaleSlider",
            string.Empty,
            FigureScaleSliderX,
            FigureScaleSliderY,
            new Color(0.12f, 0.17f, 0.3f, 0.5f),
            new Color(0.18f, 0.36f, 0.62f, 0.8f),
            null,
            _figureScalePage.transform,
            FigureScaleSliderWidth,
            FigureScaleSliderHeight,
            14);
        Transform sliderSurface = _figureScaleSlider.transform.parent;
        float trackWidth = FigureScaleSliderWidth - FigureScaleTrackInset * 2f;
        CreateImage(
            "FigureScaleTrack",
            sliderSurface,
            FigureScaleTrackInset,
            FigureScaleSliderHeight * 0.5f - 3f,
            trackWidth,
            6f,
            new Color(0.72f, 0.84f, 0.94f, 0.18f),
            false);
        _figureScaleFill = CreateImage(
            "FigureScaleFill",
            sliderSurface,
            FigureScaleTrackInset,
            FigureScaleSliderHeight * 0.5f - 3f,
            0f,
            6f,
            new Color(0.35f, 0.9f, 0.94f, 0.8f),
            false).rectTransform;
        _figureScaleThumb = CreateImage(
            "FigureScaleThumb",
            sliderSurface,
            FigureScaleTrackInset - 7f,
            6f,
            14f,
            FigureScaleSliderHeight - 12f,
            new Color(0.92f, 0.97f, 1f, 0.95f)).rectTransform;

        CreateFigureScaleTick("Min", VRFigureScalePolicy.MinScale, "0.05x");
        CreateFigureScaleTick("Life", 1f, "1x");
        CreateFigureScaleTick("Max", VRFigureScalePolicy.MaxScale, "2x");

        string[] stepLabels = { "−10%", "−2%", "+2%", "+10%" };
        int[] stepDirections = { -1, -1, 1, 1 };
        bool[] stepCoarse = { true, false, false, true };
        for (int index = 0; index < stepLabels.Length; index++)
        {
            int direction = stepDirections[index];
            bool coarse = stepCoarse[index];
            CreateButton(
                "FigureScaleStep" + index,
                stepLabels[index],
                24f + index * 130f,
                218f,
                new Color(0.12f, 0.16f, 0.2f, 0.56f),
                new Color(0.22f, 0.34f, 0.42f, 0.8f),
                () => HandleFigureScaleStep(direction, coarse),
                _figureScalePage.transform,
                122f,
                42f,
                17);
        }

        CreateText(
            "FigureScaleSection",
            _figureScalePage.transform,
            L("经典手办比例", "定番フィギュアスケール", "Classic figure scales"),
            24f,
            270f,
            512f,
            20f,
            17,
            TextAnchor.MiddleLeft,
            new Color(1f, 0.72f, 0.25f, 1f));
        string[] presetCaptions =
        {
            L("等身", "等身大", "Life size"),
            L("大比例雕像", "大型スタチュー", "Statue"),
            L("12寸兵人", "12インチ", "12-inch"),
            L("主流手办", "定番フィギュア", "Anime figure"),
            L("粘土人/figma", "ねんどろ/figma", "Nendo/figma")
        };
        for (int index = 0; index < VRFigureScalePolicy.PresetDenominators.Length; index++)
        {
            int denominator = VRFigureScalePolicy.PresetDenominators[index];
            _figureScalePresetButtons[index] = CreateButton(
                "FigureScalePreset" + denominator,
                "1/" + denominator + "\n" + presetCaptions[index],
                24f + index * 104f,
                294f,
                new Color(0.28f, 0.2f, 0.07f, 0.46f),
                new Color(0.55f, 0.36f, 0.1f, 0.74f),
                () => HandleFigureScalePreset(denominator),
                _figureScalePage.transform,
                96f,
                58f,
                14);
        }

        CreateText(
            "FigureScaleHint",
            _figureScalePage.transform,
            L(
                "以脚底为基准缩放。手办定型模式开启时，比例会和摆好的姿势一起锁定。",
                "足元を基準に拡縮します。フィギュア固定がオンならサイズもポーズと一緒に固定されます。",
                "Scales from the feet. With figure posing on, the size is locked together with the pose."),
            24f,
            358f,
            512f,
            48f,
            13,
            TextAnchor.UpperLeft,
            SecondaryTextColor);
    }

    private void CreateFigureScaleTick(string name, float scale, string label)
    {
        float trackWidth = FigureScaleSliderWidth - FigureScaleTrackInset * 2f;
        float x = FigureScaleSliderX + FigureScaleTrackInset
            + VRFigureScalePolicy.ToSlider(scale) * trackWidth;
        CreateText(
            "FigureScaleTick" + name,
            _figureScalePage.transform,
            label,
            x - 30f,
            FigureScaleSliderY + FigureScaleSliderHeight + 1f,
            60f,
            16f,
            11,
            TextAnchor.MiddleCenter,
            TertiaryTextColor);
    }

    private void ClearFigureScaleMenuReferences()
    {
        _figureScalePage = null;
        _figureScaleTargetButton = null;
        _figureScaleSlider = null;
        _figureScaleValueText = null;
        _figureScaleFill = null;
        _figureScaleThumb = null;
        for (int i = 0; i < _figureScalePresetButtons.Length; i++)
            _figureScalePresetButtons[i] = null;
        _figureScaleDragging = false;
        _figureScaleDragCharacter = null;
    }

    private void HandleOpenFigureScalePage()
    {
        ShowPage(WristMenuPage.FigureScale);
        SetStatus(
            L("选中角色后拖动滑条或点击比例", "キャラを選んでスライダーかスケールを押す", "Pick a character, then drag the slider or press a scale"),
            new Color(1f, 0.72f, 0.25f, 1f),
            0f);
    }

    /// <summary>
    /// The Studio selection wins so grabbing or clicking a character in the scene
    /// retargets the page. The last character picked here is the fallback.
    /// </summary>
    private bool TryGetFigureScaleTarget(out VRVmdActorTarget target, out string status)
    {
        target = null;
        int[] selected = VRVmdTargetService.GetSelectedObjectKeys();
        if (selected.Length > 0
            && VRVmdTargetService.TryGetTarget(selected[0], out target, out status))
        {
            _figureScaleTargetKey = target.ObjectKey;
            return true;
        }
        if (_figureScaleTargetKey >= 0
            && VRVmdTargetService.TryGetTarget(_figureScaleTargetKey, out target, out status))
        {
            return true;
        }
        _figureScaleTargetKey = -1;
        status = L("请先选中要缩放的角色", "拡縮するキャラを選択してください", "Select the character to scale first");
        return false;
    }

    private void RefreshFigureScalePage()
    {
        VRVmdActorTarget target;
        string status;
        bool hasTarget = TryGetFigureScaleTarget(out target, out status);
        float scale = 1f;
        bool hasScale = hasTarget && VRFigureScale.TryGetScale(target.Character, out scale);

        if (_figureScaleTargetButton != null)
        {
            _figureScaleTargetButton.SetLabel(hasTarget
                ? L("当前角色  ", "現在のキャラ  ", "Current character  ") + target.DisplayName + "  ›"
                : L("选择场景角色  ›", "シーンのキャラを選択  ›", "Select a scene character  ›"));
        }
        if (_figureScaleSlider != null)
            _figureScaleSlider.SetInteractable(hasScale);
        UpdateFigureScaleVisuals(hasScale, scale);
    }

    private void UpdateFigureScaleVisuals(bool hasScale, float scale)
    {
        int preset = hasScale ? VRFigureScalePolicy.MatchPreset(scale) : 0;
        if (_figureScaleValueText != null)
        {
            _figureScaleValueText.text = !hasScale
                ? L("未选择角色", "キャラ未選択", "No character selected")
                : scale.ToString("0.000") + "x   ·   "
                    + (preset > 0 ? "1/" + preset : "≈ " + VRFigureScalePolicy.FormatRatio(scale));
            _figureScaleValueText.color = hasScale
                ? PrimaryTextColor
                : new Color(1f, 0.48f, 0.4f, 1f);
        }

        float trackWidth = FigureScaleSliderWidth - FigureScaleTrackInset * 2f;
        float fraction = hasScale ? VRFigureScalePolicy.ToSlider(scale) : 0f;
        if (_figureScaleFill != null)
            _figureScaleFill.sizeDelta = new Vector2(trackWidth * fraction, _figureScaleFill.sizeDelta.y);
        if (_figureScaleThumb != null)
        {
            _figureScaleThumb.anchoredPosition = new Vector2(
                FigureScaleTrackInset - 7f + trackWidth * fraction,
                _figureScaleThumb.anchoredPosition.y);
            _figureScaleThumb.gameObject.SetActive(hasScale);
        }

        for (int index = 0; index < _figureScalePresetButtons.Length; index++)
        {
            VRWristMenuButtonTarget button = _figureScalePresetButtons[index];
            if (button == null)
                continue;
            bool active = preset == VRFigureScalePolicy.PresetDenominators[index];
            button.SetColors(
                active ? new Color(0.36f, 0.62f, 0.2f, 0.62f) : new Color(0.28f, 0.2f, 0.07f, 0.46f),
                active ? new Color(0.45f, 0.78f, 0.26f, 0.82f) : new Color(0.55f, 0.36f, 0.1f, 0.74f));
            button.SetInteractable(hasScale);
        }
    }

    private void UpdateFigureScalePage()
    {
        if (_page != WristMenuPage.FigureScale || _figureScaleDragging)
            return;
        if (Time.unscaledTime < _nextFigureScaleRefresh)
            return;
        _nextFigureScaleRefresh = Time.unscaledTime + 0.2f;
        RefreshFigureScalePage();
    }

    /// <summary>
    /// The slider follows the trigger while it is held, even when the ray slides
    /// off the track vertically. Only the horizontal position matters.
    /// </summary>
    private bool UpdateFigureScaleSliderDrag(
        SteamVR_Controller.Device rightDevice,
        VRWristMenuButtonTarget target,
        bool hasMenuSurfaceHit,
        Vector3 menuSurfacePoint)
    {
        if (_page != WristMenuPage.FigureScale || _figureScaleSlider == null)
        {
            EndFigureScaleSliderDrag();
            return false;
        }

        if (!_figureScaleDragging)
        {
            if (target != _figureScaleSlider || !rightDevice.GetPressDown(EVRButtonId.k_EButton_Axis1))
                return false;
            VRVmdActorTarget actor;
            string status;
            if (!TryGetFigureScaleTarget(out actor, out status))
            {
                SetStatus(status, new Color(1f, 0.38f, 0.34f, 1f), 4f);
                return true;
            }
            _figureScaleDragging = true;
            _figureScaleDragCharacter = actor.Character;
        }

        if (!rightDevice.GetPress(EVRButtonId.k_EButton_Axis1))
        {
            EndFigureScaleSliderDrag();
            return true;
        }
        if (!hasMenuSurfaceHit)
            return true;

        RectTransform surface = _figureScaleSlider.transform.parent as RectTransform;
        if (surface == null)
            return true;
        Vector3 local = surface.InverseTransformPoint(menuSurfacePoint);
        Rect rect = surface.rect;
        float fraction = VRFigureScalePolicy.SliderFraction(
            local.x,
            rect.xMin + FigureScaleTrackInset,
            rect.width - FigureScaleTrackInset * 2f);
        float scale = VRFigureScalePolicy.FromSlider(fraction);
        string requestStatus;
        if (VRFigureScale.Request(_figureScaleDragCharacter, scale, out requestStatus))
        {
            UpdateFigureScaleVisuals(true, scale);
            SetStatus(requestStatus, new Color(0.35f, 1f, 0.62f, 1f), 3f);
        }
        else
        {
            SetStatus(requestStatus, new Color(1f, 0.38f, 0.34f, 1f), 4f);
            EndFigureScaleSliderDrag();
        }
        return true;
    }

    private void EndFigureScaleSliderDrag()
    {
        if (!_figureScaleDragging)
            return;
        _figureScaleDragging = false;
        if (_figureScaleDragCharacter != null)
            VRFigureScale.CommitEdit(_figureScaleDragCharacter);
        _figureScaleDragCharacter = null;
        _nextFigureScaleRefresh = 0f;
    }

    private void HandleFigureScalePreset(int denominator)
    {
        ApplyFigureScale(current => VRFigureScalePolicy.PresetScale(denominator));
    }

    private void HandleFigureScaleStep(int direction, bool coarse)
    {
        ApplyFigureScale(current => VRFigureScalePolicy.Step(current, direction, coarse));
    }

    private void ApplyFigureScale(System.Func<float, float> next)
    {
        VRVmdActorTarget target;
        string status;
        float current;
        bool success = TryGetFigureScaleTarget(out target, out status)
            && VRFigureScale.TryGetScale(target.Character, out current)
            && VRFigureScale.Request(target.Character, next(current), out status);
        if (success)
        {
            VRFigureScale.CommitEdit(target.Character);
            VRLog.Info("Figure scale: " + status);
        }
        SetStatus(
            status,
            success ? new Color(0.35f, 1f, 0.62f, 1f) : new Color(1f, 0.38f, 0.34f, 1f),
            4f);
        RefreshFigureScalePage();
    }

    private void HandleChooseFigureScaleTarget()
    {
        string status;
        var targets = VRVmdTargetService.GetAllTargets(out status);
        if (targets.Count == 0)
        {
            SetStatus(status ?? "当前场景没有角色", new Color(1f, 0.38f, 0.34f, 1f), 7f);
            return;
        }

        OpenActorTargetBrowser(
            BrowserMode.SelectFigureScaleActor,
            targets,
            L("请选择要缩放的场景角色", "拡縮するキャラを選択", "Select the character to scale"),
            out status);
    }

    private void HandleFigureScaleActorSelection(VRWristFileEntry entry)
    {
        if (!entry.IsActorTarget)
            return;
        string status;
        if (!VRVmdTargetService.TrySelectTarget(entry.ObjectKey, out status))
        {
            SetStatus(status, new Color(1f, 0.38f, 0.34f, 1f), 7f);
            return;
        }
        _figureScaleTargetKey = entry.ObjectKey;
        ShowPage(WristMenuPage.FigureScale);
        SetStatus(
            L("缩放目标：", "拡縮対象：", "Scale target: ") + NormalizeUiSingleLine(entry.DisplayName),
            new Color(0.35f, 1f, 0.62f, 1f),
            0f);
    }
}
