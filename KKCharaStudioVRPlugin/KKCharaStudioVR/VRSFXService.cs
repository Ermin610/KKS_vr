using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.Networking;
using VRGIN.Core;

namespace KKCharaStudioVR
{
    /// <summary>
    /// VR SFX playback service for KKS CharaStudio VR.
    /// Loads and caches directional 3D audio clips for hand slapping, tapping, petting (traverse),
    /// and clothing interactions from UserData/VR/SFX.
    /// </summary>
    public class VRSFXService : MonoBehaviour
    {
        public enum Sfx
        {
            Tap,
            Slap,
            Traverse,
            Dress,
            Undress
        }

        public enum Surface
        {
            Skin,
            Cloth,
            Hair
        }

        public enum Intensity
        {
            Soft,
            Hard,
            Hollow,
            Wet
        }

        private const int AudioSourcePoolSize = 10;
        private static VRSFXService _instance;

        private readonly Dictionary<Sfx, Dictionary<Surface, Dictionary<Intensity, List<AudioClip>>>> _sfxDic =
            new Dictionary<Sfx, Dictionary<Surface, Dictionary<Intensity, List<AudioClip>>>>();

        private readonly List<AudioSource> _sourcePool = new List<AudioSource>();
        private int _poolIndex;
        private bool _isLoaded;
        private bool _isLoading;
        private string _resolvedSfxDir;
        private KKCharaStudioVRSettings _settings;

        public static VRSFXService Instance
        {
            get
            {
                if (_instance == null)
                {
                    Initialize();
                }
                return _instance;
            }
        }

        public bool IsLoaded => _isLoaded;

        public static void Initialize(GameObject parent = null)
        {
            if (_instance != null) return;

            GameObject go = parent;
            if (go == null)
            {
                go = new GameObject("VRSFXService");
                DontDestroyOnLoad(go);
            }

            _instance = go.GetComponent<VRSFXService>() ?? go.AddComponent<VRSFXService>();
        }

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }

            InitDictionary();
            InitAudioSources();
            ResolveSettings();
        }

        private void Start()
        {
            ResolveSettings();
            if (!_isLoaded && !_isLoading)
            {
                StartCoroutine(LoadClipsAsync());
            }
        }

        private void ResolveSettings()
        {
            if (VR.Manager != null && VR.Manager.Context != null)
            {
                _settings = VR.Manager.Context.Settings as KKCharaStudioVRSettings;
            }
        }

        private void InitAudioSources()
        {
            for (int i = 0; i < AudioSourcePoolSize; i++)
            {
                GameObject child = new GameObject("SFX_Source_" + i);
                child.transform.SetParent(transform, false);

                AudioSource src = child.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0.85f; // 3D positional audio
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 0.15f;
                src.maxDistance = 12f;
                src.dopplerLevel = 0f;
                _sourcePool.Add(src);
            }
        }

        private void InitDictionary()
        {
            _sfxDic.Clear();
            foreach (Sfx sfx in Enum.GetValues(typeof(Sfx)))
            {
                _sfxDic[sfx] = new Dictionary<Surface, Dictionary<Intensity, List<AudioClip>>>();
                foreach (Surface surface in Enum.GetValues(typeof(Surface)))
                {
                    _sfxDic[sfx][surface] = new Dictionary<Intensity, List<AudioClip>>();
                    foreach (Intensity intensity in Enum.GetValues(typeof(Intensity)))
                    {
                        _sfxDic[sfx][surface][intensity] = new List<AudioClip>();
                    }
                }
            }
        }

        public static string ResolveSFXDirectory()
        {
            string[] possiblePaths = new string[]
            {
                Path.Combine(Paths.GameRootPath ?? string.Empty, "UserData", "VR", "SFX"),
                Path.Combine(Application.dataPath, "..", "UserData", "VR", "SFX"),
                Path.Combine(Directory.GetCurrentDirectory(), "UserData", "VR", "SFX"),
                @"E:\KKS\KoikatuSunshine\UserData\VR\SFX"
            };

            for (int i = 0; i < possiblePaths.Length; i++)
            {
                string p = possiblePaths[i];
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
                {
                    return Path.GetFullPath(p);
                }
            }

            return Path.Combine(Paths.GameRootPath ?? ".", "UserData", "VR", "SFX");
        }

        private IEnumerator LoadClipsAsync()
        {
            _isLoading = true;
            _resolvedSfxDir = ResolveSFXDirectory();

            if (!Directory.Exists(_resolvedSfxDir))
            {
                VRLog.Warn("[VRSFXService] SFX directory not found: " + _resolvedSfxDir);
                _isLoading = false;
                yield break;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(_resolvedSfxDir, "*.*", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                VRLog.Warn("[VRSFXService] Could not list SFX files: " + ex.Message);
                _isLoading = false;
                yield break;
            }

            int loadedCount = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string filePath = files[i];
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext != ".wav" && ext != ".ogg")
                    continue;

                if (!TryParseMetadata(filePath, out Sfx sfx, out Surface surface, out Intensity intensity, out string clipName))
                    continue;

                AudioType audioType = ext == ".ogg" ? AudioType.OGGVORBIS : AudioType.WAV;
                string uri = "file:///" + filePath.Replace('\\', '/');

                using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
                {
                    yield return req.SendWebRequest();

                    if (req.isNetworkError || req.isHttpError)
                    {
                        VRLog.Warn("[VRSFXService] Failed loading " + filePath + ": " + req.error);
                        continue;
                    }

                    try
                    {
                        AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                        if (clip != null)
                        {
                            clip.name = clipName;
                            _sfxDic[sfx][surface][intensity].Add(clip);
                            loadedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        VRLog.Warn("[VRSFXService] Error parsing clip " + filePath + ": " + ex.Message);
                    }
                }
            }

            _isLoaded = true;
            _isLoading = false;
            VRLog.Info($"[VRSFXService] Initialized with {loadedCount} VR sound effects from {_resolvedSfxDir}");
        }

        private static bool TryParseMetadata(string filePath, out Sfx sfx, out Surface surface, out Intensity intensity, out string clipName)
        {
            sfx = Sfx.Tap;
            surface = Surface.Skin;
            intensity = Intensity.Hard;
            clipName = Path.GetFileNameWithoutExtension(filePath);

            string filename = Path.GetFileName(filePath);
            int sfxIndex = filename.IndexOf("SFX.", StringComparison.OrdinalIgnoreCase);
            string tokenStr = sfxIndex >= 0 ? filename.Substring(sfxIndex + 4) : filename;

            string[] parts = tokenStr.Split('.');
            if (parts.Length < 4)
                return false;

            if (!Enum.TryParse(parts[0], true, out sfx))
                return false;

            if (!Enum.TryParse(parts[1], true, out surface))
                return false;

            string intensityStr = parts[2];
            if (intensityStr.Equals("Rough", StringComparison.OrdinalIgnoreCase))
            {
                intensity = Intensity.Hard;
            }
            else if (!Enum.TryParse(intensityStr, true, out intensity))
            {
                return false;
            }

            clipName = parts[3];
            return true;
        }

        private void AdjustInput(ref Sfx sfx, ref Surface surface, ref Intensity intensity)
        {
            if (sfx == Sfx.Traverse)
            {
                if (surface == Surface.Cloth)
                {
                    if (intensity == Intensity.Wet)
                    {
                        surface = Surface.Skin;
                        intensity = (UnityEngine.Random.value < 0.5f) ? Intensity.Wet : Intensity.Soft;
                    }
                    else if (intensity == Intensity.Hollow)
                    {
                        intensity = Intensity.Hard;
                    }
                }
                else if (surface == Surface.Skin && intensity == Intensity.Hollow)
                {
                    intensity = Intensity.Soft;
                }
            }
            else if (sfx == Sfx.Dress && surface == Surface.Cloth && intensity == Intensity.Hard)
            {
                sfx = Sfx.Undress;
            }
        }

        private List<AudioClip> GetClipsWithFallback(Sfx sfx, Surface surface, Intensity intensity)
        {
            List<AudioClip> list = _sfxDic[sfx][surface][intensity];
            if (list != null && list.Count > 0)
                return list;

            // Fallback 1: Other intensities for same sfx + surface
            if (intensity != Intensity.Soft && _sfxDic[sfx][surface][Intensity.Soft].Count > 0)
                return _sfxDic[sfx][surface][Intensity.Soft];
            if (intensity != Intensity.Hard && _sfxDic[sfx][surface][Intensity.Hard].Count > 0)
                return _sfxDic[sfx][surface][Intensity.Hard];

            // Fallback 2: Surface Skin fallback
            if (surface != Surface.Skin)
            {
                list = _sfxDic[sfx][Surface.Skin][intensity];
                if (list != null && list.Count > 0)
                    return list;
                if (_sfxDic[sfx][Surface.Skin][Intensity.Soft].Count > 0)
                    return _sfxDic[sfx][Surface.Skin][Intensity.Soft];
                if (_sfxDic[sfx][Surface.Skin][Intensity.Hard].Count > 0)
                    return _sfxDic[sfx][Surface.Skin][Intensity.Hard];
            }

            // Fallback 3: Sfx Tap fallback (if Slap has no clips)
            if (sfx == Sfx.Slap)
            {
                return GetClipsWithFallback(Sfx.Tap, surface, intensity);
            }

            return null;
        }

        public float Play(Sfx sfx, Surface surface, Intensity intensity, Vector3 position, float volume = 1f)
        {
            if (_sourcePool.Count == 0)
                return 0f;

            AdjustInput(ref sfx, ref surface, ref intensity);

            List<AudioClip> clips = GetClipsWithFallback(sfx, surface, intensity);
            if (clips == null || clips.Count == 0)
                return 0f;

            AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Count)];
            if (clip == null)
                return 0f;

            AudioSource src = GetNextAudioSource();
            if (src == null)
                return 0f;

            src.transform.position = position;
            src.clip = clip;
            src.pitch = 0.92f + UnityEngine.Random.value * 0.16f;

            float configVolume = 1f;
            if (_settings != null)
            {
                configVolume = Mathf.Clamp01(_settings.HapticFeedbackIntensity * 1.5f);
                if (configVolume <= 0.05f) configVolume = 1f;
            }

            src.volume = Mathf.Clamp01(volume * configVolume);
            src.Play();

            return clip.length;
        }

        public float Play(string sfxName, string surfaceName, string intensityName, Vector3 position, float volume = 1f)
        {
            Sfx sfx = Sfx.Tap;
            Surface surface = Surface.Skin;
            Intensity intensity = Intensity.Hard;

            if (!string.IsNullOrEmpty(sfxName)) Enum.TryParse(sfxName, true, out sfx);
            if (!string.IsNullOrEmpty(surfaceName)) Enum.TryParse(surfaceName, true, out surface);
            if (!string.IsNullOrEmpty(intensityName))
            {
                if (intensityName.Equals("Rough", StringComparison.OrdinalIgnoreCase))
                    intensity = Intensity.Hard;
                else
                    Enum.TryParse(intensityName, true, out intensity);
            }

            return Play(sfx, surface, intensity, position, volume);
        }

        private AudioSource GetNextAudioSource()
        {
            for (int i = 0; i < _sourcePool.Count; i++)
            {
                int idx = (_poolIndex + i) % _sourcePool.Count;
                AudioSource candidate = _sourcePool[idx];
                if (candidate != null && !candidate.isPlaying)
                {
                    _poolIndex = (idx + 1) % _sourcePool.Count;
                    return candidate;
                }
            }

            AudioSource oldest = _sourcePool[_poolIndex];
            _poolIndex = (_poolIndex + 1) % _sourcePool.Count;
            return oldest;
        }
    }
}
