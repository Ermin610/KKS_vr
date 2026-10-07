using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using VRGIN.Core;

namespace KKCharaStudioVR;

public sealed partial class VRWristMenuController
{
    private const int GridVisibleCards = 6;
    private const int GridColumns = 3;
    private const int GridRows = 2;
    private const float GridCardWidth = 162f;
    private const float GridCardHeight = 136f;
    private const float GridCardGapX = 13f;
    private const float GridCardGapY = 6f;
    private const float GridStartX = 24f;
    private const float GridStartY = 98f;

    private static readonly Color GridCardNormalColor = new Color(0.06f, 0.10f, 0.14f, 0.65f);
    private static readonly Color GridCardHoverColor = new Color(0.12f, 0.26f, 0.36f, 0.88f);
    private static readonly Color GridCardSelectedNormalColor = new Color(0.10f, 0.32f, 0.42f, 0.88f);
    private static readonly Color GridCardSelectedHoverColor = new Color(0.16f, 0.44f, 0.56f, 0.95f);
    private static readonly Color GridFolderNormalColor = new Color(0.10f, 0.14f, 0.20f, 0.70f);
    private static readonly Color GridFolderHoverColor = new Color(0.18f, 0.28f, 0.38f, 0.90f);

    private sealed class VRWristGridCard
    {
        public Image Background;
        public Outline Outline;
        public RawImage ThumbnailImage;
        public Image Bezel;
        public Image FolderPlate;
        public Image FolderTab;
        public Text FolderIcon;
        public Text LoadingText;
        public Text NameText;
        public VRWristMenuButtonTarget ButtonTarget;
        public int SlotIndex;

        public void SetVisible(bool visible)
        {
            if (ButtonTarget != null)
                ButtonTarget.SetVisible(visible);
        }
    }

    private readonly VRWristGridCard[] _gridCards = new VRWristGridCard[GridVisibleCards];
    private VRCardThumbnailCache<Texture2D> _cardThumbnailCache;
    private bool _browserGridView = true;
    private bool _gridQuickLoad = true;
    private int _gridOffset = 0;
    private int _selectedGridIndex = -1;
    private string _pendingGridCardPath;
    private int _gridPageRequestId = 0;
    private Coroutine _gridThumbnailsCoroutine;
    private Coroutine _gridPrewarmCoroutine;
    private int _gridPrewarmRequestId;
    private readonly HashSet<string> _thumbnailInFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private VRWristMenuButtonTarget _browserViewModeButton;
    private VRWristMenuButtonTarget _gridPrevButton;
    private VRWristMenuButtonTarget _gridNextButton;
    private VRWristMenuButtonTarget _gridQuickLoadButton;
    private VRWristMenuButtonTarget _gridActionLoadButton;
    private VRWristMenuButtonTarget _gridActionReplaceButton;
    private Text _gridPageText;
    private Text _gridEmptyFolderText;
    private GameObject _gridControlsContainer;

    private void EnsureThumbnailCache()
    {
        if (_cardThumbnailCache == null)
        {
            _cardThumbnailCache = new VRCardThumbnailCache<Texture2D>(
                capacity: 36,
                onDestroy: tex =>
                {
                    if (tex != null)
                        Destroy(tex);
                });
        }
    }

    private void BuildBrowserCardGrid()
    {
        EnsureThumbnailCache();

        // 1. Grid Cards (3 columns x 2 rows = 6 cards)
        for (int i = 0; i < GridVisibleCards; i++)
        {
            int slot = i;
            int col = i % GridColumns;
            int row = i / GridColumns;
            float x = GridStartX + col * (GridCardWidth + GridCardGapX);
            float y = GridStartY + row * (GridCardHeight + GridCardGapY);

            VRWristGridCard card = new VRWristGridCard { SlotIndex = slot };

            // Card background image
            string cardName = "BrowserGridCard" + slot;
            card.Background = CreateImage(
                cardName,
                _browserPage.transform,
                x,
                y,
                GridCardWidth,
                GridCardHeight,
                GridCardNormalColor);
            ApplyControlEffects(card.Background);
            card.Outline = card.Background.GetComponent<Outline>();

            // Dark bezel frame behind the thumbnail (centered at card top)
            card.Bezel = CreateImage(
                cardName + "Bezel",
                card.Background.transform,
                42f,
                5f,
                78f,
                106f,
                new Color(0.015f, 0.02f, 0.035f, 0.75f),
                true);

            // RawImage for character thumbnail preview
            GameObject thumbObj = CreateRectObject(
                cardName + "Thumbnail",
                card.Background.transform,
                44f,
                7f,
                74f,
                102f,
                _visibleLayer);
            card.ThumbnailImage = thumbObj.AddComponent<RawImage>();
            card.ThumbnailImage.raycastTarget = false;
            card.ThumbnailImage.color = Color.white;
            card.ThumbnailImage.enabled = false;

            // Drawn folder so the icon still shows when the UI font has no emoji.
            card.FolderTab = CreateImage(
                cardName + "FolderTab",
                card.Background.transform,
                58f,
                22f,
                28f,
                12f,
                new Color(0.95f, 0.72f, 0.28f, 0.95f),
                true);
            card.FolderPlate = CreateImage(
                cardName + "FolderPlate",
                card.Background.transform,
                46f,
                32f,
                70f,
                48f,
                new Color(0.78f, 0.55f, 0.16f, 0.96f),
                true);
            card.FolderTab.gameObject.SetActive(false);
            card.FolderPlate.gameObject.SetActive(false);

            // Folder icon for directory entries
            card.FolderIcon = CreateText(
                cardName + "FolderIcon",
                card.Background.transform,
                "📁",
                36f,
                18f,
                90f,
                64f,
                40,
                TextAnchor.MiddleCenter,
                new Color(1f, 0.84f, 0.32f, 0.95f));
            card.FolderIcon.gameObject.SetActive(false);

            // Loading text placeholder
            card.LoadingText = CreateText(
                cardName + "LoadingText",
                card.Background.transform,
                L("读取中…", "読込中…", "Loading…"),
                36f,
                42f,
                90f,
                28f,
                12,
                TextAnchor.MiddleCenter,
                new Color(0.6f, 0.75f, 0.85f, 0.85f));
            card.LoadingText.gameObject.SetActive(false);

            // Card bottom name label
            card.NameText = CreateText(
                cardName + "NameLabel",
                card.Background.transform,
                "-",
                4f,
                113f,
                154f,
                20f,
                13,
                TextAnchor.MiddleCenter,
                PrimaryTextColor);
            card.NameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            card.NameText.verticalOverflow = VerticalWrapMode.Truncate;

            // Hitbox & button target for VR laser interaction
            GameObject hitbox = new GameObject(cardName + "Hitbox");
            hitbox.layer = _colliderLayer;
            hitbox.transform.SetParent(card.Background.transform, false);
            hitbox.transform.localPosition = new Vector3(GridCardWidth * 0.5f, -GridCardHeight * 0.5f, 0f);
            hitbox.transform.localRotation = Quaternion.identity;
            hitbox.transform.localScale = Vector3.one;

            BoxCollider collider = hitbox.AddComponent<BoxCollider>();
            collider.size = new Vector3(GridCardWidth, GridCardHeight, 12f);
            collider.isTrigger = true;

            card.ButtonTarget = hitbox.AddComponent<VRWristMenuButtonTarget>();
            card.ButtonTarget.Configure(
                card.Background,
                card.NameText,
                GridCardNormalColor,
                GridCardHoverColor,
                () => HandleGridCardClick(slot));

            _gridCards[i] = card;
        }

        // 2. Grid Controls Container (Pagination & Quick Actions)
        _gridControlsContainer = CreateRectObject(
            "BrowserGridControls",
            _browserPage.transform,
            24f,
            386f,
            512f,
            38f,
            _visibleLayer);

        _gridPrevButton = CreateButton(
            "GridPrevPage",
            L("◀ 上页", "◀ 前へ", "◀ Prev"),
            0f,
            0f,
            new Color(0.08f, 0.15f, 0.2f, 0.52f),
            new Color(0.14f, 0.3f, 0.42f, 0.82f),
            HandleGridPrevPage,
            _gridControlsContainer.transform,
            78f,
            36f,
            14);

        _gridPageText = CreateText(
            "GridPageIndicator",
            _gridControlsContainer.transform,
            "1 / 1",
            80f,
            0f,
            88f,
            36f,
            14,
            TextAnchor.MiddleCenter,
            new Color(0.82f, 0.92f, 1f, 1f));

        _gridNextButton = CreateButton(
            "GridNextPage",
            L("下页 ▶", "次へ ▶", "Next ▶"),
            170f,
            0f,
            new Color(0.08f, 0.15f, 0.2f, 0.52f),
            new Color(0.14f, 0.3f, 0.42f, 0.82f),
            HandleGridNextPage,
            _gridControlsContainer.transform,
            78f,
            36f,
            14);

        _gridQuickLoadButton = CreateButton(
            "GridQuickLoadToggle",
            L("一键:开", "即時:オン", "Quick:On"),
            252f,
            0f,
            new Color(0.12f, 0.16f, 0.22f, 0.52f),
            new Color(0.2f, 0.28f, 0.38f, 0.82f),
            HandleGridQuickLoadToggle,
            _gridControlsContainer.transform,
            82f,
            36f,
            13);

        _gridActionLoadButton = CreateButton(
            "GridActionLoad",
            L("＋ 载入", "＋ 読込", "＋ Load"),
            338f,
            0f,
            new Color(0.08f, 0.3f, 0.16f, 0.65f),
            new Color(0.12f, 0.5f, 0.26f, 0.88f),
            HandleGridActionLoad,
            _gridControlsContainer.transform,
            84f,
            36f,
            14);

        _gridActionReplaceButton = CreateButton(
            "GridActionReplace",
            L("↔ 替换", "↔ 置換", "↔ Swap"),
            426f,
            0f,
            new Color(0.09f, 0.18f, 0.3f, 0.62f),
            new Color(0.15f, 0.35f, 0.58f, 0.85f),
            HandleGridActionReplace,
            _gridControlsContainer.transform,
            86f,
            36f,
            14);

        // Empty folder hint text
        _gridEmptyFolderText = CreateText(
            "GridEmptyFolderHint",
            _browserPage.transform,
            L("此目录没有角色卡或子文件夹", "このフォルダーにカードもサブフォルダーもありません", "No cards or subfolders in this folder"),
            24f,
            180f,
            512f,
            40f,
            17,
            TextAnchor.MiddleCenter,
            new Color(0.7f, 0.8f, 0.9f, 0.9f));
        _gridEmptyFolderText.gameObject.SetActive(false);

        // 3. Header View Mode Toggle Button (Grid <-> List)
        _browserViewModeButton = CreateButton(
            "BrowserViewModeToggle",
            L("≡ 列表", "≡ 一覧", "≡ List"),
            268f,
            10f,
            new Color(0.12f, 0.18f, 0.24f, 0.56f),
            new Color(0.22f, 0.36f, 0.46f, 0.82f),
            ToggleBrowserViewMode,
            _browserPage.transform,
            86f,
            38f,
            14);
        _browserViewModeButton.SetVisible(false);
    }

    private void SetGridVisible(bool visible)
    {
        for (int i = 0; i < GridVisibleCards; i++)
        {
            _gridCards[i]?.SetVisible(visible);
        }
        if (_gridControlsContainer != null)
            _gridControlsContainer.SetActive(visible);
        if (_gridEmptyFolderText != null && !visible)
            _gridEmptyFolderText.gameObject.SetActive(false);
    }

    private void ToggleBrowserViewMode()
    {
        _browserGridView = !_browserGridView;
        if (_browserGridView)
        {
            _gridOffset = (_browserOffset / GridVisibleCards) * GridVisibleCards;
            _browserOffset = _gridOffset;
        }
        else
        {
            _browserOffset = _gridOffset;
        }
        RefreshBrowserVisuals();
        SetStatus(
            _browserGridView
                ? L("已切换至网格缩略图卡片模式", "グリッドサムネイル表示に切替", "Switched to Grid Thumbnail View")
                : L("已切换至紧凑列表模式", "コンパクトリスト表示に切替", "Switched to List View"),
            new Color(0.47f, 0.9f, 0.55f, 1f),
            3f);
    }

    private void RefreshBrowserGridVisuals()
    {
        EnsureThumbnailCache();

        int totalCount = _browserEntries != null ? _browserEntries.Count : 0;
        int totalPages = Mathf.Max(1, (totalCount + GridVisibleCards - 1) / GridVisibleCards);
        int maxOffset = Mathf.Max(0, (totalPages - 1) * GridVisibleCards);
        _gridOffset = Mathf.Clamp(_gridOffset, 0, maxOffset);
        _browserOffset = _gridOffset;

        int currentPage = totalCount == 0 ? 0 : (_gridOffset / GridVisibleCards) + 1;
        if (_gridPageText != null)
            _gridPageText.text = $"{currentPage} / {totalPages}";

        if (_gridPrevButton != null)
            _gridPrevButton.SetInteractable(currentPage > 1);
        if (_gridNextButton != null)
            _gridNextButton.SetInteractable(currentPage < totalPages);

        if (_gridQuickLoadButton != null)
        {
            _gridQuickLoadButton.SetLabel(_gridQuickLoad
                ? L("一键:开", "即時:オン", "Quick:On")
                : L("一键:关", "即時:オフ", "Quick:Off"));
            _gridQuickLoadButton.SetColors(
                _gridQuickLoad
                    ? new Color(0.08f, 0.35f, 0.18f, 0.72f)
                    : new Color(0.12f, 0.16f, 0.22f, 0.52f),
                _gridQuickLoad
                    ? new Color(0.14f, 0.52f, 0.28f, 0.92f)
                    : new Color(0.2f, 0.28f, 0.38f, 0.82f));
        }

        bool isCoordinateMode = _browserMode == BrowserMode.CoordinateCards;
        if (_gridActionLoadButton != null)
        {
            _gridActionLoadButton.SetLabel(isCoordinateMode
                ? L("换装", "着替え", "Wear")
                : L("＋ 载入", "＋ 読込", "＋ Load"));
        }
        if (_gridActionReplaceButton != null)
        {
            _gridActionReplaceButton.SetLabel(isCoordinateMode
                ? L("详情", "詳細", "Detail")
                : L("↔ 替换", "↔ 置換", "↔ Swap"));
        }

        bool hasSelection = _selectedGridIndex >= 0 && !string.IsNullOrEmpty(_pendingGridCardPath);
        if (_gridActionLoadButton != null)
            _gridActionLoadButton.SetInteractable(hasSelection);
        if (_gridActionReplaceButton != null)
            _gridActionReplaceButton.SetInteractable(hasSelection);

        if (totalCount == 0)
        {
            if (_gridEmptyFolderText != null)
                _gridEmptyFolderText.gameObject.SetActive(true);
            for (int i = 0; i < GridVisibleCards; i++)
            {
                _gridCards[i]?.SetVisible(false);
            }
            if (_gridControlsContainer != null)
                _gridControlsContainer.SetActive(true);
            return;
        }

        if (_gridEmptyFolderText != null)
            _gridEmptyFolderText.gameObject.SetActive(false);
        if (_gridControlsContainer != null)
            _gridControlsContainer.SetActive(true);

        List<VRWristFileEntry> entriesToLoad = new List<VRWristFileEntry>();

        for (int i = 0; i < GridVisibleCards; i++)
        {
            int entryIndex = _gridOffset + i;
            VRWristGridCard card = _gridCards[i];
            if (card == null)
                continue;

            if (entryIndex >= totalCount)
            {
                card.SetVisible(false);
                continue;
            }

            card.SetVisible(true);
            VRWristFileEntry entry = _browserEntries[entryIndex];

            if (entry.IsDirectory)
            {
                // Subdirectory card
                if (card.Bezel != null) card.Bezel.gameObject.SetActive(false);
                if (card.ThumbnailImage != null)
                {
                    card.ThumbnailImage.texture = null;
                    card.ThumbnailImage.enabled = false;
                }
                if (card.LoadingText != null) card.LoadingText.gameObject.SetActive(false);
                if (card.FolderPlate != null) card.FolderPlate.gameObject.SetActive(true);
                if (card.FolderTab != null) card.FolderTab.gameObject.SetActive(true);
                if (card.FolderIcon != null) card.FolderIcon.gameObject.SetActive(true);
                if (card.NameText != null)
                {
                    card.NameText.text = EllipsizeMiddleForUi(VRWristFileCatalog.FormatFolderBadge(entry.DisplayName), 20);
                    card.NameText.color = new Color(1f, 0.9f, 0.62f, 1f);
                }
                card.ButtonTarget.SetColors(GridFolderNormalColor, GridFolderHoverColor);
                if (card.Outline != null)
                {
                    card.Outline.effectColor = new Color(0.82f, 0.9f, 1f, 0.12f);
                    card.Outline.effectDistance = new Vector2(1f, -1f);
                }
            }
            else
            {
                // Character or Coordinate Card
                if (card.FolderIcon != null) card.FolderIcon.gameObject.SetActive(false);
                if (card.FolderPlate != null) card.FolderPlate.gameObject.SetActive(false);
                if (card.FolderTab != null) card.FolderTab.gameObject.SetActive(false);
                if (card.Bezel != null) card.Bezel.gameObject.SetActive(true);

                bool isSelected = (_selectedGridIndex == i)
                    && string.Equals(_pendingGridCardPath, entry.FullPath, StringComparison.OrdinalIgnoreCase);

                card.ButtonTarget.SetColors(
                    isSelected ? GridCardSelectedNormalColor : GridCardNormalColor,
                    isSelected ? GridCardSelectedHoverColor : GridCardHoverColor);
                if (card.Outline != null)
                {
                    card.Outline.effectColor = isSelected
                        ? new Color(0.35f, 0.95f, 1f, 0.95f)
                        : new Color(0.82f, 0.9f, 1f, 0.15f);
                    card.Outline.effectDistance = isSelected
                        ? new Vector2(2f, -2f)
                        : new Vector2(1f, -1f);
                }

                VRCardThumbnailItem<Texture2D> cached;
                if (_cardThumbnailCache.TryGet(entry.FullPath, out cached) && cached.Texture != null)
                {
                    if (card.ThumbnailImage != null)
                    {
                        card.ThumbnailImage.texture = cached.Texture;
                        card.ThumbnailImage.enabled = true;
                    }
                    if (card.LoadingText != null)
                        card.LoadingText.gameObject.SetActive(false);
                    if (card.NameText != null)
                    {
                        string displayName = cached.DisplayName ?? Path.GetFileNameWithoutExtension(entry.DisplayName);
                        card.NameText.text = EllipsizeMiddleForUi(displayName, 18);
                        card.NameText.color = isSelected ? Color.white : PrimaryTextColor;
                    }
                }
                else if (TryShowDiskThumbnail(card, entry, isSelected))
                {
                    // Disk hit is a few kilobytes; the page can paint immediately.
                }
                else
                {
                    if (card.ThumbnailImage != null)
                    {
                        card.ThumbnailImage.texture = null;
                        card.ThumbnailImage.enabled = false;
                    }
                    if (card.LoadingText != null)
                    {
                        card.LoadingText.text = L("读取中…", "読込中…", "Loading…");
                        card.LoadingText.gameObject.SetActive(true);
                    }
                    if (card.NameText != null)
                    {
                        card.NameText.text = EllipsizeMiddleForUi(Path.GetFileNameWithoutExtension(entry.DisplayName), 18);
                        card.NameText.color = isSelected ? Color.white : PrimaryTextColor;
                    }
                    entriesToLoad.Add(entry);
                }
            }
        }

        // Cancel previous loading pass and start smooth multi-frame decode
        _gridPageRequestId++;
        if (_gridThumbnailsCoroutine != null)
        {
            StopCoroutine(_gridThumbnailsCoroutine);
            _gridThumbnailsCoroutine = null;
        }

        if (entriesToLoad.Count > 0)
        {
            _gridThumbnailsCoroutine = StartCoroutine(
                LoadGridThumbnailsCoroutine(_gridPageRequestId, entriesToLoad));
        }
    }

    private IEnumerator LoadGridThumbnailsCoroutine(int reqId, List<VRWristFileEntry> entriesToLoad)
    {
        yield return null;
        if (reqId != _gridPageRequestId)
            yield break;

        foreach (var entry in entriesToLoad)
        {
            if (reqId != _gridPageRequestId)
                yield break;

            yield return StartCoroutine(EnsureCardThumbnail(entry.FullPath, true));
            yield return null;
        }
    }

    private void BeginCardThumbnailPrewarm()
    {
        _gridPrewarmRequestId++;
        if (_gridPrewarmCoroutine != null)
        {
            StopCoroutine(_gridPrewarmCoroutine);
            _gridPrewarmCoroutine = null;
        }
        if (!isActiveAndEnabled || !IsCardPreviewBrowserMode(_browserMode))
            return;
        _gridPrewarmCoroutine = StartCoroutine(PrewarmThumbnailCacheCoroutine(_gridPrewarmRequestId));
    }

    private IEnumerator PrewarmThumbnailCacheCoroutine(int requestId)
    {
        yield return null;
        if (requestId != _gridPrewarmRequestId || _browserEntries == null)
            yield break;

        List<string> cards = new List<string>();
        for (int i = 0; i < _browserEntries.Count; i++)
        {
            if (_browserEntries[i] != null && !_browserEntries[i].IsDirectory)
                cards.Add(_browserEntries[i].FullPath);
        }

        List<string> stale = null;
        Exception scanError = null;
        bool finished = false;
        object scanGate = new object();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            List<string> result;
            Exception error = null;
            try
            {
                result = VRCardThumbnailDiskCache.CollectStale(cards);
            }
            catch (Exception ex)
            {
                error = ex;
                result = cards;
            }
            lock (scanGate)
            {
                stale = result;
                scanError = error;
                finished = true;
            }
        });

        while (true)
        {
            bool done;
            lock (scanGate)
                done = finished;
            if (done)
                break;
            if (requestId != _gridPrewarmRequestId)
                yield break;
            yield return null;
        }
        if (scanError != null)
            VRLog.Warn("Thumbnail scan failed: " + scanError.Message);

        if (stale == null || stale.Count == 0)
            yield break;

        int generated = 0;
        try
        {
            for (int i = 0; i < stale.Count; i++)
            {
                if (requestId != _gridPrewarmRequestId)
                    yield break;
                string path = stale[i];
                if (VRCardThumbnailDiskCache.IsFresh(path))
                    continue;
                yield return StartCoroutine(EnsureCardThumbnail(path, false));
                if (VRCardThumbnailDiskCache.IsFresh(path))
                    generated++;
                yield return null;
            }
        }
        finally
        {
            // Index writes are batched; persist what this pass produced.
            VRCardThumbnailDiskCache.Flush();
        }

        if (generated > 0 && requestId == _gridPrewarmRequestId)
            VRLog.Info("Card thumbnails cached to disk: " + generated);
    }

    private IEnumerator EnsureCardThumbnail(string path, bool preferVisible)
    {
        if (string.IsNullOrEmpty(path))
            yield break;

        VRCardThumbnailItem<Texture2D> cached;
        if (_cardThumbnailCache != null && _cardThumbnailCache.TryGet(path, out cached) && cached.Texture != null)
        {
            if (preferVisible || IsGridCardVisible(path))
                UpdateGridCardThumbnailIfVisible(path, cached.Texture, cached.DisplayName);
            yield break;
        }

        while (_thumbnailInFlight.Contains(path))
        {
            yield return null;
            if (_cardThumbnailCache != null && _cardThumbnailCache.TryGet(path, out cached) && cached.Texture != null)
            {
                UpdateGridCardThumbnailIfVisible(path, cached.Texture, cached.DisplayName);
                yield break;
            }
        }

        if (VRCardThumbnailDiskCache.IsFresh(path))
        {
            Texture2D diskTexture = LoadDiskThumbnail(path, out string diskName);
            if (diskTexture != null)
            {
                RememberThumbnail(path, diskTexture, diskName, preferVisible || IsGridCardVisible(path));
                yield break;
            }
        }

        _thumbnailInFlight.Add(path);
        Texture2D texture = null;
        string displayName = Path.GetFileNameWithoutExtension(path);
        try
        {
            texture = BuildThumbnailTexture(path, out displayName);
        }
        catch (Exception ex)
        {
            VRLog.Warn("Thumbnail decode error for " + path + ": " + ex.Message);
        }
        finally
        {
            _thumbnailInFlight.Remove(path);
        }

        if (texture != null)
            RememberThumbnail(path, texture, displayName, preferVisible || IsGridCardVisible(path));
        else
            UpdateGridCardThumbnailIfVisible(path, null, displayName);
    }

    private void RememberThumbnail(string path, Texture2D texture, string displayName, bool keep)
    {
        if (texture == null)
            return;
        // The cache is dropped on menu rebuild/destroy while a decode coroutine
        // can still finish; without an owner the texture must be freed here.
        if (keep && _cardThumbnailCache != null)
        {
            _cardThumbnailCache.Put(path, texture, displayName);
            UpdateGridCardThumbnailIfVisible(path, texture, displayName);
            return;
        }
        Destroy(texture);
    }

    private bool IsGridCardVisible(string path)
    {
        if (string.IsNullOrEmpty(path) || _browserEntries == null)
            return false;
        int end = Math.Min(_browserEntries.Count, _gridOffset + GridVisibleCards);
        for (int i = _gridOffset; i < end; i++)
        {
            if (i >= 0 && _browserEntries[i] != null
                && string.Equals(_browserEntries[i].FullPath, path, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private bool TryShowDiskThumbnail(VRWristGridCard card, VRWristFileEntry entry, bool isSelected)
    {
        if (_cardThumbnailCache == null)
            return false;
        Texture2D texture = LoadDiskThumbnail(entry.FullPath, out string displayName);
        if (texture == null)
            return false;
        if (string.IsNullOrEmpty(displayName))
            displayName = Path.GetFileNameWithoutExtension(entry.DisplayName);
        _cardThumbnailCache.Put(entry.FullPath, texture, displayName);
        if (card.ThumbnailImage != null)
        {
            card.ThumbnailImage.texture = texture;
            card.ThumbnailImage.enabled = true;
        }
        if (card.LoadingText != null)
            card.LoadingText.gameObject.SetActive(false);
        if (card.NameText != null)
        {
            card.NameText.text = EllipsizeMiddleForUi(displayName, 18);
            card.NameText.color = isSelected ? Color.white : PrimaryTextColor;
        }
        return true;
    }

    private static Texture2D LoadDiskThumbnail(string path, out string displayName)
    {
        displayName = null;
        byte[] bytes;
        if (!VRCardThumbnailDiskCache.TryRead(path, out bytes, out displayName))
            return null;
        return CreateThumbTexture(bytes);
    }

    private Texture2D BuildThumbnailTexture(string path, out string displayName)
    {
        displayName = Path.GetFileNameWithoutExtension(path);
        Texture2D existing = LoadDiskThumbnail(path, out string cachedName);
        if (existing != null)
        {
            if (!string.IsNullOrEmpty(cachedName))
                displayName = cachedName;
            return existing;
        }

        Texture2D full = null;
        Texture2D small = null;
        try
        {
            full = PngAssist.LoadTexture(path);
            if (full == null)
                return null;

            small = CreateDownscaledThumbnail(full);
            if (small == null)
            {
                Destroy(full);
                full = null;
                return null;
            }
            if (!ReferenceEquals(small, full))
            {
                Destroy(full);
                full = null;
            }

            small.name = "KKVR_CardThumb_" + Path.GetFileNameWithoutExtension(path);
            small.filterMode = FilterMode.Bilinear;
            small.wrapMode = TextureWrapMode.Clamp;
            small.hideFlags = HideFlags.DontSave;

            if (_browserMode == BrowserMode.AddFemale || _browserMode == BrowserMode.AddMale)
            {
                byte sex = _browserMode == BrowserMode.AddMale ? (byte)0 : (byte)1;
                try
                {
                    ChaFileControl chara = new ChaFileControl();
                    if (chara.LoadCharaFile(path, sex, noLoadPng: true)
                        && chara.parameter != null
                        && !string.IsNullOrEmpty(chara.parameter.fullname))
                    {
                        displayName = chara.parameter.fullname;
                    }
                }
                catch (Exception)
                {
                }
            }

            byte[] encoded = EncodeThumbnail(small);
            if (encoded != null && encoded.Length > 0)
            {
                string md5 = null;
                try
                {
                    md5 = VRCardThumbnailDiskCache.ComputeMd5(path);
                }
                catch (Exception)
                {
                }
                VRCardThumbnailDiskCache.Write(path, encoded, md5, displayName);
            }
            return small;
        }
        catch (Exception)
        {
            if (full != null && !ReferenceEquals(full, small))
                Destroy(full);
            if (small != null)
                Destroy(small);
            throw;
        }
    }

    private static Texture2D CreateThumbTexture(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return null;
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        try
        {
            if (!texture.LoadImage(bytes, true))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }
        }
        catch (Exception)
        {
            UnityEngine.Object.Destroy(texture);
            return null;
        }
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.hideFlags = HideFlags.DontSave;
        texture.name = "KKVR_CardThumbDisk";
        return texture;
    }

    private static Texture2D CreateDownscaledThumbnail(Texture2D source)
    {
        if (source == null || source.width < 1 || source.height < 1)
            return null;
        int edge = VRCardThumbnailDiskCache.ThumbnailEdge;
        int srcW = source.width;
        int srcH = source.height;
        float scale = Mathf.Min(1f, edge / (float)Mathf.Max(srcW, srcH));
        int dstW = Mathf.Max(1, Mathf.RoundToInt(srcW * scale));
        int dstH = Mathf.Max(1, Mathf.RoundToInt(srcH * scale));
        if (dstW == srcW && dstH == srcH)
            return source;

        try
        {
            Color32[] pixels = source.GetPixels32();
            if (pixels != null && pixels.Length >= srcW * srcH)
                return DownscalePixels(pixels, srcW, srcH, dstW, dstH);
        }
        catch (Exception)
        {
        }

        return DownscaleBlit(source, dstW, dstH);
    }

    private static Texture2D DownscalePixels(Color32[] pixels, int srcW, int srcH, int dstW, int dstH)
    {
        Color32[] dst = new Color32[dstW * dstH];
        for (int y = 0; y < dstH; y++)
        {
            int sy = Math.Min(srcH - 1, y * srcH / dstH);
            int row = sy * srcW;
            int dstRow = y * dstW;
            for (int x = 0; x < dstW; x++)
            {
                int sx = Math.Min(srcW - 1, x * srcW / dstW);
                dst[dstRow + x] = pixels[row + sx];
            }
        }
        Texture2D thumb = new Texture2D(dstW, dstH, TextureFormat.RGB24, false);
        thumb.SetPixels32(dst);
        thumb.Apply(false, false);
        return thumb;
    }

    private static Texture2D DownscaleBlit(Texture source, int dstW, int dstH)
    {
        RenderTexture rt = RenderTexture.GetTemporary(dstW, dstH, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        try
        {
            Graphics.Blit(source, rt);
            Texture2D thumb = new Texture2D(dstW, dstH, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            thumb.ReadPixels(new Rect(0f, 0f, dstW, dstH), 0, 0);
            thumb.Apply(false, false);
            return thumb;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    private static byte[] EncodeThumbnail(Texture2D thumb)
    {
        if (thumb == null)
            return null;
        byte[] jpg = thumb.EncodeToJPG(50);
        if (jpg != null && jpg.Length > 8192)
            jpg = thumb.EncodeToJPG(32);
        if (jpg != null && jpg.Length >= 32)
            return jpg;
        return thumb.EncodeToPNG();
    }

    private void UpdateGridCardThumbnailIfVisible(string path, Texture2D texture, string displayName)
    {
        if (string.IsNullOrEmpty(path) || _browserEntries == null)
            return;

        for (int i = 0; i < GridVisibleCards; i++)
        {
            int entryIndex = _gridOffset + i;
            if (entryIndex >= 0 && entryIndex < _browserEntries.Count)
            {
                VRWristFileEntry visibleEntry = _browserEntries[entryIndex];
                if (visibleEntry != null && string.Equals(visibleEntry.FullPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    VRWristGridCard card = _gridCards[i];
                    if (card != null)
                    {
                        if (texture != null)
                        {
                            if (card.ThumbnailImage != null)
                            {
                                card.ThumbnailImage.texture = texture;
                                card.ThumbnailImage.enabled = true;
                            }
                            if (card.LoadingText != null)
                                card.LoadingText.gameObject.SetActive(false);
                        }
                        else
                        {
                            if (card.LoadingText != null)
                            {
                                card.LoadingText.text = L("无图", "なし", "None");
                                card.LoadingText.gameObject.SetActive(true);
                            }
                        }
                        if (card.NameText != null && !string.IsNullOrEmpty(displayName))
                        {
                            card.NameText.text = EllipsizeMiddleForUi(displayName, 18);
                        }
                    }
                    break;
                }
            }
        }
    }

    private void HandleGridCardClick(int slotIndex)
    {
        if (_operationInProgress)
            return;

        int entryIndex = _gridOffset + slotIndex;
        if (_browserEntries == null || entryIndex < 0 || entryIndex >= _browserEntries.Count)
            return;

        VRWristFileEntry entry = _browserEntries[entryIndex];
        if (entry == null)
            return;
        if (entry.IsDirectory)
        {
            _browserDirectory = entry.FullPath;
            _browserOffset = 0;
            _gridOffset = 0;
            _selectedGridIndex = -1;
            _pendingGridCardPath = null;
            RefreshBrowserEntries();
            SetStatus(
                L("当前目录：", "現在のフォルダー：", "Current folder: ") + entry.FullPath,
                new Color(0.47f, 0.9f, 0.55f, 1f),
                4f);
            return;
        }

        string cardName = GetGridEntryDisplayName(entry);

        if (_gridQuickLoad)
        {
            _selectedGridIndex = slotIndex;
            _pendingGridCardPath = entry.FullPath;
            RefreshGridCardHighlights();

            if (_browserMode == BrowserMode.AddFemale || _browserMode == BrowserMode.AddMale)
            {
                SetStatus(
                    L("一键读取角色：", "即時読込中：", "Quick loading character: ") + cardName,
                    new Color(0.47f, 0.9f, 0.55f, 1f),
                    0f);
                StartCoroutine(ExecuteCharacterAddAfterFrame(entry.FullPath));
            }
            else if (_browserMode == BrowserMode.CoordinateCards)
            {
                OpenCoordinateCardPreview(entry.FullPath);
            }
            return;
        }

        if (_selectedGridIndex == slotIndex && string.Equals(_pendingGridCardPath, entry.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            if (_browserMode == BrowserMode.AddFemale || _browserMode == BrowserMode.AddMale)
            {
                SetStatus(
                    L("正在读取角色：", "キャラを読込中：", "Loading character: ") + cardName,
                    new Color(0.47f, 0.9f, 0.55f, 1f),
                    0f);
                StartCoroutine(ExecuteCharacterAddAfterFrame(entry.FullPath));
            }
            else if (_browserMode == BrowserMode.CoordinateCards)
            {
                OpenCoordinateCardPreview(entry.FullPath);
            }
            return;
        }

        _selectedGridIndex = slotIndex;
        _pendingGridCardPath = entry.FullPath;
        RefreshGridCardHighlights();

        SetStatus(
            L("已选定：", "選択：", "Selected: ")
            + cardName
            + L("；再按一次卡片或按【＋载入】", "；もう一度押すか【＋読込】", " ; click again or press [+Load]"),
            new Color(0.47f, 0.9f, 0.55f, 1f),
            5f);
    }

    private void RefreshGridCardHighlights()
    {
        for (int i = 0; i < GridVisibleCards; i++)
        {
            VRWristGridCard card = _gridCards[i];
            if (card == null || card.Background == null)
                continue;

            int entryIndex = _gridOffset + i;
            if (_browserEntries == null || entryIndex < 0 || entryIndex >= _browserEntries.Count)
                continue;

            VRWristFileEntry entry = _browserEntries[entryIndex];
            if (entry == null || entry.IsDirectory)
                continue;

            bool isSelected = (_selectedGridIndex == i)
                && string.Equals(_pendingGridCardPath, entry.FullPath, StringComparison.OrdinalIgnoreCase);

            card.ButtonTarget.SetColors(
                isSelected ? GridCardSelectedNormalColor : GridCardNormalColor,
                isSelected ? GridCardSelectedHoverColor : GridCardHoverColor);
            if (card.Outline != null)
            {
                card.Outline.effectColor = isSelected
                    ? new Color(0.35f, 0.95f, 1f, 0.95f)
                    : new Color(0.82f, 0.9f, 1f, 0.15f);
                card.Outline.effectDistance = isSelected
                    ? new Vector2(2f, -2f)
                    : new Vector2(1f, -1f);
            }
        }

        bool hasSelection = _selectedGridIndex >= 0 && !string.IsNullOrEmpty(_pendingGridCardPath);
        if (_gridActionLoadButton != null)
            _gridActionLoadButton.SetInteractable(hasSelection);
        if (_gridActionReplaceButton != null)
            _gridActionReplaceButton.SetInteractable(hasSelection);
    }

    private string GetGridEntryDisplayName(VRWristFileEntry entry)
    {
        if (entry == null)
            return "-";
        VRCardThumbnailItem<Texture2D> cached;
        if (_cardThumbnailCache != null && _cardThumbnailCache.TryGet(entry.FullPath, out cached) && !string.IsNullOrEmpty(cached.DisplayName))
            return cached.DisplayName;
        return NormalizeUiSingleLine(Path.GetFileNameWithoutExtension(entry.DisplayName));
    }

    private void HandleGridPrevPage()
    {
        ScrollGridPage(-1);
    }

    private void HandleGridNextPage()
    {
        ScrollGridPage(1);
    }

    private bool ScrollGridPage(int direction)
    {
        if (_browserEntries == null || _browserEntries.Count == 0)
            return false;

        int totalPages = Mathf.Max(1, (_browserEntries.Count + GridVisibleCards - 1) / GridVisibleCards);
        int maxOffset = Mathf.Max(0, (totalPages - 1) * GridVisibleCards);
        int nextOffset = Mathf.Clamp(_gridOffset + direction * GridVisibleCards, 0, maxOffset);
        if (nextOffset == _gridOffset)
            return false;

        _gridOffset = nextOffset;
        _browserOffset = nextOffset;
        _selectedGridIndex = -1;
        _pendingGridCardPath = null;
        RefreshBrowserGridVisuals();
        return true;
    }

    private void HandleGridQuickLoadToggle()
    {
        _gridQuickLoad = !_gridQuickLoad;
        RefreshBrowserGridVisuals();
        SetStatus(
            _gridQuickLoad
                ? L("已开启【一键直接载入】：点击卡片即刻加载至眼前", "【即時読込】有効：カードクリックで目の前に即生成", "[Quick Load] On: Click any card to spawn immediately")
                : L("已开启【点选确认模式】：点击选中，再次点击或按载入读取", "【選択確認】有効：クリックで選択、再クリックで読込", "[Select Mode] On: Click to select, click again or press load"),
            _gridQuickLoad ? new Color(0.35f, 1f, 0.62f, 1f) : new Color(0.47f, 0.9f, 0.55f, 1f),
            4f);
    }

    private void HandleGridActionLoad()
    {
        if (_operationInProgress)
            return;

        if (string.IsNullOrEmpty(_pendingGridCardPath))
        {
            SetStatus(
                L("请先点击选择角色卡", "キャラカードを選択してください", "Please select a character card first"),
                new Color(1f, 0.72f, 0.25f, 1f),
                4f);
            return;
        }

        if (_browserMode == BrowserMode.AddFemale || _browserMode == BrowserMode.AddMale)
        {
            string cardName = Path.GetFileNameWithoutExtension(_pendingGridCardPath);
            SetStatus(
                L("正在读取角色卡：", "キャラカードを読込中：", "Loading character card: ") + cardName,
                new Color(0.47f, 0.9f, 0.55f, 1f),
                0f);
            StartCoroutine(ExecuteCharacterAddAfterFrame(_pendingGridCardPath));
        }
        else if (_browserMode == BrowserMode.CoordinateCards)
        {
            OpenCoordinateCardPreview(_pendingGridCardPath);
        }
    }

    private void HandleGridActionReplace()
    {
        if (_operationInProgress)
            return;

        if (string.IsNullOrEmpty(_pendingGridCardPath))
        {
            SetStatus(
                L("请先点击选择角色卡", "キャラカードを選択してください", "Please select a character card first"),
                new Color(1f, 0.72f, 0.25f, 1f),
                4f);
            return;
        }

        if (_browserMode == BrowserMode.CoordinateCards)
        {
            OpenCoordinateCardPreview(_pendingGridCardPath);
            return;
        }

        _pendingCharacterCardPath = _pendingGridCardPath;
        HandleCharacterPreviewReplace();
    }
}
