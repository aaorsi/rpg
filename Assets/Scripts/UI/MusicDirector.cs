using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Rpg.UI
{
    /// <summary>
    /// Runtime music state machine: opening -> fade -> delayed ambient loop -> victory.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class MusicDirector : MonoBehaviour
    {
        const float DefaultFadeSeconds = 2.5f;
        const float AmbientStartDelaySeconds = 60f;
        const float AmbientGapMinSeconds = 120f;
        const float AmbientGapMaxSeconds = 300f;

        static MusicDirector _instance;
        public static MusicDirector Instance => _instance;

        readonly string[] _ambientNames = { "Ambient.mp3", "Ambient 2.mp3", "Ambient 3.mp3" };
        readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>(System.StringComparer.OrdinalIgnoreCase);

        AudioSource _musicSource;
        AudioSource _victorySource;
        string _musicFolderPath;
        bool _ambientLoopEnabled;
        bool _victoryPlayed;
        Coroutine _ambientLoopCo;
        Coroutine _fadeCo;
        Coroutine _startAmbientDelayedCo;

        public void Configure(string musicFolderPath)
        {
            _musicFolderPath = string.IsNullOrWhiteSpace(musicFolderPath)
                ? Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty, "Music")
                : musicFolderPath;
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
            _musicSource.loop = false;
            _musicSource.spatialBlend = 0f;
            _musicSource.volume = 1f;

            _victorySource = gameObject.AddComponent<AudioSource>();
            _victorySource.playOnAwake = false;
            _victorySource.loop = false;
            _victorySource.spatialBlend = 0f;
            _victorySource.volume = 1f;
        }

        public void PlayOpeningMusic()
        {
            if (_victoryPlayed)
                return;
            _ambientLoopEnabled = false;
            if (_ambientLoopCo != null)
                StopCoroutine(_ambientLoopCo);
            _ambientLoopCo = null;
            PlayOnMusicSource("Opening.mp3", loop: true);
        }

        public void FadeAfterCharacterSelection()
        {
            if (_victoryPlayed)
                return;
            if (_fadeCo != null)
                StopCoroutine(_fadeCo);
            _fadeCo = StartCoroutine(CoFadeOutMusic(DefaultFadeSeconds));
            if (_startAmbientDelayedCo != null)
                StopCoroutine(_startAmbientDelayedCo);
            _startAmbientDelayedCo = StartCoroutine(CoStartAmbientAfterDelay());
        }

        public void OnGhoulKilled()
        {
            if (_victoryPlayed)
                return;
            _victoryPlayed = true;
            _ambientLoopEnabled = false;
            if (_ambientLoopCo != null)
                StopCoroutine(_ambientLoopCo);
            _ambientLoopCo = null;
            if (_startAmbientDelayedCo != null)
                StopCoroutine(_startAmbientDelayedCo);
            _startAmbientDelayedCo = null;
            if (_fadeCo != null)
                StopCoroutine(_fadeCo);
            _fadeCo = null;
            _musicSource.Stop();
            StartCoroutine(CoPlayVictoryOnce());
        }

        IEnumerator CoStartAmbientAfterDelay()
        {
            yield return new WaitForSeconds(AmbientStartDelaySeconds);
            if (_victoryPlayed)
                yield break;
            _ambientLoopEnabled = true;
            if (_ambientLoopCo != null)
                StopCoroutine(_ambientLoopCo);
            _ambientLoopCo = StartCoroutine(CoAmbientLoop());
        }

        IEnumerator CoAmbientLoop()
        {
            var last = string.Empty;
            while (_ambientLoopEnabled && !_victoryPlayed)
            {
                var next = PickNextAmbientName(last);
                last = next;
                var clip = GetClipSync(next);
                if (clip == null)
                {
                    yield return new WaitForSeconds(2f);
                    continue;
                }
                _musicSource.loop = false;
                _musicSource.clip = clip;
                _musicSource.volume = 1f;
                _musicSource.Play();
                yield return new WaitForSeconds(Mathf.Max(0.1f, clip.length));
                var gap = UnityEngine.Random.Range(AmbientGapMinSeconds, AmbientGapMaxSeconds);
                yield return new WaitForSeconds(gap);
            }
        }

        IEnumerator CoFadeOutMusic(float seconds)
        {
            var start = _musicSource.volume;
            var t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                var k = 1f - Mathf.Clamp01(t / Mathf.Max(0.01f, seconds));
                _musicSource.volume = start * k;
                yield return null;
            }
            _musicSource.Stop();
            _musicSource.volume = 1f;
            _fadeCo = null;
        }

        IEnumerator CoPlayVictoryOnce()
        {
            var clip = GetClipSync("Victory.mp3");
            if (clip == null)
                yield break;
            _victorySource.clip = clip;
            _victorySource.volume = 1f;
            _victorySource.loop = false;
            _victorySource.Play();
            yield return new WaitForSeconds(Mathf.Max(0.1f, clip.length));
        }

        void PlayOnMusicSource(string fileName, bool loop)
        {
            var clip = GetClipSync(fileName);
            if (clip == null)
                return;
            _musicSource.clip = clip;
            _musicSource.loop = loop;
            _musicSource.volume = 1f;
            _musicSource.Play();
        }

        string PickNextAmbientName(string last)
        {
            if (_ambientNames.Length <= 1)
                return _ambientNames[0];
            var idx = UnityEngine.Random.Range(0, _ambientNames.Length);
            if (_ambientNames[idx] == last)
                idx = (idx + 1) % _ambientNames.Length;
            return _ambientNames[idx];
        }

        AudioClip GetClipSync(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;
            if (_clipCache.TryGetValue(fileName, out var cached) && cached != null)
                return cached;
            var bundled = TryLoadBundledMusicClip(fileName);
            if (bundled != null)
            {
                _clipCache[fileName] = bundled;
                return bundled;
            }
            var path = ResolveMusicPath(fileName);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            var uri = new System.Uri(path).AbsoluteUri;
            var audioType = ResolveDiskMusicAudioType(path);
            using (var req = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
            {
                var op = req.SendWebRequest();
                while (!op.isDone) { }
                if (req.result != UnityWebRequest.Result.Success)
                    return null;
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null)
                    return null;
                clip.name = Path.GetFileNameWithoutExtension(fileName);
                _clipCache[fileName] = clip;
                return clip;
            }
        }

        string ResolveMusicPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;
            if (string.IsNullOrWhiteSpace(_musicFolderPath))
                Configure(null);
            foreach (var dir in GetMusicSearchDirectories())
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                var p = Path.Combine(dir, fileName);
                if (File.Exists(p))
                    return p;
            }
            return null;
        }

        static AudioType ResolveDiskMusicAudioType(string fullPath)
        {
            var ext = Path.GetExtension(fullPath)?.ToLowerInvariant() ?? string.Empty;
            switch (ext)
            {
                case ".wav": return AudioType.WAV;
                case ".ogg": return AudioType.OGGVORBIS;
                case ".mp3": return AudioType.MPEG;
                default: return AudioType.MPEG;
            }
        }

        static AudioClip TryLoadBundledMusicClip(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;
            var n = fileName.Trim();
            string path = null;
            if (string.Equals(n, "Opening.mp3", System.StringComparison.OrdinalIgnoreCase))
                path = "BundledAudio/Music/Opening";
            else if (string.Equals(n, "Ambient.mp3", System.StringComparison.OrdinalIgnoreCase))
                path = "BundledAudio/Music/Ambient1";
            else if (string.Equals(n, "Ambient 2.mp3", System.StringComparison.OrdinalIgnoreCase))
                path = "BundledAudio/Music/Ambient2";
            else if (string.Equals(n, "Ambient 3.mp3", System.StringComparison.OrdinalIgnoreCase))
                path = "BundledAudio/Music/Ambient3";
            else if (string.Equals(n, "Victory.mp3", System.StringComparison.OrdinalIgnoreCase))
                path = "BundledAudio/Music/Victory";
            return path == null ? null : Resources.Load<AudioClip>(path);
        }

        IEnumerable<string> GetMusicSearchDirectories()
        {
            // Shipped / mod-friendly: copy tracks next to StreamingAssets dialogue JSON.
            yield return Path.Combine(Application.streamingAssetsPath, "Music");
            if (!string.IsNullOrWhiteSpace(_musicFolderPath))
                yield return _musicFolderPath;
#if UNITY_EDITOR
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            var projectMusic = Path.Combine(projectRoot, "Music");
            if (!string.IsNullOrEmpty(projectRoot)
                && !string.Equals(projectMusic, _musicFolderPath, System.StringComparison.OrdinalIgnoreCase))
                yield return projectMusic;
#endif
        }
    }
}
