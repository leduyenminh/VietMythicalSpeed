using System.Collections;
using UnityEngine;
using VMS.Core.Music;

namespace VMS
{
    /// <summary>Sound effects (synthesised at startup), navigator voice and menu music.</summary>
    public sealed class AudioService : MonoBehaviour
    {
        AudioSource _sfx;
        AudioSource _voice;
        AudioSource _menuMusic;
        GameSettings _settings;

        AudioClip[] _collect;
        AudioClip[] _collectBig;
        AudioClip _miss, _hazard, _tap, _rattle, _count, _go;
        readonly AudioClip[] _bells = new AudioClip[4];
        Coroutine _musicFade;

        public void Init(GameSettings settings)
        {
            _settings = settings;
            _sfx = gameObject.AddComponent<AudioSource>();
            _voice = gameObject.AddComponent<AudioSource>();
            _menuMusic = gameObject.AddComponent<AudioSource>();
            foreach (var s in new[] { _sfx, _voice, _menuMusic })
            {
                s.playOnAwake = false;
                s.spatialBlend = 0f;
            }
            _menuMusic.loop = true;

            int n = SfxSynth.CollectVariants;
            _collect = new AudioClip[n];
            _collectBig = new AudioClip[n];
            for (int i = 0; i < n; i++)
            {
                _collect[i] = MakeClip("collect" + i, SfxSynth.Collect(i, false), SfxSynth.SampleRate);
                _collectBig[i] = MakeClip("collectBig" + i, SfxSynth.Collect(i, true), SfxSynth.SampleRate);
            }
            _miss = MakeClip("miss", SfxSynth.Miss(), SfxSynth.SampleRate);
            _hazard = MakeClip("hazard", SfxSynth.HazardHit(), SfxSynth.SampleRate);
            _tap = MakeClip("tap", SfxSynth.UiTap(), SfxSynth.SampleRate);
            _rattle = MakeClip("rattle", SfxSynth.StickRattle(1.6f), SfxSynth.SampleRate);
            _count = MakeClip("count", SfxSynth.Countdown(false), SfxSynth.SampleRate);
            _go = MakeClip("go", SfxSynth.Countdown(true), SfxSynth.SampleRate);
            for (int r = 1; r <= 3; r++) _bells[r] = MakeClip("bell" + r, SfxSynth.Bell(r), SfxSynth.SampleRate);
        }

        public static AudioClip MakeClip(string name, float[] samples, int sampleRate, int channels = 1)
        {
            var clip = AudioClip.Create(name, samples.Length / channels, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        float Sfx => _settings != null ? _settings.SfxVolume : 1f;

        public void Collect(int step, bool big)
        {
            var bank = big ? _collectBig : _collect;
            _sfx.PlayOneShot(bank[Mathf.Abs(step) % bank.Length], 0.55f * Sfx);
        }

        public void Miss() => _sfx.PlayOneShot(_miss, 0.6f * Sfx);
        public void Hazard() => _sfx.PlayOneShot(_hazard, 0.9f * Sfx);
        public void Tap() => _sfx.PlayOneShot(_tap, 0.7f * Sfx);
        public void Rattle() => _sfx.PlayOneShot(_rattle, 0.9f * Sfx);
        public void Countdown(bool go) => _sfx.PlayOneShot(go ? _go : _count, 0.9f * Sfx);
        public void Bell(int rarity) => _sfx.PlayOneShot(_bells[Mathf.Clamp(rarity, 1, 3)], 0.9f * Sfx);

        public void Voice(AudioClip clip)
        {
            if (clip == null) return;
            _voice.Stop();
            _voice.clip = clip;
            _voice.volume = _settings != null ? _settings.VoiceVolume : 1f;
            _voice.Play();
        }

        public bool VoicePlaying => _voice.isPlaying;

        public void PlayMenuMusic(AudioClip clip)
        {
            if (clip == null) return;
            if (_menuMusic.clip == clip && _menuMusic.isPlaying) return;
            if (_musicFade != null) StopCoroutine(_musicFade);
            _menuMusic.clip = clip;
            _menuMusic.time = Mathf.Min(clip.length * 0.33f, 30f);
            _menuMusic.volume = 0f;
            _menuMusic.Play();
            _musicFade = StartCoroutine(FadeMusic(0.35f * MusicVolume, 1.2f, false));
        }

        public void StopMenuMusic(float fade = 0.4f)
        {
            if (!_menuMusic.isPlaying) return;
            if (_musicFade != null) StopCoroutine(_musicFade);
            _musicFade = StartCoroutine(FadeMusic(0f, fade, true));
        }

        public void RefreshVolumes()
        {
            if (_menuMusic.isPlaying && _musicFade == null) _menuMusic.volume = 0.35f * MusicVolume;
        }

        float MusicVolume => _settings != null ? _settings.MusicVolume : 1f;

        IEnumerator FadeMusic(float target, float seconds, bool stopAtEnd)
        {
            float start = _menuMusic.volume;
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
            {
                _menuMusic.volume = Mathf.Lerp(start, target, t / seconds);
                yield return null;
            }
            _menuMusic.volume = target;
            if (stopAtEnd) _menuMusic.Stop();
            _musicFade = null;
        }
    }
}
