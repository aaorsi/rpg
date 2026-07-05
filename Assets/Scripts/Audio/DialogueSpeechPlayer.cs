using UnityEngine;

namespace Rpg.Audio
{
    public sealed class DialogueSpeechPlayer
    {
        readonly AudioSource _audioSource;
        AudioClip _activeClip;

        public DialogueSpeechPlayer(GameObject host, float volume = 0.95f)
        {
            _audioSource = host.GetComponent<AudioSource>();
            if (_audioSource == null)
                _audioSource = host.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = 0f;
            _audioSource.volume = Mathf.Clamp01(volume);
        }

        public bool TryPlayWavBytes(byte[] wavBytes, string clipName)
        {
            if (!WavClipDecoder.TryDecodePcm16(wavBytes, out var clip) || clip == null)
                return false;
            clip.name = string.IsNullOrWhiteSpace(clipName) ? "dialogue_tts" : clipName;
            ReplaceClip(clip);
            _audioSource.Play();
            return true;
        }

        public void Stop()
        {
            if (_audioSource != null)
                _audioSource.Stop();
            if (_activeClip != null)
            {
                Object.Destroy(_activeClip);
                _activeClip = null;
            }
            if (_audioSource != null)
                _audioSource.clip = null;
        }

        void ReplaceClip(AudioClip clip)
        {
            Stop();
            _activeClip = clip;
            _audioSource.clip = _activeClip;
        }
    }
}
