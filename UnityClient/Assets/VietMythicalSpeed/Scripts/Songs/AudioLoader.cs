using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using VMS.Backend;

namespace VMS.Songs
{
    /// <summary>Decodes audio files from disk into AudioClips and PCM for analysis.</summary>
    public static class AudioLoader
    {
        public static readonly string[] SupportedExtensions = { ".mp3", ".ogg", ".wav", ".aif", ".aiff" };

        public static bool IsSupported(string path)
        {
            string ext = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            return Array.IndexOf(SupportedExtensions, ext) >= 0;
        }

        static AudioType TypeFor(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".mp3": return AudioType.MPEG;
                case ".ogg": return AudioType.OGGVORBIS;
                case ".wav": return AudioType.WAV;
                case ".aif":
                case ".aiff": return AudioType.AIFF;
                default: return AudioType.UNKNOWN;
            }
        }

        public static async Task<AudioClip> LoadClipAsync(string path)
        {
            if (!File.Exists(path)) throw new BackendException("file_missing", "Audio file not found: " + path);
            string uri = new Uri(path).AbsoluteUri;
            using (var request = UnityWebRequestMultimedia.GetAudioClip(uri, TypeFor(path)))
            {
                var handler = (DownloadHandlerAudioClip)request.downloadHandler;
                // Fully decoded clips are required for AudioClip.GetData (analysis) and sample-accurate sync.
                handler.streamAudio = false;
                handler.compressed = false;
                await request.SendAsync();
                if (request.result != UnityWebRequest.Result.Success)
                    throw new BackendException("decode_failed", request.error);
                var clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null || clip.length <= 0.5f || clip.samples <= 0)
                    throw new BackendException("decode_failed", "Unsupported or empty audio file.");
                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    clip.LoadAudioData();
                    while (clip.loadState == AudioDataLoadState.Loading) await Task.Yield();
                    if (clip.loadState != AudioDataLoadState.Loaded)
                        throw new BackendException("decode_failed", "Audio data could not be loaded.");
                }
                clip.name = Path.GetFileNameWithoutExtension(path);
                return clip;
            }
        }

        /// <summary>
        /// Mono samples for analysis, decimated to at most ~24 kHz. Reads the clip in chunks and yields between
        /// them so the loading UI keeps animating.
        /// </summary>
        public static async Task<float[]> ExtractMonoAsync(AudioClip clip, Action<float> progress)
        {
            int channels = clip.channels;
            int frames = clip.samples;
            int decimation = clip.frequency > 32000 ? 2 : 1;
            var mono = new float[frames / decimation];
            const int chunkFrames = 1 << 16;
            var buffer = new float[chunkFrames * channels];
            int written = 0;
            var frameBudget = System.Diagnostics.Stopwatch.StartNew();
            for (int start = 0; start < frames; start += chunkFrames)
            {
                int count = Math.Min(chunkFrames, frames - start);
                if (buffer.Length != count * channels) buffer = new float[count * channels];
                if (!clip.GetData(buffer, start))
                    throw new BackendException("decode_failed", "Could not read audio samples.");
                for (int f = 0; f + decimation <= count && written < mono.Length; f += decimation)
                {
                    float sum = 0f;
                    for (int d = 0; d < decimation; d++)
                    {
                        int baseIdx = (f + d) * channels;
                        for (int c = 0; c < channels; c++) sum += buffer[baseIdx + c];
                    }
                    mono[written++] = sum / (decimation * channels);
                }
                progress?.Invoke((float)(start + count) / frames);
                if (frameBudget.ElapsedMilliseconds > 10)
                {
                    await Task.Yield();
                    frameBudget.Reset();
                    frameBudget.Start();
                }
            }
            if (written < mono.Length) Array.Resize(ref mono, written);
            return mono;
        }

        public static int AnalysisRate(AudioClip clip) => clip.frequency > 32000 ? clip.frequency / 2 : clip.frequency;
    }
}
