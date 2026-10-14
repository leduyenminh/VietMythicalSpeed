using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using VMS.Core;
using VMS.Core.Music;
using VMS.Rendering;
using VMS.Songs;
using VMS.UI;

namespace VMS.Race
{
    public sealed class RaceSetup
    {
        public SongEntry song;
        public GameMode mode;
        public Difficulty difficulty;
        public string navigatorId;
        public string vesselId;
        public string themeId;
        /// <summary>Equipped cosmetics (shop items, null = basic look).</summary>
        public ShopItem vesselKit;
        public ShopItem exhaust;
        public ShopItem bubbleFrame;
    }

    public sealed class RaceResult
    {
        public RaceSetup setup;
        public long score;
        public string rank;
        public float accuracy;
        public int maxCombo, perfect, good, miss, hazardHits, dodges, notes;
        public bool completed;
        public bool newBest;
        public RaceReport report;
        public RaceReward reward;
        /// <summary>Finished offline: the reward is granted when the race syncs.</summary>
        public bool rewardPending;
    }

    /// <summary>Owns one race: world, clock, input, judging, skills, HUD. Created by <see cref="GameApp"/>.</summary>
    public sealed class RaceDirector : MonoBehaviour
    {
        enum Phase { Loading, Countdown, Playing, Paused, Done }

        const float LeadIn = 3f;

        public event Action<RaceResult> Finished;
        public event Action Restarted;
        public event Action Quit;

        RaceSetup _setup;
        GameSettings _settings;
        QualityProfile _quality;
        RaceTheme _theme;
        NavigatorDef _navigator;
        AudioClip _clip;
        AudioSource _music;
        BeatMap _map;
        TrackPath _path;
        SongClock _clock;
        ScoreKeeper _score;
        SkillRuntime _skill;
        RaceInput _input;
        NoteField _field;
        Sparks _sparks;
        SpiritDragon _dragon;
        AmbientFx _ambient;
        AudioBands _bands;
        TrackRenderer _track;
        FxBatch _fx;
        SpectrumBars _bars;
        VesselRig _vessel;
        ChaseCamera _camera;
        RaceHud _hud;
        NavigatorBarks _barks;

        Phase _phase = Phase.Loading;
        int _next;
        int _targetLane = 1;
        float _lateral, _lateralVelocity;
        int _missStreak;
        bool _readyAnnounced;
        int _lastCountdown = 99;
        readonly bool[] _blockedLanes = new bool[3];
        readonly float[] _barLevels = new float[AudioBands.BandCount];
        float _offset;

        public static async Task<RaceDirector> CreateAsync(RaceSetup setup, LoadingOverlay loading)
        {
            var go = new GameObject("Race");
            var director = go.AddComponent<RaceDirector>();
            try
            {
                await director.Prepare(setup, loading);
                return director;
            }
            catch
            {
                Destroy(go);
                throw;
            }
        }

        async Task Prepare(RaceSetup setup, LoadingOverlay loading)
        {
            var app = GameApp.Instance;
            _setup = setup;
            _settings = app.Settings;
            _quality = QualityProfile.For(_settings.Graphics);
            _navigator = app.Data.Navigator(setup.navigatorId);
            _theme = RaceTheme.Resolve(setup.themeId, setup.song.id);
            _offset = _settings.AudioOffsetMs / 1000f;

            loading.Report("race.loadingAudio", -1f);
            _clip = await AudioLoader.LoadClipAsync(app.Songs.AudioPath(setup.song));
            var analysis = await app.Songs.GetAnalysisAsync(setup.song, _clip, loading.Report);
            loading.Report("race.building", -1f);
            uint seed = StableHash.Fnv1a(setup.song.id + "|" + (int)setup.mode + "|" + (int)setup.difficulty);
            _map = await Task.Run(() => BeatMapGenerator.Generate(analysis, setup.difficulty, setup.mode, seed));
            _path = new TrackPath(_map);

            BuildWorld(seed);

            var owned = app.Profile != null ? app.Profile.FindNavigator(setup.navigatorId) : null;
            int awakening = owned != null ? owned.awakening : 0;
            int bond = owned != null ? EconomyRules.BondLevel(app.Data.Economy.bond, owned.bondExp) : 1;
            bool skills = setup.mode == GameMode.Skill || setup.mode == GameMode.Dodge;
            _score = new ScoreKeeper();
            _skill = new SkillRuntime(_navigator, awakening, skills);
            _skill.ApplyPassive(_score);
            _input = new RaceInput { Scheme = _settings.Controls, TiltSensitivity = _settings.TiltSensitivity };
            _field = new NoteField(_map.notes, _path, _theme);

            string modeText = Loc.T("mode." + setup.mode.ToString().ToLowerInvariant()) + " · " + Loc.T("difficulty." + setup.difficulty.ToString().ToLowerInvariant());
            _hud = RaceHud.Create(UiRoot.Instance.HudLayer, _navigator, _skill.Enabled, setup.song.title, modeText);
            _hud.StyleBubble(setup.bubbleFrame);
            _hud.OnPause = Pause;
            _hud.OnResume = Resume;
            _hud.OnRestart = () => Restarted?.Invoke();
            _hud.OnQuit = () => Quit?.Invoke();
            _hud.OnSkill = CastSkill;
            _barks = new NavigatorBarks(_navigator, bond, _hud, _settings.ShowBarks);

            // Prewarm everything visible at the start so the first frames don't stall.
            _track.UpdateVisible(-LeadIn, int.MaxValue);
            Place(-LeadIn, 0f);
            _vessel.ClearTrails();
        }

        void BuildWorld(uint seed)
        {
            var camGo = new GameObject("RaceCamera");
            camGo.transform.SetParent(transform, false);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = _theme.fog;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1300f;
            cam.allowHDR = false;
            cam.allowMSAA = _quality.msaa > 1;
            cam.allowDynamicResolution = true;
            cam.useOcclusionCulling = false;
            camGo.AddComponent<AudioListener>();
            _camera = camGo.AddComponent<ChaseCamera>();
            _camera.Init(cam);
            PostFx.Setup(cam, _quality);
            camGo.AddComponent<PerformanceGovernor>().Init(_settings.TargetFps, _quality.renderScale);

            var sky = new GameObject("Sky").AddComponent<SkyRig>();
            sky.transform.SetParent(transform, false);
            sky.Init(camGo.transform, _theme, _quality.starScale);
            VmsGraphics.SetFog(_theme.fog, _quality.fogEnd * 0.25f, _quality.fogEnd);

            var props = new PropFactory(_theme, _path, _map.sectionTimes, _map.bpm, _map.beatOffset, seed) { Density = _quality.propDensity, Minimal = _quality.minimal };
            _track = new GameObject("Track").AddComponent<TrackRenderer>();
            _track.transform.SetParent(transform, false);
            _track.Init(_path, _map, _theme, props);
            _track.Ahead = _quality.drawAhead;

            _bars = new GameObject("SpectrumBars").AddComponent<SpectrumBars>();
            _bars.transform.SetParent(transform, false);
            _bars.Init(_path, _theme, _quality.barsPerSide);

            _vessel = new GameObject("Vessel").AddComponent<VesselRig>();
            _vessel.transform.SetParent(transform, false);
            var vesselDef = VesselStyles.WithKit(VesselDef.Find(_setup.vesselId), _setup.vesselKit);
            _vessel.Build(vesselDef, ExhaustStyle.From(_setup.exhaust, vesselDef));

            _fx = new GameObject("Fx").AddComponent<FxBatch>();
            _fx.transform.SetParent(transform, false);
            _fx.Init(cam);
            _sparks = new Sparks(_quality.maxSparks);
            _dragon = new SpiritDragon(_quality.dragon ? _quality.dragonSegments : 0);
            _ambient = new AmbientFx(_theme, _quality.minimal);
            _bands = new AudioBands();

            _music = gameObject.AddComponent<AudioSource>();
            _music.clip = _clip;
            _music.playOnAwake = false;
            _music.loop = false;
            _music.priority = 0;
            _music.volume = _settings.MusicVolume;
            _music.spatialBlend = 0f;
            _clock = new SongClock(_music);
        }

        public void Begin()
        {
            _phase = Phase.Countdown;
            _clock.Start(LeadIn);
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }

        float SongTime => _clock.Time - _offset;

        void Update()
        {
            if (_phase == Phase.Loading || _phase == Phase.Done || _phase == Phase.Paused) return;
            float dt = Time.deltaTime;
            _clock.Tick(dt);
            float t = SongTime;

            _input.Poll(_lateral / TrackPath.LaneWidth);
            if (_input.PausePressed)
            {
                Pause();
                return;
            }

            if (_phase == Phase.Countdown) UpdateCountdown(t);
            UpdateSteering(t, dt);
            if (_setup.mode != GameMode.Zen && _phase == Phase.Playing) JudgeUpTo(t);

            _skill.Tick(dt, _score);
            if (_skill.Enabled && !_readyAnnounced && _skill.CanCast(_score))
            {
                _readyAnnounced = true;
                if (_settings.AutoSkill) CastSkill();
                else _barks.Say(VoiceTriggers.SkillReady);
            }

            Place(t, dt);
            UpdateHud(t);

            if (_phase == Phase.Playing && t > _map.duration + 1.2f) Finish(true);
        }

        void UpdateCountdown(float t)
        {
            if (t >= 0f)
            {
                _phase = Phase.Playing;
                _hud.SetCountdown(Loc.T("race.go"));
                GameApp.Instance.Audio.Countdown(true);
                StartCoroutine(ClearCountdown());
                _barks.Say(VoiceTriggers.RaceStart, true);
                return;
            }
            int n = Mathf.CeilToInt(-t);
            if (n != _lastCountdown && n <= 3)
            {
                _lastCountdown = n;
                _hud.SetCountdown(n.ToString());
                GameApp.Instance.Audio.Countdown(false);
            }
        }

        IEnumerator ClearCountdown()
        {
            yield return new WaitForSecondsRealtime(0.6f);
            if (_hud != null) _hud.SetCountdown(null);
        }

        void UpdateSteering(float t, float dt)
        {
            float target;
            if (_skill.Autopilot || DevAutomation.Autoplay)
            {
                _targetLane = AutopilotLane(t);
                target = TrackPath.LaneX(_targetLane);
            }
            else if (_input.HasAnalog)
            {
                target = _input.Analog * TrackPath.LaneWidth;
                _targetLane = Mathf.Clamp(Mathf.RoundToInt(_input.Analog + 1f), 0, 2);
            }
            else
            {
                if (_input.LaneDelta != 0) _targetLane = Mathf.Clamp(_targetLane + _input.LaneDelta, 0, 2);
                target = TrackPath.LaneX(_targetLane);
            }
            _lateral = Mathf.SmoothDamp(_lateral, target, ref _lateralVelocity, 0.055f, Mathf.Infinity, Mathf.Max(dt, 0.0001f));
        }

        /// <summary>Lane the crane/autopilot skill steers to: the next pearl, or the free lane of the next hazard row.</summary>
        int AutopilotLane(float t)
        {
            var notes = _map.notes;
            if (_next >= notes.Length || notes[_next].t >= t + 0.9f) return _targetLane;
            var upcoming = notes[_next];
            if (upcoming.kind != (int)NoteKind.Hazard) return upcoming.lane;

            var blocked = _blockedLanes;
            blocked[0] = blocked[1] = blocked[2] = false;
            for (int k = _next; k < notes.Length && notes[k].t == upcoming.t; k++)
                if (notes[k].kind == (int)NoteKind.Hazard) blocked[notes[k].lane] = true;
            int best = _targetLane;
            int bestDistance = 99;
            for (int l = 0; l < 3; l++)
            {
                if (blocked[l]) continue;
                int d = Mathf.Abs(l - _targetLane);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = l;
                }
            }
            return best;
        }

        void JudgeUpTo(float t)
        {
            var notes = _map.notes;
            while (_next < notes.Length && notes[_next].t <= t)
            {
                JudgeNote(_next);
                _next++;
            }
        }

        void JudgeNote(int index)
        {
            var app = GameApp.Instance;
            var note = _map.notes[index];
            float distance = Mathf.Abs(_lateral - TrackPath.LaneX(note.lane)) / TrackPath.LaneWidth;
            bool transmute = _skill.Transmute;
            Vector3 at = _field.WorldPosition(index);
            Vector3 carry = _camera.transform.forward * _path.Speed(note.t) * 0.6f;

            if (note.kind == (int)NoteKind.Hazard && !transmute)
            {
                var outcome = _score.Register(JudgeRules.JudgeHazard(distance), false);
                if (outcome.judgement == Judgement.HazardHit)
                {
                    _field.Mark(index, NoteField.State.Hit);
                    app.Audio.Hazard();
                    _camera.Shake(0.8f);
                    _sparks.Burst(at, carry, _theme.hazard, 16, 9f, 1.2f);
                    Haptics.Tick(40, 200);
                    _hud.Judge(Judgement.HazardHit, false, false);
                    RegisterMiss();
                }
                else
                {
                    _field.Mark(index, NoteField.State.Dodged);
                    if (outcome.shieldUsed) _hud.Judge(Judgement.HazardDodge, false, true);
                    _missStreak = 0;
                    AfterHit();
                }
                return;
            }

            bool big = note.kind == (int)NoteKind.BigPearl || transmute;
            var judgement = JudgeRules.JudgePearl(distance, _skill.PerfectWindowMultiplier, _skill.MagnetReach);
            if (_setup.mode == GameMode.Dodge && judgement == Judgement.Miss)
            {
                // Pearls are optional bonuses in Dodge mode.
                _field.Mark(index, NoteField.State.Missed);
                return;
            }
            var result = _score.Register(judgement, big);
            if (result.judgement == Judgement.Perfect || result.judgement == Judgement.Good)
            {
                _field.Mark(index, NoteField.State.Collected);
                app.Audio.Collect(_score.Combo, big);
                Color c = big ? _theme.bigPearl : _theme.pearl;
                _sparks.Burst(at, carry, c, big ? 18 : 9, big ? 10f : 6f, big ? 1.1f : 0.8f);
                if (result.judgement == Judgement.Perfect) _sparks.Ring(at, carry, c, big ? 5f : 3.4f);
                if (big) _camera.Punch(4f);
                Haptics.Tick(big ? 18 : 8, big ? 160 : 70);
                _missStreak = 0;
                AfterHit();
            }
            else
            {
                _field.Mark(index, NoteField.State.Missed);
                app.Audio.Miss();
                RegisterMiss();
            }
            _hud.Judge(result.judgement, big, result.shieldUsed);
        }

        void AfterHit()
        {
            int combo = _score.Combo;
            if (combo > 0 && (combo == 25 || combo % 50 == 0)) _barks.Say(VoiceTriggers.Combo);
        }

        void RegisterMiss()
        {
            _missStreak++;
            if (_missStreak == 3) _barks.Say(VoiceTriggers.MissStreak);
        }

        void CastSkill()
        {
            if (_phase != Phase.Playing || !_skill.TryCast(_score)) return;
            _readyAnnounced = false;
            _hud.SkillCutIn();
            GameApp.Instance.Audio.Bell(3);
            _barks.Say(VoiceTriggers.SkillCast, true);
            Haptics.Tick(30, 180);
        }

        void Place(float t, float dt)
        {
            float energy = Mathf.Clamp01(Mathf.Max(_map.Sample(_map.intensity, Mathf.Max(0f, t)), _bands.Loudness * 1.3f));
            float pulse = t >= 0f ? Mathf.Exp(-_map.BeatPhase(t) * 7f) : 0f;
            VmsGraphics.SetPulse(pulse * (0.45f + 0.55f * energy), energy);

            _track.UpdateVisible(t);
            _vessel.Place(_path, t, _lateral, _lateralVelocity, Time.time, dt);
            float speedNorm = (_path.Speed(Mathf.Max(0f, t)) - 30f) / 25f;
            _camera.Follow(_path, t, _lateral, speedNorm, dt);

            _bands.Update(_music, dt);
            // Live spectrum, floored by the analysed energy on the beat so the towers dance even where spectrum
            // data is unavailable (muted output, some Android audio paths).
            for (int b = 0; b < AudioBands.BandCount; b++)
                _barLevels[b] = Mathf.Max(_bands.Bands[b], energy * (0.3f + 0.7f * pulse) * (0.95f - b * 0.07f));
            _bars.Rebuild(t, _barLevels);

            _fx.Begin();
            if (_setup.mode != GameMode.Zen) _field.Draw(_fx, t, _quality.drawAhead - 0.5f, _skill.Transmute, Time.time);
            _vessel.EmitEmbers(_sparks, dt);
            _sparks.Update(dt, _fx);
            _dragon.Draw(_fx, _path, _theme, t, energy, dt);
            _ambient.Draw(_fx, _camera.transform, Time.time);
            var vesselGlow = _vessel.Exhaust.head;
            _fx.Add(_vessel.transform.position - _vessel.transform.forward * 1.9f + _vessel.transform.up * 0.4f, 1.6f + pulse * 0.6f, vesselGlow * 0.8f, ProceduralTextures.FxCell.Dot, 0f, 0.5f);
            if (_skill.Active) _fx.Add(_vessel.transform.position + _vessel.transform.up * 0.8f, 6f, UiTheme.NavigatorColor(_navigator) * 0.35f, ProceduralTextures.FxCell.Ring, Time.time, 0.8f);
            _fx.End();
        }

        void UpdateHud(float t)
        {
            _hud.SetScore(_score.Score, _score.Combo, _score.ComboMultiplier);
            _hud.SetProgress(_map.duration > 0f ? t / _map.duration : 0f);
            _hud.SetSpirit(_score.Spirit / ScoreKeeper.SpiritMax, _skill.CanCast(_score), _skill.Active ? _skill.Remaining / Mathf.Max(0.01f, _skill.Duration) : 0f);
        }

        // ------------------------------------------------------------------ pause / end

        public void Pause()
        {
            if (_phase != Phase.Playing && _phase != Phase.Countdown) return;
            _resumePhase = _phase;
            _phase = Phase.Paused;
            _clock.Pause();
            _hud.ShowPause(true);
        }

        Phase _resumePhase;

        public void Resume()
        {
            if (_phase != Phase.Paused) return;
            _hud.ShowPause(false);
            StartCoroutine(ResumeRoutine());
        }

        IEnumerator ResumeRoutine()
        {
            // A short beat to put thumbs back in place before the music continues.
            _hud.SetCountdown(Loc.T("race.ready"));
            yield return new WaitForSecondsRealtime(0.9f);
            _hud.SetCountdown(null);
            _phase = _resumePhase;
            _clock.Resume();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) Pause();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && Application.isMobilePlatform) Pause();
        }

        void Finish(bool completed)
        {
            if (_phase == Phase.Done) return;
            _phase = Phase.Done;
            _clock.Stop();
            Screen.sleepTimeout = SleepTimeout.SystemSetting;

            var result = new RaceResult
            {
                setup = _setup,
                score = _score.Score,
                rank = _setup.mode == GameMode.Zen ? "-" : _score.Rank,
                accuracy = _score.Accuracy,
                maxCombo = _score.MaxCombo,
                perfect = _score.Perfect,
                good = _score.Good,
                miss = _score.Miss,
                hazardHits = _score.HazardHits,
                dodges = _score.HazardDodges,
                notes = _map.notes.Length,
                completed = completed,
            };
            if (completed && _setup.mode != GameMode.Zen)
            {
                result.newBest = GameApp.Instance.Songs.RecordBest(_setup.song, SongLibrary.ScoreKey(_setup.mode, _setup.difficulty),
                    (int)Math.Min(int.MaxValue, _score.Score), result.rank, result.accuracy, result.maxCombo);
            }
            result.report = new RaceReport
            {
                songId = _setup.song.id,
                durationSec = _map.duration,
                mode = (int)_setup.mode,
                difficulty = (int)_setup.difficulty,
                score = (int)Math.Min(int.MaxValue, _score.Score),
                maxCombo = _score.MaxCombo,
                perfect = _score.Perfect,
                good = _score.Good,
                miss = _score.Miss + _score.HazardHits,
                accuracy = _score.Accuracy,
                navigatorId = _setup.navigatorId,
            };
            Finished?.Invoke(result);
        }

        void OnDestroy()
        {
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            VmsGraphics.SetPulse(0f, 0f);
            if (_hud != null) Destroy(_hud.gameObject);
            if (_music != null) _music.Stop();
            if (_clip != null) Destroy(_clip);
        }
    }
}
