using System;
using UnityEngine;
using VMS.Core.Music;

namespace VMS
{
    public enum ControlScheme
    {
        /// <summary>Tap the left or right half of the screen to change lane.</summary>
        Tap = 0,
        /// <summary>Swipe left or right to change lane.</summary>
        Swipe = 1,
        /// <summary>Drag a finger to steer freely, like Music Racer's mouse mode.</summary>
        Drag = 2,
        /// <summary>Tilt the phone to steer.</summary>
        Tilt = 3,
    }

    public enum GraphicsTier
    {
        Low = 0,
        Medium = 1,
        High = 2,
        /// <summary>Lowest cost that still looks like the game: for very old phones and long battery life.</summary>
        UltraLow = 3,
    }

    /// <summary>Device-local preferences, persisted in PlayerPrefs.</summary>
    public sealed class GameSettings
    {
        const string Prefix = "vms.settings.";

        public string Language = Loc.Vietnamese;
        public float MusicVolume = 0.9f;
        public float SfxVolume = 0.8f;
        public float VoiceVolume = 1f;
        /// <summary>Visual delay applied to the race (positive = notes later) to compensate audio latency.</summary>
        public int AudioOffsetMs;
        public ControlScheme Controls = ControlScheme.Tap;
        public GraphicsTier Graphics = GraphicsTier.Medium;
        public int TargetFps = 60;
        public bool Haptics = true;
        public bool AutoSkill;
        public bool ShowBarks = true;
        public float TiltSensitivity = 1f;
        /// <summary>Solid colours, no shadows, glows, patterns or idle animations in menus.</summary>
        public bool FlatUi;
        /// <summary>Menus run at 30 fps (races keep <see cref="TargetFps"/>).</summary>
        public bool LowPowerMenus;

        public GameMode LastMode = GameMode.Classic;
        public Difficulty LastDifficulty = Difficulty.Normal;
        public string LastSongId = string.Empty;
        public string Theme = "auto";
        public string Vessel = "thuyen_rong";

        /// <summary>Developer override of Resources/Config/backend.json ("", "offline" or an https URL).</summary>
        public string ServerOverride = string.Empty;

        public event Action Changed;

        public static GameSettings Load()
        {
            var s = new GameSettings();
            s.Language = PlayerPrefs.GetString(Prefix + "lang", Loc.FromSystemLanguage(Application.systemLanguage));
            if (!Loc.IsSupported(s.Language)) s.Language = Loc.English;
            s.MusicVolume = PlayerPrefs.GetFloat(Prefix + "music", s.MusicVolume);
            s.SfxVolume = PlayerPrefs.GetFloat(Prefix + "sfx", s.SfxVolume);
            s.VoiceVolume = PlayerPrefs.GetFloat(Prefix + "voice", s.VoiceVolume);
            s.AudioOffsetMs = PlayerPrefs.GetInt(Prefix + "offset", s.AudioOffsetMs);
            s.Controls = (ControlScheme)PlayerPrefs.GetInt(Prefix + "controls", (int)s.Controls);
            s.Graphics = (GraphicsTier)PlayerPrefs.GetInt(Prefix + "graphics", (int)DefaultTier());
            s.TargetFps = PlayerPrefs.GetInt(Prefix + "fps", s.TargetFps);
            s.Haptics = PlayerPrefs.GetInt(Prefix + "haptics", 1) == 1;
            s.AutoSkill = PlayerPrefs.GetInt(Prefix + "autoskill", 0) == 1;
            s.ShowBarks = PlayerPrefs.GetInt(Prefix + "barks", 1) == 1;
            s.TiltSensitivity = PlayerPrefs.GetFloat(Prefix + "tilt", s.TiltSensitivity);
            s.FlatUi = PlayerPrefs.GetInt(Prefix + "flatui", 0) == 1;
            s.LowPowerMenus = PlayerPrefs.GetInt(Prefix + "menu30", 0) == 1;
            s.LastMode = (GameMode)PlayerPrefs.GetInt(Prefix + "mode", (int)s.LastMode);
            s.LastDifficulty = (Difficulty)PlayerPrefs.GetInt(Prefix + "difficulty", (int)s.LastDifficulty);
            s.LastSongId = PlayerPrefs.GetString(Prefix + "song", s.LastSongId);
            s.Theme = PlayerPrefs.GetString(Prefix + "theme", s.Theme);
            s.Vessel = PlayerPrefs.GetString(Prefix + "vessel", s.Vessel);
            s.ServerOverride = PlayerPrefs.GetString(Prefix + "server", s.ServerOverride);
            return s;
        }

        public void Save()
        {
            PlayerPrefs.SetString(Prefix + "lang", Language);
            PlayerPrefs.SetFloat(Prefix + "music", MusicVolume);
            PlayerPrefs.SetFloat(Prefix + "sfx", SfxVolume);
            PlayerPrefs.SetFloat(Prefix + "voice", VoiceVolume);
            PlayerPrefs.SetInt(Prefix + "offset", AudioOffsetMs);
            PlayerPrefs.SetInt(Prefix + "controls", (int)Controls);
            PlayerPrefs.SetInt(Prefix + "graphics", (int)Graphics);
            PlayerPrefs.SetInt(Prefix + "fps", TargetFps);
            PlayerPrefs.SetInt(Prefix + "haptics", Haptics ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "autoskill", AutoSkill ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "barks", ShowBarks ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "tilt", TiltSensitivity);
            PlayerPrefs.SetInt(Prefix + "flatui", FlatUi ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "menu30", LowPowerMenus ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "mode", (int)LastMode);
            PlayerPrefs.SetInt(Prefix + "difficulty", (int)LastDifficulty);
            PlayerPrefs.SetString(Prefix + "song", LastSongId ?? string.Empty);
            PlayerPrefs.SetString(Prefix + "theme", Theme);
            PlayerPrefs.SetString(Prefix + "vessel", Vessel);
            PlayerPrefs.SetString(Prefix + "server", ServerOverride ?? string.Empty);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Low and Ultra Low: the cheapest tiers, which also keep menus at 30 fps.</summary>
        public bool Economy => Graphics == GraphicsTier.Low || Graphics == GraphicsTier.UltraLow;

        public void Apply()
        {
            Application.targetFrameRate = TargetFps;
            QualitySettings.vSyncCount = 0;
            VMS.UI.UiTheme.Flat = FlatUi;
            if (Loc.Language != Language) Loc.SetLanguage(Language);
        }

        static GraphicsTier DefaultTier()
        {
            if (!Application.isMobilePlatform) return GraphicsTier.High;
            return SystemInfo.systemMemorySize >= 6000 ? GraphicsTier.High : GraphicsTier.Medium;
        }
    }
}
