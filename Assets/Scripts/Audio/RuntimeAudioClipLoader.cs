using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Rpg.Audio
{
    /// <summary>
    /// Loads UI/gameplay clips: bundled <see cref="Resources"/> copies work in standalone builds;
    /// editor can still read from <c>Assets/...</c> on disk; optional StreamingAssets/GameAudio mirror.
    /// </summary>
    public static class RuntimeAudioClipLoader
    {
        static readonly Dictionary<string, AudioClip> Cache =
            new Dictionary<string, AudioClip>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>Project asset path (Assets/...) → Resources path (no extension).</summary>
        static readonly Dictionary<string, string> BundledResourcePaths =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["Assets/FREE SOUND PACK_TM(355)/UI(27)/Clicks.wav"] = "BundledAudio/UI/Clicks",
                ["Assets/FREE SOUND PACK_TM(355)/UI(27)/Interface Button 1.wav"] = "BundledAudio/UI/InterfaceButton1",
                ["Assets/FREE SOUND PACK_TM(355)/UI(27)/Interface Button 9.wav"] = "BundledAudio/UI/InterfaceButton9",
                ["Assets/FREE SOUND PACK_TM(355)/UI(27)/Interface Button 20.wav"] = "BundledAudio/UI/InterfaceButton20",
                ["Assets/FREE SOUND PACK_TM(355)/UI(27)/Clicks-001.wav"] = "BundledAudio/UI/Clicks001",
                ["Assets/FREE SOUND PACK_TM(355)/Footsteps(15)/Grass_06.wav"] = "BundledAudio/Footsteps/Grass06",
                ["Assets/FREE SOUND PACK_TM(355)/Footsteps(15)/Snow 04.wav"] = "BundledAudio/Footsteps/Snow04",
                ["Assets/FREE SOUND PACK_TM(355)/Ambiences(43)/Birds_Singing-005.wav"] = "BundledAudio/Ambience/BirdsSinging005",
                ["Assets/FREE SOUND PACK_TM(355)/Creatures(29)/Spectral 05.wav"] = "BundledAudio/Magic/Spectral05",
                ["Assets/FREE SOUND PACK_TM(355)/Horror(13)/Spirits_In_Dispair-006.wav"] = "BundledAudio/Horror/SpiritsInDispair006",
            };

        public static AudioClip LoadFromAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;
            var key = assetPath.Trim().Replace('\\', '/');
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            if (BundledResourcePaths.TryGetValue(key, out var resourcePath))
            {
                var bundled = Resources.Load<AudioClip>(resourcePath);
                if (bundled != null)
                {
                    Cache[key] = bundled;
                    return bundled;
                }
            }

            if (!key.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
                return null;

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            var full = Path.Combine(projectRoot, key);
            if (!File.Exists(full))
            {
                var rel = key.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase)
                    ? key.Substring("Assets/".Length)
                    : key;
                rel = rel.Replace('/', Path.DirectorySeparatorChar);
                var stream = Path.Combine(Application.streamingAssetsPath, "GameAudio", rel);
                if (File.Exists(stream))
                    full = stream;
                else
                    return null;
            }

            var uri = new System.Uri(full).AbsoluteUri;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(uri, ResolveAudioType(full)))
            {
                var op = req.SendWebRequest();
                while (!op.isDone) { }
                if (req.result != UnityWebRequest.Result.Success)
                    return null;
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null)
                    return null;
                clip.name = Path.GetFileNameWithoutExtension(full);
                Cache[key] = clip;
                return clip;
            }
        }

        static AudioType ResolveAudioType(string fullPath)
        {
            var ext = Path.GetExtension(fullPath)?.ToLowerInvariant() ?? string.Empty;
            switch (ext)
            {
                case ".wav": return AudioType.WAV;
                case ".mp3": return AudioType.MPEG;
                case ".ogg": return AudioType.OGGVORBIS;
                default: return AudioType.UNKNOWN;
            }
        }
    }
}
