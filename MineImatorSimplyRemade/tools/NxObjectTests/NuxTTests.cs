using System.Numerics;
using System.Reflection;
using System.Text.Json;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.graphics;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using GlmSharp;

internal static partial class Program
{
    private static void RunNuxTTests()
    {
        NuxTSettings normalized = new NuxTSettings(0, 40, float.NaN, float.PositiveInfinity, 7, true).Normalized();
        NuxTAssert(normalized.Samples == 1 && normalized.MaxBounces == 16 &&
                   normalized.ResolutionScale == 0.5f && normalized.Exposure == 1f && normalized.Seed == 7 &&
                   normalized.DenoiserEnabled,
            "NuxT settings must clamp invalid and out-of-range values.");

        var animatedTexture = new NuxTTexture([255, 0, 0, 255, 0, 0, 255, 255], 1, 2);
        var firstAnimatedFrame = new NuxTMaterial(Vector3.One, Vector3.Zero, AlbedoTexture: animatedTexture,
            TextureFrameScale: 0.5f);
        var secondAnimatedFrame = firstAnimatedFrame with { TextureFrameOffset = 0.5f };
        NuxTAssert(firstAnimatedFrame.SampleAlbedo(new Vector2(0.5f)).X > 0.99f &&
                   firstAnimatedFrame.SampleAlbedo(new Vector2(0.5f)).Z < 0.01f &&
                   secondAnimatedFrame.SampleAlbedo(new Vector2(0.5f)).Z > 0.99f &&
                   secondAnimatedFrame.SampleAlbedo(new Vector2(0.5f)).X < 0.01f,
            "Animated texture UV transforms must sample the selected spritesheet frame.");
        var colorAdjustedMaterial = new NuxTMaterial(new Vector3(0.2f, 0.4f, 0.6f), Vector3.Zero,
            AddRgb: new Vector3(0.1f, 0f, 0f), HasColorAdjustments: true, MultiplyHsv: Vector3.One);
        Vector4 adjustedColor = colorAdjustedMaterial.SampleAlbedo(Vector2.Zero);
        NuxTAssert(Vector3.Distance(new Vector3(adjustedColor.X, adjustedColor.Y, adjustedColor.Z),
                new Vector3(0.3f, 0.4f, 0.6f)) < 1e-5f,
            "NuxT material color adjustments must apply rasterizer RGB operations.");

        var unlitScene = new NuxTScene(
            [NuxTTriangleAtZ(0f, new NuxTMaterial(new Vector3(0.2f, 0.4f, 0.6f), Vector3.Zero, Unlit: true))],
            Vector3.Zero, [new NuxTLight(new Vector3(0f, 0f, 1f), Vector3.UnitZ, new Vector3(100f), 100f, 10f)],
            ambientLight: new Vector3(50f));
        Vector3 unlitColor = unlitScene.TraceRay(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, 4);
        NuxTAssert(Vector3.Distance(unlitColor, new Vector3(0.2f, 0.4f, 0.6f)) < 1e-5f,
            "Unlit surfaces must preserve their base color without direct or ambient shading.");

        NuxTTriangle[] layeredTriangles = Enumerable.Range(0, 96)
            .Select(index => NuxTTriangleAtZ(index, new NuxTMaterial(Vector3.One, new Vector3(index + 1f))))
            .ToArray();
        var bvhScene = new NuxTScene(layeredTriangles, Vector3.Zero);
        Vector3 nearestEmission = bvhScene.TraceRay(new Vector3(0f, 0f, 100f), -Vector3.UnitZ, 0);
        NuxTAssert(bvhScene.TriangleCount == 96 && Vector3.Distance(nearestEmission, new Vector3(96f)) < 1e-5f,
            "Binned-SAH BLAS construction must retain nearest-hit traversal for larger scenes.");

        NuxTTriangle[] sharedGeometry =
        [
            NuxTTriangleAtZ(0f, new NuxTMaterial(Vector3.Zero, Vector3.Zero))
        ];
        object sharedGeometryKey = new();
        var repeatedMeshScene = NuxTScene.CreateInstances(
        [
            new NuxTSceneInstance(sharedGeometry,
                Matrix4x4.CreateScale(1.5f, 0.5f, 1f) * Matrix4x4.CreateRotationZ(0.3f) *
                Matrix4x4.CreateTranslation(-3f, 0f, 0f), sharedGeometryKey,
                new NuxTMaterial(Vector3.Zero, new Vector3(2f, 0f, 0f))),
            new NuxTSceneInstance(sharedGeometry,
                Matrix4x4.CreateScale(0.75f, 1.25f, 1f) * Matrix4x4.CreateRotationZ(-0.2f) *
                Matrix4x4.CreateTranslation(3f, 0f, 0f), sharedGeometryKey,
                new NuxTMaterial(Vector3.Zero, new Vector3(0f, 0f, 3f)))
        ], Vector3.Zero);
        Vector3 leftInstance = repeatedMeshScene.TraceRay(new Vector3(-3f, 0f, 1f), -Vector3.UnitZ, 0);
        Vector3 rightInstance = repeatedMeshScene.TraceRay(new Vector3(3f, 0f, 1f), -Vector3.UnitZ, 0);
        NuxTAssert(repeatedMeshScene.TriangleCount == 2 &&
                   Vector3.Distance(leftInstance, new Vector3(2f, 0f, 0f)) < 1e-5f &&
                   Vector3.Distance(rightInstance, new Vector3(0f, 0f, 3f)) < 1e-5f,
            "Transformed instances must share local geometry while retaining their own transforms and materials.");

        var relightSource = new NuxTScene([NuxTTriangleAtZ(0f, new NuxTMaterial(Vector3.One, Vector3.Zero))],
            Vector3.Zero);
        NuxTScene relitScene = relightSource.WithLighting(
            [new NuxTLight(new Vector3(0f, 0f, 1f), Vector3.UnitZ, Vector3.One, 4f, 10f)], [], Vector3.Zero,
            Vector3.Zero);
        PropertyInfo geometryIdentity = typeof(NuxTScene).GetProperty("GpuGeometryIdentity",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Vector3 unlitReference = relightSource.TraceRay(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, 0);
        Vector3 relitReference = relitScene.TraceRay(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, 0);
        NuxTAssert(ReferenceEquals(geometryIdentity.GetValue(relightSource), geometryIdentity.GetValue(relitScene)) &&
                   relitScene.Fingerprint != relightSource.Fingerprint &&
                   Vector3.Distance(unlitReference, relitReference) > 1e-4f,
            "Relighting must change traced lighting and scene identity without replacing shared geometry acceleration.");

        var settingsManifest = new ProjectManifest();
        settingsManifest.Settings.NuxTSamples = 128;
        settingsManifest.Settings.NuxTMaxBounces = 7;
        settingsManifest.Settings.NuxTResolutionScale = 0.75f;
        settingsManifest.Settings.NuxTExposure = 2.25f;
        settingsManifest.Settings.NuxTSeed = 42;
        settingsManifest.Settings.NuxTDenoiserEnabled = true;
        string settingsJson = JsonSerializer.Serialize(settingsManifest, AppJsonContext.Default.ProjectManifest);
        ProjectManifest restoredSettings = JsonSerializer.Deserialize(settingsJson, AppJsonContext.Default.ProjectManifest)!;
        var settingsPanel = new PropertiesPanel();
        settingsPanel.LoadProjectSettingsFromManifest(restoredSettings);
        NuxTAssert(settingsPanel.NuxTSamples == 128 && settingsPanel.NuxTMaxBounces == 7 &&
                   settingsPanel.NuxTResolutionScale == 0.75f && settingsPanel.NuxTExposure == 2.25f &&
                   settingsPanel.NuxTSeed == 42 && settingsPanel.NuxTDenoiserEnabled,
            "NuxT render settings must survive project serialization and load into the render settings panel.");
        var savedSettings = new ProjectManifest();
        settingsPanel.WriteProjectSettingsToManifest(savedSettings);
        NuxTAssert(savedSettings.Settings.NuxTDenoiserEnabled,
            "The NuxT denoiser control must write its value back to project settings.");
        NuxTAssert(!new ProjectManifest().Settings.NuxTDenoiserEnabled,
            "Projects must leave NuxT denoising disabled unless the user enables it.");

        const int denoiserTestWidth = 32;
        const int denoiserTestHeight = 32;
        var noisyLinearRgb = new float[denoiserTestWidth * denoiserTestHeight * 3];
        var noiseSource = new Random(17);
        for (int i = 0; i < noisyLinearRgb.Length; i++)
            noisyLinearRgb[i] = 0.6f + ((float)noiseSource.NextDouble() - 0.5f) * 0.5f;
        float[] originalNoisyLinearRgb = noisyLinearRgb.ToArray();
        NuxTAssert(NuxTDenoiser.TryDenoiseLinearRgb(noisyLinearRgb, denoiserTestWidth, denoiserTestHeight,
                out float[] denoisedLinearRgb, out string? denoiserError),
            $"OIDN must execute its CPU RT filter: {denoiserError}");
        NuxTAssert(noisyLinearRgb.SequenceEqual(originalNoisyLinearRgb) &&
                   denoisedLinearRgb.Where((value, index) => MathF.Abs(value - noisyLinearRgb[index]) > 1e-5f).Any(),
            "OIDN must produce a separate filtered HDR color image without modifying its input.");

        var empty = new NuxTScene([], new Vector3(0.2f, 0.4f, 0.6f));
        Vector3 background = empty.TraceRay(Vector3.Zero, -Vector3.UnitZ, 0);
        NuxTAssert(Vector3.Distance(background, new Vector3(0.2f, 0.4f, 0.6f)) < 1e-5f,
            "An empty scene must return its environment color.");

        var backgroundCamera = new NuxTCamera(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY, 0.2f);
        byte[] transparentBackground = empty.RenderSample(backgroundCamera, 1, 1, 0, 7,
            new NuxTSettings(1, 0), transparentBackground: true);
        NuxTAssert(transparentBackground[3] == 0,
            "A primary ray that misses the scene must leave the raster sky visible when composited.");
        var coveringScene = new NuxTScene([NuxTTriangleAtZ(-1f,
            new NuxTMaterial(new Vector3(0.3f, 0.5f, 0.7f), Vector3.Zero, Unlit: true))], Vector3.Zero);
        byte[] opaqueSurface = coveringScene.RenderSample(backgroundCamera, 1, 1, 0, 7,
            new NuxTSettings(1, 0), transparentBackground: true);
        NuxTAssert(opaqueSurface[3] == 255,
            "A primary ray that hits an opaque surface must produce a fully covered NuxT pixel.");

        NuxTTriangle emitter = NuxTTriangleAtZ(0f, new NuxTMaterial(Vector3.One, new Vector3(4f, 2f, 1f)));
        var emissiveScene = new NuxTScene([emitter], Vector3.Zero);
        Vector3 emission = emissiveScene.TraceRay(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, 0);
        NuxTAssert(Vector3.Distance(emission, new Vector3(4f, 2f, 1f)) < 1e-5f,
            "Visible emissive triangles must contribute their emission.");

        NuxTTriangle blocker = NuxTTriangleAtZ(0.5f, new NuxTMaterial(Vector3.One, Vector3.Zero));
        var occludedEmitter = new NuxTScene([blocker, emitter], Vector3.Zero);
        Vector3 blocked = occludedEmitter.TraceRay(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, 0);
        NuxTAssert(blocked.LengthSquared() == 0f, "The nearest opaque triangle must occlude geometry behind it.");

        var clearCutout = new NuxTTexture([255, 255, 255, 0], 1, 1);
        NuxTTriangle cutout = NuxTTriangleAtZ(0.5f,
            new NuxTMaterial(Vector3.One, Vector3.Zero, AlphaClip: true, AlbedoTexture: clearCutout));
        var cutoutScene = new NuxTScene([cutout, emitter], Vector3.Zero);
        Vector3 throughCutout = cutoutScene.TraceRay(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, 0);
        NuxTAssert(Vector3.Distance(throughCutout, new Vector3(4f, 2f, 1f)) < 1e-5f,
            "Clipped transparent texture pixels must continue the ray to geometry behind them.");
        var clearAlphaMask = new NuxTTexture([255, 255, 255, 0], 1, 1);
        NuxTTriangle maskedCutout = NuxTTriangleAtZ(0.5f,
            new NuxTMaterial(Vector3.One, Vector3.Zero, AlphaClip: true, AlphaTexture: clearAlphaMask));
        var alphaMaskScene = new NuxTScene([maskedCutout, emitter], Vector3.Zero);
        Vector3 throughAlphaMask = alphaMaskScene.TraceRay(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, 0);
        NuxTAssert(Vector3.Distance(throughAlphaMask, new Vector3(4f, 2f, 1f)) < 1e-5f,
            "Separate alpha-mask texture pixels must clip the surface correctly.");

        var receiver = NuxTTriangleAtZ(0f, new NuxTMaterial(Vector3.One, Vector3.Zero));
        var pointLight = new NuxTLight(new Vector3(0.8f, 0f, 1f), Vector3.UnitZ, Vector3.One, 10f, 5f);
        var litScene = new NuxTScene([receiver], Vector3.Zero, [pointLight]);
        Vector3 directLight = litScene.TraceRay(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, 0);
        NuxTAssert(directLight.X > 0.1f, "Point lights must contribute direct diffuse illumination.");
        var directionalScene = new NuxTScene([receiver], Vector3.Zero, directionalLights:
            [new NuxTDirectionalLight(Vector3.UnitZ, new Vector3(2f), false)]);
        Vector3 directionalLight = directionalScene.TraceRay(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, 0);
        NuxTAssert(directionalLight.X > 0.5f, "Directional fill lights must contribute direct illumination.");
        var ambientScene = new NuxTScene([receiver], Vector3.Zero, ambientLight: new Vector3(0.2f));
        Vector3 ambientLight = ambientScene.TraceRay(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, 0);
        NuxTAssert(Vector3.Distance(ambientLight, new Vector3(0.2f)) < 1e-5f,
            "Global ambient light must contribute diffuse illumination.");

        NuxTTriangle shadowBlocker = new(
            new Vector3(0.4f, -0.5f, 0f), new Vector3(0.4f, 0.5f, 0f), new Vector3(0.4f, 0f, 1f),
            Vector3.UnitX, Vector3.UnitX, Vector3.UnitX,
            Vector2.Zero, Vector2.Zero, Vector2.Zero,
            new NuxTMaterial(Vector3.One, Vector3.Zero));
        var shadowedScene = new NuxTScene([receiver, shadowBlocker], Vector3.Zero, [pointLight]);
        Vector3 shadowed = shadowedScene.TraceRay(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, 0);
        NuxTAssert(shadowed.LengthSquared() == 0f, "Opaque geometry must cast ray-traced shadows from point lights.");
        NuxTTriangle clearShadowCutout = shadowBlocker with
        {
            Material = new NuxTMaterial(Vector3.One, Vector3.Zero, AlphaClip: true, AlbedoTexture: clearCutout)
        };
        var cutoutShadowScene = new NuxTScene([receiver, clearShadowCutout], Vector3.Zero, [pointLight]);
        Vector3 lightThroughCutout = cutoutShadowScene.TraceRay(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, 0);
        NuxTAssert(lightThroughCutout.X > 0.1f, "Transparent alpha-cutout pixels must not cast opaque shadows.");
        NuxTTriangle maskedShadowCutout = shadowBlocker with
        {
            Material = new NuxTMaterial(Vector3.One, Vector3.Zero, AlphaClip: true, AlphaTexture: clearAlphaMask)
        };
        var alphaMaskShadowScene = new NuxTScene([receiver, maskedShadowCutout], Vector3.Zero, [pointLight]);
        Vector3 lightThroughAlphaMask = alphaMaskShadowScene.TraceRay(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, 0);
        NuxTAssert(lightThroughAlphaMask.X > 0.1f, "Transparent alpha-mask pixels must not cast opaque shadows.");

        var diffuseScene = new NuxTScene([receiver], Vector3.One);
        Vector3 noBounce = diffuseScene.TraceRay(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, 0, 19);
        Vector3 oneBounce = diffuseScene.TraceRay(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, 1, 19);
        NuxTAssert(noBounce.LengthSquared() == 0f && oneBounce.X > 0f,
            "Additional bounces must gather indirect environment light from diffuse surfaces.");

        var halfScene = new NuxTScene(
            [new NuxTTriangle(new Vector3(-0.02f, -2f, 0f), new Vector3(2f, -2f, 0f), new Vector3(-0.02f, 2f, 0f),
                Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
                Vector2.Zero, Vector2.UnitX, Vector2.UnitY,
                new NuxTMaterial(Vector3.One, new Vector3(3f, 1f, 0f)))],
            new Vector3(0.01f));
        var camera = new NuxTCamera(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, Vector3.UnitY, MathF.PI / 3f);
        byte[] firstSample = halfScene.RenderSample(camera, 32, 24, 0, 123, new NuxTSettings(1, 0));
        byte[] repeatedSample = halfScene.RenderSample(camera, 32, 24, 0, 123, new NuxTSettings(1, 0));
        byte[] differentSeed = halfScene.RenderSample(camera, 32, 24, 0, 456, new NuxTSettings(1, 0));
        NuxTAssert(firstSample.SequenceEqual(repeatedSample), "NuxT camera samples must be deterministic for a fixed seed.");
        NuxTAssert(!firstSample.SequenceEqual(differentSeed), "Changing the seed must change stochastic edge samples.");

        using var renderer = new NuxTProgressiveRenderer();
        renderer.StartOrUpdate(empty, camera, 256, 256, new NuxTSettings(4096, 4, 1f));
        var replacement = new NuxTScene([], new Vector3(0.5f, 0.25f, 0.1f));
        renderer.StartOrUpdate(replacement, camera, 2, 2, new NuxTSettings(1, 0, 1f));
        NuxTFrame? finalFrame = null;
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (renderer.TryTakeLatestFrame(out NuxTFrame frame))
                finalFrame = frame;
            if (renderer.Status == "Complete")
                break;
            Thread.Sleep(5);
        }
        NuxTAssert(renderer.Status == "Complete" && renderer.CompletedSamples == 1 &&
                   finalFrame is { Width: 2, Height: 2, Samples: 1 },
            "Changing the scene must cancel obsolete work and restart a bounded progressive render.");
        NuxTFrame finalRender = renderer.RenderToCompletion(replacement, camera, 2, 2,
            new NuxTSettings(4, 0, 1f), transparentBackground: true);
        NuxTAssert(finalRender.Samples == 4 && renderer.CompletedSamples == 4 &&
                   Enumerable.Range(0, 4).All(pixel => finalRender.Rgba[pixel * 4 + 3] == 0),
            "Final CPU rendering must wait for every requested sample and preserve a transparent environment.");

        using var exportRenderer = new NuxTProgressiveRenderer();
        const int exportSamples = 12;
        exportRenderer.StartOrUpdate(empty, camera, 32, 24, new NuxTSettings(exportSamples, 0));
        NuxTFrame? exportFrame = null;
        int publishedExportSamples = 0;
        deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (exportRenderer.TryTakeLatestFrame(out NuxTFrame frame))
            {
                NuxTAssert(frame.Samples >= publishedExportSamples,
                    "Progressive export preview samples must never move backward.");
                publishedExportSamples = frame.Samples;
                exportFrame = frame;
            }
            if (exportRenderer.Status == "Complete")
                break;
            Thread.Sleep(2);
        }
        NuxTAssert(exportRenderer.Status == "Complete" && exportRenderer.CompletedSamples == exportSamples &&
                   exportFrame is { Samples: exportSamples },
            "An exported path-traced image must not be considered ready until its final sample is published.");

        using var denoisedCpuRenderer = new NuxTProgressiveRenderer();
        var coverageSettings = new NuxTSettings(4, 0, 1f, 1f, 123);
        NuxTFrame rawCoverageFrame = denoisedCpuRenderer.RenderToCompletion(halfScene, camera, 32, 24,
            coverageSettings, transparentBackground: true);
        NuxTFrame denoisedCoverageFrame = denoisedCpuRenderer.RenderToCompletion(halfScene, camera, 32, 24,
            coverageSettings with { DenoiserEnabled = true }, transparentBackground: true);
        NuxTAssert(denoisedCpuRenderer.Status.Contains("Denoised", StringComparison.Ordinal) &&
                   Enumerable.Range(0, 32 * 24).All(pixel =>
                       rawCoverageFrame.Rgba[pixel * 4 + 3] == denoisedCoverageFrame.Rgba[pixel * 4 + 3]),
            "Completed CPU output must run OIDN while preserving the original transparent coverage.");

        using var sceneBuilder = new NuxTSceneBuildCoordinator();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        sceneBuilder.Start(WaitForNuxTBuild(entered, release, emitter), Vector3.Zero);
        NuxTAssert(entered.Wait(TimeSpan.FromSeconds(5)), "Background NuxT scene construction must start asynchronously.");
        var replacementEnvironment = new Vector3(0.35f, 0.2f, 0.1f);
        sceneBuilder.Start([], replacementEnvironment);
        release.Set();

        NuxTScene? completedScene = null;
        deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (sceneBuilder.TryTakeCompleted(out NuxTScene scene))
            {
                completedScene = scene;
                break;
            }
            Thread.Sleep(5);
        }
        NuxTAssert(completedScene is { TriangleCount: 0 } &&
                   Vector3.Distance(completedScene.EnvironmentColor, replacementEnvironment) < 1e-5f,
            "Replacing a scene build must discard the cancelled snapshot rather than publishing stale geometry.");

        using var cancelledEntered = new ManualResetEventSlim();
        using var cancelledRelease = new ManualResetEventSlim();
        sceneBuilder.Start(WaitForNuxTBuild(cancelledEntered, cancelledRelease, emitter), Vector3.Zero);
        NuxTAssert(cancelledEntered.Wait(TimeSpan.FromSeconds(5)), "The cancellable NuxT scene build must start.");
        sceneBuilder.Cancel();
        cancelledRelease.Set();
        NuxTAssert(sceneBuilder.Status == "Cancelled" && !sceneBuilder.TryTakeCompleted(out _),
            "Cancelling scene preparation must not expose a completed or stale scene.");

        sceneBuilder.Start([emitter], Vector3.Zero);
        NuxTScene? preparedScene = null;
        deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (sceneBuilder.TryTakeCompleted(out NuxTScene scene))
            {
                preparedScene = scene;
                break;
            }
            Thread.Sleep(5);
        }
        NuxTAssert(preparedScene is { TriangleCount: 1 },
            "A verified geometry snapshot must complete scene preparation and be available for tracing.");

        using var instanceBuilder = new NuxTSceneBuildCoordinator();
        NuxTTriangle[] reusableGeometry = [NuxTTriangleAtZ(0f, new NuxTMaterial(Vector3.Zero, Vector3.One))];
        object reusableGeometryKey = new();
        instanceBuilder.StartInstances(
            [new NuxTSceneInstance(reusableGeometry, Matrix4x4.Identity, reusableGeometryKey)], Vector3.Zero);
        NuxTScene firstInstanceScene = WaitForNuxTSceneBuild(instanceBuilder);
        instanceBuilder.StartInstances(
            [new NuxTSceneInstance(reusableGeometry, Matrix4x4.CreateTranslation(2f, 0f, 0f), reusableGeometryKey)],
            Vector3.Zero);
        NuxTScene movedInstanceScene = WaitForNuxTSceneBuild(instanceBuilder);
        NuxTAssert(firstInstanceScene.BlasBuildCount == 1 && movedInstanceScene.BlasBuildCount == 0 &&
                   ReferenceEquals(firstInstanceScene.GpuTriangles, movedInstanceScene.GpuTriangles) &&
                   ReferenceEquals(firstInstanceScene.GpuTriangleIndices, movedInstanceScene.GpuTriangleIndices) &&
                   Vector3.Distance(movedInstanceScene.TraceRay(new Vector3(2f, 0f, 1f), -Vector3.UnitZ, 0),
                       Vector3.One) < 1e-5f,
            "Moving a static instance must reuse its packed geometry and BLAS while updating TLAS bounds.");

        using var preparedRenderer = new NuxTProgressiveRenderer();
        var renderCamera = new NuxTCamera(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, Vector3.UnitY, MathF.PI / 3f);
        var renderSettings = new NuxTSettings(1, 0);
        preparedRenderer.StartOrUpdate(preparedScene!, renderCamera, 2, 2, renderSettings);
        NuxTFrame? initialRender = WaitForNuxTRender(preparedRenderer);
        NuxTAssert(initialRender is { Samples: 1 }, "A prepared scene must enter progressive tracing.");

        NuxTCamera movedCamera = renderCamera with { Position = new Vector3(0f, 0f, 2f) };
        preparedRenderer.StartOrUpdate(preparedScene!, movedCamera, 2, 2, renderSettings);
        NuxTFrame? movedRender = WaitForNuxTRender(preparedRenderer);
        NuxTAssert(movedRender is { Samples: 1 } && preparedScene!.TriangleCount == 1,
            "Moving the camera must restart image accumulation while reusing the completed geometry scene.");

        var editedScene = new NuxTScene([], new Vector3(0.35f, 0.2f, 0.1f));
        preparedRenderer.StartOrUpdate(editedScene, movedCamera, 2, 2, renderSettings);
        NuxTFrame? editedRender = WaitForNuxTRender(preparedRenderer);
        NuxTAssert(editedRender is { Samples: 1 } &&
                   !editedRender.Value.Rgba.SequenceEqual(movedRender!.Value.Rgba),
            "A changed scene snapshot must replace the prior geometry and produce a new traced image.");

        RunNuxTViewportPreparationTests();
    }

    private static void RunNuxTViewportPreparationTests()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var viewport = new Viewport();
        var sceneBuilder = new NuxTSceneBuildCoordinator();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        NuxTTriangle capturedTriangle = NuxTTriangleAtZ(0f, new NuxTMaterial(Vector3.One, Vector3.Zero));
        sceneBuilder.Start(WaitForNuxTBuild(entered, release, capturedTriangle), Vector3.Zero);
        NuxTAssert(entered.Wait(TimeSpan.FromSeconds(5)), "The viewport lifecycle test must hold a scene build in progress.");

        FieldInfo builderField = typeof(Viewport).GetField("_nuxtSceneBuilder", flags)!;
        FieldInfo captureField = typeof(Viewport).GetField("_nuxtCapture", flags)!;
        FieldInfo cachedSceneField = typeof(Viewport).GetField("_nuxtCachedScene", flags)!;
        FieldInfo captureStampField = typeof(Viewport).GetField("_nuxtCaptureStamp", flags)!;
        FieldInfo preparationStatusField = typeof(Viewport).GetField("_nuxtPreparationStatus", flags)!;
        PropertyInfo geometryIdentity = typeof(NuxTScene).GetProperty("GpuGeometryIdentity",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        builderField.SetValue(viewport, sceneBuilder);

        try
        {
            for (int i = 0; i < 4; i++)
                InvokeNuxTViewportMethod(viewport, "EnsureNuxTSceneScan", viewport, new vec3(i, 0f, 0f));
            NuxTAssert(captureField.GetValue(viewport) is null && sceneBuilder.Status == "Building scene" &&
                       (string?)preparationStatusField.GetValue(viewport) == "Building scene",
                "Repeated viewport checks, including camera movement, must not restart or obscure an active scene build.");
        }
        finally
        {
            release.Set();
        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && cachedSceneField.GetValue(viewport) is null)
        {
            InvokeNuxTViewportMethod(viewport, "PollNuxTSceneBuild");
            Thread.Sleep(5);
        }
        NuxTScene? initialScene = cachedSceneField.GetValue(viewport) as NuxTScene;
        NuxTAssert(initialScene is { TriangleCount: 1 }, "The viewport must accept the completed prepared scene.");

        object scan = InvokeNuxTViewportMethod(viewport, "CreateNuxTScan", viewport, new vec3(0f), false)!;
        HashCode unchangedStamp = (HashCode)scan.GetType().GetField("Hash", flags)!.GetValue(scan)!;
        captureStampField.SetValue(viewport, unchangedStamp.ToHashCode());
        HashCode unchangedLightingStamp = (HashCode)scan.GetType().GetField("LightingHash", flags)!.GetValue(scan)!;
        typeof(Viewport).GetField("_nuxtLightingStamp", flags)!.SetValue(viewport, unchangedLightingStamp.ToHashCode());
        InvokeNuxTViewportMethod(viewport, "EnsureNuxTSceneScan", viewport, new vec3(3f, 2f, 1f));
        InvokeNuxTViewportMethod(viewport, "ProcessNuxTCaptureSlice", viewport, new vec3(3f, 2f, 1f));
        NuxTAssert(captureField.GetValue(viewport) is null,
            "An unchanged scene check after camera movement must not start another capture.");
        NuxTAssert(ReferenceEquals(cachedSceneField.GetValue(viewport), initialScene),
            "An unchanged scene check after camera movement must retain the prepared geometry.");
        NuxTAssert(sceneBuilder.Status == "Ready",
            "An unchanged scene check after camera movement must retain the prepared geometry and avoid another build.");

        var bentObject = new SceneObject { Name = "NuxT bend edit" };
        var bentMesh = new Mesh(null!)
        {
            GpuBend = new() { SegmentAxis = 1, BendStart = 0f, BendEnd = 1f, BendSize = 1f, Plane = true }
        };
        bentMesh.Vertices.AddRange([vec3.Zero, vec3.UnitX, vec3.UnitY]);
        bentMesh.Normals.AddRange(Enumerable.Repeat(vec3.UnitZ, 3));
        bentObject.Visuals.Add(bentMesh);
        viewport.SceneObjects.Add(bentObject);
        object bentScan = InvokeNuxTViewportMethod(viewport, "CreateNuxTScan", viewport, new vec3(3f, 2f, 1f), false)!;
        HashCode bentStamp = (HashCode)bentScan.GetType().GetField("Hash", flags)!.GetValue(bentScan)!;
        captureStampField.SetValue(viewport, bentStamp.ToHashCode());

        bentMesh.GpuBend!.Angle = new vec3(25f, 0f, 0f);
        InvokeNuxTViewportMethod(viewport, "EnsureNuxTSceneScan", viewport, new vec3(3f, 2f, 1f));
        InvokeNuxTViewportMethod(viewport, "ProcessNuxTCaptureSlice", viewport, new vec3(3f, 2f, 1f));
        NuxTAssert(cachedSceneField.GetValue(viewport) is null && captureField.GetValue(viewport) is not null,
            "Changing a paused mesh bend must invalidate the cached scene and begin a replacement capture.");

        deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && cachedSceneField.GetValue(viewport) is null)
        {
            InvokeNuxTViewportMethod(viewport, "ProcessNuxTCaptureSlice", viewport, new vec3(3f, 2f, 1f));
            InvokeNuxTViewportMethod(viewport, "PollNuxTSceneBuild");
            Thread.Sleep(5);
        }
        NuxTScene? replacement = cachedSceneField.GetValue(viewport) as NuxTScene;
        NuxTAssert(replacement is { TriangleCount: 1 } && !ReferenceEquals(replacement, initialScene),
            "The bent mesh edit must complete preparation and replace the old cached geometry.");

        InvokeNuxTViewportMethod(viewport, "EnsureNuxTSceneScan", viewport, new vec3(3f, 2f, 1f));
        InvokeNuxTViewportMethod(viewport, "ProcessNuxTCaptureSlice", viewport, new vec3(3f, 2f, 1f));
        NuxTAssert(captureField.GetValue(viewport) is null &&
                   ReferenceEquals(cachedSceneField.GetValue(viewport), replacement) && sceneBuilder.Status == "Ready",
            "An unchanged bent mesh must keep the replacement scene without starting another build.");

        var light = new LightSceneObject { Name = "NuxT light energy edit" };
        viewport.SceneObjects.Add(light);
        object lightBaselineScan = ScanViewportForNuxTTests(viewport);
        HashCode lightGeometryStamp = (HashCode)lightBaselineScan.GetType().GetField("Hash", flags)!.GetValue(lightBaselineScan)!;
        HashCode lightStateStamp = (HashCode)lightBaselineScan.GetType().GetField("LightingHash", flags)!.GetValue(lightBaselineScan)!;
        captureStampField.SetValue(viewport, lightGeometryStamp.ToHashCode());
        typeof(Viewport).GetField("_nuxtLightingStamp", flags)!.SetValue(viewport, lightStateStamp.ToHashCode());
        InvokeNuxTViewportMethod(viewport, "CompleteNuxTScan", viewport, new vec3(3f, 2f, 1f), lightBaselineScan);
        NuxTScene beforeLightEdit = (NuxTScene)cachedSceneField.GetValue(viewport)!;

        light.LightEnergy = 3f;
        object lightEditScan = ScanViewportForNuxTTests(viewport);
        HashCode editedGeometryStamp = (HashCode)lightEditScan.GetType().GetField("Hash", flags)!.GetValue(lightEditScan)!;
        HashCode editedLightingStamp = (HashCode)lightEditScan.GetType().GetField("LightingHash", flags)!.GetValue(lightEditScan)!;
        NuxTAssert(editedGeometryStamp.ToHashCode() == lightGeometryStamp.ToHashCode() &&
                   editedLightingStamp.ToHashCode() != lightStateStamp.ToHashCode(),
            "Changing a light's energy must change lighting state without invalidating the scene geometry stamp.");
        InvokeNuxTViewportMethod(viewport, "CompleteNuxTScan", viewport, new vec3(3f, 2f, 1f), lightEditScan);
        NuxTScene afterLightEdit = (NuxTScene)cachedSceneField.GetValue(viewport)!;
        NuxTLight[] relitLights = (NuxTLight[])typeof(NuxTScene).GetProperty("GpuLights",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(afterLightEdit)!;
        NuxTAssert(!ReferenceEquals(beforeLightEdit, afterLightEdit) &&
                   ReferenceEquals(geometryIdentity.GetValue(beforeLightEdit), geometryIdentity.GetValue(afterLightEdit)) &&
                   relitLights.Length == 1 && relitLights[0].Energy == light.LightEnergy,
            "A light energy edit must replace the lighting snapshot while retaining the same acceleration structures.");
        replacement = afterLightEdit;

        var previewViewport = new Viewport { IsPreviewViewport = true, MainViewport = viewport };
        object mainOutput = InvokeNuxTViewportMethod(viewport, "GetNuxTViewOutput", viewport)!;
        object previewOutput = InvokeNuxTViewportMethod(viewport, "GetNuxTViewOutput", previewViewport)!;
        NuxTAssert(!ReferenceEquals(mainOutput, previewOutput),
            "The main viewport and camera preview must accumulate NuxT samples in independent output states.");

        MethodInfo fallbackMethod = typeof(Viewport).GetMethod("ShouldUseNuxTRasterFallback", flags | BindingFlags.Static)!;
        bool usesFallback = (bool)fallbackMethod.Invoke(null, [true, true, true])!;
        bool skipsFallbackWhenDisabled = !(bool)fallbackMethod.Invoke(null, [false, true, true])!;
        bool skipsFallbackForDiagnosticPass = !(bool)fallbackMethod.Invoke(null, [true, true, false])!;
        bool skipsFallbackOutsideRenderedMode = !(bool)fallbackMethod.Invoke(null, [true, false, true])!;
        NuxTAssert(usesFallback && skipsFallbackWhenDisabled && skipsFallbackForDiagnosticPass &&
                   skipsFallbackOutsideRenderedMode,
            "Only NuxT Combined rendering must replace expensive raster lighting with the shaded fallback.");

        typeof(Viewport).GetField("_useNuxT", flags)!.SetValue(viewport, true);
        preparationStatusField.SetValue(viewport, "Capturing geometry (1 triangle)");
        FieldInfo previewModeField = typeof(Viewport).GetField("_viewportRenderMode", flags)!;
        previewModeField.SetValue(previewViewport, Enum.Parse(previewModeField.FieldType, "Rendered"));
        FieldInfo previewPassField = typeof(Viewport).GetField("_renderedPassMode", flags)!;
        previewPassField.SetValue(previewViewport, Enum.Parse(previewPassField.FieldType, "Combined"));
        string? previewStatus = viewport.GetNuxTStatus(previewViewport);
        NuxTAssert(previewStatus == "Capturing geometry (1 triangle)",
            "The preview must display NuxT scene preparation status before tracing samples begin.");

        object interruptedCapture = InvokeNuxTViewportMethod(viewport, "CreateNuxTScan", viewport, new vec3(3f, 2f, 1f), false)!;
        captureField.SetValue(viewport, interruptedCapture);
        long editTimestamp = 1_000_000L;
        bool paused = (bool)InvokeNuxTViewportMethod(viewport, "ShouldPauseNuxTForInteraction", true, editTimestamp)!;
        FieldInfo editQuietUntilField = typeof(Viewport).GetField("_nuxtEditQuietUntil", flags)!;
        long editQuietUntil = (long)editQuietUntilField.GetValue(viewport)!;
        NuxTAssert(paused && captureField.GetValue(viewport) is null &&
                   ReferenceEquals(cachedSceneField.GetValue(viewport), replacement) && sceneBuilder.Status == "Ready",
            "Starting an edit must cancel partial capture while retaining the last completed geometry and renderer state.");
        NuxTAssert((bool)InvokeNuxTViewportMethod(viewport, "ShouldPauseNuxTForInteraction", false, editQuietUntil - 1)! &&
                   !(bool)InvokeNuxTViewportMethod(viewport, "ShouldPauseNuxTForInteraction", false, editQuietUntil)!,
            "NuxT preparation must remain paused through the edit debounce interval and resume afterward.");

        sceneBuilder.Dispose();

        var activeEditViewport = new Viewport();
        var activeBuilder = new NuxTSceneBuildCoordinator();
        using var activeEntered = new ManualResetEventSlim();
        using var activeRelease = new ManualResetEventSlim();
        activeBuilder.Start(WaitForNuxTBuild(activeEntered, activeRelease, capturedTriangle), Vector3.Zero);
        NuxTAssert(activeEntered.Wait(TimeSpan.FromSeconds(5)), "The obsolete scene build must be running before cancellation.");
        builderField.SetValue(activeEditViewport, activeBuilder);
        bool cancelledBuild = (bool)InvokeNuxTViewportMethod(activeEditViewport, "ShouldPauseNuxTForInteraction", true, editTimestamp)!;
        NuxTAssert(cancelledBuild && activeBuilder.Status == "Cancelled",
            "An in-progress scene build without a completed snapshot must still be cancelled when editing starts.");
        activeRelease.Set();
        activeBuilder.Dispose();
    }

    private static object ScanViewportForNuxTTests(Viewport viewport)
    {
        object scan = InvokeNuxTViewportMethod(viewport, "CreateNuxTScan", viewport, new vec3(3f, 2f, 1f), false)!;
        InvokeNuxTViewportMethod(viewport, "ScanNuxTSceneSlice", viewport, new vec3(3f, 2f, 1f), scan, long.MaxValue);
        return scan;
    }

    private static NuxTScene WaitForNuxTSceneBuild(NuxTSceneBuildCoordinator coordinator)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (coordinator.TryTakeCompleted(out NuxTScene scene))
                return scene;
            Thread.Sleep(5);
        }
        throw new InvalidOperationException($"NuxT scene build did not complete: {coordinator.Status}.");
    }

    private static unsafe void RunNuxTProject(string projectPath)
    {
        if (Directory.Exists(projectPath))
            projectPath = Path.Combine(projectPath, Path.GetFileName(Path.TrimEndingDirectorySeparator(projectPath)) + ".nxProj");
        projectPath = Path.GetFullPath(projectPath);
        var manifest = JsonSerializer.Deserialize(File.ReadAllText(projectPath), AppJsonContext.Default.ProjectManifest)
            ?? throw new InvalidDataException("Missing project manifest.");

        IEnumerable<ProjectSceneObjectEntry> Entries(ProjectSceneObjectEntry root)
        {
            yield return root;
            foreach (ProjectSceneObjectEntry child in root.Children)
                foreach (ProjectSceneObjectEntry entry in Entries(child))
                    yield return entry;
        }

        var roots = manifest.SceneObjects.Where(root => !root.IsHidden).ToList();
        foreach (ProjectSceneObjectEntry root in roots)
        foreach (ProjectSceneObjectEntry entry in Entries(root))
        {
            if (string.IsNullOrEmpty(entry.SourceAssetPath))
                continue;
            entry.SourceAssetPath = entry.SourceAssetPath.StartsWith(DataAssetRemapper.ReferencePrefix,
                StringComparison.OrdinalIgnoreCase)
                ? DataAssetRemapper.Current.Resolve(entry.SourceAssetPath) ??
                  throw new FileNotFoundException($"Cannot resolve {entry.SourceAssetPath}.")
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, entry.SourceAssetPath));
        }

        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        NuxTAssert(glfw.Init(), "GLFW initialization failed.");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 4);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "NuxT supplied project regression", null, null);
        NuxTAssert(window != null, "OpenGL 4.3 compute context creation failed.");

        var meshes = new HashSet<Mesh>();
        Viewport? viewport = null;
        try
        {
            glfw.MakeContextCurrent(window);
            using var gl = Silk.NET.OpenGL.GL.GetApi(glfw.GetProcAddress);
            NuxTAssert(NuxTComputeRenderer.IsSupported(gl), "The project regression requires OpenGL 4.3 compute support.");
            var loader = MineImatorLoader.Instance;
            loader.Initialize(gl);
            MineImatorSimplyRemade.BlockRegistry.Initialize();
            MineImatorSimplyRemade.CharacterRegistry.Initialize();
            MineImatorSimplyRemade.TerrainAtlas.Initialize(gl);
            MineImatorSimplyRemade.ItemsAtlas.Initialize(gl);

            var properties = new PropertiesPanel();
            properties.LoadProjectSettingsFromManifest(manifest);
            viewport = new Viewport { Gl = gl, PropertiesPanel = properties };
            var spawn = new SpawnMenu { Viewport = viewport, Gl = gl };
            ProjectSceneSerializer.LoadSceneFromManifest(new() { SceneObjects = roots }, viewport, spawn);
            var timeline = new Timeline { Viewport = viewport };
            timeline.ImportProjectState(manifest.Timeline ?? new() { MaxFrames = 10 });
            IEnumerable<SceneObject> Objects(SceneObject root)
            {
                yield return root;
                foreach (SceneObject child in root.Children)
                    foreach (SceneObject obj in Objects(child))
                        yield return obj;
            }
            foreach (SceneObject obj in viewport.SceneObjects.SelectMany(Objects))
                meshes.UnionWith(obj.Visuals);

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo captureField = typeof(Viewport).GetField("_nuxtCapture", flags)!;
            FieldInfo sceneField = typeof(Viewport).GetField("_nuxtCachedScene", flags)!;
            FieldInfo builderField = typeof(Viewport).GetField("_nuxtSceneBuilder", flags)!;
            vec3 capturePosition = vec3.Zero;
            object initialCapture = InvokeNuxTViewportMethod(viewport, "CreateNuxTScan", viewport, capturePosition, false)!;
            captureField.SetValue(viewport, initialCapture);
            DateTime deadline = DateTime.UtcNow.AddMinutes(15);
            while (DateTime.UtcNow < deadline && sceneField.GetValue(viewport) is null)
            {
                if (captureField.GetValue(viewport) is not null)
                    InvokeNuxTViewportMethod(viewport, "ProcessNuxTCaptureSlice", viewport, capturePosition);
                InvokeNuxTViewportMethod(viewport, "PollNuxTSceneBuild");
                object? builder = builderField.GetValue(viewport);
                string? status = builder?.GetType().GetProperty("Status")?.GetValue(builder) as string;
                if (status?.StartsWith("Error:", StringComparison.Ordinal) == true)
                    throw new InvalidOperationException($"NuxT project scene preparation failed: {status}");
                Thread.Sleep(1);
            }

            NuxTScene scene = sceneField.GetValue(viewport) as NuxTScene ??
                              throw new TimeoutException("NuxT project scene capture did not complete within 15 minutes.");
            var camera = new NuxTCamera(new Vector3(0f, 0f, 10f), -Vector3.UnitZ, Vector3.UnitY, 1f);
            var settings = new NuxTSettings(1, 0, 0.1f, 1f, 1);
            using var renderer = new NuxTComputeRenderer(gl);
            bool usedGpu = renderer.Render(scene, camera, 8, 8, settings);
            Console.WriteLine($"NuxT project scene: {scene.TriangleCount:N0} triangles; {renderer.Status}.");
            NuxTAssert(usedGpu || renderer.Status.StartsWith("CPU fallback:", StringComparison.Ordinal),
                "A project outside device limits must use the explicit CPU fallback.");
            if (usedGpu)
                NuxTAssert(renderer.CompletedSamples == 1, "The supplied project GPU check must dispatch one sample.");
        }
        finally
        {
            if (viewport is not null)
            {
                InvokeNuxTViewportMethod(viewport, "StopNuxTRendering");
                foreach (Mesh mesh in meshes)
                    mesh.Dispose();
            }
            glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }

    private static unsafe void RunNuxTGraphicsTests()
    {
        using var glfw = Silk.NET.GLFW.Glfw.GetApi();
        NuxTAssert(glfw.Init(), "GLFW initialization failed.");
        glfw.WindowHint(Silk.NET.GLFW.WindowHintBool.Visible, false);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMajor, 4);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintInt.ContextVersionMinor, 3);
        glfw.WindowHint(Silk.NET.GLFW.WindowHintOpenGlProfile.OpenGlProfile, Silk.NET.GLFW.OpenGlProfile.Core);
        var window = glfw.CreateWindow(64, 64, "NuxT compute regression", null, null);
        try
        {
            NuxTAssert(window != null, "OpenGL 4.3 compute context creation failed.");
            glfw.MakeContextCurrent(window);
            using var gl = Silk.NET.OpenGL.GL.GetApi(glfw.GetProcAddress);
            NuxTAssert(NuxTComputeRenderer.IsSupported(gl), "The graphics regression requires OpenGL 4.3 compute support.");

            const int width = 8;
            const int height = 8;
            var camera = new NuxTCamera(new Vector3(0f, 0f, 1f), -Vector3.UnitZ, Vector3.UnitY, 0.2f);
            var texture = new NuxTTexture([64, 128, 224, 255], 1, 1);
            var material = new NuxTMaterial(Vector3.One, Vector3.Zero, AlbedoTexture: texture, Unlit: true);
            var triangle = new NuxTTriangle(
                new Vector3(-10f, -10f, 0f), new Vector3(10f, -10f, 0f), new Vector3(0f, 10f, 0f),
                Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
                Vector2.Zero, Vector2.UnitX, Vector2.UnitY, material);
            var scene = new NuxTScene([triangle], Vector3.Zero);
            var settings = new NuxTSettings(2, 0, 1f, 1f, 19);

            using var renderer = new NuxTComputeRenderer(gl);
            NuxTAssert(renderer.Render(scene, camera, width, height, settings), "GPU tracing must dispatch the textured test scene.");
            NuxTAssert(renderer.CompletedSamples == 1, "The first dispatch must accumulate exactly one sample.");
            byte[] actual = ReadGpuOutput(renderer.OutputTexture, width, height);
            byte[] expected = scene.RenderSample(camera, width, height, 0, settings.Seed, settings);
            for (int i = 0; i < actual.Length; i++)
                NuxTAssert(Math.Abs(actual[i] - expected[i]) <= 1,
                    $"GPU output must match CPU reference pixels (byte {i}: {actual[i]} vs {expected[i]}).");

            const int tiledWidth = 513;
            const int tiledHeight = 515;
            var tiledSettings = new NuxTSettings(2, 0, 1f, 1f, 19);
            using (var progressiveTiledRenderer = new NuxTComputeRenderer(gl))
            using (var finalTiledRenderer = new NuxTComputeRenderer(gl))
            {
                NuxTAssert(progressiveTiledRenderer.Render(scene, camera, tiledWidth, tiledHeight, tiledSettings) &&
                           progressiveTiledRenderer.Render(scene, camera, tiledWidth, tiledHeight, tiledSettings),
                    "Progressive tracing must complete the odd-sized multi-tile reference image.");
                NuxTAssert(finalTiledRenderer.Render(scene, camera, tiledWidth, tiledHeight, tiledSettings,
                        completeImmediately: true) && finalTiledRenderer.CompletedSamples == tiledSettings.Samples,
                    "Final tracing must complete all samples across odd-sized image tiles.");
                byte[] progressivePixels = ReadGpuOutput(progressiveTiledRenderer.OutputTexture, tiledWidth, tiledHeight);
                byte[] finalPixels = ReadGpuOutput(finalTiledRenderer.OutputTexture, tiledWidth, tiledHeight);
                NuxTAssert(progressivePixels.SequenceEqual(finalPixels),
                    "Tiled final GPU pixels must exactly match progressive tracing across tile boundaries.");
            }

            var readbackViewport = new Viewport { Gl = gl };
            uint readbackTexture = 0;
            uint readbackFramebuffer = 0;
            uint pixelPackBuffer = 0;
            try
            {
                byte[] sourcePixels =
                [
                    1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255,
                    10, 11, 12, 255, 13, 14, 15, 255, 16, 17, 18, 255
                ];
                gl.GenTextures(1, out readbackTexture);
                gl.BindTexture(Silk.NET.OpenGL.GLEnum.Texture2D, readbackTexture);
                fixed (byte* pointer = sourcePixels)
                    gl.TexImage2D(Silk.NET.OpenGL.GLEnum.Texture2D, 0,
                        Silk.NET.OpenGL.InternalFormat.Rgba8, 3, 2, 0,
                        Silk.NET.OpenGL.GLEnum.Rgba, Silk.NET.OpenGL.GLEnum.UnsignedByte, pointer);
                gl.GenFramebuffers(1, out readbackFramebuffer);
                gl.BindFramebuffer(Silk.NET.OpenGL.GLEnum.ReadFramebuffer, readbackFramebuffer);
                gl.FramebufferTexture2D(Silk.NET.OpenGL.GLEnum.ReadFramebuffer,
                    Silk.NET.OpenGL.GLEnum.ColorAttachment0, Silk.NET.OpenGL.GLEnum.Texture2D, readbackTexture, 0);
                NuxTAssert(gl.CheckFramebufferStatus(Silk.NET.OpenGL.GLEnum.ReadFramebuffer) ==
                           Silk.NET.OpenGL.GLEnum.FramebufferComplete,
                    "The capture readback regression framebuffer must be complete.");

                gl.GenBuffers(1, out pixelPackBuffer);
                gl.BindBuffer(Silk.NET.OpenGL.GLEnum.PixelPackBuffer, pixelPackBuffer);
                gl.BufferData(Silk.NET.OpenGL.GLEnum.PixelPackBuffer, 256, null, Silk.NET.OpenGL.GLEnum.StreamRead);
                gl.PixelStore(Silk.NET.OpenGL.GLEnum.PackAlignment, 8);
                gl.PixelStore(Silk.NET.OpenGL.GLEnum.PackRowLength, 5);
                gl.PixelStore(Silk.NET.OpenGL.GLEnum.PackSkipPixels, 1);
                gl.PixelStore(Silk.NET.OpenGL.GLEnum.PackSkipRows, 1);

                object?[] readbackArguments = [readbackFramebuffer, 3u, 2u, false, null];
                bool readbackSucceeded = (bool)typeof(Viewport).GetMethod("ReadFramebufferPixels",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(readbackViewport, readbackArguments)!;
                byte[] capturedPixels = (byte[])readbackArguments[4]!;
                byte[] expectedPixels =
                [
                    1, 2, 3, 4, 5, 6, 7, 8, 9,
                    10, 11, 12, 13, 14, 15, 16, 17, 18
                ];
                NuxTAssert(readbackSucceeded && capturedPixels.SequenceEqual(expectedPixels),
                    "Framebuffer capture must read tightly packed RGB bytes with a non-default pixel-pack state.");
                readbackArguments[3] = true;
                bool rgbaReadbackSucceeded = (bool)typeof(Viewport).GetMethod("ReadFramebufferPixels",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(readbackViewport, readbackArguments)!;
                byte[] capturedRgbaPixels = (byte[])readbackArguments[4]!;
                NuxTAssert(rgbaReadbackSucceeded && capturedRgbaPixels.SequenceEqual(sourcePixels),
                    "Framebuffer capture must preserve tightly packed RGBA bytes with a non-default pixel-pack state.");
                NuxTAssert(gl.GetInteger(Silk.NET.OpenGL.GLEnum.PixelPackBufferBinding) == (int)pixelPackBuffer &&
                           gl.GetInteger(Silk.NET.OpenGL.GLEnum.PackAlignment) == 8 &&
                           gl.GetInteger(Silk.NET.OpenGL.GLEnum.PackRowLength) == 5 &&
                           gl.GetInteger(Silk.NET.OpenGL.GLEnum.PackSkipPixels) == 1 &&
                           gl.GetInteger(Silk.NET.OpenGL.GLEnum.PackSkipRows) == 1 &&
                           gl.GetInteger(Silk.NET.OpenGL.GLEnum.ReadFramebufferBinding) == (int)readbackFramebuffer,
                    "Framebuffer capture must restore the previous read target and pixel-pack state.");
            }
            finally
            {
                gl.BindBuffer(Silk.NET.OpenGL.GLEnum.PixelPackBuffer, 0);
                gl.PixelStore(Silk.NET.OpenGL.GLEnum.PackAlignment, 4);
                gl.PixelStore(Silk.NET.OpenGL.GLEnum.PackRowLength, 0);
                gl.PixelStore(Silk.NET.OpenGL.GLEnum.PackSkipPixels, 0);
                gl.PixelStore(Silk.NET.OpenGL.GLEnum.PackSkipRows, 0);
                gl.BindFramebuffer(Silk.NET.OpenGL.GLEnum.ReadFramebuffer, 0);
                if (pixelPackBuffer != 0)
                    gl.DeleteBuffer(pixelPackBuffer);
                if (readbackFramebuffer != 0)
                    gl.DeleteFramebuffer(readbackFramebuffer);
                if (readbackTexture != 0)
                    gl.DeleteTexture(readbackTexture);
            }

            NuxTAssert(renderer.Render(scene, camera, width, height, settings) && renderer.CompletedSamples == 2,
                "A second dispatch must accumulate without resetting the same request.");
            NuxTAssert(renderer.Render(scene, camera with { Position = new Vector3(0.01f, 0f, 1f) }, width, height, settings) &&
                       renderer.CompletedSamples == 1,
                "Changing the camera must reset GPU accumulation and trace a new first sample.");

            var litMaterial = material with { Unlit = false };
            var litTriangle = triangle with { Material = litMaterial };
            var directionalScene = new NuxTScene([litTriangle], Vector3.Zero, directionalLights:
                [new NuxTDirectionalLight(Vector3.UnitZ, new Vector3(1.5f), false)]);
            NuxTAssert(renderer.Render(directionalScene, camera, width, height, settings),
                "GPU tracing must dispatch a scene with directional lighting.");
            actual = ReadGpuOutput(renderer.OutputTexture, width, height);
            expected = directionalScene.RenderSample(camera, width, height, 0, settings.Seed, settings);
            for (int i = 0; i < actual.Length; i++)
                NuxTAssert(Math.Abs(actual[i] - expected[i]) <= 1,
                    $"Directional-light GPU pixels must match the CPU reference (byte {i}: {actual[i]} vs {expected[i]}).");

            var pointLightScene = new NuxTScene([litTriangle], Vector3.Zero,
                [new NuxTLight(new Vector3(0f, 0f, 1f), Vector3.UnitZ, Vector3.One, 2f, 10f)]);
            NuxTAssert(renderer.Render(pointLightScene, camera, width, height, settings),
                "GPU tracing must dispatch a scene with point lighting.");
            actual = ReadGpuOutput(renderer.OutputTexture, width, height);
            expected = pointLightScene.RenderSample(camera, width, height, 0, settings.Seed, settings);
            for (int i = 0; i < actual.Length; i++)
                NuxTAssert(Math.Abs(actual[i] - expected[i]) <= 1,
                    $"Point-light GPU pixels must match the CPU reference (byte {i}: {actual[i]} vs {expected[i]}).");

            PropertyInfo geometryUploadsProperty = typeof(NuxTComputeRenderer).GetProperty("SceneGeometryUploadCount",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            PropertyInfo lightingUploadsProperty = typeof(NuxTComputeRenderer).GetProperty("LightingTextureUploadCount",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            int geometryUploadsBeforeRelight = (int)geometryUploadsProperty.GetValue(renderer)!;
            int lightingUploadsBeforeRelight = (int)lightingUploadsProperty.GetValue(renderer)!;
            NuxTScene relitPointLightScene = pointLightScene.WithLighting(
                [new NuxTLight(new Vector3(0f, 0f, 1f), Vector3.UnitZ, Vector3.One, 8f, 10f)], [], Vector3.Zero,
                Vector3.Zero);
            NuxTAssert(renderer.Render(relitPointLightScene, camera, width, height, settings) &&
                       renderer.CompletedSamples == 1,
                "A point-light energy edit must reset GPU accumulation and trace a new first sample.");
            NuxTAssert((int)geometryUploadsProperty.GetValue(renderer)! == geometryUploadsBeforeRelight &&
                       (int)lightingUploadsProperty.GetValue(renderer)! == lightingUploadsBeforeRelight + 1,
                "Relighting must update only point/directional light textures without re-uploading geometry acceleration.");
            actual = ReadGpuOutput(renderer.OutputTexture, width, height);
            expected = relitPointLightScene.RenderSample(camera, width, height, 0, settings.Seed, settings);
            for (int i = 0; i < actual.Length; i++)
                NuxTAssert(Math.Abs(actual[i] - expected[i]) <= 1,
                    $"Relit point-light GPU pixels must match the CPU reference (byte {i}: {actual[i]} vs {expected[i]}).");

            NuxTTriangle[] sharedGeometry =
            [
                new NuxTTriangle(new Vector3(-0.2f, -0.2f, 0f), new Vector3(0.2f, -0.2f, 0f),
                    new Vector3(0f, 0.2f, 0f), Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
                    Vector2.Zero, Vector2.UnitX, Vector2.UnitY, material)
            ];
            object sharedGeometryKey = new();
            var instanceCamera = new NuxTCamera(new Vector3(0f, 0f, 2f), -Vector3.UnitZ, Vector3.UnitY, 0.8f);
            var instanceScene = NuxTScene.CreateInstances(
            [
                new NuxTSceneInstance(sharedGeometry,
                    Matrix4x4.CreateScale(1.2f, 0.8f, 1f) * Matrix4x4.CreateRotationZ(0.15f) *
                    Matrix4x4.CreateTranslation(-0.35f, 0f, 0f), sharedGeometryKey,
                    new NuxTMaterial(new Vector3(1f, 0f, 0f), Vector3.Zero, Unlit: true)),
                new NuxTSceneInstance(sharedGeometry,
                    Matrix4x4.CreateScale(0.8f, 1.2f, 1f) * Matrix4x4.CreateRotationZ(-0.15f) *
                    Matrix4x4.CreateTranslation(0.35f, 0f, 0f), sharedGeometryKey,
                    new NuxTMaterial(new Vector3(0f, 0f, 1f), Vector3.Zero, Unlit: true))
            ], Vector3.Zero);
            NuxTAssert(renderer.Render(instanceScene, instanceCamera, width, height, settings),
                "GPU tracing must dispatch shared geometry through transformed TLAS instances.");
            actual = ReadGpuOutput(renderer.OutputTexture, width, height);
            expected = instanceScene.RenderSample(instanceCamera, width, height, 0, settings.Seed, settings);
            for (int i = 0; i < actual.Length; i++)
                NuxTAssert(Math.Abs(actual[i] - expected[i]) <= 1,
                    $"Transformed-instance GPU pixels must match CPU output (byte {i}: {actual[i]} vs {expected[i]}).");

            var emptyScene = new NuxTScene([], new Vector3(0.2f, 0.4f, 0.6f));
            NuxTAssert(renderer.Render(emptyScene, camera, width, height, settings) && renderer.CompletedSamples == 1,
                "Replacing the scene with an empty environment must reset and dispatch successfully.");
            actual = ReadGpuOutput(renderer.OutputTexture, width, height);
            expected = emptyScene.RenderSample(camera, width, height, 0, settings.Seed, settings);
            NuxTAssert(actual.SequenceEqual(expected), "Empty-scene GPU pixels must exactly match the CPU environment output.");

            var transparentSettings = new NuxTSettings(2, 0, 1f, 1f, 23);
            NuxTAssert(renderer.Render(emptyScene, camera, width, height, transparentSettings,
                    transparentBackground: true, completeImmediately: true) &&
                       renderer.CompletedSamples == transparentSettings.Samples,
                "A final GPU render must dispatch all configured samples before returning.");
            actual = ReadGpuOutput(renderer.OutputTexture, width, height);
            NuxTAssert(Enumerable.Range(0, width * height).All(pixel => actual[pixel * 4 + 3] == 0),
                "GPU environment misses must retain transparent alpha for raster-background composition.");
            NuxTAssert(renderer.Render(scene, camera, width, height, transparentSettings,
                    transparentBackground: true, completeImmediately: true),
                "GPU surface sampling must support a transparent composition layer.");
            actual = ReadGpuOutput(renderer.OutputTexture, width, height);
            NuxTAssert(actual[(4 * width + 4) * 4 + 3] == 255,
                "GPU primary-ray surface hits must fully cover the raster background.");

            var compositorViewport = new Viewport { Gl = gl };
            uint compositorVao = gl.GenVertexArray();
            uint compositorSource = gl.GenTexture();
            uint compositorTarget = gl.GenTexture();
            uint compositorFramebuffer = gl.GenFramebuffer();
            typeof(Viewport).GetField("_edgeVao", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(compositorViewport, compositorVao);
            try
            {
                byte[] sourcePixels = Enumerable.Repeat(new byte[] { 255, 0, 0, 128 }, 4).SelectMany(pixel => pixel).ToArray();
                byte[] backgroundPixels = Enumerable.Repeat(new byte[] { 0, 0, 255, 255 }, 4).SelectMany(pixel => pixel).ToArray();
                void UploadTexture(uint textureId, byte[] pixels)
                {
                    gl.BindTexture(Silk.NET.OpenGL.GLEnum.Texture2D, textureId);
                    fixed (byte* pointer = pixels)
                        gl.TexImage2D(Silk.NET.OpenGL.GLEnum.Texture2D, 0,
                            Silk.NET.OpenGL.InternalFormat.Rgba8, 2, 2, 0,
                            Silk.NET.OpenGL.GLEnum.Rgba, Silk.NET.OpenGL.GLEnum.UnsignedByte, pointer);
                    gl.TexParameter(Silk.NET.OpenGL.GLEnum.Texture2D, Silk.NET.OpenGL.GLEnum.TextureMinFilter,
                        (int)Silk.NET.OpenGL.GLEnum.Nearest);
                    gl.TexParameter(Silk.NET.OpenGL.GLEnum.Texture2D, Silk.NET.OpenGL.GLEnum.TextureMagFilter,
                        (int)Silk.NET.OpenGL.GLEnum.Nearest);
                }

                UploadTexture(compositorSource, sourcePixels);
                UploadTexture(compositorTarget, backgroundPixels);
                gl.BindFramebuffer(Silk.NET.OpenGL.GLEnum.Framebuffer, compositorFramebuffer);
                gl.FramebufferTexture2D(Silk.NET.OpenGL.GLEnum.Framebuffer,
                    Silk.NET.OpenGL.GLEnum.ColorAttachment0, Silk.NET.OpenGL.GLEnum.Texture2D, compositorTarget, 0);
                NuxTAssert(gl.CheckFramebufferStatus(Silk.NET.OpenGL.GLEnum.Framebuffer) ==
                           Silk.NET.OpenGL.GLEnum.FramebufferComplete,
                    "The NuxT composition target framebuffer must be complete.");
                InvokeNuxTViewportMethod(compositorViewport, "CompositeNuxTImage",
                    compositorSource, compositorFramebuffer, 2u, 2u);
                byte[] composedPixels = ReadGpuOutput(compositorTarget, 2, 2);
                for (int pixel = 0; pixel < 4; pixel++)
                {
                    int offset = pixel * 4;
                    NuxTAssert(Math.Abs(composedPixels[offset] - 128) <= 1 &&
                               composedPixels[offset + 1] == 0 &&
                               Math.Abs(composedPixels[offset + 2] - 127) <= 1 &&
                               composedPixels[offset + 3] == 255,
                        "NuxT composition must alpha-blend path-traced surfaces over the raster background.");
                }
                Silk.NET.OpenGL.GLEnum compositorError = gl.GetError();
                NuxTAssert(compositorError == Silk.NET.OpenGL.GLEnum.NoError,
                    $"NuxT framebuffer composition must leave OpenGL error-free: {compositorError}.");
            }
            finally
            {
                InvokeNuxTViewportMethod(compositorViewport, "StopNuxTRendering");
                gl.DeleteFramebuffer(compositorFramebuffer);
                gl.DeleteTexture(compositorTarget);
                gl.DeleteTexture(compositorSource);
                gl.DeleteVertexArray(compositorVao);
                gl.BindFramebuffer(Silk.NET.OpenGL.GLEnum.Framebuffer, 0);
            }

            const int formerBufferBoundaryTriangles = 200_000;
            var largeTriangles = new NuxTTriangle[formerBufferBoundaryTriangles];
            largeTriangles[0] = triangle;
            for (int i = 1; i < largeTriangles.Length; i++)
            {
                float x = 100f + i * 0.1f;
                Vector3 offset = new Vector3(x, 0f, 0f);
                largeTriangles[i] = triangle with
                {
                    A = triangle.A + offset,
                    B = triangle.B + offset,
                    C = triangle.C + offset
                };
            }
            var largeScene = new NuxTScene(largeTriangles, Vector3.Zero);
            NuxTAssert((long)largeScene.TriangleCount * 22 * 4 * sizeof(float) > 64L * 1024 * 1024,
                "The large-scene regression must exceed the former 64 MiB expanded triangle buffer.");
            NuxTAssert(renderer.Render(largeScene, camera, width, height, settings),
                $"GPU tracing must accept a scene larger than the former storage-buffer limit: {renderer.Status}.");
            actual = ReadGpuOutput(renderer.OutputTexture, width, height);
            expected = largeScene.RenderSample(camera, width, height, 0, settings.Seed, settings);
            for (int i = 0; i < actual.Length; i++)
                NuxTAssert(Math.Abs(actual[i] - expected[i]) <= 1,
                    $"Paged large-scene GPU pixels must match the CPU reference (byte {i}: {actual[i]} vs {expected[i]}).");
            NuxTAssert(gl.GetError() == Silk.NET.OpenGL.GLEnum.NoError, "The NuxT compute regression must leave OpenGL error-free.");

            var coverageTriangle = new NuxTTriangle(
                new Vector3(-0.02f, -2f, 0f), new Vector3(2f, -2f, 0f), new Vector3(-0.02f, 2f, 0f),
                Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
                Vector2.Zero, Vector2.UnitX, Vector2.UnitY,
                new NuxTMaterial(Vector3.One, new Vector3(3f, 1f, 0f), Unlit: true));
            var coverageScene = new NuxTScene([coverageTriangle], new Vector3(0.01f));
            const int denoiserWidth = 32;
            const int denoiserHeight = 24;
            var rawDenoiserSettings = new NuxTSettings(1, 0, 1f, 1f, 73);
            NuxTAssert(renderer.Render(coverageScene, camera, denoiserWidth, denoiserHeight,
                    rawDenoiserSettings, transparentBackground: true, completeImmediately: true),
                "The GPU denoiser comparison must render its raw reference frame.");
            byte[] rawGpuCoverage = ReadGpuOutput(renderer.OutputTexture, denoiserWidth, denoiserHeight);
            NuxTAssert(renderer.Render(coverageScene, camera, denoiserWidth, denoiserHeight,
                    rawDenoiserSettings with { DenoiserEnabled = true },
                    transparentBackground: true, completeImmediately: true) &&
                       renderer.Status.Contains("Denoised", StringComparison.Ordinal),
                $"Completed GPU output must execute OIDN: {renderer.Status}.");
            byte[] denoisedGpuCoverage = ReadGpuOutput(renderer.OutputTexture, denoiserWidth, denoiserHeight);
            NuxTAssert(Enumerable.Range(0, denoiserWidth * denoiserHeight).All(pixel =>
                    rawGpuCoverage[pixel * 4 + 3] == denoisedGpuCoverage[pixel * 4 + 3]) &&
                       gl.GetError() == Silk.NET.OpenGL.GLEnum.NoError,
                "GPU denoising must preserve primary-ray coverage and leave OpenGL error-free.");

            byte[] ReadGpuOutput(uint outputTexture, int renderWidth, int renderHeight)
            {
                byte[] pixels = new byte[renderWidth * renderHeight * 4];
                gl.BindTexture(Silk.NET.OpenGL.GLEnum.Texture2D, outputTexture);
                fixed (byte* pointer = pixels)
                    gl.GetTexImage(Silk.NET.OpenGL.GLEnum.Texture2D, 0, Silk.NET.OpenGL.GLEnum.Rgba,
                        Silk.NET.OpenGL.GLEnum.UnsignedByte, pointer);
                gl.BindTexture(Silk.NET.OpenGL.GLEnum.Texture2D, 0);
                return pixels;
            }
        }
        finally
        {
            if (window != null)
                glfw.DestroyWindow(window);
            glfw.Terminate();
        }
    }

    private static object? InvokeNuxTViewportMethod(Viewport viewport, string name, params object?[] arguments) =>
        typeof(Viewport).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewport, arguments);

    private static NuxTFrame? WaitForNuxTRender(NuxTProgressiveRenderer renderer)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        NuxTFrame? frame = null;
        while (DateTime.UtcNow < deadline)
        {
            if (renderer.TryTakeLatestFrame(out NuxTFrame latest))
                frame = latest;
            if (renderer.Status == "Complete")
                break;
            Thread.Sleep(5);
        }
        return frame;
    }

    private static IEnumerable<NuxTTriangle> WaitForNuxTBuild(ManualResetEventSlim entered,
        ManualResetEventSlim release, NuxTTriangle triangle)
    {
        entered.Set();
        release.Wait();
        yield return triangle;
    }

    private static NuxTTriangle NuxTTriangleAtZ(float z, NuxTMaterial material) => new(
        new Vector3(-2f, -2f, z), new Vector3(2f, -2f, z), new Vector3(0f, 2f, z),
        Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
        Vector2.Zero, Vector2.UnitX, Vector2.UnitY, material);

    private static void NuxTAssert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}