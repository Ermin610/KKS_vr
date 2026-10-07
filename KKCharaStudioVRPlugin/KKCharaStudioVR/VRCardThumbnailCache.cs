using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KKCharaStudioVR;

internal sealed class VRCardThumbnailItem<T> where T : class
{
    public string Path { get; set; }
    public T Texture { get; set; }
    public string DisplayName { get; set; }
    public long FileSize { get; set; }
}

internal sealed class VRCardThumbnailCache<T> where T : class
{
    private readonly int _capacity;
    private readonly Action<T> _onDestroy;
    private readonly Dictionary<string, LinkedListNode<VRCardThumbnailItem<T>>> _map;
    private readonly LinkedList<VRCardThumbnailItem<T>> _list;

    public VRCardThumbnailCache(int capacity = 36, Action<T> onDestroy = null)
    {
        _capacity = Math.Max(6, capacity);
        _onDestroy = onDestroy;
        _map = new Dictionary<string, LinkedListNode<VRCardThumbnailItem<T>>>(StringComparer.OrdinalIgnoreCase);
        _list = new LinkedList<VRCardThumbnailItem<T>>();
    }

    public int Count => _map.Count;
    public int Capacity => _capacity;

    public bool TryGet(string path, out VRCardThumbnailItem<T> item)
    {
        if (string.IsNullOrEmpty(path))
        {
            item = null;
            return false;
        }

        if (_map.TryGetValue(path, out var node))
        {
            _list.Remove(node);
            _list.AddFirst(node);
            item = node.Value;
            return true;
        }

        item = null;
        return false;
    }

    public void Put(string path, T texture, string displayName, long fileSize = 0)
    {
        if (string.IsNullOrEmpty(path))
        {
            // The cache owns the texture once it is handed over. Rejecting the
            // key must not strand a native texture that nobody else references.
            if (texture != null)
                _onDestroy?.Invoke(texture);
            return;
        }

        if (_map.TryGetValue(path, out var existingNode))
        {
            _list.Remove(existingNode);
            if (existingNode.Value.Texture != null && !ReferenceEquals(existingNode.Value.Texture, texture))
            {
                _onDestroy?.Invoke(existingNode.Value.Texture);
            }
            existingNode.Value.Texture = texture;
            existingNode.Value.DisplayName = displayName;
            existingNode.Value.FileSize = fileSize;
            _list.AddFirst(existingNode);
            return;
        }

        while (_list.Count >= _capacity)
        {
            var lru = _list.Last;
            _list.RemoveLast();
            _map.Remove(lru.Value.Path);
            if (lru.Value.Texture != null)
            {
                _onDestroy?.Invoke(lru.Value.Texture);
                lru.Value.Texture = null;
            }
        }

        var newItem = new VRCardThumbnailItem<T>
        {
            Path = path,
            Texture = texture,
            DisplayName = displayName,
            FileSize = fileSize
        };
        var newNode = new LinkedListNode<VRCardThumbnailItem<T>>(newItem);
        _list.AddFirst(newNode);
        _map[path] = newNode;
    }

    public bool Remove(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        if (_map.TryGetValue(path, out var node))
        {
            _list.Remove(node);
            _map.Remove(path);
            if (node.Value.Texture != null)
            {
                _onDestroy?.Invoke(node.Value.Texture);
                node.Value.Texture = null;
            }
            return true;
        }
        return false;
    }

    public void Clear()
    {
        foreach (var item in _list)
        {
            if (item.Texture != null)
            {
                _onDestroy?.Invoke(item.Texture);
                item.Texture = null;
            }
        }
        _list.Clear();
        _map.Clear();
    }
}

// Persistent 160px JPEG/PNG thumbs under UserData/VR/Thumbnails.
// Lookup uses file length + UTC write time, so reopening the wrist menu
// does not hash or decode the multi-megabyte character-card PNG.
// The file name still carries the card's content MD5 and that write time.
internal static class VRCardThumbnailDiskCache
{
    public const int ThumbnailEdge = 160;
    private static readonly object Gate = new object();
    private static readonly Dictionary<string, Entry> Entries =
        new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;
    private static string _loadedDirectory;
    private static bool _manifestDirty;
    private static DateTime _lastManifestSaveUtc = DateTime.MinValue;
    private static readonly TimeSpan ManifestSaveInterval = TimeSpan.FromSeconds(3);

    private sealed class Entry
    {
        public long Length;
        public long Ticks;
        public string RelativePath;
        public string DisplayName;
    }

    public static string CacheDirectoryOverride { get; set; }

    public static void ResetForTests(string directory)
    {
        lock (Gate)
        {
            FlushLocked();
            CacheDirectoryOverride = directory;
            Entries.Clear();
            _loaded = false;
            _loadedDirectory = null;
            _manifestDirty = false;
        }
    }

    public static void ForgetMemoryIndex()
    {
        lock (Gate)
        {
            FlushLocked();
            Entries.Clear();
            _loaded = false;
            _loadedDirectory = null;
            _manifestDirty = false;
        }
    }

    /// <summary>
    /// Writes a pending manifest. Thumbnail writes batch their index updates (a
    /// library of thousands of cards used to rewrite the whole index per card),
    /// so callers flush at the end of a batch and when the menu goes away.
    /// </summary>
    public static void Flush()
    {
        lock (Gate)
        {
            FlushLocked();
        }
    }

    private static void FlushLocked()
    {
        if (!_manifestDirty)
            return;
        try
        {
            SaveManifest();
            _manifestDirty = false;
            _lastManifestSaveUtc = DateTime.UtcNow;
        }
        catch (Exception)
        {
            // Keep the dirty flag so the next flush retries.
        }
    }

    public static string GetCacheDirectory()
    {
        if (!string.IsNullOrEmpty(CacheDirectoryOverride))
            return CacheDirectoryOverride;
        return Path.Combine(VRMmdDanceRatingStore.ResolveGameRoot(), "UserData", "VR", "Thumbnails");
    }

    public static string ComputeMd5(string path)
    {
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (MD5 md5 = MD5.Create())
        {
            return ToHex(md5.ComputeHash(stream));
        }
    }

    public static string BuildCacheFileName(string contentMd5, long lastWriteUtcTicks, bool png)
    {
        string hash = string.IsNullOrEmpty(contentMd5) ? "00000000000000000000000000000000" : contentMd5.ToLowerInvariant();
        string extension = png ? ".png" : ".jpg";
        return hash + "_" + lastWriteUtcTicks.ToString(CultureInfo.InvariantCulture) + extension;
    }

    public static bool IsFresh(string sourcePath)
    {
        string ignored;
        return TryGetFreshEntry(sourcePath, out ignored);
    }

    public static bool TryRead(string sourcePath, out byte[] bytes, out string displayName)
    {
        bytes = null;
        displayName = null;
        string relative;
        if (!TryGetFreshEntry(sourcePath, out relative))
            return false;

        try
        {
            string full = Path.Combine(GetCacheDirectory(), relative);
            if (!File.Exists(full))
                return false;
            bytes = File.ReadAllBytes(full);
            if (bytes == null || bytes.Length == 0)
            {
                bytes = null;
                return false;
            }

            lock (Gate)
            {
                Entry entry;
                if (Entries.TryGetValue(Normalize(sourcePath), out entry))
                    displayName = entry.DisplayName;
            }
            return true;
        }
        catch (Exception)
        {
            bytes = null;
            displayName = null;
            return false;
        }
    }

    public static bool Write(string sourcePath, byte[] encoded, string contentMd5, string displayName)
    {
        if (string.IsNullOrEmpty(sourcePath) || encoded == null || encoded.Length == 0)
            return false;
        if (!File.Exists(sourcePath))
            return false;

        try
        {
            FileInfo info = new FileInfo(sourcePath);
            long ticks = info.LastWriteTimeUtc.Ticks;
            long length = info.Length;
            string md5 = string.IsNullOrEmpty(contentMd5) ? ComputeMd5(sourcePath) : contentMd5.ToLowerInvariant();
            bool png = encoded.Length >= 4
                && encoded[0] == 0x89
                && encoded[1] == 0x50
                && encoded[2] == 0x4E
                && encoded[3] == 0x47;
            string fileName = BuildCacheFileName(md5, ticks, png);
            string relative = Path.Combine(fileName.Substring(0, 2), fileName);
            string directory = GetCacheDirectory();
            string full = Path.Combine(directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, encoded);

            lock (Gate)
            {
                EnsureLoaded();
                string key = Normalize(sourcePath);
                Entry previous;
                if (Entries.TryGetValue(key, out previous)
                    && !string.IsNullOrEmpty(previous.RelativePath)
                    && !string.Equals(previous.RelativePath, relative, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(Path.Combine(directory, previous.RelativePath));
                }

                Entries[key] = new Entry
                {
                    Length = length,
                    Ticks = ticks,
                    RelativePath = relative,
                    DisplayName = Sanitize(displayName)
                };
                _manifestDirty = true;
                if (DateTime.UtcNow - _lastManifestSaveUtc >= ManifestSaveInterval)
                    FlushLocked();
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static List<string> CollectStale(IList<string> sourcePaths)
    {
        List<string> stale = new List<string>();
        if (sourcePaths == null)
            return stale;
        for (int i = 0; i < sourcePaths.Count; i++)
        {
            string path = sourcePaths[i];
            if (string.IsNullOrEmpty(path) || IsFresh(path))
                continue;
            stale.Add(path);
        }
        return stale;
    }

    private static bool TryGetFreshEntry(string sourcePath, out string relativePath)
    {
        relativePath = null;
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            return false;
        try
        {
            FileInfo info = new FileInfo(sourcePath);
            lock (Gate)
            {
                EnsureLoaded();
                Entry entry;
                if (!Entries.TryGetValue(Normalize(sourcePath), out entry))
                    return false;
                if (entry.Length != info.Length || entry.Ticks != info.LastWriteTimeUtc.Ticks)
                    return false;
                if (string.IsNullOrEmpty(entry.RelativePath))
                    return false;
                string full = Path.Combine(GetCacheDirectory(), entry.RelativePath);
                if (!File.Exists(full))
                    return false;
                relativePath = entry.RelativePath;
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void EnsureLoaded()
    {
        string directory = GetCacheDirectory();
        if (_loaded && string.Equals(_loadedDirectory, directory, StringComparison.OrdinalIgnoreCase))
            return;

        Entries.Clear();
        _loadedDirectory = directory;
        _loaded = true;
        string manifest = Path.Combine(directory, "index.tsv");
        if (!File.Exists(manifest))
            return;

        string[] lines = File.ReadAllLines(manifest, Encoding.UTF8);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrEmpty(line) || line[0] == '#')
                continue;
            string[] parts = line.Split('\t');
            if (parts.Length < 4)
                continue;
            long length;
            long ticks;
            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out length))
                continue;
            if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks))
                continue;
            if (string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[3]))
                continue;
            Entries[parts[0]] = new Entry
            {
                Length = length,
                Ticks = ticks,
                RelativePath = parts[3],
                DisplayName = parts.Length > 4 ? parts[4] : null
            };
        }
    }

    private static void SaveManifest()
    {
        string directory = GetCacheDirectory();
        Directory.CreateDirectory(directory);
        StringBuilder builder = new StringBuilder();
        foreach (KeyValuePair<string, Entry> pair in Entries)
        {
            Entry entry = pair.Value;
            if (entry == null || string.IsNullOrEmpty(entry.RelativePath))
                continue;
            builder.Append(Sanitize(pair.Key));
            builder.Append('\t');
            builder.Append(entry.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(entry.Ticks.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(Sanitize(entry.RelativePath));
            builder.Append('\t');
            builder.Append(Sanitize(entry.DisplayName));
            builder.Append('\n');
        }
        // Write beside the index and swap, so a crash mid-write cannot leave a
        // truncated manifest that silently drops every cached thumbnail.
        string target = Path.Combine(directory, "index.tsv");
        string temp = target + ".tmp";
        File.WriteAllText(temp, builder.ToString(), new UTF8Encoding(false));
        File.Copy(temp, target, true);
        TryDelete(temp);
    }

    private static string Normalize(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
        }
    }

    private static string ToHex(byte[] hash)
    {
        StringBuilder builder = new StringBuilder(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
            builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        return builder.ToString();
    }
}
