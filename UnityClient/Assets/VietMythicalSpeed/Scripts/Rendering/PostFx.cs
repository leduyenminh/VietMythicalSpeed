using UnityEngine;
using UnityEngine.Rendering;
#if VMS_URP
using UnityEngine.Rendering.Universal;
#endif

namespace VMS.Rendering
{
    /// <summary>Applies the quality profile (resolution scale, MSAA, bloom) for whichever render pipeline is active.</summary>
    public static class PostFx
    {
        public static void Setup(Camera camera, QualityProfile quality)
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                QualitySettings.antiAliasing = quality.msaa > 1 ? quality.msaa : 0;
                if (quality.bloom) camera.gameObject.AddComponent<GlowBloom>().iterations = quality.renderScale < 0.95f ? 3 : 4;
                return;
            }
#if VMS_URP
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            // Only touch the asset in players: in the editor it is a project file and the change would persist.
            if (asset != null && !Application.isEditor)
            {
                asset.renderScale = quality.renderScale;
                asset.msaaSampleCount = quality.msaa;
            }
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = quality.bloom;
            cameraData.antialiasing = AntialiasingMode.None;
            cameraData.renderShadows = false;
            cameraData.requiresDepthOption = CameraOverrideOption.Off;
            cameraData.requiresColorOption = CameraOverrideOption.Off;
            if (!quality.bloom) return;

            var volumeObject = new GameObject("Race PostFx");
            volumeObject.transform.SetParent(camera.transform, false);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.72f);
            bloom.intensity.Override(1.1f);
            bloom.scatter.Override(0.68f);
            bloom.highQualityFiltering.Override(false);
            bloom.downscale.Override(quality.renderScale < 0.95f ? BloomDownscaleMode.Quarter : BloomDownscaleMode.Half);
            bloom.maxIterations.Override(5);
            volume.sharedProfile = profile;
#else
            Debug.Log("[PostFx] A scriptable render pipeline is active but VMS_URP is not defined; bloom skipped.");
#endif
        }
    }

    /// <summary>Built-in pipeline bloom (threshold → mip chain → additive upsample → composite).</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class GlowBloom : MonoBehaviour
    {
        const int PrefilterPass = 0, DownPass = 1, UpPass = 2, CompositePass = 3;
        static readonly int FilterId = Shader.PropertyToID("_Filter");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int SourceId = Shader.PropertyToID("_SourceTex");

        public float threshold = 0.75f;
        [Range(0, 1)] public float softKnee = 0.5f;
        public float intensity = 0.9f;
        [Range(1, 8)] public int iterations = 4;

        Material _material;
        readonly RenderTexture[] _levels = new RenderTexture[8];

        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (_material == null)
            {
                var shader = Shader.Find("Hidden/VMS/Bloom");
                if (shader == null || !shader.isSupported)
                {
                    Graphics.Blit(source, destination);
                    enabled = false;
                    return;
                }
                _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            float knee = threshold * softKnee;
            _material.SetVector(FilterId, new Vector4(threshold, threshold - knee, 2f * knee, 0.25f / (knee + 0.00001f)));
            _material.SetFloat(IntensityId, intensity);

            int width = source.width / 2, height = source.height / 2;
            var format = source.format;
            var current = _levels[0] = RenderTexture.GetTemporary(width, height, 0, format);
            Graphics.Blit(source, current, _material, PrefilterPass);
            var currentSource = current;

            int i = 1;
            for (; i < iterations; i++)
            {
                width /= 2;
                height /= 2;
                if (height < 2) break;
                current = _levels[i] = RenderTexture.GetTemporary(width, height, 0, format);
                Graphics.Blit(currentSource, current, _material, DownPass);
                currentSource = current;
            }
            for (i -= 2; i >= 0; i--)
            {
                current = _levels[i];
                _levels[i] = null;
                Graphics.Blit(currentSource, current, _material, UpPass);
                RenderTexture.ReleaseTemporary(currentSource);
                currentSource = current;
            }
            _material.SetTexture(SourceId, source);
            Graphics.Blit(currentSource, destination, _material, CompositePass);
            RenderTexture.ReleaseTemporary(currentSource);
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
