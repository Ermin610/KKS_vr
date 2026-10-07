using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KKCharaStudioVR;

[Serializable]
public sealed class VRMmdDanceRatingEntry
{
    public string MotionName = string.Empty;
    public float Rating = 5.5f; // 1.0f ~ 9.0f
    public string Category = VRMmdDanceCategories.OtakuDance;
    public string Description = string.Empty;
    public int BPM = 0;
    public string CameraQuality = "普通"; // 优秀, 特化近景, 原版动摄, 普通, 无镜头
    public float DurationSeconds = 0f;
    public bool HasCamera = true;
    public bool HasAudio = true;
    public bool IsMoCap = false;
    public bool Favorite = false;
    public string RelativePath = string.Empty;
    public string FullPath = string.Empty;
    public int VmdCount = 1;
    public int UserRatingAdjust = 0;

    public string FormatBadge()
    {
        return "[" + Rating.ToString("0.0", CultureInfo.InvariantCulture) + "★]";
    }

    public string FormatCategoryShort()
    {
        if (Category == VRMmdDanceCategories.Masterpiece) return "神作";
        if (Category == VRMmdDanceCategories.MoCap) return "动捕";
        if (Category == VRMmdDanceCategories.HighEnergy) return "动感";
        if (Category == VRMmdDanceCategories.OtakuDance) return "宅舞";
        if (Category == VRMmdDanceCategories.GentleSlow) return "柔美";
        if (Category == VRMmdDanceCategories.SexyGroove) return "摇摆";
        if (Category == VRMmdDanceCategories.Gentlemen) return "绅士";
        return Category;
    }
}

public static class VRMmdDanceCategories
{
    public const string All = "全部";
    public const string Masterpiece = "精选神作";
    public const string OtakuDance = "热门宅舞";
    public const string HighEnergy = "高燃动感";
    public const string GentleSlow = "柔美慢节奏";
    public const string SexyGroove = "性感摇摆";
    public const string MoCap = "动捕写实";
    public const string Gentlemen = "绅士特选";

    public static readonly string[] DisplayCategories =
    {
        All,
        Masterpiece,
        OtakuDance,
        HighEnergy,
        GentleSlow,
        SexyGroove,
        MoCap,
        Gentlemen
    };

    public static string GetNext(string current)
    {
        for (int i = 0; i < DisplayCategories.Length; i++)
        {
            if (string.Equals(DisplayCategories[i], current, StringComparison.OrdinalIgnoreCase))
                return DisplayCategories[(i + 1) % DisplayCategories.Length];
        }
        return All;
    }

    public static string GetPrev(string current)
    {
        for (int i = 0; i < DisplayCategories.Length; i++)
        {
            if (string.Equals(DisplayCategories[i], current, StringComparison.OrdinalIgnoreCase))
                return DisplayCategories[(i - 1 + DisplayCategories.Length) % DisplayCategories.Length];
        }
        return All;
    }
}

public static class VRMmdHeuristicScorer
{
    /// <summary>
    /// Whole-word match for short ASCII keywords. A plain Contains("test") also
    /// hit "latest"/"contest" and Contains("ive") hit "active"/"creative", which
    /// mis-scored and mis-filed ordinary motion names.
    /// </summary>
    internal static bool HasToken(string text, string token)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token))
            return false;
        int from = 0;
        while (from <= text.Length - token.Length)
        {
            int idx = text.IndexOf(token, from, StringComparison.Ordinal);
            if (idx < 0)
                return false;
            bool leftOk = idx == 0 || !IsAsciiWordChar(text[idx - 1]);
            int endIdx = idx + token.Length;
            bool rightOk = endIdx >= text.Length || !IsAsciiWordChar(text[endIdx]);
            if (leftOk && rightOk)
                return true;
            from = idx + 1;
        }
        return false;
    }

    private static bool IsAsciiWordChar(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
    }

    public static float ClampRating(float rating)
    {
        if (float.IsNaN(rating) || float.IsInfinity(rating)) return 5.5f;
        if (rating < 1.0f) rating = 1.0f;
        if (rating > 9.0f) rating = 9.0f;
        return (float)Math.Round(rating, 1);
    }

    public static float CalculateScore(
        string name,
        string pathOrTags,
        bool hasCamera,
        bool hasAudio,
        uint motionFrames,
        uint morphFrames,
        bool isMoCapHint,
        string tagsHint)
    {
        float score = 5.5f;

        if (hasCamera)
            score += 0.8f;
        else
            score -= 0.6f;

        if (hasAudio)
            score += 0.6f;
        else
            score -= 0.5f;

        // Frames evaluation (standard 30 fps)
        if (motionFrames > 4500) // > 150s
            score += 0.7f;
        else if (motionFrames > 2700) // > 90s
            score += 0.5f;
        else if (motionFrames > 900) // > 30s
            score += 0.2f;
        else if (motionFrames > 0 && motionFrames < 450) // < 15s short fragment
            score -= 0.4f;

        if (morphFrames > 120)
            score += 0.4f;

        string combined = ((name ?? "") + " " + (pathOrTags ?? "") + " " + (tagsHint ?? "")).ToLowerInvariant();

        if (isMoCapHint || combined.Contains("动捕") || combined.Contains("mocap") || combined.Contains("羽毛满天飞"))
            score += 1.0f;

        if (combined.Contains("高质量候选") || combined.Contains("联网精选") || combined.Contains("60fps") || combined.Contains("4k"))
            score += 0.6f;

        // Masterpieces keyword bonus
        if (combined.Contains("极乐净土") || combined.Contains("gokuraku") ||
            combined.Contains("兔子洞") || combined.Contains("rabbit hole") || combined.Contains("ラビットホール") ||
            combined.Contains("lamb") || combined.Contains("gimme") || combined.Contains("sweet devil") ||
            combined.Contains("supernova") || combined.Contains("jump up") || combined.Contains("midas touch") ||
            combined.Contains("queencard") || combined.Contains("tomboy") || combined.Contains("wife") ||
            combined.Contains("how you like that") || combined.Contains("hi-fi raver") || combined.Contains("ch4nge") ||
            combined.Contains("うまぴょい") || combined.Contains("star-t-rain") || combined.Contains("宵々古今") ||
            combined.Contains("千本樱") || combined.Contains("千本櫻") || combined.Contains("学猫叫"))
        {
            score += 1.8f;
        }

        // Penalty keywords
        if (combined.Contains("异常待查") || combined.Contains("测试") || HasToken(combined, "test") ||
            HasToken(combined, "demo") || combined.Contains("半成品") || combined.Contains("未完成"))
        {
            score -= 1.2f;
        }

        return ClampRating(score);
    }

    public static string DetectCategory(string name, string pathOrFolder, float rating, bool isMoCap, string tagsHint)
    {
        string combined = ((name ?? "") + " " + (pathOrFolder ?? "") + " " + (tagsHint ?? "")).ToLowerInvariant();

        if (rating >= 8.5f || combined.Contains("神作") || combined.Contains("极乐净土") ||
            combined.Contains("兔子洞") || combined.Contains("lamb") || combined.Contains("gimme"))
        {
            return VRMmdDanceCategories.Masterpiece;
        }

        if (isMoCap || combined.Contains("02_动捕写实") || combined.Contains("动捕") || combined.Contains("mocap") ||
            combined.Contains("jump up") || combined.Contains("ch4nge") || combined.Contains("midas touch") ||
            combined.Contains("うまぴょい") || combined.Contains("star-t-rain"))
        {
            return VRMmdDanceCategories.MoCap;
        }

        if (combined.Contains("06_绅士特选") || combined.Contains("绅士") || combined.Contains("r18") ||
            combined.Contains("骑乘") || combined.Contains("色气") || combined.Contains("sweet devil"))
        {
            return VRMmdDanceCategories.Gentlemen;
        }

        if (combined.Contains("05_短视频循环") || combined.Contains("慢摇") || combined.Contains("摇摆") ||
            combined.Contains("抖音") || combined.Contains("tiktok") || combined.Contains("phut hon") || combined.Contains("卡点"))
        {
            return VRMmdDanceCategories.SexyGroove;
        }

        if (combined.Contains("古风") || combined.Contains("扇子") || combined.Contains("宵々古今") ||
            combined.Contains("千本樱") || combined.Contains("千本櫻") || combined.Contains("柔美") || combined.Contains("慢节奏"))
        {
            return VRMmdDanceCategories.GentleSlow;
        }

        if (combined.Contains("01_热舞流行") || combined.Contains("k-pop") || combined.Contains("女团") ||
            combined.Contains("aespa") || combined.Contains("blackpink") || HasToken(combined, "ive") ||
            HasToken(combined, "idle") || combined.Contains("tomboy") || combined.Contains("queencard") ||
            combined.Contains("wife") || combined.Contains("动感") || combined.Contains("电音") || combined.Contains("supernova"))
        {
            return VRMmdDanceCategories.HighEnergy;
        }

        return VRMmdDanceCategories.OtakuDance;
    }
}

public sealed class VRMmdDanceRatingDatabase
{
    public int Version = 1;
    public string LastUpdated = string.Empty;
    public List<VRMmdDanceRatingEntry> Entries = new List<VRMmdDanceRatingEntry>();

    private readonly Dictionary<string, VRMmdDanceRatingEntry> _byExactName =
        new Dictionary<string, VRMmdDanceRatingEntry>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VRMmdDanceRatingEntry> _byNormalizedKey =
        new Dictionary<string, VRMmdDanceRatingEntry>(StringComparer.OrdinalIgnoreCase);

    // Fuzzy matching used to normalise every candidate name (allocating a
    // StringBuilder each) on every lookup; the browser sort does two lookups per
    // comparison. The normalised names are computed once per index rebuild.
    private readonly List<KeyValuePair<string, VRMmdDanceRatingEntry>> _fuzzyKeys =
        new List<KeyValuePair<string, VRMmdDanceRatingEntry>>();
    private readonly Dictionary<string, VRMmdDanceRatingEntry> _fuzzyCache =
        new Dictionary<string, VRMmdDanceRatingEntry>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _fuzzyMisses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Names shorter than this never take part in substring matching, otherwise a
    // two-letter motion name would "contain-match" half the library.
    private const int MinFuzzyLength = 3;

    public void RebuildIndex()
    {
        _byExactName.Clear();
        _byNormalizedKey.Clear();
        _fuzzyKeys.Clear();
        ClearLookupCaches();

        foreach (var entry in Entries)
        {
            if (entry == null) continue;
            IndexEntry(entry);
        }
    }

    private void ClearLookupCaches()
    {
        _fuzzyCache.Clear();
        _fuzzyMisses.Clear();
    }

    private void IndexEntry(VRMmdDanceRatingEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.MotionName))
        {
            _byExactName[entry.MotionName] = entry;
            string normName = NormalizeKey(entry.MotionName);
            if (!string.IsNullOrEmpty(normName))
            {
                _byNormalizedKey[normName] = entry;
                _fuzzyKeys.Add(new KeyValuePair<string, VRMmdDanceRatingEntry>(normName, entry));
            }
        }
        if (!string.IsNullOrEmpty(entry.RelativePath))
        {
            _byExactName[entry.RelativePath] = entry;
            string normPath = NormalizeKey(entry.RelativePath);
            if (!string.IsNullOrEmpty(normPath))
                _byNormalizedKey[normPath] = entry;
        }
        if (!string.IsNullOrEmpty(entry.FullPath))
        {
            _byExactName[entry.FullPath] = entry;
        }
    }

    public void AddOrUpdate(VRMmdDanceRatingEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.MotionName))
            return;

        for (int i = 0; i < Entries.Count; i++)
        {
            VRMmdDanceRatingEntry existing = Entries[i];
            if (existing == null)
                continue;
            if (string.Equals(existing.MotionName, entry.MotionName, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(entry.RelativePath) && string.Equals(existing.RelativePath, entry.RelativePath, StringComparison.OrdinalIgnoreCase)))
            {
                Entries[i] = entry;
                // A replacement can drop keys the old entry owned; rebuild.
                RebuildIndex();
                return;
            }
        }

        Entries.Add(entry);
        // Appending only adds keys. Indexing just the new entry keeps bulk
        // imports (CSV seed, directory scan) linear instead of quadratic.
        IndexEntry(entry);
        ClearLookupCaches();
    }

    public bool TryGetEntry(string query, out VRMmdDanceRatingEntry entry)
    {
        entry = null;
        if (string.IsNullOrEmpty(query))
            return false;

        if (_byExactName.TryGetValue(query, out entry))
            return true;

        string norm = NormalizeKey(query);
        // A query with no letters or digits normalises to "", and "".Contains
        // matches everything. That used to return an arbitrary entry.
        if (norm.Length == 0)
        {
            entry = null;
            return false;
        }
        if (_byNormalizedKey.TryGetValue(norm, out entry))
            return true;

        if (_fuzzyCache.TryGetValue(norm, out entry))
            return true;
        if (_fuzzyMisses.Contains(norm))
        {
            entry = null;
            return false;
        }

        // Substring / fuzzy match
        for (int i = 0; i < _fuzzyKeys.Count; i++)
        {
            string candNorm = _fuzzyKeys[i].Key;
            if (Math.Min(candNorm.Length, norm.Length) < MinFuzzyLength)
                continue;
            if (candNorm.Contains(norm) || norm.Contains(candNorm))
            {
                entry = _fuzzyKeys[i].Value;
                _fuzzyCache[norm] = entry;
                return true;
            }
        }

        entry = null;
        _fuzzyMisses.Add(norm);
        return false;
    }

    public List<VRMmdDanceRatingEntry> GetFilteredEntries(
        string category,
        bool favoriteOnly = false,
        bool sortRatingDesc = true)
    {
        List<VRMmdDanceRatingEntry> result = new List<VRMmdDanceRatingEntry>();

        foreach (var e in Entries)
        {
            if (favoriteOnly && !e.Favorite)
                continue;

            if (!string.IsNullOrEmpty(category) && !string.Equals(category, VRMmdDanceCategories.All, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(category, VRMmdDanceCategories.Masterpiece, StringComparison.OrdinalIgnoreCase))
                {
                    if (e.Rating < 8.5f && !e.Favorite && !string.Equals(e.Category, VRMmdDanceCategories.Masterpiece, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                else
                {
                    if (!string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
            }

            result.Add(e);
        }

        if (sortRatingDesc)
        {
            result.Sort((a, b) =>
            {
                int rComp = b.Rating.CompareTo(a.Rating); // Descending (9.0 -> 1.0)
                if (rComp != 0) return rComp;
                int fComp = b.Favorite.CompareTo(a.Favorite); // Favorites first
                if (fComp != 0) return fComp;
                return StringComparer.CurrentCultureIgnoreCase.Compare(a.MotionName, b.MotionName);
            });
        }
        else
        {
            result.Sort((a, b) =>
                StringComparer.CurrentCultureIgnoreCase.Compare(a.MotionName, b.MotionName));
        }

        return result;
    }

    public VRMmdDanceRatingEntry AdjustRating(string nameOrPath, float delta)
    {
        VRMmdDanceRatingEntry entry;
        if (!TryGetEntry(nameOrPath, out entry))
        {
            entry = new VRMmdDanceRatingEntry
            {
                MotionName = SafeMotionName(nameOrPath),
                FullPath = nameOrPath,
                Rating = 5.5f,
                Category = VRMmdDanceCategories.OtakuDance
            };
            AddOrUpdate(entry);
        }

        entry.Rating = VRMmdHeuristicScorer.ClampRating(entry.Rating + delta);
        entry.UserRatingAdjust += (delta > 0 ? 1 : -1);
        if (entry.Rating >= 8.5f && entry.Category == VRMmdDanceCategories.OtakuDance)
        {
            entry.Category = VRMmdDanceCategories.Masterpiece;
        }
        return entry;
    }

    public VRMmdDanceRatingEntry ToggleFavorite(string nameOrPath)
    {
        VRMmdDanceRatingEntry entry;
        if (!TryGetEntry(nameOrPath, out entry))
        {
            entry = new VRMmdDanceRatingEntry
            {
                MotionName = SafeMotionName(nameOrPath),
                FullPath = nameOrPath,
                Rating = 8.5f,
                Category = VRMmdDanceCategories.Masterpiece,
                Favorite = true
            };
            AddOrUpdate(entry);
            return entry;
        }

        entry.Favorite = !entry.Favorite;
        if (entry.Favorite && entry.Rating < 8.5f)
        {
            entry.Rating = 8.5f;
            entry.Category = VRMmdDanceCategories.Masterpiece;
        }
        return entry;
    }

    private static string SafeMotionName(string nameOrPath)
    {
        if (string.IsNullOrEmpty(nameOrPath))
            return string.Empty;
        try
        {
            string name = Path.GetFileNameWithoutExtension(nameOrPath);
            return string.IsNullOrEmpty(name) ? nameOrPath : name;
        }
        catch (ArgumentException)
        {
            // Names with characters Path rejects (|, <, > ...) are still valid motion labels.
            return nameOrPath;
        }
    }

    public static string NormalizeKey(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        StringBuilder sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}

public static class VRMmdDanceRatingStore
{
    private static VRMmdDanceRatingDatabase _database;
    private static readonly object _lock = new object();
    public static string CustomDatabasePath = null;

    public static VRMmdDanceRatingDatabase Database
    {
        get
        {
            EnsureLoaded();
            return _database;
        }
    }

    public static void EnsureLoaded()
    {
        if (_database == null)
        {
            lock (_lock)
            {
                if (_database == null)
                {
                    LoadDatabase();
                }
            }
        }
    }

    public static string GetDatabaseFilePath()
    {
        if (!string.IsNullOrEmpty(CustomDatabasePath))
            return CustomDatabasePath;

        string root = ResolveGameRoot();
        return Path.Combine(root, "UserData", "VR", "MMD", "mmd_dance_ratings.json");
    }

    public static string ResolveGameRoot()
    {
        try
        {
            Type pathsType = Type.GetType("BepInEx.Paths, BepInEx");
            if (pathsType != null)
            {
                var prop = pathsType.GetProperty("GameRootPath");
                if (prop != null)
                {
                    string path = prop.GetValue(null, null) as string;
                    if (!string.IsNullOrEmpty(path))
                        return path;
                }
            }
        }
        catch { }

        if (Directory.Exists(@"E:\KKS\KoikatuSunshine"))
            return @"E:\KKS\KoikatuSunshine";

        return AppDomain.CurrentDomain.BaseDirectory;
    }

    public static void LoadDatabase(string customPath = null)
    {
        string filePath = customPath ?? GetDatabaseFilePath();
        lock (_lock)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath, Encoding.UTF8);
                    VRMmdDanceRatingDatabase loaded = DeserializeFromJson(json);
                    // A non-trivial file that yields no entries is damaged, not empty.
                    if (loaded.Entries.Count > 0 || json.Length < 64)
                    {
                        loaded.RebuildIndex();
                        // Publish only a fully built database. EnsureLoaded checks
                        // _database outside the lock, so a half-indexed instance
                        // must never be visible to another thread.
                        _database = loaded;
                        return;
                    }
                    PreserveUnreadableDatabase(filePath, "no entries could be parsed");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[VRMmd] Failed to load JSON: " + ex.Message);
                    PreserveUnreadableDatabase(filePath, ex.Message);
                }
            }

            // Database file doesn't exist yet: initialize with seed
            VRMmdDanceRatingDatabase seeded = new VRMmdDanceRatingDatabase();
            SeedBuiltInMasterpieces(seeded);

            // Attempt seeding from E:\action\动作全量分类索引.csv if present
            string csvPath = @"E:\action\动作全量分类索引.csv";
            if (File.Exists(csvPath))
            {
                SeedFromCsv(seeded, csvPath);
            }

            seeded.RebuildIndex();
            _database = seeded;
            SaveDatabase(filePath);
        }
    }

    /// <summary>
    /// A ratings file that cannot be read must not be overwritten by the fresh
    /// seed (the .bak rotation in SaveDatabase would then destroy the last copy).
    /// Move it aside so the user's data can still be recovered by hand.
    /// </summary>
    private static void PreserveUnreadableDatabase(string filePath, string reason)
    {
        try
        {
            string aside = filePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Copy(filePath, aside, true);
            Console.WriteLine("[VRMmd] Unreadable ratings file kept as " + aside + " (" + reason + ")");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[VRMmd] Could not preserve unreadable ratings file: " + ex.Message);
        }
    }

    public static void SaveDatabase(string customPath = null, VRMmdDanceRatingDatabase dbToSave = null)
    {
        string filePath = customPath ?? GetDatabaseFilePath();
        lock (_lock)
        {
            VRMmdDanceRatingDatabase targetDb = dbToSave ?? _database;
            if (targetDb == null)
            {
                EnsureLoaded();
                targetDb = _database;
            }
            if (targetDb == null)
                return;

            try
            {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = SerializeToJson(targetDb);
                string tempFile = filePath + ".tmp";
                File.WriteAllText(tempFile, json, Encoding.UTF8);

                if (File.Exists(filePath))
                {
                    string bakFile = filePath + ".bak";
                    try { File.Copy(filePath, bakFile, true); } catch { }
                }
                // Overwrite in place instead of delete + move: a crash between
                // those two steps used to leave no ratings file at all.
                File.Copy(tempFile, filePath, true);
                try { File.Delete(tempFile); } catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[VRMmd] Failed to save JSON: " + ex.Message);
            }
        }
    }

    public static string FormatBadge(string displayName, string fullPath)
    {
        EnsureLoaded();
        VRMmdDanceRatingEntry entry;
        if (_database != null && (_database.TryGetEntry(displayName, out entry) || _database.TryGetEntry(fullPath, out entry)))
        {
            return entry.FormatBadge() + " ";
        }
        return string.Empty;
    }

    public static int ScanDirectory(string rootDirectory, bool forceRescore = false)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(rootDirectory) || !Directory.Exists(rootDirectory))
            return 0;

        int addedCount = 0;
        try
        {
            string[] subDirs = Directory.GetDirectories(rootDirectory);
            foreach (string dir in subDirs)
            {
                string folderName = Path.GetFileName(dir);
                if (folderName.StartsWith(".") || folderName.StartsWith("_"))
                    continue;

                VRMmdDanceRatingEntry existing;
                if (!forceRescore && _database.TryGetEntry(folderName, out existing))
                    continue;

                // Inspect files in directory
                string[] vmds = Directory.GetFiles(dir, "*.vmd");
                if (vmds.Length == 0)
                    continue;

                bool hasCamera = false;
                bool hasAudio = Directory.GetFiles(dir, "*.wav").Length > 0
                    || Directory.GetFiles(dir, "*.mp3").Length > 0
                    || Directory.GetFiles(dir, "*.ogg").Length > 0;
                uint maxMotionFrames = 0;
                uint maxMorphFrames = 0;

                foreach (string vmd in vmds)
                {
                    VRVmdMetadata meta;
                    if (VRWristFileCatalog.TryReadVmdMetadata(vmd, out meta))
                    {
                        if (meta.HasCameraData) hasCamera = true;
                        if (meta.MotionFrames > maxMotionFrames) maxMotionFrames = meta.MotionFrames;
                        if (meta.MorphFrames > maxMorphFrames) maxMorphFrames = meta.MorphFrames;
                    }
                }

                float score = VRMmdHeuristicScorer.CalculateScore(
                    folderName,
                    dir,
                    hasCamera,
                    hasAudio,
                    maxMotionFrames,
                    maxMorphFrames,
                    false,
                    null);

                string category = VRMmdHeuristicScorer.DetectCategory(
                    folderName,
                    dir,
                    score,
                    false,
                    null);

                VRMmdDanceRatingEntry entry = new VRMmdDanceRatingEntry
                {
                    MotionName = folderName,
                    FullPath = dir,
                    RelativePath = folderName,
                    Rating = score,
                    Category = category,
                    HasCamera = hasCamera,
                    HasAudio = hasAudio,
                    VmdCount = vmds.Length,
                    DurationSeconds = maxMotionFrames / 30f,
                    Description = "启发式自动打分：" + score.ToString("0.0", CultureInfo.InvariantCulture) + "分"
                };

                _database.AddOrUpdate(entry);
                addedCount++;
            }

            if (addedCount > 0)
            {
                SaveDatabase();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[VRMmd] Scan directory error: " + ex.Message);
        }

        return addedCount;
    }

    public static int SeedFromCsv(VRMmdDanceRatingDatabase db, string csvPath)
    {
        if (!File.Exists(csvPath))
            return 0;

        int count = 0;
        try
        {
            string[] lines = File.ReadAllLines(csvPath, Encoding.UTF8);
            if (lines.Length <= 1) return 0;

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                List<string> columns = ParseCsvRow(line);
                if (columns.Count < 14) continue;

                // Headers: 0:包ID, 1:一级分类, 2:二级分类, 3:动作包名, 4:当前完整路径, 5:时长秒, 6:是否动捕, 7:VMD总数, 8:身体VMD, 9:相机VMD, 10:表情VMD, 11:音频文件数, 12:综合评分, 13:原始来源, 14:标签
                string topCategory = columns[1];
                string motionName = columns[3];
                string fullPath = columns[4];
                float duration = 0f;
                float.TryParse(columns[5], NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
                bool isMoCap = string.Equals(columns[6], "是", StringComparison.OrdinalIgnoreCase);
                int vmdCount = 1;
                int.TryParse(columns[7], out vmdCount);
                int camCount = 0;
                int.TryParse(columns[9], out camCount);
                int audioCount = 0;
                int.TryParse(columns[11], out audioCount);
                float csvScore = 55f;
                float.TryParse(columns[12], NumberStyles.Float, CultureInfo.InvariantCulture, out csvScore);
                string tags = columns.Count > 14 ? columns[14] : string.Empty;

                float rating = 5.0f + (csvScore - 50.0f) * 0.1f;
                if (tags.Contains("高质量候选") || tags.Contains("联网精选试用"))
                    rating += 0.8f;
                if (isMoCap)
                    rating += 1.0f;
                if (tags.Contains("缺相机"))
                    rating -= 0.8f;
                if (tags.Contains("缺音频"))
                    rating -= 0.6f;
                if (tags.Contains("异常待查"))
                    rating -= 0.8f;

                bool isProtectedUserFavorite = string.Equals(topCategory, "听过", StringComparison.OrdinalIgnoreCase);
                if (isProtectedUserFavorite)
                {
                    rating = Math.Max(8.5f, rating + 1.5f);
                }

                rating = VRMmdHeuristicScorer.ClampRating(rating);
                string category = VRMmdHeuristicScorer.DetectCategory(motionName, topCategory, rating, isMoCap, tags);

                VRMmdDanceRatingEntry entry = new VRMmdDanceRatingEntry
                {
                    MotionName = motionName,
                    FullPath = fullPath,
                    RelativePath = motionName,
                    Rating = rating,
                    Category = category,
                    DurationSeconds = duration,
                    IsMoCap = isMoCap,
                    HasCamera = camCount > 0,
                    HasAudio = audioCount > 0,
                    VmdCount = vmdCount,
                    Favorite = isProtectedUserFavorite || rating >= 8.8f,
                    Description = isProtectedUserFavorite ? "用户常用高频听过精选" : (tags.Length > 0 ? tags : "全量分类索引收纳")
                };

                db.AddOrUpdate(entry);
                count++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[VRMmd] CSV parse error: " + ex.Message);
        }

        return count;
    }

    private static List<string> ParseCsvRow(string row)
    {
        List<string> cols = new List<string>();
        bool inQuotes = false;
        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < row.Length; i++)
        {
            char c = row[i];
            if (c == '\"')
            {
                if (inQuotes && i + 1 < row.Length && row[i + 1] == '\"')
                {
                    sb.Append('\"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                cols.Add(sb.ToString().Trim());
                sb.Length = 0;
            }
            else
            {
                sb.Append(c);
            }
        }
        cols.Add(sb.ToString().Trim());
        return cols;
    }

    public static void SeedBuiltInMasterpieces(VRMmdDanceRatingDatabase db)
    {
        // ACG S+ Classics & Top recommendations
        AddSeed(db, "極楽浄土（３人組）", 9.0f, VRMmdDanceCategories.Masterpiece, "ACG传世神作，蝴蝶步与扇子舞华丽3人舞台", "优秀", 218f, true, true, false, true);
        AddSeed(db, "兔子洞ラビットホール", 9.0f, VRMmdDanceCategories.Masterpiece, "百亿级播放顶流，魔性兔耳与高频扭胯近景互动", "特化近景", 161f, true, true, false, true);
        AddSeed(db, "【34】Lamb", 9.0f, VRMmdDanceCategories.Masterpiece, "yurie优雅与帅气巅峰，手部细节顺滑，骨骼表现力极佳", "优秀", 239f, true, true, false, true);
        AddSeed(db, "Gimme Gimme Gimme", 9.0f, VRMmdDanceCategories.Masterpiece, "电音双人/单人神曲，身体Wave律动张力十足", "优秀", 140f, true, true, false, true);
        AddSeed(db, "[普通]_Hai Phut Hon - Full Dance Motion By MEI 抖音摇摆", 9.0f, VRMmdDanceCategories.SexyGroove, "短视频现象级卡点，极致侧面扭胯与节奏摆动", "特化近景", 68f, true, true, false, true);
        AddSeed(db, "JUMP UP", 8.9f, VRMmdDanceCategories.MoCap, "nico动捕技术标杆，羽毛满天飞运镜，重心起伏肌肉回弹逼真", "原版动摄", 134f, true, true, true, true);
        AddSeed(db, "aespa - Supernova", 8.8f, VRMmdDanceCategories.HighEnergy, "超自然力量感，29.7fps高密度动捕齐舞卡点", "优秀", 178f, true, true, true, true);
        AddSeed(db, "【MMDモーショントレース】うまぴょい伝説", 8.8f, VRMmdDanceCategories.MoCap, "赛马娘萌系活力巅峰，可爱跳步与萌系互动", "优秀", 108f, true, true, true, true);
        AddSeed(db, "KISS OF LIFE - Midas Touch [2.0 ver]", 8.7f, VRMmdDanceCategories.MoCap, "5代女团顶级骨盆律动，真人动捕无漂浮感，身体物理真实", "优秀", 165f, true, true, true, true);
        AddSeed(db, "[MMD cover] BLACKPINK - How You Like That (4ver.)", 8.7f, VRMmdDanceCategories.HighEnergy, "霸气女团气场，Jennie/Jisoo/Lisa/Rosé 4人独立舞台齐舞", "优秀", 182f, true, true, false, true);
        AddSeed(db, "(G)I-DLE - Wife", 8.6f, VRMmdDanceCategories.HighEnergy, "独特律动，卡点极佳，全网魔性摇摆舞曲", "优秀", 122f, true, true, false, true);
        AddSeed(db, "CH4NGE", 8.6f, VRMmdDanceCategories.MoCap, "Giga-P动捕神曲，街舞元素与手势速切细腻平滑", "原版动摄", 135f, true, true, true, true);
        AddSeed(db, "Yoiyoi Kokon 宵々古今 【Motion by Ureshiiiiii】", 8.6f, VRMmdDanceCategories.GentleSlow, "和风电音与扇子舞步，动作连贯优美，4分半长视觉盛宴", "优秀", 267f, true, true, false, true);
        AddSeed(db, "aespa - Whiplash  R18镜头", 8.6f, VRMmdDanceCategories.Gentlemen, "强烈的电子节拍与颈腰律动，特化低机位微仰视冲击力极强", "特化近景", 185f, true, true, false, true);
        AddSeed(db, "(G)I-DLE - Queencard", 8.5f, VRMmdDanceCategories.HighEnergy, "欢快自信摇摆编舞，洗脑副歌，舞台展现力十足", "优秀", 163f, true, true, false, true);
        AddSeed(db, "IVE - AFTER LIKE", 8.5f, VRMmdDanceCategories.HighEnergy, "清爽华丽迪斯科电音，欢快轻盈步伐与自然肢体力学", "优秀", 128f, true, true, true, true);
        AddSeed(db, "Hi-Fi Raver (ハイファイレイヴァー)表情好看", 8.5f, VRMmdDanceCategories.OtakuDance, "元气阳光系天花板，充满活力的跳跃、挥手与眼神表情", "优秀", 200f, true, true, false, true);
        AddSeed(db, "sweet devil h~~", 8.5f, VRMmdDanceCategories.Gentlemen, "小恶魔系经典，绅士视角运镜特写，挑逗感拉满", "特化近景", 271f, true, true, false, true);
        AddSeed(db, "B Komachi - STAR-T-RAIN", 8.5f, VRMmdDanceCategories.MoCap, "我推的孩子纯正日系偶像现场动捕，手势走位充满元气", "优秀", 86f, true, true, true, true);
        AddSeed(db, "いーあるふぁんくらぶ ai fanclub", 8.5f, VRMmdDanceCategories.MoCap, "经典欢快曲目，长达4分钟流畅动捕，节奏轻快跳步", "优秀", 244f, true, true, true, true);
        AddSeed(db, "CHUNG HA - Dream of You", 8.5f, VRMmdDanceCategories.HighEnergy, "金请夏Solo舞台女王教科书级编舞，线条柔美富有张力", "优秀", 191f, true, true, false, true);
        AddSeed(db, "【55】千本櫻", 8.5f, VRMmdDanceCategories.GentleSlow, "ACG传世经典，优雅和风扇子律动与经典步伐", "优秀", 240f, true, true, false, true);
        AddSeed(db, "202——学猫叫", 8.5f, VRMmdDanceCategories.OtakuDance, "萌系可爱动作，经典猫爪手势与甜美互动", "优秀", 90f, true, true, false, true);
        AddSeed(db, "极乐净土_简单骑乘啪", 8.2f, VRMmdDanceCategories.Gentlemen, "绅士互动特选动作，节奏律动与亲密姿态", "特化近景", 180f, true, true, false, true);
    }

    private static void AddSeed(
        VRMmdDanceRatingDatabase db,
        string name,
        float rating,
        string category,
        string desc,
        string cameraQuality,
        float duration,
        bool hasCam,
        bool hasAud,
        bool isMocap,
        bool favorite)
    {
        db.AddOrUpdate(new VRMmdDanceRatingEntry
        {
            MotionName = name,
            Rating = rating,
            Category = category,
            Description = desc,
            CameraQuality = cameraQuality,
            DurationSeconds = duration,
            HasCamera = hasCam,
            HasAudio = hasAud,
            IsMoCap = isMocap,
            Favorite = favorite,
            RelativePath = name
        });
    }

    public static string SerializeToJson(VRMmdDanceRatingDatabase db)
    {
        StringBuilder sb = new StringBuilder(1024 * 32);
        sb.AppendLine("{");
        sb.Append("  \"version\": ").Append(db.Version).Append(",\n");
        sb.Append("  \"lastUpdated\": \"").Append(DateTime.UtcNow.ToString("o")).Append("\",\n");
        sb.AppendLine("  \"entries\": [");

        for (int i = 0; i < db.Entries.Count; i++)
        {
            var e = db.Entries[i];
            sb.Append("    {");
            sb.Append("\"motionName\": \"").Append(EscapeJson(e.MotionName)).Append("\", ");
            sb.Append("\"rating\": ").Append(e.Rating.ToString("0.0", CultureInfo.InvariantCulture)).Append(", ");
            sb.Append("\"category\": \"").Append(EscapeJson(e.Category)).Append("\", ");
            sb.Append("\"description\": \"").Append(EscapeJson(e.Description)).Append("\", ");
            sb.Append("\"bpm\": ").Append(e.BPM).Append(", ");
            sb.Append("\"cameraQuality\": \"").Append(EscapeJson(e.CameraQuality)).Append("\", ");
            sb.Append("\"durationSeconds\": ").Append(e.DurationSeconds.ToString("0.0", CultureInfo.InvariantCulture)).Append(", ");
            sb.Append("\"hasCamera\": ").Append(e.HasCamera ? "true" : "false").Append(", ");
            sb.Append("\"hasAudio\": ").Append(e.HasAudio ? "true" : "false").Append(", ");
            sb.Append("\"isMoCap\": ").Append(e.IsMoCap ? "true" : "false").Append(", ");
            sb.Append("\"favorite\": ").Append(e.Favorite ? "true" : "false").Append(", ");
            sb.Append("\"relativePath\": \"").Append(EscapeJson(e.RelativePath)).Append("\", ");
            sb.Append("\"fullPath\": \"").Append(EscapeJson(e.FullPath)).Append("\", ");
            sb.Append("\"vmdCount\": ").Append(e.VmdCount).Append(", ");
            sb.Append("\"userRatingAdjust\": ").Append(e.UserRatingAdjust);
            sb.Append("}");
            if (i < db.Entries.Count - 1) sb.Append(",");
            sb.AppendLine();
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    public static VRMmdDanceRatingDatabase DeserializeFromJson(string json)
    {
        VRMmdDanceRatingDatabase db = new VRMmdDanceRatingDatabase();
        if (string.IsNullOrEmpty(json))
            return db;

        int entriesIdx = json.IndexOf("\"entries\"", StringComparison.OrdinalIgnoreCase);
        if (entriesIdx < 0)
            return db;

        int arrayStart = json.IndexOf('[', entriesIdx);
        if (arrayStart < 0)
            return db;

        int arrayEnd = json.LastIndexOf(']');
        if (arrayEnd <= arrayStart)
            return db;

        string arrayBody = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
        int pos = 0;

        while (pos < arrayBody.Length)
        {
            int objStart = arrayBody.IndexOf('{', pos);
            if (objStart < 0) break;
            int objEnd = FindMatchingBrace(arrayBody, objStart);
            if (objEnd < 0) break;

            string objJson = arrayBody.Substring(objStart, objEnd - objStart + 1);
            VRMmdDanceRatingEntry entry = ParseEntryObject(objJson);
            if (entry != null && !string.IsNullOrEmpty(entry.MotionName))
            {
                db.Entries.Add(entry);
            }

            pos = objEnd + 1;
        }

        return db;
    }

    private static int FindMatchingBrace(string text, int openIndex)
    {
        int depth = 0;
        bool inQuote = false;
        bool escaped = false;
        for (int i = openIndex; i < text.Length; i++)
        {
            char c = text[i];
            // Track escapes explicitly: a string ending in an escaped backslash
            // (a path such as "E:\\dance\\") must still close at its quote.
            if (inQuote && escaped)
            {
                escaped = false;
                continue;
            }
            if (inQuote && c == '\\')
            {
                escaped = true;
                continue;
            }
            if (c == '\"')
            {
                inQuote = !inQuote;
            }
            else if (!inQuote)
            {
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
        }
        return -1;
    }

    private static VRMmdDanceRatingEntry ParseEntryObject(string objJson)
    {
        VRMmdDanceRatingEntry entry = new VRMmdDanceRatingEntry();

        entry.MotionName = ExtractStringField(objJson, "motionName");
        entry.Category = ExtractStringField(objJson, "category") ?? VRMmdDanceCategories.OtakuDance;
        entry.Description = ExtractStringField(objJson, "description") ?? string.Empty;
        entry.CameraQuality = ExtractStringField(objJson, "cameraQuality") ?? "普通";
        entry.RelativePath = ExtractStringField(objJson, "relativePath") ?? string.Empty;
        entry.FullPath = ExtractStringField(objJson, "fullPath") ?? string.Empty;

        float rating;
        if (TryExtractFloatField(objJson, "rating", out rating))
            entry.Rating = VRMmdHeuristicScorer.ClampRating(rating);

        float duration;
        if (TryExtractFloatField(objJson, "durationSeconds", out duration))
            entry.DurationSeconds = duration;

        int bpm;
        if (TryExtractIntField(objJson, "bpm", out bpm))
            entry.BPM = bpm;

        int vmdCount;
        if (TryExtractIntField(objJson, "vmdCount", out vmdCount))
            entry.VmdCount = vmdCount;

        int userAdjust;
        if (TryExtractIntField(objJson, "userRatingAdjust", out userAdjust))
            entry.UserRatingAdjust = userAdjust;

        bool hasCamera;
        if (TryExtractBoolField(objJson, "hasCamera", out hasCamera))
            entry.HasCamera = hasCamera;

        bool hasAudio;
        if (TryExtractBoolField(objJson, "hasAudio", out hasAudio))
            entry.HasAudio = hasAudio;

        bool isMoCap;
        if (TryExtractBoolField(objJson, "isMoCap", out isMoCap))
            entry.IsMoCap = isMoCap;

        bool favorite;
        if (TryExtractBoolField(objJson, "favorite", out favorite))
            entry.Favorite = favorite;

        return entry;
    }

    private static string ExtractStringField(string json, string key)
    {
        string pattern = "\"" + key + "\"";
        int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        int colon = json.IndexOf(':', idx + pattern.Length);
        if (colon < 0) return null;

        int quoteStart = json.IndexOf('\"', colon + 1);
        if (quoteStart < 0) return null;

        StringBuilder sb = new StringBuilder();
        for (int i = quoteStart + 1; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '\\' && i + 1 < json.Length)
            {
                char next = json[++i];
                if (next == '\"') sb.Append('\"');
                else if (next == '\\') sb.Append('\\');
                else if (next == 'n') sb.Append('\n');
                else if (next == 'r') sb.Append('\r');
                else if (next == 't') sb.Append('\t');
                else if (next == 'u' && i + 4 < json.Length)
                {
                    int code;
                    if (int.TryParse(json.Substring(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code))
                    {
                        sb.Append((char)code);
                        i += 4;
                    }
                    else
                    {
                        sb.Append(next);
                    }
                }
                else sb.Append(next);
            }
            else if (c == '\"')
            {
                return sb.ToString();
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static bool TryExtractFloatField(string json, string key, out float value)
    {
        value = 0f;
        string token = ExtractRawValue(json, key);
        if (token == null) return false;
        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryExtractIntField(string json, string key, out int value)
    {
        value = 0;
        string token = ExtractRawValue(json, key);
        if (token == null) return false;
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryExtractBoolField(string json, string key, out bool value)
    {
        value = false;
        string token = ExtractRawValue(json, key);
        if (token == null) return false;
        return bool.TryParse(token, out value);
    }

    private static string ExtractRawValue(string json, string key)
    {
        string pattern = "\"" + key + "\"";
        int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        int colon = json.IndexOf(':', idx + pattern.Length);
        if (colon < 0) return null;

        int start = colon + 1;
        while (start < json.Length && char.IsWhiteSpace(json[start]))
            start++;

        int end = start;
        while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\n' && json[end] != '\r')
            end++;

        return json.Substring(start, end - start).Trim().Trim('\"');
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        StringBuilder sb = new StringBuilder(s.Length + 4);
        foreach (char c in s)
        {
            if (c == '\\') sb.Append("\\\\");
            else if (c == '\"') sb.Append("\\\"");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
