using UnityEngine;
using UnityEngine.Rendering;
#if VMS_URP
using UnityEngine.Rendering.Universal;
#endif

namespace VMS.Rendering
{
    /// <summary>
    /// Keeps races at the target frame rate when a phone heats up and throttles: measures the real CPU/GPU cost
    /// of each frame (FrameTimingManager, not the vsync-capped delta) and trades render resolution for frame time.
    /// Steps down quickly when over budget, creeps back up slowly when there is headroom.
    /// </summary>
    public sealed class PerformanceGovernor : MonoBehaviour
    {
        const float StepDown = 0.1f, StepUp = 0.05f;
        const float OverBudget = 1.12f, UnderBudget = 0.7f;
        const float DownAfter = 1.5f, UpAfter = 6f;

        readonly FrameTiming[] _timings = new FrameTiming[1];
        float _budgetMs;
        float _workMs;
        float _over, _under;
        float _scale, _min, _max;
        bool _timingSupported = true;

        public float Scale => _scale;

        public void Init(int targetFps, float maxScale)
        {
            _budgetMs = 1000f / Mathf.Max(30, targetFps);
            _max = Mathf.Clamp(maxScale, 0.5f, 1f);
            _min = Mathf.Max(0.5f, _max - 0.35f);
            _workMs = _budgetMs * 0.8f;
            Apply(_max); // a previous throttled race must not carry its reduced resolution over
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f || dt > 0.25f) return; // pauses, loading hitches

            float work = dt * 1000f;
            if (_timingSupported)
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
                {
                    var t = _timings[0];
                    double measured = System.Math.Max(t.gpuFrameTime, System.Math.Max(t.cpuMainThreadFrameTime, t.cpuRenderThreadFrameTime));
                    if (measured > 0.0) work = (float)measured;
                }
                else if (Time.frameCount > 120)
                {
                    _timingSupported = false; // fall back to frame delta (can only detect slowdowns, not headroom)
                }
            }
            _workMs = Mathf.Lerp(_workMs, work, 0.06f);

            if (_workMs > _budgetMs * OverBudget)
            {
                _over += dt;
                _under = 0f;
            }
            else if (_timingSupported && _workMs < _budgetMs * UnderBudget)
            {
                _under += dt;
                _over = 0f;
            }
            else
            {
                _over = _under = 0f;
            }

            if (_over > DownAfter && _scale > _min + 0.001f)
            {
                Apply(_scale - StepDown);
                _over = 0f;
            }
            else if (_under > UpAfter && _scale < _max - 0.001f)
            {
                Apply(_scale + StepUp);
                _under = 0f;
            }
        }

        void Apply(float scale)
        {
            _scale = Mathf.Clamp(scale, _min, _max);
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                // Built-in pipeline: dynamic resolution (Vulkan / Metal / D3D12); cameras opt in via allowDynamicResolution.
                ScalableBufferManager.ResizeBuffers(_scale, _scale);
                return;
            }
#if VMS_URP
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset != null && !Application.isEditor) asset.renderScale = _scale;
#endif
        }
    }
}
