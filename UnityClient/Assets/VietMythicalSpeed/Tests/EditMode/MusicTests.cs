using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using VMS.Core.Music;

namespace VMS.Tests
{
    [TestFixture]
    public class MusicTests
    {
        static SongAnalysis _demoAnalysis;
        static ProceduralSongSynth.Arrangement _demoArrangement;

        /// <summary>Renders and analyses the demo song once (it takes a moment) and shares it between tests.</summary>
        static SongAnalysis DemoAnalysis()
        {
            if (_demoAnalysis != null) return _demoAnalysis;
            _demoArrangement = new ProceduralSongSynth.Arrangement();
            var pcm = ProceduralSongSynth.Render(20261012u, _demoArrangement);
            // The game analyses at half rate to save time on phones; do the same here.
            var half = new float[pcm.Length / 2];
            for (int i = 0; i < half.Length; i++) half[i] = 0.5f * (pcm[2 * i] + pcm[2 * i + 1]);
            _demoAnalysis = SongAnalyzer.Analyze(half, ProceduralSongSynth.SampleRate / 2);
            return _demoAnalysis;
        }

        [Test]
        public void FftFindsPureTone()
        {
            const int n = 1024, sr = 8192;
            var fft = new Fft(n);
            var re = new float[n];
            var im = new float[n];
            for (int i = 0; i < n; i++) re[i] = (float)Math.Sin(2 * Math.PI * 1000.0 * i / sr);
            fft.Forward(re, im);
            int peak = 0;
            float best = 0f;
            for (int k = 1; k < n / 2; k++)
            {
                float m = re[k] * re[k] + im[k] * im[k];
                if (m > best) { best = m; peak = k; }
            }
            Assert.AreEqual(125, peak);
        }

        [Test]
        public void DetectsMostKicksOfDemoSong()
        {
            var analysis = DemoAnalysis();
            int found = 0;
            foreach (float kick in _demoArrangement.KickTimes)
            {
                foreach (var o in analysis.onsets)
                {
                    if (Math.Abs(o.t - kick) <= 0.05f)
                    {
                        found++;
                        break;
                    }
                }
            }
            float recall = found / (float)_demoArrangement.KickTimes.Count;
            Assert.Greater(recall, 0.85f, "kick recall " + recall);
        }

        [Test]
        public void EstimatesDemoTempo()
        {
            float bpm = DemoAnalysis().bpm;
            bool close = Math.Abs(bpm - ProceduralSongSynth.Bpm) < 2f || Math.Abs(bpm * 2f - ProceduralSongSynth.Bpm) < 2f;
            Assert.IsTrue(close, "bpm " + bpm);
        }

        [Test]
        public void CurvesAreNormalisedAndCoverTheSong()
        {
            var a = DemoAnalysis();
            Assert.AreEqual(a.duration, (a.intensity.Length - 1) * a.curveStep, 0.2f);
            foreach (float v in a.intensity) Assert.IsTrue(v >= 0f && v <= 1f);
            // The drop (bars 32–43) must be louder than the intro (bars 0–7).
            float bar = 60f / ProceduralSongSynth.Bpm * 4f;
            Assert.Greater(a.IntensityAt(36 * bar), a.IntensityAt(3 * bar));
        }

        [Test]
        public void BeatMapsArePlayable()
        {
            var a = DemoAnalysis();
            foreach (Difficulty d in Enum.GetValues(typeof(Difficulty)))
            {
                var profile = BeatMapGenerator.Profile(d);
                var map = BeatMapGenerator.Generate(a, d, GameMode.Classic, 42u);
                Assert.Greater(map.notes.Length, 40, d + " has too few notes");
                for (int i = 0; i < map.notes.Length; i++)
                {
                    var n = map.notes[i];
                    Assert.IsTrue(n.lane >= 0 && n.lane <= 2);
                    Assert.IsTrue(n.t >= BeatMapGenerator.LeadIn - 0.001f);
                    if (i == 0) continue;
                    float gap = n.t - map.notes[i - 1].t;
                    Assert.GreaterOrEqual(gap, profile.minGap - 0.001f, d + " notes too dense");
                    if (gap < profile.fastGap) Assert.LessOrEqual(Math.Abs(n.lane - map.notes[i - 1].lane), 1, d + " impossible jump");
                }
                Assert.AreEqual(map.speed.Length, map.yawRate.Length);
            }
        }

        [Test]
        public void DodgeMapsAlwaysLeaveAReachableLane()
        {
            var a = DemoAnalysis();
            foreach (Difficulty d in Enum.GetValues(typeof(Difficulty)))
            {
                var profile = BeatMapGenerator.Profile(d);
                var map = BeatMapGenerator.Generate(a, d, GameMode.Dodge, 7u);
                Assert.Greater(map.CountKind(NoteKind.Hazard), 20);
                int prevSafe = -1;
                float prevT = -10f;
                int i = 0;
                while (i < map.notes.Length)
                {
                    float t = map.notes[i].t;
                    var blocked = new bool[3];
                    for (; i < map.notes.Length && map.notes[i].t == t; i++)
                        if (map.notes[i].kind == (int)NoteKind.Hazard) blocked[map.notes[i].lane] = true;
                    int free = 0, safe = -1;
                    for (int l = 0; l < 3; l++) if (!blocked[l]) { free++; safe = l; }
                    Assert.GreaterOrEqual(free, 1, "wall at " + t);
                    if (prevSafe >= 0 && t - prevT < profile.reactionTime)
                    {
                        bool reachable = false;
                        for (int l = 0; l < 3; l++)
                            if (!blocked[l] && Math.Abs(l - prevSafe) <= 1) reachable = true;
                        Assert.IsTrue(reachable, "unreachable safe lane at " + t);
                    }
                    prevSafe = safe;
                    prevT = t;
                }
            }
        }

        [Test]
        public void ZenHasNoNotesButHasATrack()
        {
            var map = BeatMapGenerator.Generate(DemoAnalysis(), Difficulty.Normal, GameMode.Zen, 1u);
            Assert.AreEqual(0, map.notes.Length);
            Assert.Greater(map.speed.Length, 100);
        }

        [Test]
        public void AmbientAudioStillGetsNotes()
        {
            // A soft, slowly swelling sine has almost no onsets; the beat-grid fallback must kick in.
            const int sr = 22050;
            var pcm = new float[sr * 60];
            for (int i = 0; i < pcm.Length; i++)
            {
                double t = i / (double)sr;
                pcm[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 220 * t) * (0.6 + 0.4 * Math.Sin(t * 0.3)));
            }
            var analysis = SongAnalyzer.Analyze(pcm, sr);
            var map = BeatMapGenerator.Generate(analysis, Difficulty.Normal, GameMode.Classic, 3u);
            Assert.Greater(map.notes.Length, 20);
        }

        [Test]
        public void WavWriterProducesValidHeader()
        {
            var samples = new float[] { 0f, 0.5f, -0.5f, 1f };
            using (var ms = new MemoryStream())
            {
                WavWriter.Write(ms, samples, 22050);
                var bytes = ms.ToArray();
                Assert.AreEqual(44 + samples.Length * 2, bytes.Length);
                Assert.AreEqual("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
                Assert.AreEqual("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
                Assert.AreEqual(22050, BitConverter.ToInt32(bytes, 24));
            }
        }

        [Test]
        public void Id3ReaderReadsTitleAndArtist()
        {
            using (var ms = new MemoryStream())
            {
                var frames = new MemoryStream();
                WriteFrame(frames, "TIT2", 3, Encoding.UTF8.GetBytes("Bèo Dạt Mây Trôi"));
                WriteFrame(frames, "TPE1", 0, Encoding.ASCII.GetBytes("Dan Ca"));
                var body = frames.ToArray();
                ms.Write(new byte[] { (byte)'I', (byte)'D', (byte)'3', 3, 0, 0 }, 0, 6);
                int size = body.Length;
                ms.Write(new[] { (byte)((size >> 21) & 0x7F), (byte)((size >> 14) & 0x7F), (byte)((size >> 7) & 0x7F), (byte)(size & 0x7F) }, 0, 4);
                ms.Write(body, 0, body.Length);
                ms.Position = 0;
                var tags = Id3Reader.Read(ms);
                Assert.AreEqual("Bèo Dạt Mây Trôi", tags.title);
                Assert.AreEqual("Dan Ca", tags.artist);
            }
        }

        static void WriteFrame(Stream s, string id, byte encoding, byte[] text)
        {
            int size = text.Length + 1;
            s.Write(Encoding.ASCII.GetBytes(id), 0, 4);
            s.Write(new[] { (byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size, (byte)0, (byte)0 }, 0, 6);
            s.WriteByte(encoding);
            s.Write(text, 0, text.Length);
        }
    }
}
