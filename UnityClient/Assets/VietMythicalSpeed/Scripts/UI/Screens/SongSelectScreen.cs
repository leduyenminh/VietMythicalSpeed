using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using VMS.Backend;
using VMS.Core.Music;
using VMS.Race;
using VMS.Songs;

namespace VMS.UI
{
    public sealed class SongSelectScreen : UiScreen
    {
        static readonly GameMode[] Modes = { GameMode.Classic, GameMode.Skill, GameMode.Dodge, GameMode.Zen };
        static readonly Difficulty[] Difficulties = { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard };

        RectTransform _listContent;
        readonly Dictionary<string, Image> _rowFrames = new Dictionary<string, Image>();
        Text _selectedTitle, _selectedInfo, _modeHint, _best;
        SongEntry _selected;

        GameSettings S => App.Settings;

        protected override void Build(object arg)
        {
            Backdrop.Sky(Root);
            Ui.Header(Root, Loc.T("songs.title"), () => App.Ui.Back());
            CurrencyBar.Create(Root);

            // ---- left: library
            var left = Ui.Node("Library", Root).Region(0f, 0f, 0.55f, 1f);
            left.offsetMin = new Vector2(40f, 40f);
            left.offsetMax = new Vector2(-10f, -140f);
            var import = Ui.Button(left, "+  " + Loc.T("songs.import"), Import, ButtonStyle.Accent, 32);
            import.GetComponent<RectTransform>().At(0f, 1f, 10f, 0f, 520f, 90f, 0f, 1f);
            var count = Ui.Label(left, Loc.F("songs.count", App.Songs.Songs.Count), 24, UiTheme.InkSoft, TextAnchor.MiddleRight);
            count.rectTransform.At(1f, 1f, -10f, -20f, 400f, 50f, 1f, 1f);
            var listCard = Ui.Card(left, UiTheme.Panel.WithAlpha(0.85f), "ListCard");
            listCard.rectTransform.Fill(0f, 110f, 0f, 0f);
            Ui.ScrollList(listCard.rectTransform, out _listContent, 12f, 16).GetComponent<RectTransform>().Fill(4f, 4f, 4f, 4f);

            // ---- right: options
            var right = Ui.Card(Root, UiTheme.Panel.WithAlpha(0.96f), "Options").rectTransform;
            right.Region(0.56f, 0f, 1f, 1f);
            right.offsetMin = new Vector2(20f, 40f);
            right.offsetMax = new Vector2(-40f, -140f);
            BuildOptions(right);

            _selected = App.Songs.Find(S.LastSongId) ?? (App.Songs.Songs.Count > 0 ? App.Songs.Songs[0] : null);
            RebuildList();
            Select(_selected);
        }

        void BuildOptions(RectTransform panel)
        {
            var stack = Ui.Node("Stack", panel).Fill(36f, 26f, 36f, 150f);
            Ui.VStack(stack, 8f);

            _selectedTitle = Ui.FitLabel(stack, "-", 44, UiTheme.Ink, TextAnchor.MiddleLeft);
            _selectedTitle.Layout(-1f, 60f);
            _selectedInfo = Ui.Label(stack, string.Empty, 24, UiTheme.InkSoft);
            _selectedInfo.Layout(-1f, 34f);

            Section(stack, Loc.T("songs.mode"));
            var modeNames = new string[Modes.Length];
            for (int i = 0; i < Modes.Length; i++) modeNames[i] = Loc.T("mode." + Modes[i].ToString().ToLowerInvariant());
            Ui.Segmented(stack, modeNames, Array.IndexOf(Modes, S.LastMode), i =>
            {
                S.LastMode = Modes[i];
                S.Save();
                RefreshHints();
            }, 26);
            _modeHint = Ui.Label(stack, string.Empty, 22, UiTheme.InkSoft);
            _modeHint.Layout(-1f, 46f);

            Section(stack, Loc.T("songs.difficulty"));
            var diffNames = new string[Difficulties.Length];
            for (int i = 0; i < Difficulties.Length; i++) diffNames[i] = Loc.T("difficulty." + Difficulties[i].ToString().ToLowerInvariant());
            Ui.Segmented(stack, diffNames, Array.IndexOf(Difficulties, S.LastDifficulty), i =>
            {
                S.LastDifficulty = Difficulties[i];
                S.Save();
                RefreshHints();
            }, 26);

            Section(stack, Loc.T("songs.theme"));
            Button road = null;
            road = Ui.Button(stack, RoadPicker.Describe(S.Theme), () => RoadPicker.Show(S.Theme, id =>
            {
                S.Theme = id;
                S.Save();
                road.SetLabel(RoadPicker.Describe(id));
            }), ButtonStyle.Secondary, 26);
            road.Layout(-1f, 64f);

            Section(stack, Loc.T("songs.vessel"));
            var vesselNames = new string[VesselDef.All.Length];
            int vesselIndex = 0;
            for (int i = 0; i < VesselDef.All.Length; i++)
            {
                vesselNames[i] = Loc.T(VesselDef.All[i].nameKey);
                if (VesselDef.All[i].id == S.Vessel) vesselIndex = i;
            }
            Ui.Segmented(stack, vesselNames, vesselIndex, i =>
            {
                S.Vessel = VesselDef.All[i].id;
                S.Save();
            }, 20);

            _best = Ui.Label(stack, string.Empty, 24, UiTheme.PrimaryDark, TextAnchor.MiddleLeft, FontStyle.Bold);
            _best.Layout(-1f, 40f);

            // Navigator + start.
            var nav = App.SelectedNavigator;
            var navRow = Ui.Node("Navigator", panel).At(0f, 0f, 36f, 26f, 470f, 110f, 0f, 0f);
            var portrait = NavigatorArt.Portrait(navRow, nav, 104f);
            portrait.anchorMin = portrait.anchorMax = new Vector2(0f, 0.5f);
            portrait.pivot = new Vector2(0f, 0.5f);
            portrait.anchoredPosition = Vector2.zero;
            var navName = Ui.FitLabel(navRow, Loc.Pick(nav.name), 30, UiTheme.Ink, TextAnchor.UpperLeft);
            navName.rectTransform.Fill(120f, 6f, 0f, 56f);
            var change = Ui.Button(navRow, Loc.T("songs.changeNavigator"), () => App.Ui.Show<NavigatorsScreen>(), ButtonStyle.Ghost, 24);
            change.GetComponent<RectTransform>().Fill(110f, 54f, 120f, 0f);

            var start = Ui.Button(panel, Loc.T("songs.start"), StartRace, ButtonStyle.Accent, 48);
            start.GetComponent<RectTransform>().At(1f, 0f, -36f, 26f, 330f, 110f, 1f, 0f);
        }

        static void Section(RectTransform stack, string title)
        {
            var t = Ui.Label(stack, title, 26, UiTheme.Primary, TextAnchor.LowerLeft, FontStyle.Bold);
            t.Layout(-1f, 40f);
        }

        void RefreshHints()
        {
            _modeHint.text = Loc.T("mode." + S.LastMode.ToString().ToLowerInvariant() + ".hint");
            if (_selected == null)
            {
                _best.text = string.Empty;
                return;
            }
            var best = _selected.Best(SongLibrary.ScoreKey(S.LastMode, S.LastDifficulty));
            _best.text = best != null ? Loc.F("songs.best", best.score.ToString("N0"), best.rank, Format.Percent(best.accuracy)) : Loc.T("songs.noBest");
        }

        void RebuildList()
        {
            for (int i = _listContent.childCount - 1; i >= 0; i--) Destroy(_listContent.GetChild(i).gameObject);
            _rowFrames.Clear();
            foreach (var song in App.Songs.Songs) BuildRow(song);
            if (App.Songs.Songs.Count <= 1)
            {
                var hint = Ui.Label(_listContent, Loc.T("songs.importHint"), 26, UiTheme.InkSoft, TextAnchor.MiddleCenter);
                hint.Layout(-1f, 160f);
            }
        }

        void BuildRow(SongEntry song)
        {
            var row = Ui.Card(_listContent, UiTheme.Panel, "Song " + song.id, false);
            row.Layout(-1f, 128f);
            var frame = Ui.Image(row.rectTransform, UiSprites.RoundedOutline, UiTheme.Line, "Frame");
            frame.rectTransform.Fill();
            _rowFrames[song.id] = frame;

            var accent = Ui.Skew(row.rectTransform, song.builtIn ? UiTheme.Accent : UiTheme.Primary, song.builtIn ? UiTheme.AccentDark : UiTheme.PrimaryDark, 8f, "Accent");
            accent.rectTransform.At(0f, 0.5f, 22f, 0f, 14f, 90f, 0f, 0.5f);
            var title = Ui.FitLabel(row.rectTransform, song.title, 34, UiTheme.Ink, TextAnchor.UpperLeft);
            title.rectTransform.Fill(56f, 14f, 120f, 62f);
            string info = (string.IsNullOrEmpty(song.artist) ? string.Empty : song.artist + "  ·  ") + Format.Duration(song.duration) + "  ·  " + Mathf.RoundToInt(song.bpm) + " BPM";
            var meta = Ui.Label(row.rectTransform, info, 22, UiTheme.InkSoft, TextAnchor.LowerLeft);
            meta.rectTransform.Fill(56f, 66f, 120f, 16f);
            Ui.AddClick(row.gameObject, () => Select(song));

            if (!song.builtIn)
            {
                var delete = Ui.RoundButton(row.rectTransform, "✕", () => ConfirmDelete(song), UiTheme.PanelAlt, UiTheme.InkSoft, "Delete");
                delete.GetComponent<RectTransform>().At(1f, 0.5f, -20f, 0f, 68f, 68f, 1f, 0.5f);
            }
        }

        void Select(SongEntry song)
        {
            _selected = song;
            foreach (var kv in _rowFrames) kv.Value.color = song != null && kv.Key == song.id ? UiTheme.Primary : UiTheme.Line;
            if (song == null)
            {
                _selectedTitle.text = Loc.T("songs.none");
                _selectedInfo.text = string.Empty;
            }
            else
            {
                S.LastSongId = song.id;
                S.Save();
                _selectedTitle.text = song.title;
                _selectedInfo.text = Format.Duration(song.duration) + "  ·  " + Mathf.RoundToInt(song.bpm) + " BPM" + (song.builtIn ? "  ·  " + Loc.T("songs.demo") : string.Empty);
            }
            RefreshHints();
        }

        void ConfirmDelete(SongEntry song)
        {
            Popup.Confirm(Loc.T("songs.deleteTitle"), Loc.F("songs.deleteBody", song.title), Loc.T("songs.delete"), () =>
            {
                App.Songs.Delete(song);
                if (_selected == song) _selected = App.Songs.Songs.Count > 0 ? App.Songs.Songs[0] : null;
                RebuildList();
                Select(_selected);
            }, null, ButtonStyle.Danger);
        }

        void Import()
        {
            AudioFilePicker.Pick(path =>
            {
                if (!string.IsNullOrEmpty(path)) DoImport(path);
            });
        }

        async void DoImport(string path)
        {
            var cancel = new CancellationTokenSource();
            App.Ui.Loading.Show(Loc.T("import.title"), () => cancel.Cancel());
            try
            {
                var entry = await App.Songs.ImportAsync(path, App.Ui.Loading.Report, cancel.Token);
                if (this == null) return;
                RebuildList();
                Select(entry);
                App.Ui.Toast(Loc.F("import.done", entry.title));
            }
            catch (OperationCanceledException)
            {
                App.Ui.Toast(Loc.T("import.cancelled"));
            }
            catch (BackendException e)
            {
                Popup.Message(Loc.T("import.failed"), e.Friendly);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Popup.Message(Loc.T("import.failed"), e.Message);
            }
            finally
            {
                App.Ui.Loading.Hide();
                cancel.Dispose();
            }
        }

        void StartRace()
        {
            if (_selected == null)
            {
                App.Ui.Toast(Loc.T("songs.none"));
                return;
            }
            App.StartRace(new RaceSetup
            {
                song = _selected,
                mode = S.LastMode,
                difficulty = S.LastDifficulty,
                navigatorId = App.SelectedNavigator.id,
                vesselId = S.Vessel,
                themeId = S.Theme,
            });
        }
    }
}
