using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VMS.Backend;
using VMS.Core.Music;

namespace VMS.Songs
{
    [Serializable]
    public class BestScore
    {
        public string key;
        public int score;
        public string rank;
        public float accuracy;
        public int maxCombo;
    }

    [Serializable]
    public class SongEntry
    {
        public string id;
        public string title;
        public string artist;
        public string fileName;
        public float duration;
        public float bpm;
        public bool builtIn;
        public string importedUtc;
        public List<BestScore> best = new List<BestScore>();

        public BestScore Best(string key)
        {
            if (best == null) return null;
            foreach (var b in best) if (b.key == key) return b;
            return null;
        }
    }

    [Serializable]
    class SongIndex
    {
        public List<SongEntry> songs = new List<SongEntry>();
    }

    /// <summary>
    /// The player's music library. Imported files are copied into persistentDataPath/songs (the picker's copy is
    /// temporary on Android), analysed once, and the analysis is cached next to the audio.
    /// Progress callbacks (stage key, 0..1) may arrive on worker threads – only store the values in them.
    /// </summary>
    public sealed class SongLibrary
    {
        public const string DemoId = "demo_lac_hong";
        const long MaxFileBytes = 200L * 1024 * 1024;
        const float MaxDurationSec = 15f * 60f;

        readonly string _root;
        SongIndex _index;

        public event Action Changed;

        public SongLibrary(string root)
        {
            _root = root;
            Directory.CreateDirectory(_root);
            string indexPath = IndexPath;
            try
            {
                _index = File.Exists(indexPath) ? JsonUtility.FromJson<SongIndex>(File.ReadAllText(indexPath)) : null;
            }
            catch (Exception e)
            {
                Debug.LogError("[Songs] Library index unreadable, starting fresh: " + e.Message);
            }
            if (_index == null) _index = new SongIndex();
            _index.songs.RemoveAll(s => !File.Exists(Path.Combine(_root, s.fileName)));
        }

        string IndexPath => Path.Combine(_root, "library.json");
        string AnalysisPath(string id) => Path.Combine(_root, id + ".analysis.json");

        public IReadOnlyList<SongEntry> Songs => _index.songs;

        public SongEntry Find(string id) => _index.songs.Find(s => s.id == id);

        public string AudioPath(SongEntry song) => Path.Combine(_root, song.fileName);

        public static string ScoreKey(GameMode mode, Difficulty difficulty) =>
            mode.ToString().ToLowerInvariant() + "_" + difficulty.ToString().ToLowerInvariant();

        // ------------------------------------------------------------------ demo song

        public async Task EnsureDemoAsync(Action<string, float> progress)
        {
            var existing = Find(DemoId);
            if (existing != null && File.Exists(AnalysisPath(DemoId))) return;

            progress?.Invoke("import.synth", 0f);
            var arrangement = new ProceduralSongSynth.Arrangement();
            float[] pcm = await Task.Run(() => ProceduralSongSynth.Render(20261012u, arrangement, p => progress?.Invoke("import.synth", p)));
            string fileName = DemoId + ".wav";
            await Task.Run(() =>
            {
                using (var fs = File.Create(Path.Combine(_root, fileName)))
                    WavWriter.Write(fs, pcm, ProceduralSongSynth.SampleRate);
            });

            progress?.Invoke("import.analyze", 0f);
            var analysis = await Task.Run(() =>
            {
                var half = new float[pcm.Length / 2];
                for (int i = 0; i < half.Length; i++) half[i] = 0.5f * (pcm[2 * i] + pcm[2 * i + 1]);
                return SongAnalyzer.Analyze(half, ProceduralSongSynth.SampleRate / 2, p => progress?.Invoke("import.analyze", p));
            });
            SaveAnalysis(DemoId, analysis);

            _index.songs.RemoveAll(s => s.id == DemoId);
            _index.songs.Insert(0, new SongEntry
            {
                id = DemoId,
                title = ProceduralSongSynth.Title,
                artist = ProceduralSongSynth.Artist,
                fileName = fileName,
                duration = analysis.duration,
                bpm = analysis.bpm,
                builtIn = true,
                importedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            });
            Save();
        }

        // ------------------------------------------------------------------ import

        public async Task<SongEntry> ImportAsync(string sourcePath, Action<string, float> progress, CancellationToken cancel)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                throw new BackendException("file_missing", "File not found.");
            if (!AudioLoader.IsSupported(sourcePath))
                throw new BackendException("unsupported_format", Path.GetExtension(sourcePath));
            var info = new FileInfo(sourcePath);
            if (info.Length > MaxFileBytes) throw new BackendException("file_too_large", "File is larger than 200 MB.");

            progress?.Invoke("import.copy", 0f);
            string id = await Task.Run(() => HashFile(sourcePath));
            var duplicate = Find(id);
            if (duplicate != null) return duplicate;

            string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            string fileName = id + ext;
            string destination = Path.Combine(_root, fileName);
            await Task.Run(() => File.Copy(sourcePath, destination, true));
            cancel.ThrowIfCancellationRequested();

            try
            {
                var tags = ext == ".mp3" ? Id3Reader.Read(destination) : default(AudioTags);
                progress?.Invoke("import.decode", 0f);
                var clip = await AudioLoader.LoadClipAsync(destination);
                SongAnalysis analysis;
                try
                {
                    if (clip.length > MaxDurationSec) throw new BackendException("too_long", "Songs longer than 15 minutes are not supported.");
                    analysis = await AnalyseClipAsync(clip, progress, cancel);
                }
                finally
                {
                    UnityEngine.Object.Destroy(clip);
                }
                SaveAnalysis(id, analysis);

                var entry = new SongEntry
                {
                    id = id,
                    title = !string.IsNullOrEmpty(tags.title) ? tags.title : Path.GetFileNameWithoutExtension(sourcePath),
                    artist = tags.artist ?? string.Empty,
                    fileName = fileName,
                    duration = analysis.duration,
                    bpm = analysis.bpm,
                    importedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                };
                _index.songs.Add(entry);
                Save();
                return entry;
            }
            catch
            {
                TryDelete(destination);
                TryDelete(AnalysisPath(id));
                throw;
            }
        }

        async Task<SongAnalysis> AnalyseClipAsync(AudioClip clip, Action<string, float> progress, CancellationToken cancel)
        {
            progress?.Invoke("import.decode", 0.5f);
            float[] mono = await AudioLoader.ExtractMonoAsync(clip, p => progress?.Invoke("import.decode", 0.5f + 0.5f * p));
            int rate = AudioLoader.AnalysisRate(clip);
            progress?.Invoke("import.analyze", 0f);
            return await Task.Run(() => SongAnalyzer.Analyze(mono, rate, p => progress?.Invoke("import.analyze", p), cancel), cancel);
        }

        /// <summary>Cached analysis, recomputed (from the already-loaded clip) when missing or outdated.</summary>
        public async Task<SongAnalysis> GetAnalysisAsync(SongEntry song, AudioClip clip, Action<string, float> progress)
        {
            string path = AnalysisPath(song.id);
            if (File.Exists(path))
            {
                try
                {
                    string json = await Task.Run(() => File.ReadAllText(path));
                    var cached = JsonUtility.FromJson<SongAnalysis>(json);
                    if (cached != null && cached.version == SongAnalysis.CurrentVersion) return cached;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Songs] Re-analysing " + song.title + ": " + e.Message);
                }
            }
            var analysis = await AnalyseClipAsync(clip, progress, CancellationToken.None);
            SaveAnalysis(song.id, analysis);
            return analysis;
        }

        void SaveAnalysis(string id, SongAnalysis analysis) => File.WriteAllText(AnalysisPath(id), JsonUtility.ToJson(analysis));

        // ------------------------------------------------------------------ maintenance

        public void Delete(SongEntry song)
        {
            if (song == null || song.builtIn) return;
            _index.songs.Remove(song);
            TryDelete(AudioPath(song));
            TryDelete(AnalysisPath(song.id));
            Save();
        }

        /// <summary>Stores the score if it beats the previous best; returns true for a new record.</summary>
        public bool RecordBest(SongEntry song, string key, int score, string rank, float accuracy, int maxCombo)
        {
            var best = song.Best(key);
            if (best != null && best.score >= score) return false;
            if (best == null)
            {
                best = new BestScore { key = key };
                song.best.Add(best);
            }
            best.score = score;
            best.rank = rank;
            best.accuracy = accuracy;
            best.maxCombo = maxCombo;
            Save();
            return true;
        }

        void Save()
        {
            _index.songs.Sort((a, b) =>
            {
                if (a.builtIn != b.builtIn) return a.builtIn ? -1 : 1;
                return string.CompareOrdinal(b.importedUtc, a.importedUtc);
            });
            File.WriteAllText(IndexPath, JsonUtility.ToJson(_index));
            Changed?.Invoke();
        }

        /// <summary>Content id: SHA-1 over the size, the first MiB and the last 64 KiB (fast for big files).</summary>
        static string HashFile(string path)
        {
            using (var sha = SHA1.Create())
            using (var fs = File.OpenRead(path))
            {
                long length = fs.Length;
                var head = new byte[(int)Math.Min(length, 1 << 20)];
                ReadExactly(fs, head);
                var tail = new byte[(int)Math.Min(length, 1 << 16)];
                fs.Position = length - tail.Length;
                ReadExactly(fs, tail);
                var lenBytes = BitConverter.GetBytes(length);
                sha.TransformBlock(lenBytes, 0, lenBytes.Length, null, 0);
                sha.TransformBlock(head, 0, head.Length, null, 0);
                sha.TransformFinalBlock(tail, 0, tail.Length);
                return BitConverter.ToString(sha.Hash, 0, 8).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        static void ReadExactly(Stream s, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int n = s.Read(buffer, total, buffer.Length - total);
                if (n <= 0) break;
                total += n;
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException e)
            {
                Debug.LogWarning("[Songs] Could not delete " + path + ": " + e.Message);
            }
        }
    }
}
