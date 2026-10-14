using System;
using UnityEngine;

namespace VMS.Songs
{
    /// <summary>
    /// Lets the player choose an audio file:
    /// • Editor: native file dialog.
    /// • Android/iOS with yasirkula's NativeFilePicker (VMS_NATIVE_FILE_PICKER): the system document picker
    ///   (Storage Access Framework – no storage permission needed).
    /// • Everything else: the in-game <see cref="VMS.UI.FileBrowserPopup"/>.
    /// </summary>
    public static class AudioFilePicker
    {
        public static bool UsesSystemPicker
        {
            get
            {
#if UNITY_EDITOR
                return true;
#elif VMS_NATIVE_FILE_PICKER && (UNITY_ANDROID || UNITY_IOS)
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Invokes <paramref name="onPicked"/> with a readable file path, or null when cancelled.</summary>
        public static void Pick(Action<string> onPicked)
        {
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanelWithFilters(Loc.T("import.title"), string.Empty,
                new[] { "Audio", "mp3,ogg,wav,aif,aiff" });
            onPicked(string.IsNullOrEmpty(path) ? null : path);
#elif VMS_NATIVE_FILE_PICKER && (UNITY_ANDROID || UNITY_IOS)
            string[] types =
            {
                NativeFilePicker.ConvertExtensionToFileType("mp3"),
                NativeFilePicker.ConvertExtensionToFileType("ogg"),
                NativeFilePicker.ConvertExtensionToFileType("wav"),
            };
            NativeFilePicker.PickFile(path => onPicked(string.IsNullOrEmpty(path) ? null : path), types);
#else
            VMS.UI.FileBrowserPopup.Show(onPicked);
#endif
        }
    }
}
