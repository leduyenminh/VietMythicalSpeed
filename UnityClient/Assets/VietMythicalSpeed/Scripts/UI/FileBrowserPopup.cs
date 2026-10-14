using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using VMS.Songs;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace VMS.UI
{
    /// <summary>
    /// In-game file browser used when no system picker is available (PC builds, or Android without the
    /// NativeFilePicker plugin – then it asks for the audio/storage read permission first).
    /// </summary>
    public sealed class FileBrowserPopup : MonoBehaviour
    {
        Popup _popup;
        RectTransform _content;
        Text _path;
        Action<string> _onPicked;
        bool _picked;

        public static void Show(Action<string> onPicked)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            string permission = AndroidSdkInt() >= 33 ? "android.permission.READ_MEDIA_AUDIO" : Permission.ExternalStorageRead;
            if (!Permission.HasUserAuthorizedPermission(permission))
            {
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => Open(onPicked);
                callbacks.PermissionDenied += _ => { Popup.Message(Loc.T("import.title"), Loc.T("import.permission")); onPicked(null); };
                callbacks.PermissionDeniedAndDontAskAgain += _ => { Popup.Message(Loc.T("import.title"), Loc.T("import.permission")); onPicked(null); };
                Permission.RequestUserPermission(permission, callbacks);
                return;
            }
#endif
            Open(onPicked);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static int AndroidSdkInt()
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION")) return version.GetStatic<int>("SDK_INT");
        }
#endif

        static void Open(Action<string> onPicked)
        {
            var popup = Popup.Open(Loc.T("import.title"), 1400f, 940f);
            var browser = popup.gameObject.AddComponent<FileBrowserPopup>();
            browser._popup = popup;
            browser._onPicked = onPicked;
            popup.Closed += browser.OnClosed;

            var roots = Ui.Node("Roots", popup.Body).At(0f, 1f, 0f, 0f, 1320f, 70f, 0f, 1f);
            Ui.HStack(roots, 14f);
            foreach (var root in Roots())
            {
                string dir = root.Value;
                Ui.Button(roots, root.Key, () => browser.Navigate(dir), ButtonStyle.Secondary, 24).Layout(250f, 66f);
            }
            browser._path = Ui.Label(popup.Body, string.Empty, 22, UiTheme.InkSoft);
            browser._path.rectTransform.At(0f, 1f, 0f, -80f, 1320f, 40f, 0f, 1f);
            var list = Ui.ScrollList(popup.Body, out browser._content, 6f, 4);
            list.GetComponent<RectTransform>().Fill(0f, 130f, 0f, 0f);
            foreach (var root in Roots())
            {
                browser.Navigate(root.Value);
                break;
            }
        }

        static IEnumerable<KeyValuePair<string, string>> Roots()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const string storage = "/storage/emulated/0";
            yield return new KeyValuePair<string, string>(Loc.T("import.music"), storage + "/Music");
            yield return new KeyValuePair<string, string>(Loc.T("import.downloads"), storage + "/Download");
            yield return new KeyValuePair<string, string>(Loc.T("import.storage"), storage);
#else
            yield return new KeyValuePair<string, string>(Loc.T("import.music"), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return new KeyValuePair<string, string>(Loc.T("import.downloads"), Path.Combine(home, "Downloads"));
            yield return new KeyValuePair<string, string>(Loc.T("import.storage"), home);
#endif
        }

        void Navigate(string directory)
        {
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            _path.text = directory;
            string[] dirs, files;
            try
            {
                dirs = Directory.GetDirectories(directory);
                files = Directory.GetFiles(directory);
            }
            catch (Exception e)
            {
                Ui.Label(_content, Loc.T("import.cannotOpen") + "\n" + e.Message, 24, UiTheme.Bad).Layout(-1f, 90f);
                return;
            }
            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            var parent = Directory.GetParent(directory);
            if (parent != null) Row("⬑  ..", UiTheme.InkSoft, () => Navigate(parent.FullName));
            foreach (var d in dirs)
            {
                string name = Path.GetFileName(d);
                if (name.StartsWith(".")) continue;
                string target = d;
                Row("▸  " + name, UiTheme.PrimaryDark, () => Navigate(target));
            }
            int shown = 0;
            foreach (var f in files)
            {
                if (!AudioLoader.IsSupported(f)) continue;
                string target = f;
                Row("♪  " + Path.GetFileName(f), UiTheme.Ink, () => Pick(target));
                shown++;
            }
            if (shown == 0 && dirs.Length == 0) Ui.Label(_content, Loc.T("import.empty"), 24, UiTheme.InkSoft, TextAnchor.MiddleCenter).Layout(-1f, 80f);
        }

        void Row(string label, Color color, Action onClick)
        {
            var b = Ui.Button(_content, label, onClick, ButtonStyle.Ghost, 28);
            b.Layout(-1f, 70f);
            var text = b.GetComponentInChildren<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.color = color;
        }

        void Pick(string path)
        {
            _picked = true;
            _popup.Close();
            _onPicked(path);
        }

        void OnClosed()
        {
            if (!_picked) _onPicked(null);
        }
    }
}
