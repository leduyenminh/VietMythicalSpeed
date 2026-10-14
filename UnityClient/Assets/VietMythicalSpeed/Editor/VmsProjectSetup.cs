using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
#if VMS_URP_PACKAGE
using UnityEngine.Rendering.Universal;
#endif

namespace VMS.EditorTools
{
    /// <summary>
    /// One-click (and first-open) project configuration: main scene, Android player settings tuned for
    /// performance, URP asset, scripting defines. Lives in its own editor assembly so it still runs when the
    /// game scripts have compile errors (e.g. a missing optional plugin).
    /// </summary>
    [InitializeOnLoad]
    public static class VmsProjectSetup
    {
        public const string ScenePath = "Assets/VietMythicalSpeed/Scenes/Main.unity";
        public const string SettingsFolder = "Assets/VietMythicalSpeed/Settings";
        public const string PackageId = "com.vietmythical.speed";
        const string DoneKeyPrefix = "VMS.Setup.v2:";

        static VmsProjectSetup()
        {
            EditorApplication.delayCall += AutoSetup;
        }

        static string DoneKey => DoneKeyPrefix + Application.dataPath;

        static void AutoSetup()
        {
            if (Application.isBatchMode) return;
            if (!EditorPrefs.GetBool(DoneKey))
            {
                RunAll();
                EditorPrefs.SetBool(DoneKey, true);
                if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
                Debug.Log("[VMS] Project configured for Android. Press Play to run the game (VietMythicalSpeed menu has build commands).");
            }
            SetPlayModeStartScene();
        }

        [MenuItem("VietMythicalSpeed/Setup/Run Full Project Setup", priority = 1)]
        public static void RunAll()
        {
            EnsureScene();
            ApplyPlayerSettings();
            SetupRenderPipeline();
            VmsDefineSync.Sync();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Batch-mode entry: Unity.exe -batchmode -quit -projectPath … -executeMethod VMS.EditorTools.VmsProjectSetup.RunAllBatch</summary>
        public static void RunAllBatch()
        {
            RunAll();
            Debug.Log("[VMS] Batch setup complete.");
        }

        public static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length != 1 || scenes[0].path != ScenePath)
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            SetPlayModeStartScene();
        }

        /// <summary>Pressing Play from any open scene starts the game from Main.</summary>
        static void SetPlayModeStartScene()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (scene != null) EditorSceneManager.playModeStartScene = scene;
        }

        [MenuItem("VietMythicalSpeed/Setup/Apply Android Player Settings", priority = 2)]
        public static void ApplyPlayerSettings()
        {
            var android = NamedBuildTarget.Android;
            // Unity derives a default id from company/product names; only replace ids that were never chosen.
            string currentId = PlayerSettings.GetApplicationIdentifier(android);
            bool defaultId = string.IsNullOrEmpty(currentId) || currentId.StartsWith("com.DefaultCompany") || currentId.StartsWith("com.VietMythicalStudio.");
            PlayerSettings.companyName = "VietMythical Studio";
            PlayerSettings.productName = "Viet Mythical Speed";
            if (defaultId) PlayerSettings.SetApplicationIdentifier(android, PackageId);
            VmsAppIcon.EnsureAssigned();
            if (string.IsNullOrEmpty(PlayerSettings.bundleVersion) || PlayerSettings.bundleVersion == "1.0")
                PlayerSettings.bundleVersion = "0.1.0";

            // Landscape racer, auto-rotating between the two landscape orientations.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // Performance: IL2CPP ARM64 release code, incremental GC, Vulkan first, frame pacing, gamma colour.
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetIl2CppCompilerConfiguration(android, Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetIl2CppCodeGeneration(android, Il2CppCodeGeneration.OptimizeSpeed);
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Medium);
            PlayerSettings.gcIncremental = true;
            PlayerSettings.Android.optimizedFramePacing = true;
            // Lets VMS.Rendering.PerformanceGovernor read real CPU/GPU frame cost for dynamic resolution.
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.forceInternetPermission = true;
            // Release builds only talk HTTPS; development builds may reach a local test server over plain HTTP.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.Android.renderOutsideSafeArea = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.runInBackground = false;

            // Desktop/editor builds (Steam): windowed 16:9 by default.
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;

            QualitySettings.vSyncCount = 0;
            QualitySettings.shadows = UnityEngine.ShadowQuality.Disable;
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.skinWeights = SkinWeights.OneBone;
            Debug.Log("[VMS] Android player settings applied (IL2CPP/ARM64, Vulkan+GLES3, API 26+, landscape).");
        }

        [MenuItem("VietMythicalSpeed/Setup/Create or Assign URP Asset", priority = 3)]
        public static void SetupRenderPipeline()
        {
#if VMS_URP_PACKAGE
            string assetPath = SettingsFolder + "/VMS_URP.asset";
            string rendererPath = SettingsFolder + "/VMS_URP_Renderer.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (asset == null)
            {
                Directory.CreateDirectory(SettingsFolder);
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            // Mobile-lean defaults: no HDR, no shadows, no depth/opaque copies; MSAA and render scale are set
            // per graphics tier at runtime (VMS.Rendering.PostFx).
            asset.supportsHDR = false;
            asset.msaaSampleCount = 2;
            asset.renderScale = 1f;
            asset.shadowDistance = 0f;
            asset.supportsCameraDepthTexture = false;
            asset.supportsCameraOpaqueTexture = false;
            var so = new SerializedObject(asset);
            SetIfPresent(so, "m_MainLightRenderingMode", 0);
            SetIfPresent(so, "m_AdditionalLightsRenderingMode", 0);
            SetIfPresent(so, "m_MainLightShadowsSupported", false);
            SetIfPresent(so, "m_AdditionalLightShadowsSupported", false);
            SetIfPresent(so, "m_UseSRPBatcher", true);
            SetIfPresent(so, "m_SupportsDynamicBatching", false);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
            Debug.Log("[VMS] URP asset assigned: " + assetPath);
#else
            Debug.Log("[VMS] URP package not installed – using the Built-in Render Pipeline (fully supported by the game's shaders).");
#endif
        }

        static void SetIfPresent(SerializedObject so, string property, int value)
        {
            var p = so.FindProperty(property);
            if (p != null) p.intValue = value;
        }

        static void SetIfPresent(SerializedObject so, string property, bool value)
        {
            var p = so.FindProperty(property);
            if (p != null) p.boolValue = value;
        }

        [MenuItem("VietMythicalSpeed/Open Persistent Data Folder", priority = 50)]
        static void OpenPersistentData() => EditorUtility.RevealInFinder(Application.persistentDataPath);

        [MenuItem("VietMythicalSpeed/Reset Local Save (offline mode)", priority = 51)]
        static void ResetLocalSave()
        {
            if (!EditorUtility.DisplayDialog("Reset local save", "Delete offline accounts, profiles and the imported song library on this machine?", "Delete", "Cancel")) return;
            foreach (var dir in new[] { "offline", "songs" })
            {
                string path = Path.Combine(Application.persistentDataPath, dir);
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            PlayerPrefs.DeleteAll();
            Debug.Log("[VMS] Local save cleared.");
        }
    }
}
