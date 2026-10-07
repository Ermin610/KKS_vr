using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Valve.VR;
using VRGIN.Core;

namespace KKCharaStudioVR;

public sealed partial class VRWristMenuController
{
    private const int DanceVisibleRows = 6;
    private const float DanceStickThreshold = 0.55f;
    private const float DanceStickReleaseThreshold = 0.2f;

    private GameObject _mmdDancePage;
    private readonly VRWristMenuButtonTarget[] _mmdDanceEntryButtons = new VRWristMenuButtonTarget[DanceVisibleRows];
    private Text _mmdDanceTitleText;
    private Text _mmdDanceSummaryText;
    private Text _mmdDanceScrollText;
    private VRWristMenuButtonTarget _mmdDanceCategoryPrevButton;
    private VRWristMenuButtonTarget _mmdDanceCategoryCurrentButton;
    private VRWristMenuButtonTarget _mmdDanceCategoryNextButton;
    private VRWristMenuButtonTarget _mmdDanceFavoriteFilterButton;
    private VRWristMenuButtonTarget _mmdDanceSortButton;
    private VRWristMenuButtonTarget _mmdDanceScanButton;

    // Quick Rating Controls in MmdPlayback page
    private Text _playbackDanceRatingInfoText;
    private VRWristMenuButtonTarget _playbackDanceRatingMinusButton;
    private VRWristMenuButtonTarget _playbackDanceRatingPlusButton;
    private VRWristMenuButtonTarget _playbackDanceRatingFavoriteButton;

    private List<VRMmdDanceRatingEntry> _danceFilteredEntries = new List<VRMmdDanceRatingEntry>();
    private int _danceCategoryIndex = 0;
    private bool _danceFavoriteOnly = false;
    private bool _danceSortByRatingDesc = true;
    private int _danceOffset = 0;
    private int _danceStickDirection = 0;
    private float _nextDanceStickScroll = 0f;

    private void BuildMmdDancePage()
    {
        CreateButton(
            "MmdDanceBack",
            "<",
            18f,
            10f,
            new Color(1f, 1f, 1f, 0.08f),
            new Color(1f, 1f, 1f, 0.2f),
            HandleBackToMmdPlayback,
            _mmdDancePage.transform,
            58f,
            38f,
            28);

        _mmdDanceTitleText = CreateText(
            "MmdDanceTitle",
            _mmdDancePage.transform,
            L("MMD 智能选舞与评分", "MMD おすすめダンス選曲", "MMD Smart Dance Select"),
            86f,
            10f,
            240f,
            38f,
            24,
            TextAnchor.MiddleLeft,
            Color.white);

        _mmdDanceSortButton = CreateButton(
            "MmdDanceSort",
            L("9★->1★ 降序", "9★->1★ 降順", "9★->1★ Desc"),
            332f,
            10f,
            new Color(0.24f, 0.18f, 0.08f, 0.6f),
            new Color(0.48f, 0.36f, 0.12f, 0.85f),
            HandleMmdDanceSortToggle,
            _mmdDancePage.transform,
            116f,
            38f,
            14);

        _mmdDanceScanButton = CreateButton(
            "MmdDanceScan",
            L("扫描新舞", "再スキャン", "Scan"),
            454f,
            10f,
            new Color(0.12f, 0.16f, 0.2f, 0.56f),
            new Color(0.22f, 0.34f, 0.42f, 0.8f),
            HandleMmdDanceScan,
            _mmdDancePage.transform,
            82f,
            38f,
            14);

        // Category Filter Bar (y = 54)
        _mmdDanceCategoryPrevButton = CreateButton(
            "MmdDanceCatPrev",
            "◀",
            24f,
            54f,
            new Color(0.12f, 0.18f, 0.24f, 0.6f),
            new Color(0.2f, 0.32f, 0.45f, 0.85f),
            HandleMmdDanceCategoryPrev,
            _mmdDancePage.transform,
            44f,
            34f,
            18);

        _mmdDanceCategoryCurrentButton = CreateButton(
            "MmdDanceCatCurrent",
            "分区：全部",
            74f,
            54f,
            new Color(0.14f, 0.26f, 0.38f, 0.7f),
            new Color(0.24f, 0.44f, 0.64f, 0.9f),
            HandleMmdDanceCategoryNext,
            _mmdDancePage.transform,
            354f,
            34f,
            16);

        _mmdDanceCategoryNextButton = CreateButton(
            "MmdDanceCatNext",
            "▶",
            434f,
            54f,
            new Color(0.12f, 0.18f, 0.24f, 0.6f),
            new Color(0.2f, 0.32f, 0.45f, 0.85f),
            HandleMmdDanceCategoryNext,
            _mmdDancePage.transform,
            44f,
            34f,
            18);

        _mmdDanceFavoriteFilterButton = CreateButton(
            "MmdDanceFavFilter",
            "★",
            484f,
            54f,
            new Color(0.22f, 0.18f, 0.1f, 0.6f),
            new Color(0.5f, 0.38f, 0.14f, 0.9f),
            HandleMmdDanceFavoriteToggle,
            _mmdDancePage.transform,
            52f,
            34f,
            18);

        // Summary Text Bar (y = 90)
        Image summaryBg = CreateImage(
            "MmdDanceSummaryBg",
            _mmdDancePage.transform,
            24f,
            90f,
            512f,
            24f,
            new Color(0.04f, 0.08f, 0.12f, 0.45f));
        ApplyGlassEffects(summaryBg, new Color(0.4f, 0.7f, 0.9f, 0.1f), false, 0f);

        _mmdDanceSummaryText = CreateText(
            "MmdDanceSummary",
            _mmdDancePage.transform,
            string.Empty,
            32f,
            90f,
            496f,
            24f,
            13,
            TextAnchor.MiddleLeft,
            new Color(0.6f, 0.82f, 0.95f, 1f));

        // 6 Dance Rows (y = 118 to 368)
        for (int i = 0; i < DanceVisibleRows; i++)
        {
            int row = i;
            _mmdDanceEntryButtons[i] = CreateButton(
                "MmdDanceEntry" + i,
                "-",
                24f,
                118f + i * 44f,
                new Color(0.08f, 0.15f, 0.2f, 0.55f),
                new Color(0.15f, 0.38f, 0.52f, 0.82f),
                () => HandleDanceEntry(row),
                _mmdDancePage.transform,
                512f,
                40f,
                16);
        }

        // Footer Pagination / Scroll Text (y = 384)
        _mmdDanceScrollText = CreateText(
            "MmdDanceScroll",
            _mmdDancePage.transform,
            "↑                           ↓",
            24f,
            384f,
            512f,
            25f,
            14,
            TextAnchor.MiddleCenter,
            new Color(0.6f, 0.74f, 0.82f, 1f));
    }

    private void HandleOpenMmdDance()
    {
        VRMmdDanceRatingStore.EnsureLoaded();
        _danceOffset = 0;
        RefreshMmdDanceEntries();
        ShowPage(WristMenuPage.MmdDance);
    }

    private void HandleBackToMmdPlayback()
    {
        RefreshCurrentDanceRatingDisplay();
        ShowPage(WristMenuPage.MmdPlayback);
    }

    private void HandleMmdDanceCategoryPrev()
    {
        string current = VRMmdDanceCategories.DisplayCategories[_danceCategoryIndex];
        string prev = VRMmdDanceCategories.GetPrev(current);
        _danceCategoryIndex = Array.IndexOf(VRMmdDanceCategories.DisplayCategories, prev);
        if (_danceCategoryIndex < 0) _danceCategoryIndex = 0;
        _danceOffset = 0;
        RefreshMmdDanceEntries();
    }

    private void HandleMmdDanceCategoryNext()
    {
        string current = VRMmdDanceCategories.DisplayCategories[_danceCategoryIndex];
        string next = VRMmdDanceCategories.GetNext(current);
        _danceCategoryIndex = Array.IndexOf(VRMmdDanceCategories.DisplayCategories, next);
        if (_danceCategoryIndex < 0) _danceCategoryIndex = 0;
        _danceOffset = 0;
        RefreshMmdDanceEntries();
    }

    private void HandleMmdDanceFavoriteToggle()
    {
        _danceFavoriteOnly = !_danceFavoriteOnly;
        _danceOffset = 0;
        RefreshMmdDanceEntries();
    }

    private void HandleMmdDanceSortToggle()
    {
        _danceSortByRatingDesc = !_danceSortByRatingDesc;
        _danceOffset = 0;
        RefreshMmdDanceEntries();
    }

    private void HandleMmdDanceScan()
    {
        string root = GetConfiguredVmdRoot();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            if (Directory.Exists(@"E:\action"))
                root = @"E:\action";
            else
                root = Path.Combine(VRMmdDanceRatingStore.ResolveGameRoot(), "UserData", "vmd");
        }

        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            SetStatus(L("未找到动作目录，请先设置", "モーションフォルダー未設定", "No motion folder set"), new Color(1f, 0.38f, 0.34f, 1f), 4f);
            return;
        }

        SetStatus(L("正在扫描并启发式打分新动作…", "モーションスキャン＆自動評価中…", "Scanning & scoring dances…"), new Color(0.42f, 0.72f, 1f, 1f), 0f);
        int added = VRMmdDanceRatingStore.ScanDirectory(root, false);
        RefreshMmdDanceEntries();
        SetStatus(
            L("扫描完成：发现 ", "スキャン完了：", "Scan complete: ") + added + L(" 首新舞蹈并完成 1~9 分评级", " 曲を自動評価完了", " new dances auto-rated"),
            new Color(0.35f, 1f, 0.62f, 1f),
            5f);
    }

    private void RefreshMmdDanceEntries()
    {
        string category = VRMmdDanceCategories.DisplayCategories[_danceCategoryIndex];
        _danceFilteredEntries = VRMmdDanceRatingStore.Database.GetFilteredEntries(
            category,
            _danceFavoriteOnly,
            _danceSortByRatingDesc);

        int maxOffset = Math.Max(0, _danceFilteredEntries.Count - DanceVisibleRows);
        _danceOffset = Mathf.Clamp(_danceOffset, 0, maxOffset);
        RefreshMmdDanceVisuals();
    }

    private void RefreshMmdDanceVisuals()
    {
        if (_mmdDancePage == null) return;

        string currentCat = VRMmdDanceCategories.DisplayCategories[_danceCategoryIndex];
        string catLabel = L("分区: ", "カテゴリ: ", "Category: ") + currentCat + " (" + _danceFilteredEntries.Count + ")";
        if (_danceFavoriteOnly)
            catLabel += " [★收藏]";
        _mmdDanceCategoryCurrentButton.SetLabel(catLabel);

        if (_mmdDanceFavoriteFilterButton != null)
        {
            _mmdDanceFavoriteFilterButton.SetLabel(_danceFavoriteOnly ? "★[开]" : "★[关]");
        }

        if (_mmdDanceSortButton != null)
        {
            _mmdDanceSortButton.SetLabel(_danceSortByRatingDesc
                ? L("9★->1★ 降序", "9★->1★ 降順", "9★->1★ Desc")
                : L("名称排序", "名前順", "Name Asc"));
        }

        // Summary bar
        int firstVisible = _danceFilteredEntries.Count == 0 ? 0 : _danceOffset + 1;
        int lastVisible = Math.Min(_danceFilteredEntries.Count, _danceOffset + DanceVisibleRows);
        _mmdDanceSummaryText.text = currentCat
            + " (" + firstVisible + "-" + lastVisible + " / " + _danceFilteredEntries.Count + ")"
            + (_danceSortByRatingDesc ? "  ·  高分置顶 [9★->1★]" : "  ·  按名称排序");

        // Buttons
        for (int i = 0; i < DanceVisibleRows; i++)
        {
            int entryIndex = _danceOffset + i;
            VRWristMenuButtonTarget btn = _mmdDanceEntryButtons[i];
            bool visible = entryIndex >= 0 && entryIndex < _danceFilteredEntries.Count;
            btn.SetVisible(visible);

            if (visible)
            {
                var entry = _danceFilteredEntries[entryIndex];
                string line = FormatDanceListLine(entry);
                btn.SetLabel(line);
            }
        }

        // Scroll hint
        bool canScroll = _danceFilteredEntries.Count > DanceVisibleRows;
        _mmdDanceScrollText.text = canScroll
            ? "↑   (" + firstVisible + "-" + lastVisible + " / " + _danceFilteredEntries.Count + ")   ↓"
            : (_danceFilteredEntries.Count == 0 ? L("当前分区暂无舞蹈", "該当ダンスなし", "No dances in category") : string.Empty);
    }

    private static string FormatDanceListLine(VRMmdDanceRatingEntry entry)
    {
        string badge = entry.FormatBadge();
        string catShort = "[" + entry.FormatCategoryShort() + "]";
        string dur = entry.DurationSeconds > 0 ? " · " + (int)entry.DurationSeconds + "s" : "";
        string fav = entry.Favorite ? " ★" : "";

        string name = entry.MotionName;
        int budget = BrowserEntryMaxUiUnits - GetUiVisualUnits(badge) - GetUiVisualUnits(catShort) - GetUiVisualUnits(dur) - 3;
        string safeName = EllipsizeTailForUi(name, Math.Max(6, budget));

        return badge + " " + catShort + " " + safeName + dur + fav;
    }

    private void HandleDanceEntry(int row)
    {
        if (_operationInProgress)
            return;

        int index = _danceOffset + row;
        if (index < 0 || index >= _danceFilteredEntries.Count)
            return;

        VRMmdDanceRatingEntry entry = _danceFilteredEntries[index];
        LoadDancePackageByEntry(entry);
    }

    private void LoadDancePackageByEntry(VRMmdDanceRatingEntry entry)
    {
        string resolvedMotionPath = null;

        // 1. If FullPath points to an existing file
        if (!string.IsNullOrEmpty(entry.FullPath) && File.Exists(entry.FullPath) && entry.FullPath.EndsWith(".vmd", StringComparison.OrdinalIgnoreCase))
        {
            resolvedMotionPath = entry.FullPath;
        }
        // 2. If FullPath points to an existing directory
        else if (!string.IsNullOrEmpty(entry.FullPath) && Directory.Exists(entry.FullPath))
        {
            resolvedMotionPath = FindPrimaryMotionInDirectory(entry.FullPath);
        }

        // 3. Try resolving in configured VMD root or E:\action
        if (string.IsNullOrEmpty(resolvedMotionPath) || !File.Exists(resolvedMotionPath))
        {
            string root = GetConfiguredVmdRoot();
            string[] searchRoots = new[] { root, @"E:\action", Path.Combine(VRMmdDanceRatingStore.ResolveGameRoot(), "UserData", "vmd") };

            foreach (string baseDir in searchRoots)
            {
                if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir))
                    continue;

                // Try relative path
                if (!string.IsNullOrEmpty(entry.RelativePath))
                {
                    string candidate = Path.Combine(baseDir, entry.RelativePath);
                    if (File.Exists(candidate)) { resolvedMotionPath = candidate; break; }
                    if (Directory.Exists(candidate))
                    {
                        string inside = FindPrimaryMotionInDirectory(candidate);
                        if (!string.IsNullOrEmpty(inside)) { resolvedMotionPath = inside; break; }
                    }
                }

                // Try matching directory name
                string folder = Path.Combine(baseDir, entry.MotionName);
                if (Directory.Exists(folder))
                {
                    string inside = FindPrimaryMotionInDirectory(folder);
                    if (!string.IsNullOrEmpty(inside)) { resolvedMotionPath = inside; break; }
                }
            }
        }

        if (string.IsNullOrEmpty(resolvedMotionPath) || !File.Exists(resolvedMotionPath))
        {
            SetStatus(
                L("未找到动作文件：", "モーションが見つかりません：", "Motion file not found: ") + entry.MotionName,
                new Color(1f, 0.38f, 0.34f, 1f),
                5f);
            return;
        }

        VRVmdMetadata metadata;
        if (!VRWristFileCatalog.TryReadVmdMetadata(resolvedMotionPath, out metadata))
        {
            SetStatus(
                L("无法读取 VMD 数据：", "VMD データを読めません：", "Cannot read VMD: ") + Path.GetFileName(resolvedMotionPath),
                new Color(1f, 0.38f, 0.34f, 1f),
                5f);
            return;
        }

        VRWristFileEntry wristEntry = new VRWristFileEntry
        {
            DisplayName = entry.MotionName,
            FullPath = resolvedMotionPath,
            HasVmdMetadata = true,
            VmdMetadata = metadata
        };

        SetStatus(
            L("载入舞蹈：", "ダンス読込：", "Loading dance: ") + entry.MotionName + " " + entry.FormatBadge(),
            new Color(0.4f, 0.85f, 1f, 1f),
            0f);

        HandleVmdSelection(wristEntry);
    }

    private static string FindPrimaryMotionInDirectory(string dir)
    {
        try
        {
            string[] vmdFiles = Directory.GetFiles(dir, "*.vmd");
            if (vmdFiles.Length == 0) return null;

            string bestMotion = null;
            long bestSize = -1;

            foreach (string vmd in vmdFiles)
            {
                VRVmdMetadata meta;
                if (VRWristFileCatalog.TryReadVmdMetadata(vmd, out meta) && meta.HasActorData)
                {
                    string fname = Path.GetFileName(vmd).ToLowerInvariant();
                    // Priority keywords
                    if (fname.Contains("full") || fname.Contains("motion") || fname.Contains("1人") || fname.Contains("main"))
                        return vmd;

                    FileInfo fi = new FileInfo(vmd);
                    if (fi.Length > bestSize)
                    {
                        bestSize = fi.Length;
                        bestMotion = vmd;
                    }
                }
            }
            return bestMotion ?? vmdFiles[0];
        }
        catch
        {
            return null;
        }
    }

    private void UpdateMmdDanceStickScroll(SteamVR_Controller.Device rightDevice)
    {
        if (_page != WristMenuPage.MmdDance || _danceFilteredEntries.Count <= DanceVisibleRows)
        {
            _danceStickDirection = 0;
            return;
        }

        float axis = rightDevice.GetAxis(EVRButtonId.k_EButton_Axis0).y;
        int direction = axis > DanceStickThreshold
            ? -1
            : (axis < -DanceStickThreshold ? 1 : 0);

        if (Mathf.Abs(axis) < DanceStickReleaseThreshold)
        {
            _danceStickDirection = 0;
            return;
        }

        if (direction == 0)
            return;

        bool shouldScroll = direction != _danceStickDirection
            || Time.unscaledTime >= _nextDanceStickScroll;

        if (!shouldScroll)
            return;

        int maxOffset = Math.Max(0, _danceFilteredEntries.Count - DanceVisibleRows);
        int nextOffset = Mathf.Clamp(_danceOffset + direction, 0, maxOffset);
        bool changed = nextOffset != _danceOffset;

        if (changed)
        {
            _danceOffset = nextOffset;
            RefreshMmdDanceVisuals();
            rightDevice.TriggerHapticPulse(120, EVRButtonId.k_EButton_Axis0);
        }

        _danceStickDirection = direction;
        _nextDanceStickScroll = Time.unscaledTime + (changed ? 0.13f : 0.22f);
    }

    // Quick Rating Controls on MmdPlayback Page
    private void BuildMmdPlaybackRatingControls(Transform parent)
    {
        CreateText(
            "PlaybackRatingHeading",
            parent,
            L("当前动作评分与收藏 (实时写回本地 JSON)", "現在の評価と保存 (JSON即時反映)", "Live Rating & Favorite (Writes to JSON)"),
            24f,
            160f,
            512f,
            20f,
            15,
            TextAnchor.MiddleLeft,
            new Color(1f, 0.8f, 0.3f, 1f));

        _playbackDanceRatingMinusButton = CreateButton(
            "PlaybackRatingMinus",
            "-1★",
            24f,
            182f,
            new Color(0.35f, 0.12f, 0.12f, 0.65f),
            new Color(0.65f, 0.2f, 0.2f, 0.85f),
            HandleCurrentDanceRatingMinus,
            parent,
            76f,
            40f,
            16);

        _playbackDanceRatingInfoText = CreateText(
            "PlaybackRatingInfo",
            parent,
            L("未载入动作", "モーション未読込", "No motion loaded"),
            106f,
            182f,
            244f,
            40f,
            15,
            TextAnchor.MiddleCenter,
            Color.white);

        _playbackDanceRatingPlusButton = CreateButton(
            "PlaybackRatingPlus",
            "+1★",
            356f,
            182f,
            new Color(0.12f, 0.32f, 0.18f, 0.65f),
            new Color(0.2f, 0.6f, 0.3f, 0.85f),
            HandleCurrentDanceRatingPlus,
            parent,
            76f,
            40f,
            16);

        _playbackDanceRatingFavoriteButton = CreateButton(
            "PlaybackRatingFav",
            L("★ 收藏", "★ お気に入り", "★ Favorite"),
            438f,
            182f,
            new Color(0.32f, 0.24f, 0.08f, 0.65f),
            new Color(0.68f, 0.48f, 0.12f, 0.85f),
            HandleCurrentDanceRatingFavorite,
            parent,
            98f,
            40f,
            15);
    }

    public void RefreshCurrentDanceRatingDisplay()
    {
        if (_playbackDanceRatingInfoText == null)
            return;

        string currentPath = VRMmdPlaybackController.Instance != null
            ? VRMmdPlaybackController.Instance.CurrentVmdPath
            : null;

        if (string.IsNullOrEmpty(currentPath))
        {
            _playbackDanceRatingInfoText.text = L("未载入动作", "モーション未読込", "No motion loaded");
            return;
        }

        string motionName = Path.GetFileNameWithoutExtension(currentPath);
        VRMmdDanceRatingEntry entry;
        if (!VRMmdDanceRatingStore.Database.TryGetEntry(motionName, out entry) &&
            !VRMmdDanceRatingStore.Database.TryGetEntry(currentPath, out entry))
        {
            // Evaluate heuristic
            float score = VRMmdHeuristicScorer.CalculateScore(motionName, currentPath, true, true, 3000, 100, false, null);
            string cat = VRMmdHeuristicScorer.DetectCategory(motionName, currentPath, score, false, null);
            entry = new VRMmdDanceRatingEntry
            {
                MotionName = motionName,
                FullPath = currentPath,
                Rating = score,
                Category = cat
            };
            VRMmdDanceRatingStore.Database.AddOrUpdate(entry);
            VRMmdDanceRatingStore.SaveDatabase();
        }

        string badge = entry.FormatBadge();
        string fav = entry.Favorite ? " ★" : "";
        string displayName = EllipsizeTailForUi(entry.MotionName, 14);
        _playbackDanceRatingInfoText.text = displayName + "  " + badge + fav + " [" + entry.FormatCategoryShort() + "]";
    }

    private void HandleCurrentDanceRatingMinus()
    {
        string currentPath = VRMmdPlaybackController.Instance != null
            ? VRMmdPlaybackController.Instance.CurrentVmdPath
            : null;

        if (string.IsNullOrEmpty(currentPath))
        {
            SetStatus(L("当前未载入动作", "モーションがありません", "No motion active"), new Color(1f, 0.38f, 0.34f, 1f), 3f);
            return;
        }

        string key = Path.GetFileNameWithoutExtension(currentPath);
        var entry = VRMmdDanceRatingStore.Database.AdjustRating(key, -1.0f);
        VRMmdDanceRatingStore.SaveDatabase();
        RefreshCurrentDanceRatingDisplay();

        SetStatus(
            L("已将《", "「", "Set ") + entry.MotionName + L("》评分调为 ", "」の評価を ", " rating to ") + entry.Rating.ToString("0.0") + L("★ 并保存", "★ に保存", "★ and saved"),
            new Color(0.35f, 1f, 0.62f, 1f),
            4f);
    }

    private void HandleCurrentDanceRatingPlus()
    {
        string currentPath = VRMmdPlaybackController.Instance != null
            ? VRMmdPlaybackController.Instance.CurrentVmdPath
            : null;

        if (string.IsNullOrEmpty(currentPath))
        {
            SetStatus(L("当前未载入动作", "モーションがありません", "No motion active"), new Color(1f, 0.38f, 0.34f, 1f), 3f);
            return;
        }

        string key = Path.GetFileNameWithoutExtension(currentPath);
        var entry = VRMmdDanceRatingStore.Database.AdjustRating(key, +1.0f);
        VRMmdDanceRatingStore.SaveDatabase();
        RefreshCurrentDanceRatingDisplay();

        SetStatus(
            L("已将《", "「", "Set ") + entry.MotionName + L("》评分调为 ", "」の評価を ", " rating to ") + entry.Rating.ToString("0.0") + L("★ 并保存", "★ に保存", "★ and saved"),
            new Color(0.35f, 1f, 0.62f, 1f),
            4f);
    }

    private void HandleCurrentDanceRatingFavorite()
    {
        string currentPath = VRMmdPlaybackController.Instance != null
            ? VRMmdPlaybackController.Instance.CurrentVmdPath
            : null;

        if (string.IsNullOrEmpty(currentPath))
        {
            SetStatus(L("当前未载入动作", "モーションがありません", "No motion active"), new Color(1f, 0.38f, 0.34f, 1f), 3f);
            return;
        }

        string key = Path.GetFileNameWithoutExtension(currentPath);
        var entry = VRMmdDanceRatingStore.Database.ToggleFavorite(key);
        VRMmdDanceRatingStore.SaveDatabase();
        RefreshCurrentDanceRatingDisplay();

        SetStatus(
            (entry.Favorite ? L("已收藏《", "お気に入りに追加「", "Favorited ") : L("已取消收藏《", "お気に入り解除「", "Unfavorited "))
            + entry.MotionName + L("》", "」", ""),
            new Color(1f, 0.85f, 0.3f, 1f),
            4f);
    }
}
