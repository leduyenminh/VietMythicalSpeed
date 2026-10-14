using System;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VMS.EditorTools
{
    /// <summary>
    /// Build commands. From the command line:
    ///   Unity.exe -batchmode -quit -projectPath UnityClient -executeMethod VMS.EditorTools.VmsBuild.BuildAndroidCli -vmsOut Builds/vms.apk [-vmsRelease] [-vmsAab]
    /// </summary>
    public static class VmsBuild
    {
        [MenuItem("VietMythicalSpeed/Build/Android APK (development)", priority = 20)]
        public static void BuildDevApk() => BuildAndroid("Builds/Android/VietMythicalSpeed-dev.apk", false, false);

        [MenuItem("VietMythicalSpeed/Build/Android APK (release)", priority = 21)]
        public static void BuildReleaseApk() => BuildAndroid("Builds/Android/VietMythicalSpeed.apk", true, false);

        [MenuItem("VietMythicalSpeed/Build/Android App Bundle (Google Play)", priority = 22)]
        public static void BuildAab() => BuildAndroid("Builds/Android/VietMythicalSpeed.aab", true, true);

        [MenuItem("VietMythicalSpeed/Build/Windows (Steam)", priority = 23)]
        public static void BuildWindows() => BuildWindowsPlayer("Builds/Windows/VietMythicalSpeed.exe", false);

        /// <summary>-executeMethod VMS.EditorTools.VmsBuild.BuildWindowsCli -vmsOut Builds/Windows/vms.exe [-vmsRelease]</summary>
        public static void BuildWindowsCli()
        {
            var args = Environment.GetCommandLineArgs();
            VmsProjectSetup.EnsureScene();
            var report = BuildWindowsPlayer(ArgValue(args, "-vmsOut") ?? "Builds/Windows/VietMythicalSpeed.exe", !args.Contains("-vmsRelease"));
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        static BuildReport BuildWindowsPlayer(string output, bool development)
        {
            VmsProjectSetup.EnsureScene();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { VmsProjectSetup.ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = development ? BuildOptions.Development : BuildOptions.None,
            });
            Log(report);
            return report;
        }

        public static void BuildAndroidCli()
        {
            var args = Environment.GetCommandLineArgs();
            string output = ArgValue(args, "-vmsOut") ?? "Builds/Android/VietMythicalSpeed-dev.apk";
            bool release = args.Contains("-vmsRelease");
            bool aab = args.Contains("-vmsAab");
            VmsProjectSetup.RunAll();
            var report = BuildAndroid(output, release, aab);
            EditorApplication.Exit(report != null && report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        internal static string ArgValue(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static BuildReport BuildAndroid(string output, bool release, bool aab)
        {
            VmsProjectSetup.EnsureScene();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.buildAppBundle = aab;
            if (release && string.IsNullOrEmpty(PlayerSettings.Android.keystoreName))
                Debug.LogWarning("[VMS] Release build without a keystore: it will be signed with the debug key. Set Player Settings > Publishing Settings before uploading to Google Play.");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            if (!release) CleanIncrementalApk();
            var options = release ? BuildOptions.None : BuildOptions.Development;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { VmsProjectSetup.ScenePath },
                locationPathName = output,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = options,
            });
            Log(report);
            return report;
        }

        /// <summary>
        /// Debuggable builds are packaged incrementally by Gradle: it patches the previous APK in place
        /// and leaves the replaced entries as dead space (a 43 MB APK grew to 62 MB). Removing the old
        /// package forces a compact one; the compiled code stays cached, so this costs a few seconds.
        /// </summary>
        static void CleanIncrementalApk()
        {
            string prj = Path.Combine("Library", "Bee", "Android", "Prj");
            if (!Directory.Exists(prj)) return;
            foreach (string backend in Directory.GetDirectories(prj)) // IL2CPP, Mono2x
                foreach (string rel in new[] { "launcher/build/outputs/apk", "launcher/build/intermediates/incremental/packageDebug" })
                {
                    string dir = Path.Combine(backend, "Gradle", rel);
                    try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
                    catch (IOException e) { Debug.LogWarning("[VMS] Could not clean " + dir + ": " + e.Message); }
                }
        }

        internal static void Log(BuildReport report)
        {
            var s = report.summary;
            // summary.totalSize is the uncompressed content size; report the real file size when there is one.
            long bytes = File.Exists(s.outputPath) ? new FileInfo(s.outputPath).Length : (long)s.totalSize;
            Debug.Log(string.Format("[VMS] Build {0}: {1} ({2:N1} MB, {3:mm\\:ss})", s.result, s.outputPath, bytes / 1048576.0, s.totalTime));
        }
    }

    /// <summary>
    /// Adds what the generated Android manifest needs, whatever the application entry point
    /// (Activity or GameActivity): the custom-scheme deep link used by the Steam browser sign-in,
    /// the vibrate permission for haptics, and – only without the NativeFilePicker plugin – audio read
    /// permission for the in-game file browser.
    /// </summary>
    public sealed class VmsAndroidManifestPatcher : IPostGenerateGradleAndroidProject
    {
        const string AndroidNs = "http://schemas.android.com/apk/res/android";
        public const string Scheme = "vietmythicalspeed";

        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            string manifestPath = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning("[VMS] Manifest not found at " + manifestPath);
                return;
            }
            var doc = new XmlDocument();
            doc.Load(manifestPath);
            var manifest = doc.DocumentElement;

            AddPermission(doc, manifest, "android.permission.VIBRATE", null);
#if !VMS_NATIVE_FILE_PICKER
            AddPermission(doc, manifest, "android.permission.READ_MEDIA_AUDIO", null);
            AddPermission(doc, manifest, "android.permission.READ_EXTERNAL_STORAGE", "32");
#endif

            XmlElement launcher = null;
            foreach (XmlElement activity in doc.GetElementsByTagName("activity"))
                foreach (XmlElement filter in activity.GetElementsByTagName("intent-filter"))
                    foreach (XmlElement category in filter.GetElementsByTagName("category"))
                        if (category.GetAttribute("name", AndroidNs) == "android.intent.category.LAUNCHER") launcher = activity;
            if (launcher == null)
            {
                Debug.LogWarning("[VMS] No launcher activity found; deep link not registered.");
            }
            else if (!launcher.OuterXml.Contains("android:scheme=\"" + Scheme + "\""))
            {
                var filter = doc.CreateElement("intent-filter");
                filter.AppendChild(Element(doc, "action", "name", "android.intent.action.VIEW"));
                filter.AppendChild(Element(doc, "category", "name", "android.intent.category.DEFAULT"));
                filter.AppendChild(Element(doc, "category", "name", "android.intent.category.BROWSABLE"));
                var data = Element(doc, "data", "scheme", Scheme);
                data.SetAttribute("host", AndroidNs, "auth");
                filter.AppendChild(data);
                launcher.AppendChild(filter);
                launcher.SetAttribute("launchMode", AndroidNs, "singleTask");
            }
            doc.Save(manifestPath);
        }

        static XmlElement Element(XmlDocument doc, string tag, string attribute, string value)
        {
            var e = doc.CreateElement(tag);
            e.SetAttribute(attribute, AndroidNs, value);
            return e;
        }

        static void AddPermission(XmlDocument doc, XmlElement manifest, string permission, string maxSdk)
        {
            foreach (XmlElement p in doc.GetElementsByTagName("uses-permission"))
                if (p.GetAttribute("name", AndroidNs) == permission) return;
            var e = Element(doc, "uses-permission", "name", permission);
            if (maxSdk != null) e.SetAttribute("maxSdkVersion", AndroidNs, maxSdk);
            manifest.PrependChild(e);
        }
    }
}
