using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using VMS.Backend;
using VMS.Core;

namespace VMS.UI
{
    public sealed class SettingsScreen : UiScreen
    {
        static readonly int[] FpsOptions = { 30, 60, 90, 120 };

        GameSettings S => App.Settings;
        Text _offsetLabel;

        protected override void Build(object arg)
        {
            Backdrop.Sky(Root);
            Ui.Header(Root, Loc.T("settings.title"), () => App.Ui.Back());
            var card = Ui.Card(Root, UiTheme.Panel.WithAlpha(0.97f), "Card").rectTransform;
            card.Fill(200f, 150f, 200f, 40f);
            Ui.ScrollList(card, out var content, 14f, 40).GetComponent<RectTransform>().Fill(0f, 10f, 0f, 10f);

            Section(content, Loc.T("settings.audio"));
            SliderRow(content, Loc.T("settings.music"), 0f, 1f, S.MusicVolume, v => S.MusicVolume = v);
            SliderRow(content, Loc.T("settings.sfx"), 0f, 1f, S.SfxVolume, v => S.SfxVolume = v);
            SliderRow(content, Loc.T("settings.voice"), 0f, 1f, S.VoiceVolume, v => S.VoiceVolume = v);
            _offsetLabel = SliderRow(content, OffsetText(), -200f, 200f, S.AudioOffsetMs, v =>
            {
                S.AudioOffsetMs = Mathf.RoundToInt(v / 5f) * 5;
                _offsetLabel.text = OffsetText();
            }, true);
            Hint(content, Loc.T("settings.offsetHint"));

            Section(content, Loc.T("settings.controls"));
            Ui.Segmented(content, new[] { Loc.T("controls.tap"), Loc.T("controls.swipe"), Loc.T("controls.drag"), Loc.T("controls.tilt") }, (int)S.Controls, i =>
            {
                S.Controls = (ControlScheme)i;
                App.ApplySettings();
            }, 26);
            SliderRow(content, Loc.T("settings.tilt"), 0.4f, 2f, S.TiltSensitivity, v => S.TiltSensitivity = v);
            Ui.Toggle(content, Loc.T("settings.autoSkill"), S.AutoSkill, v => { S.AutoSkill = v; App.ApplySettings(); }).Layout(-1f, 64f);
            Ui.Toggle(content, Loc.T("settings.haptics"), S.Haptics, v => { S.Haptics = v; App.ApplySettings(); }).Layout(-1f, 64f);
            Ui.Toggle(content, Loc.T("settings.barks"), S.ShowBarks, v => { S.ShowBarks = v; App.ApplySettings(); }).Layout(-1f, 64f);

            Section(content, Loc.T("settings.graphics"));
            Ui.Segmented(content, new[] { Loc.T("graphics.ultraLow"), Loc.T("graphics.low"), Loc.T("graphics.medium"), Loc.T("graphics.high") },
                System.Array.IndexOf(TierOrder, S.Graphics), i =>
            {
                S.Graphics = TierOrder[i];
                App.ApplySettings();
            }, 24);
            Hint(content, Loc.T("settings.graphicsHint"));
            Ui.Toggle(content, Loc.T("settings.flatUi"), S.FlatUi, v =>
            {
                S.FlatUi = v;
                App.ApplySettings();
                // Screens read the flag while they are built: rebuild the stack in the new style.
                App.Ui.Reset<HomeScreen>();
                App.Ui.Show<SettingsScreen>();
            }).Layout(-1f, 64f);
            Ui.Toggle(content, Loc.T("settings.lowPowerMenus"), S.LowPowerMenus, v => { S.LowPowerMenus = v; App.ApplySettings(); }).Layout(-1f, 64f);
            Hint(content, Loc.T("settings.flatUiHint"));
            int fpsIndex = System.Array.IndexOf(FpsOptions, S.TargetFps);
            Ui.Segmented(content, new[] { "30 FPS", "60 FPS", "90 FPS", "120 FPS" }, fpsIndex < 0 ? 1 : fpsIndex, i =>
            {
                S.TargetFps = FpsOptions[i];
                App.ApplySettings();
            }, 26);

            Section(content, Loc.T("settings.language"));
            var languageNames = System.Array.ConvertAll(Loc.Languages, Loc.NativeName);
            Ui.SegmentedGrid(content, languageNames, System.Array.IndexOf(Loc.Languages, S.Language), 4, i =>
            {
                S.Language = Loc.Languages[i];
                App.ApplySettings();
                App.Ui.Reset<HomeScreen>();
                App.Ui.Show<SettingsScreen>();
            }, 26);

            BuildAccount(content);
            if (App.Cloud != null) BuildCloud(content);
            // Pointing the game at another server is a development tool, not a player setting.
            if (Debug.isDebugBuild || Application.isEditor) BuildServer(content);

            Section(content, Loc.T("settings.about"));
            Hint(content, Loc.F("settings.aboutBody", Application.version, Application.unityVersion));
        }

        void OnDisable()
        {
            if (App != null) App.ApplySettings();
        }

        /// <summary>Graphics options left to right; the enum keeps its stored values.</summary>
        static readonly GraphicsTier[] TierOrder = { GraphicsTier.UltraLow, GraphicsTier.Low, GraphicsTier.Medium, GraphicsTier.High };

        string OffsetText() => Loc.F("settings.offset", S.AudioOffsetMs);

        static void Section(RectTransform content, string title)
        {
            var t = Ui.Label(content, title, 34, UiTheme.Primary, TextAnchor.LowerLeft, FontStyle.Bold);
            t.Layout(-1f, 70f);
        }

        static void Hint(RectTransform content, string text)
        {
            var t = Ui.Label(content, text, 22, UiTheme.InkSoft);
            t.Layout(-1f, 60f);
        }

        static Text SliderRow(RectTransform content, string label, float min, float max, float value, System.Action<float> onChange, bool whole = false)
        {
            var row = Ui.Node("Row " + label, content);
            row.Layout(-1f, 70f);
            var text = Ui.Label(row, label, 28, UiTheme.Ink);
            text.rectTransform.Region(0f, 0f, 0.4f, 1f);
            var slider = Ui.Slider(row, min, max, value, onChange, whole);
            slider.GetComponent<RectTransform>().Region(0.42f, 0f, 1f, 1f);
            return text;
        }

        void BuildAccount(RectTransform content)
        {
            Section(content, Loc.T("settings.account"));
            var account = App.Auth.Account;
            string name = account != null ? account.displayName : "-";
            string providers = account != null && account.providers != null
                ? string.Join(", ", System.Array.ConvertAll(account.providers, p => Loc.T("provider." + p)))
                : "-";
            Hint(content, Loc.F("settings.accountInfo", name, account != null && !string.IsNullOrEmpty(account.email) ? account.email : "-", providers));
            if (!App.Backend.IsConnected) Hint(content, Loc.T("net.offlineSettings"));
            if (account != null && !account.HasProvider(Providers.Google))
            {
                var link = Ui.Button(content, Loc.T("settings.linkGoogle"), async () =>
                {
                    if (await App.Ui.Busy(Loc.T("common.working"), () => App.Auth.LinkGooglePlayAsync())) App.Ui.Toast(Loc.T("settings.linked"));
                }, ButtonStyle.Secondary, 28);
                link.Layout(-1f, 84f);
                link.interactable = App.Auth.GooglePlayAvailable;
            }
            if (account != null && !account.HasProvider(Providers.Steam))
            {
                var link = Ui.Button(content, Loc.T("settings.linkSteam"), LinkSteam, ButtonStyle.Secondary, 28);
                link.Layout(-1f, 84f);
                link.interactable = App.Auth.SteamAvailable;
            }
            Ui.Button(content, Loc.T("settings.signOut"), () =>
            {
                int pending = App.Backend.PendingRaceCount;
                string body = Loc.T("settings.signOutBody") + (pending > 0 ? "\n" + Loc.F("settings.signOutPending", pending) : string.Empty);
                Popup.Confirm(Loc.T("settings.signOut"), body, Loc.T("settings.signOut"), async () =>
                    await App.Ui.Busy(Loc.T("common.working"), () => App.Auth.SignOutAsync()), null, ButtonStyle.Danger);
            }, ButtonStyle.Danger, 28).Layout(-1f, 84f);
            if (account != null)
                Ui.Button(content, Loc.T("settings.deleteAccount"), ShowDeleteAccount, ButtonStyle.Secondary, 28).Layout(-1f, 84f);
        }

        /// <summary>Offline mode: back up the offline save to an online account and restore it on another device.</summary>
        void BuildCloud(RectTransform content)
        {
            var cloud = App.Cloud;
            Section(content, Loc.T("cloud.title"));
            if (!cloud.SignedIn)
            {
                Hint(content, Loc.T("cloud.intro"));
                Ui.Button(content, Loc.T("cloud.connect"), ShowCloudSignIn, ButtonStyle.Primary, 28).Layout(-1f, 84f);
                return;
            }
            var status = Ui.Label(content, Loc.F("cloud.account", cloud.Account.displayName), 22, UiTheme.InkSoft);
            status.Layout(-1f, 60f);
            RefreshCloudStatus(status);
            Ui.Button(content, Loc.T("cloud.backup"), () => Backup(false, status), ButtonStyle.Primary, 28).Layout(-1f, 84f);
            Ui.Button(content, Loc.T("cloud.restore"), ConfirmRestore, ButtonStyle.Secondary, 28).Layout(-1f, 84f);
            Ui.Toggle(content, Loc.T("cloud.auto"), cloud.AutoBackup, v => cloud.AutoBackup = v).Layout(-1f, 64f);
            Ui.Button(content, Loc.T("cloud.signOut"), async () =>
            {
                if (await App.Ui.Busy(Loc.T("common.working"), () => cloud.SignOutAsync())) App.Ui.Show<SettingsScreen>(null, false);
            }, ButtonStyle.Secondary, 26).Layout(-1f, 76f);
        }

        async void RefreshCloudStatus(Text status)
        {
            try
            {
                var save = await App.Cloud.GetAsync();
                if (status == null) return;
                status.text = Loc.F("cloud.account", App.Cloud.Account.displayName) + "\n" + (save.exists
                    ? Loc.F("cloud.lastBackup", System.DateTimeOffset.FromUnixTimeMilliseconds(save.updatedAt).ToLocalTime().ToString(Loc.T("format.dateTime")), save.device, CloudSave.DescribeSummary(save.summary))
                    : Loc.T("cloud.noBackup"));
            }
            catch (BackendException)
            {
                if (status != null) status.text = Loc.F("cloud.account", App.Cloud.Account.displayName) + "\n" + Loc.T("mail.offline");
            }
        }

        async void Backup(bool force, Text status)
        {
            try
            {
                App.Ui.Loading.Show(Loc.T("cloud.uploading"));
                await App.Cloud.BackupAsync(force);
                App.Ui.Toast(Loc.T("cloud.backedUp"));
                if (status != null) RefreshCloudStatus(status);
            }
            catch (BackendException e)
            {
                if (e.Code == "save_conflict")
                    Popup.Confirm(Loc.T("cloud.conflictTitle"), Loc.T("cloud.conflictBody"), Loc.T("cloud.overwrite"), () => Backup(true, status), Loc.T("common.cancel"), ButtonStyle.Danger);
                else Popup.Message(Loc.T("common.error"), e.Friendly);
            }
            finally
            {
                App.Ui.Loading.Hide();
            }
        }

        async void ConfirmRestore()
        {
            CloudSaveInfo save = null;
            if (!await App.Ui.Busy(Loc.T("common.working"), async () => save = await App.Cloud.GetAsync())) return;
            if (!save.exists)
            {
                Popup.Message(Loc.T("cloud.title"), Loc.T("cloud.noBackup"));
                return;
            }
            string when = System.DateTimeOffset.FromUnixTimeMilliseconds(save.updatedAt).ToLocalTime().ToString(Loc.T("format.dateTime"));
            Popup.Confirm(Loc.T("cloud.restore"), Loc.F("cloud.restoreBody", when, save.device, CloudSave.DescribeSummary(save.summary)), Loc.T("cloud.restoreGo"), async () =>
            {
                PlayerProfile restored = null;
                if (!await App.Ui.Busy(Loc.T("common.working"), async () => restored = await App.Cloud.RestoreAsync())) return;
                App.SetProfile(restored);
                App.Ui.Toast(Loc.T("cloud.restored"));
            }, null, ButtonStyle.Danger);
        }

        void ShowCloudSignIn()
        {
            var cloud = App.Cloud;
            var p = Popup.Open(Loc.T("cloud.connect"), 1040f, 900f);
            var stack = Ui.Node("Form", p.Body).Fill();
            Ui.VStack(stack, 16f);
            Ui.Label(stack, Loc.T("cloud.signInHint"), 24, UiTheme.InkSoft, TextAnchor.UpperLeft).Layout(-1f, 80f);
            var login = Ui.Input(stack, Loc.T("login.usernameOrEmail"), InputField.ContentType.EmailAddress);
            login.Layout(-1f, 90f);
            var password = Ui.Input(stack, Loc.T("login.password"), InputField.ContentType.Password);
            password.Layout(-1f, 90f);
            System.Action reopen = () =>
            {
                p.Close();
                App.Ui.Show<SettingsScreen>(null, false);
                App.Ui.Toast(Loc.T("cloud.connected"));
            };
            Ui.Button(stack, Loc.T("login.signIn"), async () =>
            {
                if (string.IsNullOrWhiteSpace(login.text) || string.IsNullOrEmpty(password.text)) return;
                if (await App.Ui.Busy(Loc.T("login.signingIn"), () => cloud.SignInAsync(login.text, password.text))) reopen();
            }, ButtonStyle.Accent).Layout(-1f, 92f);
            Ui.Button(stack, Loc.T("cloud.createAccount"), () =>
            {
                p.Close();
                ShowCloudRegister();
            }, ButtonStyle.Secondary, 28).Layout(-1f, 80f);
            if (cloud.SupportsGoogle)
                Ui.Button(stack, "Google Play Games", async () => { if (await App.Ui.Busy(Loc.T("login.signingIn"), () => cloud.SignInGoogleAsync())) reopen(); }, ButtonStyle.Primary, 28).Layout(-1f, 80f);
            if (cloud.SupportsSteam)
                Ui.Button(stack, "Steam", async () => { if (await App.Ui.Busy(Loc.T("login.signingIn"), () => cloud.SignInSteamAsync())) reopen(); }, ButtonStyle.Primary, 28).Layout(-1f, 80f);
        }

        void ShowCloudRegister()
        {
            var p = Popup.Open(Loc.T("cloud.createAccount"), 1040f, 860f);
            var stack = Ui.Node("Form", p.Body).Fill();
            Ui.VStack(stack, 16f);
            var username = Ui.Input(stack, Loc.T("login.username"), InputField.ContentType.Alphanumeric);
            username.characterLimit = 20;
            username.Layout(-1f, 90f);
            var email = Ui.Input(stack, Loc.T("login.email"), InputField.ContentType.EmailAddress);
            email.Layout(-1f, 90f);
            var password = Ui.Input(stack, Loc.T("login.password"), InputField.ContentType.Password);
            password.Layout(-1f, 90f);
            var error = Ui.Label(stack, Loc.T("login.rules"), 22, UiTheme.InkSoft, TextAnchor.MiddleLeft);
            error.Layout(-1f, 60f);
            Ui.Button(stack, Loc.T("login.create"), async () =>
            {
                string problem = AccountRules.ValidateRegistration(username.text.Trim(), email.text.Trim(), password.text);
                if (problem != null)
                {
                    error.text = Loc.T("err." + problem);
                    error.color = UiTheme.Bad;
                    return;
                }
                if (!await App.Ui.Busy(Loc.T("login.creating"), () => App.Cloud.RegisterAsync(username.text, email.text, password.text))) return;
                p.Close();
                App.Ui.Show<SettingsScreen>(null, false);
                App.Ui.Toast(Loc.T("cloud.connected"));
            }, ButtonStyle.Accent).Layout(-1f, 96f);
        }

        /// <summary>
        /// In-app account deletion (Google Play policy). Retyping a word guards against a stray tap;
        /// password accounts also confirm their password, which the server checks again.
        /// </summary>
        public void ShowDeleteAccount()
        {
            bool needsPassword = App.Auth.HasPassword;
            var p = Popup.Open(Loc.T("delete.title"), 1180f, needsPassword ? 820f : 710f);
            var stack = Ui.Node("Form", p.Body).Fill();
            Ui.VStack(stack, 16f);
            Ui.Label(stack, Loc.T(App.Backend.IsOnline ? "delete.body" : "delete.bodyOffline"), 26, UiTheme.Ink, TextAnchor.UpperLeft).Layout(-1f, 230f);
            InputField password = null;
            if (needsPassword)
            {
                password = Ui.Input(stack, Loc.T("delete.password"), InputField.ContentType.Password);
                password.Layout(-1f, 92f);
            }
            var prompt = Ui.Label(stack, Loc.F("delete.typeWord", Loc.T("delete.word")), 26, UiTheme.InkSoft, TextAnchor.MiddleLeft, FontStyle.Bold);
            prompt.Layout(-1f, 50f);
            var word = Ui.Input(stack, Loc.T("delete.word"), InputField.ContentType.Standard);
            word.Layout(-1f, 92f);
            Ui.Button(stack, Loc.T("delete.button"), async () =>
            {
                if (!DeleteWordMatches(word.text))
                {
                    prompt.color = UiTheme.Bad;
                    return;
                }
                if (password != null && string.IsNullOrEmpty(password.text))
                {
                    password.ActivateInputField();
                    return;
                }
                string pw = password != null ? password.text : null;
                if (!await App.Ui.Busy(Loc.T("delete.working"), () => App.Auth.DeleteAccountAsync(pw))) return;
                p.Close();
                Popup.Message(Loc.T("delete.doneTitle"), Loc.T("delete.doneBody"));
            }, ButtonStyle.Danger, 30).Layout(-1f, 96f);
        }

        static bool DeleteWordMatches(string text)
        {
            string typed = (text ?? string.Empty).Trim().ToUpperInvariant();
            return typed.Length > 0 &&
                   (typed == Loc.T("delete.word").ToUpperInvariant() || typed == Loc.T("delete.wordAlt").ToUpperInvariant());
        }

        async void LinkSteam()
        {
            var cancel = new CancellationTokenSource();
            App.Ui.Loading.Show(Loc.T("login.steamBrowser"), () => cancel.Cancel());
            try
            {
                await App.Auth.LinkSteamAsync(cancel.Token);
                App.Ui.Toast(Loc.T("settings.linked"));
            }
            catch (BackendException e)
            {
                if (e.Code != "cancelled") Popup.Message(Loc.T("common.error"), e.Friendly);
            }
            finally
            {
                App.Ui.Loading.Hide();
                cancel.Dispose();
            }
        }

        void BuildServer(RectTransform content)
        {
            Section(content, Loc.T("settings.server"));
            Hint(content, Loc.T("settings.serverHint"));
            var input = Ui.Input(content, "https://… / offline", InputField.ContentType.Standard, S.ServerOverride);
            input.Layout(-1f, 84f);
            Ui.Button(content, Loc.T("settings.serverApply"), () =>
            {
                S.ServerOverride = input.text.Trim();
                S.Save();
                Popup.Message(Loc.T("settings.server"), Loc.T("settings.serverRestart"));
            }, ButtonStyle.Secondary, 28).Layout(-1f, 84f);
        }
    }
}
