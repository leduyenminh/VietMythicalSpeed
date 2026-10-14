using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using VMS.Core;

namespace VMS.EditorTools
{
    /// <summary>
    /// Store builds. Raises the version, signs Android with the upload key taken from environment variables
    /// (Unity never writes keystore passwords to disk), and refuses to produce a build a store would reject.
    ///
    ///   Unity.exe -batchmode -quit -projectPath UnityClient -buildTarget Android
    ///     -executeMethod VMS.EditorTools.VmsRelease.BuildCli -vmsTarget android[,android-apk,windows]
    ///     [-vmsBump patch|minor|major|none] [-vmsVersion 1.2.3] [-vmsOutDir Builds/Release]
    ///
    /// Android signing: VMS_KEYSTORE_PATH, VMS_KEYSTORE_PASS, VMS_KEY_ALIAS, VMS_KEY_PASS.
    /// tools/build-release.ps1 wraps this.
    /// </summary>
    public static class VmsRelease
    {
        public const string KeystorePathVar = "VMS_KEYSTORE_PATH";
        public const string KeystorePassVar = "VMS_KEYSTORE_PASS";
        public const string KeyAliasVar = "VMS_KEY_ALIAS";
        public const string KeyPassVar = "VMS_KEY_PASS";

        const string BackendConfigPath = "Assets/VietMythicalSpeed/Resources/Config/backend.json";
        static readonly string[] Targets = { "android", "android-apk", "windows" };

        [Serializable]
        class BackendFile
        {
            public string mode = "offline";
            public string baseUrl = string.Empty;
        }

        public sealed class Options
        {
            public string[] targets = { "android" };
            public string bump = "patch";
            public string version;
            public string outDir = "Builds/Release";
        }

        [MenuItem("VietMythicalSpeed/Build/Release: Google Play bundle (bump version, sign from env)", priority = 30)]
        public static void BuildPlayBundleFromMenu() => Build(new Options());

        [MenuItem("VietMythicalSpeed/Build/Release: Windows (bump version)", priority = 31)]
        public static void BuildWindowsFromMenu() => Build(new Options { targets = new[] { "windows" } });

        public static void BuildCli()
        {
            var args = Environment.GetCommandLineArgs();
            bool ok = false;
            try
            {
                var options = new Options();
                string targets = VmsBuild.ArgValue(args, "-vmsTarget");
                if (!string.IsNullOrEmpty(targets)) options.targets = targets.Split(',').Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToArray();
                options.bump = VmsBuild.ArgValue(args, "-vmsBump") ?? options.bump;
                options.version = VmsBuild.ArgValue(args, "-vmsVersion");
                options.outDir = VmsBuild.ArgValue(args, "-vmsOutDir") ?? options.outDir;
                ok = Build(options);
            }
            catch (Exception e)
            {
                Debug.LogError("[VMS] Release failed: " + e.Message);
            }
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Returns true when every requested target built. On failure the version is left unchanged.</summary>
        public static bool Build(Options o)
        {
            var problems = Check(o);
            if (problems.Count > 0)
            {
                foreach (var p in problems) Debug.LogError("[VMS] Release blocked: " + p);
                return false;
            }

            string oldVersion = PlayerSettings.bundleVersion;
            int oldCode = PlayerSettings.Android.bundleVersionCode;
            string version = !string.IsNullOrEmpty(o.version) ? o.version.Trim() : AppVersion.Bump(oldVersion, o.bump);
            bool keepVersion = string.IsNullOrEmpty(o.version) && o.bump == "none";
            int code = keepVersion ? oldCode : oldCode + 1;

            bool allOk = false;
            var artifacts = new List<string>();
            try
            {
                VmsProjectSetup.RunAll();
                PlayerSettings.bundleVersion = version;
                PlayerSettings.Android.bundleVersionCode = code;
                EditorUserBuildSettings.development = false;
                AssetDatabase.SaveAssets();
                Debug.Log(string.Format("[VMS] Release version {0} (code {1}), was {2} (code {3})", version, code, oldVersion, oldCode));

                allOk = true;
                foreach (string target in o.targets)
                {
                    string artifact = BuildOne(target, version, code, o.outDir);
                    if (artifact == null)
                    {
                        allOk = false;
                        break;
                    }
                    artifacts.Add(artifact);
                }
            }
            finally
            {
                if (!allOk)
                {
                    PlayerSettings.bundleVersion = oldVersion;
                    PlayerSettings.Android.bundleVersionCode = oldCode;
                    Debug.LogError("[VMS] Release failed; version restored to " + oldVersion + " (code " + oldCode + ").");
                }
                AssetDatabase.SaveAssets();
            }
            foreach (var a in artifacts) Debug.Log("[VMS] Release artifact: " + Path.GetFullPath(a));
            return allOk;
        }

        /// <summary>Everything that would make a store reject the build, checked before anything changes.</summary>
        public static List<string> Check(Options o)
        {
            var problems = new List<string>();
            foreach (string t in o.targets)
                if (Array.IndexOf(Targets, t) < 0) problems.Add("unknown target \"" + t + "\" (use " + string.Join(", ", Targets) + ")");
            if (!string.IsNullOrEmpty(o.version))
            {
                if (!AppVersion.TryParse(o.version, out _, out _, out _)) problems.Add("-vmsVersion \"" + o.version + "\" is not a version like 1.2.3");
                else if (AppVersion.Compare(o.version, PlayerSettings.bundleVersion) <= 0)
                    problems.Add("-vmsVersion " + o.version + " must be newer than the current " + PlayerSettings.bundleVersion);
            }
            else
            {
                try
                {
                    AppVersion.Bump(PlayerSettings.bundleVersion, o.bump);
                }
                catch (ArgumentException e)
                {
                    problems.Add(e.Message);
                }
            }

            var backend = File.Exists(BackendConfigPath) ? JsonUtility.FromJson<BackendFile>(File.ReadAllText(BackendConfigPath)) : new BackendFile();
            if (backend.mode == "online" && !(backend.baseUrl ?? string.Empty).StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                problems.Add("Resources/Config/backend.json is online with \"" + backend.baseUrl + "\"; release builds need an https:// server URL");
            else if (backend.mode != "online")
                Debug.LogWarning("[VMS] backend.json is \"" + backend.mode + "\": this release plays fully offline (no server accounts).");

            if (o.targets.Any(t => t.StartsWith("android")))
            {
                var missing = new[] { KeystorePathVar, KeystorePassVar, KeyAliasVar, KeyPassVar }.Where(v => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v))).ToList();
                if (missing.Count > 0) problems.Add("set the Android upload key environment variables: " + string.Join(", ", missing));
                else if (!File.Exists(Environment.GetEnvironmentVariable(KeystorePathVar)))
                    problems.Add(KeystorePathVar + " points to a missing file: " + Environment.GetEnvironmentVariable(KeystorePathVar));
            }
            return problems;
        }

        static string BuildOne(string target, string version, int code, string outDir)
        {
            string name = "VietMythicalSpeed-" + version;
            BuildReport report;
            string output;
            if (target == "windows")
            {
                output = Path.Combine(outDir, name + "-windows", "VietMythicalSpeed.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { VmsProjectSetup.ScenePath },
                    locationPathName = output,
                    target = UnityEditor.BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                });
            }
            else
            {
                bool bundle = target == "android";
                output = Path.Combine(outDir, name + "-" + code + (bundle ? ".aab" : ".apk"));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                if (EditorUserBuildSettings.activeBuildTarget != UnityEditor.BuildTarget.Android)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, UnityEditor.BuildTarget.Android);
                EditorUserBuildSettings.buildAppBundle = bundle;

                // The upload key is set only for this build and put back right after it, before anything else
                // (e.g. a target switch) can save ProjectSettings, so no machine path ends up in the repository.
                // Unity never writes the passwords to disk.
                bool usedCustom = PlayerSettings.Android.useCustomKeystore;
                string oldKeystore = PlayerSettings.Android.keystoreName;
                string oldAlias = PlayerSettings.Android.keyaliasName;
                try
                {
                    PlayerSettings.Android.useCustomKeystore = true;
                    PlayerSettings.Android.keystoreName = Path.GetFullPath(Environment.GetEnvironmentVariable(KeystorePathVar));
                    PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable(KeystorePassVar);
                    PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable(KeyAliasVar);
                    PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable(KeyPassVar);
                    report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                    {
                        scenes = new[] { VmsProjectSetup.ScenePath },
                        locationPathName = output,
                        target = UnityEditor.BuildTarget.Android,
                        targetGroup = BuildTargetGroup.Android,
                        options = BuildOptions.None,
                    });
                }
                finally
                {
                    PlayerSettings.Android.useCustomKeystore = usedCustom;
                    PlayerSettings.Android.keystoreName = oldKeystore;
                    PlayerSettings.Android.keyaliasName = oldAlias;
                    PlayerSettings.Android.keystorePass = string.Empty;
                    PlayerSettings.Android.keyaliasPass = string.Empty;
                    AssetDatabase.SaveAssets();
                }
            }
            VmsBuild.Log(report);
            return report.summary.result == BuildResult.Succeeded ? output : null;
        }
    }
}
