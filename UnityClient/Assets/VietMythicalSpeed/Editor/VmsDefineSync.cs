using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace VMS.EditorTools
{
    /// <summary>
    /// Turns optional integrations on/off by detecting installed plugins and maintaining scripting defines:
    /// VMS_GPGS (Google Play Games plugin), VMS_STEAMWORKS (Steamworks.NET), VMS_NATIVE_FILE_PICKER
    /// (yasirkula NativeFilePicker), VMS_UNITY_IAP (Unity In-App Purchasing, Google Play Billing) and
    /// VMS_URP (Universal Render Pipeline).
    /// </summary>
    [InitializeOnLoad]
    public static class VmsDefineSync
    {
        static readonly KeyValuePair<string, string>[] Detect =
        {
            new KeyValuePair<string, string>("VMS_GPGS", "GooglePlayGames.PlayGamesPlatform"),
            new KeyValuePair<string, string>("VMS_STEAMWORKS", "Steamworks.SteamAPI"),
            new KeyValuePair<string, string>("VMS_NATIVE_FILE_PICKER", "NativeFilePicker"),
            new KeyValuePair<string, string>("VMS_UNITY_IAP", "UnityEngine.Purchasing.StoreController"),
            new KeyValuePair<string, string>("VMS_URP", "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset"),
        };

        static readonly NamedBuildTarget[] Targets =
        {
            NamedBuildTarget.Android, NamedBuildTarget.Standalone, NamedBuildTarget.iOS,
        };

        static VmsDefineSync()
        {
            EditorApplication.delayCall += Sync;
        }

        [MenuItem("VietMythicalSpeed/Setup/Refresh Plugin Defines", priority = 4)]
        public static void Sync()
        {
            var present = new HashSet<string>();
            foreach (var pair in Detect)
                if (TypeExists(pair.Value)) present.Add(pair.Key);

            foreach (var target in Targets)
            {
                string[] current;
                PlayerSettings.GetScriptingDefineSymbols(target, out current);
                var set = new HashSet<string>(current ?? new string[0]);
                bool changed = false;
                foreach (var pair in Detect)
                {
                    bool want = present.Contains(pair.Key);
                    // Platform-specific SDKs: Play Games only matters on Android, Steamworks only on desktop.
                    if (pair.Key == "VMS_GPGS" && target != NamedBuildTarget.Android) want = false;
                    if (pair.Key == "VMS_STEAMWORKS" && target != NamedBuildTarget.Standalone) want = false;
                    // Google Play Billing; Steam builds pay through the Steam wallet instead.
                    if (pair.Key == "VMS_UNITY_IAP" && target != NamedBuildTarget.Android) want = false;
                    if (want && set.Add(pair.Key)) changed = true;
                    if (!want && set.Remove(pair.Key)) changed = true;
                }
                if (!changed) continue;
                var ordered = set.OrderBy(s => s, StringComparer.Ordinal).ToArray();
                PlayerSettings.SetScriptingDefineSymbols(target, ordered);
                Debug.Log("[VMS] Scripting defines for " + target.TargetName + ": " + string.Join(";", ordered));
            }
        }

        static bool TypeExists(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (asm.GetType(fullName, false) != null) return true;
                }
                catch (Exception)
                {
                    // Dynamic or unloadable assemblies – ignore.
                }
            }
            // Plugins compiled into Assembly-CSharp disappear while that assembly has errors; fall back to scripts.
            string shortName = fullName.Substring(fullName.LastIndexOf('.') + 1);
            foreach (var guid in AssetDatabase.FindAssets(shortName + " t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == shortName && !path.Contains("/VietMythicalSpeed/")) return true;
            }
            return false;
        }
    }
}
