# Performance Notes

Target: a steady **60 fps on mid-range Android** (and 90/120 fps where the panel supports it), with no hitches when chunks stream in, and sustained performance when the phone gets hot.

## Rendering budget (per race frame)

| What | Draw calls | Notes |
|---|---|---|
| Track chunks (≈5 visible) | 2 each | Road (opaque) + lane lines/rails (additive) in one mesh with 2 submeshes |
| Scenery per chunk | 2 each | All props of a chunk merged: opaque silhouettes + additive glow |
| Pearls, ghost fires, sparks, engine glow, spirit dragon | **1** | `FxBatch`: one dynamic billboard mesh rebuilt per frame |
| Spectrum towers | **1** | One dynamic mesh, 2 crossed quads per bar |
| Sky dome, moon, vessel, trails | ~6 | |
| **Total** | **≈30** | No real-time lights, no shadows, no transparency sorting issues |

- **Shaders:** unlit, vertex-coloured, with fog computed per vertex (`Resources/Shaders`); no texture sampling on opaque geometry. All glow is additive with a 256² procedural atlas.
- **The beat pulse costs nothing per object:** the shaders read a global `_VmsPulse`, and vertex alpha says how strongly each element reacts.
- **Gamma colour space, no HDR, no depth/opaque textures** on the URP asset, SRP Batcher on.
- **Bloom only on Medium/High:** URP Bloom with quarter-resolution downscale on Medium and high-quality filtering off. In the Built-in pipeline, a 3–4 level dual-filter bloom at half resolution.
- **MSAA:** off on Ultra Low and Low, ×2 on Medium, ×4 on High (cheap on tile-based mobile GPUs).
- **Render scale:** 0.6 / 0.75 / 0.9 / 1.0 per tier (Ultra Low / Low / Medium / High), then adjusted live by `PerformanceGovernor`. The UI is drawn at full resolution on top, so text stays sharp.

## Lowest-cost options (Settings → Graphics)

For very old phones, long sessions or battery saving, three switches stack:

| Option | What changes | What stays |
|---|---|---|
| **Ultra Low** graphics | Render scale 0.6 (governor floor 0.5), no bloom or MSAA, 30% scenery, 8 spectrum bars a side, no spirit dragon, 48 sparks, shorter fog distance, fewer stars. Glow-only decorations (fireflies, candles, boat lights, cloud wisps, goblin fires, wisps, ice glints) and far landmarks (pagodas, castles) are skipped, realm particles drop to a third, and the shrine-gate tunnel thins to one gate every four beats. | Road, beat lines, pearls, ghost fires, vessel, exhaust, each realm's silhouettes, the aurora sky, a few music bars |
| **Flat UI** | Menus lose drop shadows, hatch patterns, decorative glows, clouds, rays and lantern backdrops; gradients become solid colours; nothing bobs or spins except the loading spinner. The 3D vessel preview renders at half resolution, about 15 times a second. | Layout, colours, the slanted shapes, cosmetics art |
| **Power-saving menus** | Menus at 30 fps (automatic on Low and Ultra Low). | Races use the chosen frame rate |

Low and Ultra Low also make the garage/shop vessel preview cheap, as Flat UI does.

## Sustained performance (thermal)

`PerformanceGovernor` reads the real CPU main/render-thread and GPU frame cost (`FrameTimingManager`; `enableFrameTimingStats` is switched on by the setup script) instead of the vsync-capped delta time.
- Over budget by 12% for 1.5 s → render scale −0.1.
- Under 70% of budget for 6 s → scale back +0.05, never below max − 0.35.

The result is resolution, not frame rate, dropping when the SoC throttles, which is what matters most in a rhythm game.

## CPU and memory

- **Streaming without spikes:** `TrackRenderer.UpdateVisible` builds at most one 2-second chunk per frame (the chunk under the player is always built). All start chunks are prewarmed during the loading screen.
- **No per-frame allocations in the race loop:** meshes are rebuilt from pooled `List<>`s (`SetVertices(List)`), particles live in a fixed struct array, HUD text is only reassigned when its value changes, and the FX mesh uses fixed bounds (no `RecalculateBounds`).
- **The HUD sits on its own canvas,** and score, combo and spirit are on a nested canvas so they don't rebuild the static parts. Menus are disabled (not just hidden) during races.
- **UI raycasts:** non-interactive graphics have `raycastTarget = false`. Navigator cards avoid stencil masks so grids still batch.
- **Audio:**
  - Imported songs are analysed once on a worker thread; the cached analysis is roughly 100–300 KB JSON.
  - Decoding yields every 10 ms so the loading UI stays smooth.
  - DSP buffer is limited to 512 samples on mobile (lower latency).
  - SFX are synthesised once at startup (≈20 short clips).
- **Menus run at ≤60 fps** (30 on Low, Ultra Low or with power-saving menus) to save battery; races use the player's target (30/60/90/120).
- **Build:** IL2CPP ARM64, "Optimize Speed" code generation, Release C++ configuration, incremental GC, managed stripping Medium (`link.xml` protects JSON types), Vulkan first with GLES3 fallback, and Android optimised frame pacing (Swappy).

## Measuring on a device

1. Build a development APK (**VietMythicalSpeed → Build → Android APK (development)**) and attach the Unity Profiler over USB (`adb forward` is automatic in the Profiler's target list).
2. Check `Gfx.WaitForPresent` (GPU-bound) against main-thread time. The most expensive settings to turn down are bloom, MSAA and render scale, in that order.
3. Android GPU Inspector or Snapdragon Profiler show fill-rate and overdraw. Additive glow quads are the main overdraw source; `QualityProfile` scales their counts and Ultra Low drops the glow-only ones.
4. Development builds accept `-vmsGraphics UltraLow -vmsFlat` to start in the cheapest mode ([Testing.md](Testing.md)).
