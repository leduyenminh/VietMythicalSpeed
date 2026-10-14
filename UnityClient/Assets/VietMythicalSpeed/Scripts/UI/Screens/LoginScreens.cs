using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using VMS.Backend;
using VMS.Core;

namespace VMS.UI
{
    public sealed class SplashScreen : UiScreen
    {
        protected override void Build(object arg)
        {
            Backdrop.Sky(Root);
            Logo.Build(Root, 0.5f, 0.56f, 1f);
            var hint = Ui.Label(Root, Loc.T("boot.loading"), UiTheme.BodySize, UiTheme.InkSoft, TextAnchor.MiddleCenter);
            hint.rectTransform.At(0.5f, 0f, 0f, 120f, 1200f, 60f);
        }

        public override bool OnBack() => true;
    }

    /// <summary>The game logo block: slanted title plate, Vietnamese subtitle and halo.</summary>
    public static class Logo
    {
        public static RectTransform Build(RectTransform parent, float ax, float ay, float scale)
        {
            var root = Ui.Node("Logo", parent).At(ax, ay, 0f, 0f, 1100f, 420f);
            root.localScale = Vector3.one * scale;
            var halo = Ui.Image(root, UiSprites.Halo, UiTheme.Accent, "Halo");
            halo.rectTransform.At(0.5f, 0.5f, -40f, 150f, 420f, 420f);
            halo.gameObject.AddComponent<Bob>();
            var plate = Ui.Skew(root, UiTheme.Hex("#4FB0FF"), UiTheme.PrimaryDark, 60f, "Plate");
            plate.rectTransform.At(0.5f, 0.5f, 0f, 10f, 1040f, 170f);
            var title = Ui.FitLabel(plate.rectTransform, "VIET MYTHICAL SPEED", 96, Color.white, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic);
            title.rectTransform.Fill(40f, 10f, 40f, 10f);
            var stripe = Ui.Skew(root, UiTheme.Accent, UiTheme.AccentDark, 30f, "Stripe");
            stripe.rectTransform.At(0.5f, 0.5f, 120f, -100f, 640f, 64f);
            var sub = Ui.FitLabel(stripe.rectTransform, Loc.T("game.subtitle"), 38, UiTheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            sub.rectTransform.Fill(30f, 4f, 30f, 4f);
            return root;
        }
    }

    public sealed class LoginScreen : UiScreen
    {
        CancellationTokenSource _steamCancel;

        protected override void Build(object arg)
        {
            Backdrop.Sky(Root);
            Logo.Build(Root, 0.3f, 0.6f, 0.8f);
            var lore = Ui.Label(Root, Loc.T("login.lore"), 30, UiTheme.InkSoft, TextAnchor.UpperCenter, FontStyle.Italic);
            lore.rectTransform.At(0.3f, 0.28f, 0f, 0f, 900f, 200f);

            var card = Ui.Card(Root, UiTheme.Panel.WithAlpha(0.96f));
            card.rectTransform.At(1f, 0.5f, -90f, -10f, 720f, 860f, 1f, 0.5f);
            var title = Ui.Label(card.rectTransform, Loc.T("login.title"), UiTheme.HeadingSize, UiTheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.rectTransform.At(0.5f, 1f, 0f, -40f, 640f, 70f, 0.5f, 1f);

            var stack = Ui.Node("Buttons", card.rectTransform).Fill(70f, 140f, 70f, 110f);
            Ui.VStack(stack, 22f);
            Ui.Button(stack, Loc.T("login.guest"), Guest, ButtonStyle.Accent, 36).Layout(-1f, 104f);

            var google = Ui.Button(stack, "Google Play Games", Google, ButtonStyle.Primary, 34);
            google.Layout(-1f, 96f);
            google.interactable = App.Auth.GooglePlayAvailable;

            var steam = Ui.Button(stack, "Steam", Steam, ButtonStyle.Primary, 34);
            steam.Layout(-1f, 96f);
            steam.interactable = App.Auth.SteamAvailable;

            Ui.Button(stack, Loc.T("login.signIn"), ShowSignIn, ButtonStyle.Secondary, 34).Layout(-1f, 96f);
            Ui.Button(stack, Loc.T("login.register"), ShowRegister, ButtonStyle.Secondary, 34).Layout(-1f, 96f);

            string note = !App.Auth.GooglePlayAvailable ? Loc.T("login.gpgsNote") : string.Empty;
            if (!App.Auth.SteamAvailable) note += (note.Length > 0 ? "\n" : string.Empty) + Loc.T("login.steamNote");
            var notes = Ui.Label(card.rectTransform, note, 22, UiTheme.InkFaint, TextAnchor.LowerCenter);
            notes.rectTransform.Fill(50f, 760f, 50f, 24f);

            string server = App.Backend.IsOnline ? Loc.F("login.serverOnline", App.Backend.Key) : Loc.T("login.serverOffline");
            var serverLabel = Ui.Label(Root, server + "  ·  v" + Application.version, 24, UiTheme.InkSoft, TextAnchor.LowerLeft);
            serverLabel.rectTransform.At(0f, 0f, 40f, 24f, 1400f, 40f, 0f, 0f);
        }

        public override bool OnBack()
        {
            Application.Quit();
            return true;
        }

        async void Guest() => await App.Ui.Busy(Loc.T("login.signingIn"), () => App.Auth.SignInGuestAsync());

        async void Google() => await App.Ui.Busy(Loc.T("login.signingIn"), () => App.Auth.SignInGooglePlayAsync());

        async void Steam()
        {
            _steamCancel = new CancellationTokenSource();
            var cancel = _steamCancel;
            App.Ui.Loading.Show(Loc.T("login.steamBrowser"), () => cancel.Cancel());
            try
            {
                await App.Auth.SignInSteamAsync(cancel.Token);
            }
            catch (BackendException e)
            {
                if (e.Code != "cancelled") Popup.Message(Loc.T("common.error"), e.Friendly);
            }
            finally
            {
                App.Ui.Loading.Hide();
            }
        }

        void ShowSignIn()
        {
            var p = Popup.Open(Loc.T("login.signIn"), 980f, 720f);
            var stack = Ui.Node("Form", p.Body).Fill();
            Ui.VStack(stack, 22f);
            var login = Ui.Input(stack, Loc.T("login.usernameOrEmail"), InputField.ContentType.EmailAddress);
            login.Layout(-1f, 96f);
            var password = Ui.Input(stack, Loc.T("login.password"), InputField.ContentType.Password);
            password.Layout(-1f, 96f);
            Ui.Button(stack, Loc.T("login.signIn"), async () =>
            {
                if (string.IsNullOrWhiteSpace(login.text) || string.IsNullOrEmpty(password.text)) return;
                bool ok = await App.Ui.Busy(Loc.T("login.signingIn"), () => App.Auth.SignInPasswordAsync(login.text, password.text));
                if (ok) p.Close();
            }, ButtonStyle.Accent).Layout(-1f, 100f);
            Ui.Button(stack, Loc.T("forgot.link"), () =>
            {
                p.Close();
                ShowForgot(login.text.Contains("@") ? login.text.Trim() : string.Empty);
            }, ButtonStyle.Ghost, 26).Layout(-1f, 60f);
        }

        /// <summary>Step 1 of the password reset: the server emails a 6-digit code (online accounts only).</summary>
        void ShowForgot(string email)
        {
            if (!App.Backend.IsOnline)
            {
                Popup.Message(Loc.T("forgot.title"), Loc.T("forgot.offline"));
                return;
            }
            var p = Popup.Open(Loc.T("forgot.title"), 1040f, 600f);
            var stack = Ui.Node("Form", p.Body).Fill();
            Ui.VStack(stack, 20f);
            Ui.Label(stack, Loc.T("forgot.intro"), 26, UiTheme.Ink, TextAnchor.UpperLeft).Layout(-1f, 90f);
            var input = Ui.Input(stack, Loc.T("login.email"), InputField.ContentType.EmailAddress, email);
            input.Layout(-1f, 96f);
            Ui.Button(stack, Loc.T("forgot.send"), async () =>
            {
                string address = input.text.Trim();
                if (!AccountRules.IsValidEmail(address))
                {
                    Popup.Message(Loc.T("forgot.title"), Loc.T("err.invalid_email"));
                    return;
                }
                string lang = Loc.Language;
                if (!await App.Ui.Busy(Loc.T("common.working"), () => App.Backend.RequestPasswordResetAsync(address, lang))) return;
                p.Close();
                ShowReset(address);
            }, ButtonStyle.Accent).Layout(-1f, 100f);
        }

        /// <summary>Step 2: code + new password; success signs the player in.</summary>
        public void ShowReset(string email)
        {
            var p = Popup.Open(Loc.T("forgot.title"), 1040f, 900f);
            var stack = Ui.Node("Form", p.Body).Fill();
            Ui.VStack(stack, 16f);
            Ui.Label(stack, Loc.F("forgot.sent", email), 26, UiTheme.Ink, TextAnchor.UpperLeft).Layout(-1f, 90f);
            var code = Ui.Input(stack, Loc.T("forgot.code"), InputField.ContentType.IntegerNumber);
            code.characterLimit = 6;
            code.Layout(-1f, 92f);
            var password = Ui.Input(stack, Loc.T("forgot.newPassword"), InputField.ContentType.Password);
            password.Layout(-1f, 92f);
            var confirm = Ui.Input(stack, Loc.T("login.confirm"), InputField.ContentType.Password);
            confirm.Layout(-1f, 92f);
            var error = Ui.Label(stack, string.Empty, 24, UiTheme.Bad, TextAnchor.MiddleLeft);
            error.Layout(-1f, 44f);
            Ui.Button(stack, Loc.T("forgot.reset"), async () =>
            {
                if (code.text.Trim().Length != 6) error.text = Loc.T("err.code_invalid");
                else if (password.text.Length < 8) error.text = Loc.T("err.weak_password");
                else if (password.text != confirm.text) error.text = Loc.T("err.password_mismatch");
                else
                {
                    error.text = string.Empty;
                    if (await App.Ui.Busy(Loc.T("login.signingIn"), () => App.Auth.ResetPasswordAsync(email, code.text, password.text)))
                    {
                        p.Close();
                        App.Ui.Toast(Loc.T("forgot.done"), 3.5f);
                    }
                }
            }, ButtonStyle.Accent).Layout(-1f, 100f);
            Ui.Button(stack, Loc.T("forgot.resend"), async () =>
            {
                string lang = Loc.Language;
                if (await App.Ui.Busy(Loc.T("common.working"), () => App.Backend.RequestPasswordResetAsync(email, lang)))
                    App.Ui.Toast(Loc.T("forgot.resent"));
            }, ButtonStyle.Ghost, 24).Layout(-1f, 56f);
        }

        void ShowRegister()
        {
            var p = Popup.Open(Loc.T("login.register"), 1040f, 900f);
            var stack = Ui.Node("Form", p.Body).Fill();
            Ui.VStack(stack, 18f);
            var username = Ui.Input(stack, Loc.T("login.username"), InputField.ContentType.Alphanumeric);
            username.characterLimit = 20;
            username.Layout(-1f, 92f);
            var email = Ui.Input(stack, Loc.T("login.email"), InputField.ContentType.EmailAddress);
            email.Layout(-1f, 92f);
            var password = Ui.Input(stack, Loc.T("login.password"), InputField.ContentType.Password);
            password.Layout(-1f, 92f);
            var confirm = Ui.Input(stack, Loc.T("login.confirm"), InputField.ContentType.Password);
            confirm.Layout(-1f, 92f);
            var error = Ui.Label(stack, Loc.T("login.rules"), 24, UiTheme.InkSoft, TextAnchor.MiddleLeft);
            error.Layout(-1f, 70f);
            Ui.Button(stack, Loc.T("login.create"), async () =>
            {
                string problem = AccountRules.ValidateRegistration(username.text.Trim(), email.text.Trim(), password.text);
                if (problem == null && password.text != confirm.text) problem = "password_mismatch";
                if (problem != null)
                {
                    error.text = Loc.T("err." + problem);
                    error.color = UiTheme.Bad;
                    return;
                }
                bool ok = await App.Ui.Busy(Loc.T("login.creating"), () => App.Auth.RegisterAsync(username.text, email.text, password.text));
                if (ok) p.Close();
            }, ButtonStyle.Accent).Layout(-1f, 100f);
        }

        void OnDestroy()
        {
            if (_steamCancel != null) _steamCancel.Dispose();
        }
    }
}
