using System.Diagnostics;
using System.Numerics;
using GlmSharp;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.graphics;
using MineImatorSimplyRemade.core.mdl;
using MineImatorSimplyRemade.core.mdl.meshes;
using MineImatorSimplyRemadeNuxi.core.objs;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;
using Silk.NET.OpenGL;
using Shader = MineImatorSimplyRemade.core.mdl.Shader;
using static MineImatorSimplyRemade.core.graphics.MeshSkinning;

namespace MineImatorSimplyRemade.core.ui.Panels;

public partial class Viewport
{
    public static bool StartWithNuxT;

    private bool _useNuxT = StartWithNuxT;
    private readonly Dictionary<Viewport, NuxTViewOutput> _nuxtViewOutputs = new(ReferenceEqualityComparer.Instance);
    private long _nuxtTextureCacheFrame = long.MinValue;
    private int _nuxtTextureCacheBytes;
    private NuxTScene? _nuxtCachedScene;
    private int _nuxtCaptureStamp;
    private NuxTCaptureJob? _nuxtCapture;
    private NuxTSceneBuildCoordinator? _nuxtSceneBuilder;
    private readonly Dictionary<Mesh, NuxTGeometrySnapshot> _nuxtStaticGeometry = new(ReferenceEqualityComparer.Instance);
    private Shader? _nuxtCompositeShader;
    private string _nuxtPreparationStatus = "Idle";
    private int _nuxtCaptureInputKey = int.MinValue;
    private int _nuxtLightingStamp = int.MinValue;
    private long _nuxtEditQuietUntil;
    private bool _nuxtEditingPaused;
    private readonly Dictionary<uint, NuxTTexture> _nuxtTextureCache = new();
    private const int NuxTTextureCacheLimit = 96 * 1024 * 1024;
    private const int NuxTScanObjectBudget = 256;
    private const int NuxTTriangleBudget = 65536;
    private const double NuxTCaptureSliceMilliseconds = 4d;
    private const double NuxTEditQuietPeriodMilliseconds = 180d;

    private sealed class NuxTViewOutput
    {
        public NuxTProgressiveRenderer? Renderer;
        public NuxTComputeRenderer? ComputeRenderer;
        public bool ComputeUnavailable;
        public string ComputeStatus = "GPU tracing unavailable";
        public uint Texture;
        public int Width;
        public int Height;
        public bool IsCurrent;
        public int RequestKey = int.MinValue;
    }

    private sealed class NuxTCaptureJob(bool verificationOnly)
    {
        public readonly Stack<(SceneObject Object, SceneObject Root)> PendingObjects = new();
        public readonly List<NuxTMeshInstance> Meshes = new();
        public readonly List<NuxTSceneInstance> Instances = new();
        public readonly Dictionary<Mesh, long> GeometrySignatures = new(ReferenceEqualityComparer.Instance);
        public readonly List<NuxTLight> Lights = new();
        public readonly List<NuxTDirectionalLight> DirectionalLights = new();
        public HashCode Hash;
        public HashCode LightingHash;
        public bool VerificationOnly { get; } = verificationOnly;
        public bool IsScanning = true;
        public int MeshIndex;
        public int ExpectedStamp;
        public NuxTCaptureJob? CaptureToVerify;
        public int CapturedTriangleCount;
        public Vector3 Environment;
        public Vector3 Ambient;
        public NuxTMeshCapture? CurrentMesh;
    }

    private sealed record NuxTGeometrySnapshot(long Signature, NuxTTriangle[] Triangles);

    private sealed class NuxTMeshCapture(NuxTMeshInstance instance, VeldridVertex[] vertices,
        NuxTMaterial material, uint[]? indices, Matrix4x4 instanceTransform, object geometryKey,
        bool cacheGeometry, long geometrySignature)
    {
        public NuxTMeshInstance Instance { get; } = instance;
        public VeldridVertex[] Vertices { get; } = vertices;
        public NuxTMaterial Material { get; } = material;
        public uint[]? Indices { get; } = indices;
        public Matrix4x4 InstanceTransform { get; set; } = instanceTransform;
        public object GeometryKey { get; } = geometryKey;
        public bool CacheGeometry { get; } = cacheGeometry;
        public long GeometrySignature { get; } = geometrySignature;
        public List<NuxTTriangle> Triangles { get; } = new(Math.Max(0, (indices is { Length: > 0 }
            ? indices.Length
            : vertices.Length) / 3));
        public int TriangleIndex;
        public int TriangleCount => Indices is { Length: > 0 } ? Indices.Length : Vertices.Length;
    }

    private readonly record struct NuxTMeshInstance(SceneObject? Root, Mesh Mesh, mat4 Source, mat4 Model);

    private static bool ShouldUseNuxTRasterFallback(bool enabled, bool rendered, bool combined) =>
        enabled && rendered && combined;

    public string? GetNuxTStatus(Viewport renderView)
    {
        if (!_useNuxT)
            return null;
        if (renderView._viewportRenderMode != ViewportRenderMode.Rendered ||
            renderView._renderedPassMode != RenderedPassMode.Combined)
            return "Raster viewport (select Rendered mode)";

        int samples = PropertiesPanel?.NuxTSamples ?? 32;
        if (_nuxtViewOutputs.TryGetValue(renderView, out NuxTViewOutput? output))
        {
            if (output.ComputeRenderer is not null)
                return $"{output.ComputeRenderer.CompletedSamples}/{samples} spp  ·  {output.ComputeRenderer.Status}";
            if (output.Renderer is not null && _nuxtCachedScene is not null)
                return $"{output.Renderer.CompletedSamples}/{samples} spp  ·  {output.Renderer.Status}  ·  {output.ComputeStatus}";
        }

        return _nuxtPreparationStatus == "Idle" ? "Waiting to start" : _nuxtPreparationStatus;
    }

    public void DrawNuxTStatusOverlay(Viewport renderView, Vector2 imageMin)
    {
        string? status = GetNuxTStatus(renderView);
        if (status is null)
            return;

        ImGui.GetWindowDrawList().AddText(imageMin + new Vector2(8f, 8f) * UiTheme.Scale,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.9f)), $"NuxT  ·  {status}");
    }

    private void DrawNuxTRendererSelector(float itemY)
    {
        ImGui.SameLine();
        ImGui.SetCursorPosY(itemY);
        ImGui.SetNextItemWidth(108f * UiTheme.Scale);
        if (ImGui.BeginCombo("##vpRenderer", _useNuxT ? "NuxT" : "OpenGL"))
        {
            if (ImGui.Selectable("OpenGL", !_useNuxT))
            {
                _useNuxT = false;
                StopNuxTRendering();
            }
            if (ImGui.Selectable("NuxT", _useNuxT))
            {
                _useNuxT = true;
                _nuxtCaptureInputKey = int.MinValue;
            }
            ImGui.EndCombo();
        }

    }

    private void StopNuxTRendering()
    {
        foreach (NuxTViewOutput output in _nuxtViewOutputs.Values)
        {
            output.Renderer?.Dispose();
            output.ComputeRenderer?.Dispose();
            if (output.Texture != 0)
                Gl?.DeleteTexture(output.Texture);
        }
        _nuxtViewOutputs.Clear();
        _nuxtSceneBuilder?.Dispose();
        _nuxtSceneBuilder = null;
        _nuxtCapture = null;
        _nuxtCaptureInputKey = int.MinValue;
        _nuxtEditQuietUntil = 0;
        _nuxtEditingPaused = false;
        _nuxtCompositeShader?.Dispose();
        _nuxtCompositeShader = null;
        _nuxtPreparationStatus = "Idle";
    }

    private unsafe uint RenderNuxTImage(Camera activeCamera, CameraSceneObject? activeCameraObject, uint width,
        uint height, bool finalOutput = false, Viewport? renderView = null, bool progressiveExport = false)
    {
        if (!_useNuxT || Gl == null || width == 0 || height == 0)
            return 0;

        NuxTViewOutput output = GetNuxTViewOutput(renderView ?? this);
        Camera camera = BuildPreparedCamera(activeCamera, activeCameraObject);
        Vector3 position = ToNumerics(camera.Position);
        mat4 cameraWorld = camera.GetViewMatrix().Inverse;
        Vector3 forward = Vector3.Normalize(ToNumerics((cameraWorld * new vec4(0f, 0f, -1f, 0f)).xyz));
        Vector3 worldUp = Vector3.Normalize(ToNumerics((cameraWorld * new vec4(0f, 1f, 0f, 0f)).xyz));
        var tracerCamera = new NuxTCamera(position, forward, worldUp, camera.FovY).Normalized();
        PropertiesPanel? renderSettings = PropertiesPanel;
        var settings = new NuxTSettings(renderSettings?.NuxTSamples ?? 32,
            renderSettings?.NuxTMaxBounces ?? 4, renderSettings?.NuxTResolutionScale ?? 0.5f,
            renderSettings?.NuxTExposure ?? 1f, renderSettings?.NuxTSeed ?? 1,
            renderSettings?.NuxTDenoiserEnabled ?? false).Normalized();
        if (finalOutput)
            settings = settings with { ResolutionScale = 1f };
        int renderWidth = Math.Max(1, (int)MathF.Round(width * settings.ResolutionScale));
        int renderHeight = Math.Max(1, (int)MathF.Round(height * settings.ResolutionScale));

        Viewport owner = MainViewport ?? this;
        int inputKey = HashCode.Combine(Timeline.Instance?.CurrentFrame ?? 0);
        if (inputKey != _nuxtCaptureInputKey)
        {
            _nuxtCaptureInputKey = inputKey;
            _nuxtCapture = null;
            _nuxtSceneBuilder?.Cancel();
            _nuxtCachedScene = null;
            _nuxtCaptureStamp = int.MinValue;
            _nuxtLightingStamp = int.MinValue;
            InvalidateNuxTViewOutputs(disposeRenderers: true);
        }

        if (!finalOutput && ShouldPauseNuxTForInteraction(IsNuxTInteractionActive(), Stopwatch.GetTimestamp()))
            return 0;

        PollNuxTSceneBuild();
        EnsureNuxTSceneScan(owner, camera.Position);
        ProcessNuxTCaptureSlice(owner, camera.Position);
        PollNuxTSceneBuild();

        if (finalOutput)
            EnsureNuxTSceneReady(owner, camera.Position);
        if (_nuxtCapture is not null)
            return 0;

        if (_nuxtCachedScene is { } scene)
        {
            int requestKey = HashCode.Combine(scene.Fingerprint, tracerCamera, renderWidth, renderHeight,
                settings, true);
            if (requestKey != output.RequestKey)
            {
                output.RequestKey = requestKey;
                output.IsCurrent = false;
            }

            if (!output.ComputeUnavailable && NuxTComputeRenderer.IsSupported(Gl))
            {
                try
                {
                    output.ComputeRenderer ??= new NuxTComputeRenderer(Gl);
                    if (output.ComputeRenderer.Render(scene, tracerCamera, renderWidth, renderHeight, settings,
                            transparentBackground: true, completeImmediately: finalOutput && !progressiveExport))
                    {
                        output.Renderer?.Dispose();
                        output.Renderer = null;
                        output.IsCurrent = true;
                        _nuxtPreparationStatus = output.ComputeRenderer.Status;
                        return output.ComputeRenderer.OutputTexture;
                    }
                    output.ComputeStatus = output.ComputeRenderer.Status;
                }
                catch (Exception exception)
                {
                    output.ComputeUnavailable = true;
                    output.ComputeStatus = $"GPU fallback: {exception.Message}";
                    output.ComputeRenderer?.Dispose();
                    output.ComputeRenderer = null;
                }
            }
            else if (!output.ComputeUnavailable)
            {
                output.ComputeUnavailable = true;
                output.ComputeStatus = "CPU fallback: OpenGL 4.3 compute is unavailable";
            }

            output.Renderer ??= new NuxTProgressiveRenderer();
            if (finalOutput && !progressiveExport)
            {
                NuxTFrame frame = output.Renderer.RenderToCompletion(scene, tracerCamera, renderWidth, renderHeight,
                    settings, transparentBackground: true);
                UploadNuxTFrame(output, frame);
                output.IsCurrent = true;
            }
            else
            {
                output.Renderer.StartOrUpdate(scene, tracerCamera, renderWidth, renderHeight, settings,
                    transparentBackground: true);
                if (output.Renderer.TryTakeLatestFrame(out NuxTFrame frame))
                {
                    UploadNuxTFrame(output, frame);
                    output.IsCurrent = true;
                }
            }
            _nuxtPreparationStatus = _nuxtSceneBuilder?.Status is "Building scene" ? "Building scene" :
                _nuxtCapture == null && output.IsCurrent ? output.Renderer.Status : _nuxtPreparationStatus;
        }

        return _nuxtCachedScene is not null && output.IsCurrent ? output.Texture : 0;
    }

    public bool TryGetNuxTExportProgress(Viewport renderView, out int completedSamples, out int totalSamples,
        out bool complete)
    {
        completedSamples = 0;
        totalSamples = 0;
        complete = true;
        if (!_useNuxT || !_nuxtViewOutputs.TryGetValue(renderView, out NuxTViewOutput? output))
            return false;

        string status;
        totalSamples = Math.Max(1, PropertiesPanel?.NuxTSamples ?? 32);
        if (output.Renderer is { } renderer)
        {
            completedSamples = renderer.CompletedSamples;
            status = renderer.Status;
        }
        else if (output.ComputeRenderer is { } computeRenderer)
        {
            completedSamples = computeRenderer.CompletedSamples;
            status = computeRenderer.Status;
        }
        else
            return false;

        if (status.StartsWith("Error:", StringComparison.Ordinal))
            throw new InvalidOperationException($"NuxT path tracing failed: {status}");

        complete = completedSamples >= totalSamples && status.StartsWith("Complete", StringComparison.Ordinal);
        return true;
    }

    public void CancelNuxTExportCapture()
    {
        Viewport owner = MainViewport ?? this;
        if (!owner._nuxtViewOutputs.TryGetValue(this, out NuxTViewOutput? output))
            return;

        output.Renderer?.Dispose();
        output.Renderer = null;
        output.ComputeRenderer?.Dispose();
        output.ComputeRenderer = null;
        if (output.Texture != 0)
            owner.Gl?.DeleteTexture(output.Texture);
        output.Texture = 0;
        output.Width = 0;
        output.Height = 0;
        output.IsCurrent = false;
        output.RequestKey = int.MinValue;
    }

    private NuxTViewOutput GetNuxTViewOutput(Viewport view)
    {
        if (_nuxtViewOutputs.TryGetValue(view, out NuxTViewOutput? output))
            return output;
        output = new NuxTViewOutput();
        _nuxtViewOutputs.Add(view, output);
        return output;
    }

    private void InvalidateNuxTViewOutputs(bool disposeRenderers)
    {
        foreach (NuxTViewOutput output in _nuxtViewOutputs.Values)
        {
            if (disposeRenderers)
            {
                output.Renderer?.Dispose();
                output.Renderer = null;
                output.ComputeRenderer?.Dispose();
                output.ComputeRenderer = null;
            }
            output.IsCurrent = false;
            output.RequestKey = int.MinValue;
        }
    }

    private bool IsNuxTInteractionActive() =>
        Gizmo?.Editing == true || ImGui.IsAnyItemActive() || (Timeline.Instance?.IsPlaying ?? false);

    private bool ShouldPauseNuxTForInteraction(bool interactionActive, long timestamp)
    {
        if (interactionActive)
        {
            _nuxtEditQuietUntil = timestamp + (long)(Stopwatch.Frequency * NuxTEditQuietPeriodMilliseconds / 1000d);
            if (!_nuxtEditingPaused)
            {
                _nuxtEditingPaused = true;
                _nuxtCapture = null;
                if (_nuxtCachedScene is null)
                {
                    _nuxtSceneBuilder?.Cancel();
                    _nuxtCaptureStamp = int.MinValue;
                    _nuxtLightingStamp = int.MinValue;
                }
                InvalidateNuxTViewOutputs(disposeRenderers: false);
            }
            _nuxtPreparationStatus = "Waiting for edits";
            return true;
        }

        _nuxtEditingPaused = false;
        return timestamp < _nuxtEditQuietUntil;
    }

    private void EnsureNuxTSceneReady(Viewport owner, vec3 cameraPosition)
    {
        DateTime deadline = DateTime.UtcNow.AddMinutes(15);
        while (_nuxtCachedScene is null)
        {
            PollNuxTSceneBuild();
            if (_nuxtPreparationStatus.StartsWith("Error:", StringComparison.Ordinal))
                throw new InvalidOperationException($"NuxT scene preparation failed: {_nuxtPreparationStatus}");
            if (_nuxtCapture is null)
                EnsureNuxTSceneScan(owner, cameraPosition);
            ProcessNuxTCaptureSlice(owner, cameraPosition);
            PollNuxTSceneBuild();
            if (_nuxtPreparationStatus.StartsWith("Error:", StringComparison.Ordinal))
                throw new InvalidOperationException($"NuxT scene preparation failed: {_nuxtPreparationStatus}");
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("NuxT final scene preparation did not finish within 15 minutes.");
            Thread.Sleep(1);
        }
    }

    private void CompositeNuxTImage(uint sourceTexture, uint targetFramebuffer, uint width, uint height)
    {
        if (Gl == null || sourceTexture == 0 || targetFramebuffer == 0 || width == 0 || height == 0 ||
            _edgeVao == 0)
            return;
        CheckNuxTCompositeError("before composition");
        if (_nuxtCompositeShader == null)
        {
            _nuxtCompositeShader = new Shader(Gl);
            _nuxtCompositeShader.CompileShader("edge.vert", "nuxt_composite.frag");
            Gl.GetProgram(_nuxtCompositeShader.ShaderProgram, GLEnum.LinkStatus, out int linked);
            if (linked == 0)
                throw new InvalidOperationException("NuxT compositor shader failed to link.");
            CheckNuxTCompositeError("shader initialization");
        }

        bool depthTest = Gl.IsEnabled(GLEnum.DepthTest);
        bool cullFace = Gl.IsEnabled(GLEnum.CullFace);
        bool blend = Gl.IsEnabled(GLEnum.Blend);
        int drawFramebuffer = Gl.GetInteger(GLEnum.DrawFramebufferBinding);
        Span<int> viewport = stackalloc int[4];
        Gl.GetInteger(GLEnum.Viewport, viewport);
        int activeTexture = Gl.GetInteger(GLEnum.ActiveTexture);
        Gl.ActiveTexture(GLEnum.Texture0);
        int texture0 = Gl.GetInteger(GLEnum.TextureBinding2D);
        Gl.ActiveTexture((GLEnum)activeTexture);
        int texture = Gl.GetInteger(GLEnum.TextureBinding2D);
        int program = Gl.GetInteger(GLEnum.CurrentProgram);
        int vertexArray = Gl.GetInteger(GLEnum.VertexArrayBinding);
        int sourceRgb = Gl.GetInteger(GLEnum.BlendSrcRgb);
        int destinationRgb = Gl.GetInteger(GLEnum.BlendDstRgb);
        int sourceAlpha = Gl.GetInteger(GLEnum.BlendSrcAlpha);
        int destinationAlpha = Gl.GetInteger(GLEnum.BlendDstAlpha);
        int equationRgb = Gl.GetInteger(GLEnum.BlendEquationRgb);
        int equationAlpha = Gl.GetInteger(GLEnum.BlendEquationAlpha);
        try
        {
            Gl.BindFramebuffer(GLEnum.DrawFramebuffer, targetFramebuffer);
            Gl.DrawBuffer(GLEnum.ColorAttachment0);
            Gl.Viewport(0, 0, width, height);
            Gl.Disable(GLEnum.DepthTest);
            Gl.Disable(GLEnum.CullFace);
            Gl.Enable(GLEnum.Blend);
            Gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);
            Gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha,
                GLEnum.One, GLEnum.OneMinusSrcAlpha);
            CheckNuxTCompositeError("framebuffer and blend setup");
            Gl.UseProgram(_nuxtCompositeShader.ShaderProgram);
            Gl.ActiveTexture(GLEnum.Texture0);
            Gl.BindTexture(GLEnum.Texture2D, sourceTexture);
            Gl.Uniform1(_nuxtCompositeShader.GetUniformLocation("uNuxT"), 0);
            Gl.Uniform2(_nuxtCompositeShader.GetUniformLocation("uOutputSize"), (float)width, (float)height);
            Gl.BindVertexArray(_edgeVao);
            CheckNuxTCompositeError("shader and texture setup");
            Gl.DrawArrays(GLEnum.Triangles, 0, 3);
            CheckNuxTCompositeError("fullscreen draw");
        }
        finally
        {
            Gl.BindVertexArray((uint)vertexArray);
            Gl.UseProgram((uint)program);
            Gl.ActiveTexture(GLEnum.Texture0);
            Gl.BindTexture(GLEnum.Texture2D, (uint)texture0);
            Gl.ActiveTexture((GLEnum)activeTexture);
            Gl.BindTexture(GLEnum.Texture2D, (uint)texture);
            Gl.BlendEquationSeparate((GLEnum)equationRgb, (GLEnum)equationAlpha);
            Gl.BlendFuncSeparate((GLEnum)sourceRgb, (GLEnum)destinationRgb,
                (GLEnum)sourceAlpha, (GLEnum)destinationAlpha);
            if (depthTest) Gl.Enable(GLEnum.DepthTest); else Gl.Disable(GLEnum.DepthTest);
            if (cullFace) Gl.Enable(GLEnum.CullFace); else Gl.Disable(GLEnum.CullFace);
            if (blend) Gl.Enable(GLEnum.Blend); else Gl.Disable(GLEnum.Blend);
            Gl.BindFramebuffer(GLEnum.DrawFramebuffer, (uint)drawFramebuffer);
            Gl.Viewport(viewport[0], viewport[1], (uint)viewport[2], (uint)viewport[3]);
            CheckNuxTCompositeError("state restoration");
        }
    }

    private void CheckNuxTCompositeError(string stage)
    {
        if (Gl == null)
            return;
        GLEnum error = Gl.GetError();
        if (error != GLEnum.NoError)
            throw new InvalidOperationException($"NuxT composition {stage} failed: {error}.");
    }

    private void EnsureNuxTSceneScan(Viewport owner, vec3 cameraPosition)
    {
        if (_nuxtCapture is not null)
            return;
        if (_nuxtCachedScene is null && _nuxtSceneBuilder?.Status == "Building scene")
        {
            _nuxtPreparationStatus = "Building scene";
            return;
        }

        _nuxtCapture = CreateNuxTScan(owner, cameraPosition, verificationOnly: false);
    }

    private NuxTCaptureJob CreateNuxTScan(Viewport owner, vec3 cameraPosition, bool verificationOnly)
    {
        var job = new NuxTCaptureJob(verificationOnly);
        job.Hash.Add(Timeline.Instance?.CurrentFrame ?? 0);
        job.Hash.Add(Mesh.EffectTime);

        float[] background = PropertiesPanel?.BackgroundColor ?? [0.18f, 0.18f, 0.18f, 1f];
        job.Environment = new Vector3(background[0], background[1], background[2]) * 0.08f;
        job.DirectionalLights.Add(new NuxTDirectionalLight(new Vector3(1f, 1f, 1f),
            ToNumerics(Mesh.GlobalFillLightColor) * Mesh.GlobalFillLightStrength, Mesh.MainFillLightCastsShadows));
        job.DirectionalLights.Add(new NuxTDirectionalLight(ToNumerics(Mesh.GlobalSunFillLightDirection),
            ToNumerics(Mesh.GlobalSunFillLightColor) * Mesh.GlobalSunFillLightStrength, Mesh.SunFillLightCastsShadows));
        job.DirectionalLights.Add(new NuxTDirectionalLight(ToNumerics(Mesh.GlobalMoonFillLightDirection),
            ToNumerics(Mesh.GlobalMoonFillLightColor) * Mesh.GlobalMoonFillLightStrength, Mesh.MoonFillLightCastsShadows));
        job.Ambient = ToNumerics(Mesh.GlobalAmbientColor) * Mesh.GlobalAmbientStrength;

        if (background is { Length: >= 3 })
        {
            job.LightingHash.Add(background[0]);
            job.LightingHash.Add(background[1]);
            job.LightingHash.Add(background[2]);
        }
        job.LightingHash.Add(Mesh.GlobalAmbientColor); job.LightingHash.Add(Mesh.GlobalAmbientStrength);
        job.LightingHash.Add(Mesh.GlobalFillLightColor); job.LightingHash.Add(Mesh.GlobalFillLightStrength); job.LightingHash.Add(Mesh.MainFillLightCastsShadows);
        job.LightingHash.Add(Mesh.GlobalSunFillLightColor); job.LightingHash.Add(Mesh.GlobalSunFillLightDirection);
        job.LightingHash.Add(Mesh.GlobalSunFillLightStrength); job.LightingHash.Add(Mesh.SunFillLightCastsShadows);
        job.LightingHash.Add(Mesh.GlobalMoonFillLightColor); job.LightingHash.Add(Mesh.GlobalMoonFillLightDirection);
        job.LightingHash.Add(Mesh.GlobalMoonFillLightStrength); job.LightingHash.Add(Mesh.MoonFillLightCastsShadows);

        for (int i = owner.SceneObjects.Count - 1; i >= 0; i--)
        {
            SceneObject root = owner.SceneObjects[i];
            job.PendingObjects.Push((root, root));
        }
        if (owner._groundPlane is { } ground && owner.GroundPlaneVisible)
        {
            mat4 groundModel = mat4.Identity;
            job.Hash.Add(ground.GeometryVersion);
            job.Hash.Add(groundModel);
            AddNuxTMaterialHash(ref job.Hash, ground, includeTextureOffset: false);
            if (!verificationOnly && ground.Vertices.Count >= 3 && ground.Alpha > 0f)
                job.Meshes.Add(new NuxTMeshInstance(null, ground, mat4.Identity, groundModel));
        }
        _nuxtPreparationStatus = verificationOnly ? "Verifying scene snapshot" : "Checking scene";
        return job;
    }

    private void ProcessNuxTCaptureSlice(Viewport owner, vec3 cameraPosition)
    {
        if (_nuxtCapture is not { } job)
            return;
        long deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * NuxTCaptureSliceMilliseconds / 1000d);
        if (job.IsScanning)
        {
            ScanNuxTSceneSlice(owner, cameraPosition, job, deadline);
            if (job.PendingObjects.Count == 0)
            {
                if (!job.VerificationOnly)
                    PruneNuxTGeometryCache(job);
                CompleteNuxTScan(owner, cameraPosition, job);
            }
            return;
        }

        CaptureNuxTMeshSlice(owner, cameraPosition, job, deadline);
    }

    private void ScanNuxTSceneSlice(Viewport owner, vec3 cameraPosition, NuxTCaptureJob job, long deadline)
    {
        owner.ResetModifierEvaluation();
        using var evaluation = SceneObject.BeginRenderEvaluation();
        int visited = 0;
        while (job.PendingObjects.Count > 0 && visited++ < NuxTScanObjectBudget && Stopwatch.GetTimestamp() < deadline)
        {
            (SceneObject obj, SceneObject root) = job.PendingObjects.Pop();
            bool visible = obj.GetEffectiveVisibility();
            job.Hash.Add(obj.GetHashCode());
            job.Hash.Add(visible);
            if (!visible)
                continue;

            if (owner.IsObjectVisible(obj))
            {
                mat4 source = GetRenderableWorldMatrix(obj, cameraPosition);
                if (obj is LightSceneObject)
                    job.LightingHash.Add(source);
                else
                    job.Hash.Add(source);
                mat4[] models = owner.GetModifiedWorldMatrices(obj, source, cameraPosition).ToArray();
                if (obj.Visuals.Count > 0)
                    foreach (mat4 model in models)
                        job.Hash.Add(model);

                if (obj is LightSceneObject light)
                {
                    job.LightingHash.Add(light.Type); job.LightingHash.Add(light.LightColor); job.LightingHash.Add(light.LightEnergy);
                    job.LightingHash.Add(light.LightIndirectEnergy); job.LightingHash.Add(light.LightRange); job.LightingHash.Add(light.LightShadowEnabled);
                    job.LightingHash.Add(light.LightSpotAngle); job.LightingHash.Add(light.LightSpotBlend);
                    Vector3 lightPosition = ToNumerics((source * new vec4(vec3.Zero, 1f)).xyz);
                    Vector3 lightDirection = TransformDirection(source, vec3.UnitZ);
                    float outerCosine = light.Type == LightType.Spot
                        ? MathF.Cos(Math.Clamp(light.LightSpotAngle, 0f, 180f) * MathF.PI / 360f)
                        : -2f;
                    float innerCosine = light.Type == LightType.Spot
                        ? MathF.Cos(Math.Clamp(light.LightSpotAngle - light.LightSpotBlend, 0f, 180f) * MathF.PI / 360f)
                        : -2f;
                    job.Lights.Add(new NuxTLight(lightPosition, lightDirection, ToNumerics(light.LightColor.xyz),
                        light.LightEnergy, light.LightRange, innerCosine, outerCosine, light.LightShadowEnabled));
                }

                foreach (Mesh mesh in obj.Visuals)
                {
                    job.Hash.Add(mesh.GetHashCode());
                    job.Hash.Add(mesh.GeometryVersion);
                    job.Hash.Add(mesh.Vertices.Count);
                    job.Hash.Add(mesh.Indices?.Length ?? 0);
                    mesh.AddBendStateHash(ref job.Hash);
                    AddNuxTMaterialHash(ref job.Hash, mesh);
                    if (job.VerificationOnly || mesh.PickOnly || mesh.DepthTestDisabled ||
                        mesh.Vertices.Count < 3 || mesh.Alpha <= 0f)
                        continue;
                    foreach (mat4 model in models)
                        job.Meshes.Add(new NuxTMeshInstance(root, mesh, source, model));
                }
            }

            for (int child = obj.Children.Count - 1; child >= 0; child--)
                job.PendingObjects.Push((obj.Children[child], root));
        }
    }

    private static void AddNuxTMaterialHash(ref HashCode hash, Mesh mesh, bool includeTextureOffset = true)
    {
        hash.Add(mesh.RenderAlbedo);
        hash.Add(mesh.Alpha);
        hash.Add(mesh.AlphaMode);
        hash.Add(mesh.AlphaClipThreshold);
        hash.Add(mesh.TextureId);
        hash.Add(mesh.AlphaMaskTextureId);
        hash.Add(mesh.EmissionTexture);
        if (includeTextureOffset)
            hash.Add(mesh.TextureOffset);
        hash.Add(mesh.TextureRepeat);
        hash.Add(mesh.TextureMirror);
        hash.Add(mesh.EmissionEnabled);
        hash.Add(mesh.EmissionIndirectOnly);
        hash.Add(mesh.EmissionColor);
        hash.Add(mesh.EmissionEnergy);
        hash.Add(mesh.Metallic);
        hash.Add(mesh.Roughness);
        hash.Add(mesh.PbrEnabled);
        hash.Add(mesh.Unlit);
        hash.Add(mesh.BlendColor);
        hash.Add(mesh.MixColor);
        hash.Add(mesh.ColorAdjustments);
        hash.Add(mesh.AnimationKey);
        hash.Add(mesh.WindActive);
        hash.Add(mesh.WindKind);
    }

    private void CompleteNuxTScan(Viewport owner, vec3 cameraPosition, NuxTCaptureJob job)
    {
        int stamp = job.Hash.ToHashCode();
        int lightingStamp = job.LightingHash.ToHashCode();
        if (job.VerificationOnly)
        {
            NuxTCaptureJob capture = job.CaptureToVerify!;
            _nuxtCapture = null;
            if (stamp != capture.ExpectedStamp)
            {
                _nuxtCapture = CreateNuxTScan(owner, cameraPosition, verificationOnly: false);
                _nuxtPreparationStatus = "Scene changed; restarting capture";
                return;
            }

            _nuxtCaptureStamp = stamp;
            _nuxtLightingStamp = lightingStamp;
            capture.Lights.Clear();
            capture.Lights.AddRange(job.Lights);
            capture.DirectionalLights.Clear();
            capture.DirectionalLights.AddRange(job.DirectionalLights);
            capture.Environment = job.Environment;
            capture.Ambient = job.Ambient;
            _nuxtSceneBuilder ??= new NuxTSceneBuildCoordinator();
            _nuxtSceneBuilder.StartInstances(capture.Instances, capture.Environment, capture.Lights,
                capture.DirectionalLights, capture.Ambient);
            _nuxtPreparationStatus = _nuxtSceneBuilder.Status;
            return;
        }

        bool buildInProgress = _nuxtSceneBuilder?.Status == "Building scene";
        if (stamp == _nuxtCaptureStamp && (_nuxtCachedScene != null || buildInProgress ||
                                            _nuxtSceneBuilder?.Status.StartsWith("Error:", StringComparison.Ordinal) == true))
        {
            if (_nuxtCachedScene is { } scene && lightingStamp != _nuxtLightingStamp)
            {
                _nuxtCachedScene = scene.WithLighting(job.Lights, job.DirectionalLights, job.Environment, job.Ambient);
                _nuxtLightingStamp = lightingStamp;
                InvalidateNuxTViewOutputs(disposeRenderers: false);
            }
            _nuxtCapture = null;
            _nuxtPreparationStatus = buildInProgress ? "Building scene" :
                _nuxtCachedScene != null ? "Checking scene" : _nuxtSceneBuilder!.Status;
            return;
        }

        _nuxtSceneBuilder?.Cancel();
        _nuxtCachedScene = null;
        _nuxtLightingStamp = int.MinValue;
        InvalidateNuxTViewOutputs(disposeRenderers: true);
        job.ExpectedStamp = stamp;
        job.IsScanning = false;
        _nuxtPreparationStatus = "Capturing geometry";
    }

    private void CaptureNuxTMeshSlice(Viewport owner, vec3 cameraPosition, NuxTCaptureJob job, long deadline)
    {
        int remainingTriangles = NuxTTriangleBudget;
        while (remainingTriangles > 0 && Stopwatch.GetTimestamp() < deadline)
        {
            if (job.CurrentMesh is null)
            {
                if (job.MeshIndex >= job.Meshes.Count)
                {
                    BeginNuxTVerification(owner, cameraPosition, job);
                    return;
                }

                NuxTMeshInstance instance = job.Meshes[job.MeshIndex];
                Mesh mesh = instance.Mesh;
                if (mesh.Vertices.Count < 3 || mesh.Alpha <= 0f)
                {
                    job.MeshIndex++;
                    continue;
                }

                bool cacheGeometry = IsStaticNuxTGeometry(mesh);
                owner.ResetModifierEvaluation();
                using (SceneObject.BeginRenderEvaluation())
                {
                    if (mesh.IsSkinned && instance.Root is { } root)
                        UpdateBoneMatrices(mesh, instance.Source, BuildBoneDictionary(root));
                    NuxTMaterial material = CreateNuxTMaterial(instance);
                    long geometrySignature = cacheGeometry ? GetNuxTGeometrySignature(mesh, job) : 0;
                    if (cacheGeometry && _nuxtStaticGeometry.TryGetValue(mesh, out NuxTGeometrySnapshot? snapshot) &&
                        snapshot.Signature == geometrySignature)
                    {
                        job.Instances.Add(new NuxTSceneInstance(snapshot.Triangles, ToNumerics(instance.Model), mesh,
                            material, CacheAcceleration: true));
                        job.CapturedTriangleCount += snapshot.Triangles.Length;
                        job.MeshIndex++;
                        continue;
                    }

                    VeldridVertex[] vertices = VeldridSceneRenderer.BuildVertices(mesh);
                    if (vertices.Length < 3)
                    {
                        job.MeshIndex++;
                        continue;
                    }

                    Matrix4x4 instanceTransform = ToNumerics(instance.Model);
                    object geometryKey = cacheGeometry ? mesh : new object();
                    job.CurrentMesh = new NuxTMeshCapture(instance, vertices, material, mesh.Indices?.ToArray(),
                        instanceTransform, geometryKey, cacheGeometry, geometrySignature);
                }
            }

            NuxTMeshCapture current = job.CurrentMesh;
            Mesh sourceMesh = current.Instance.Mesh;
            int triangleLimit = current.TriangleCount;
            while (current.TriangleIndex + 2 < triangleLimit && remainingTriangles > 0 && Stopwatch.GetTimestamp() < deadline)
            {
                int i = current.TriangleIndex;
                int a = current.Indices is { Length: > 0 } ? checked((int)current.Indices[i]) : i;
                int b = current.Indices is { Length: > 0 } ? checked((int)current.Indices[i + 1]) : i + 1;
                int c = current.Indices is { Length: > 0 } ? checked((int)current.Indices[i + 2]) : i + 2;
                current.TriangleIndex += 3;
                remainingTriangles--;
                if ((uint)a >= current.Vertices.Length || (uint)b >= current.Vertices.Length ||
                    (uint)c >= current.Vertices.Length)
                    continue;

                VeldridVertex vertexA = current.Vertices[a];
                VeldridVertex vertexB = current.Vertices[b];
                VeldridVertex vertexC = current.Vertices[c];
                Vector3 positionA = ToNumerics(vertexA.Position);
                Vector3 positionB = ToNumerics(vertexB.Position);
                Vector3 positionC = ToNumerics(vertexC.Position);
                if (sourceMesh.WindActive)
                {
                    Vector3 worldA = TransformPosition(current.Instance.Model, vertexA.Position, vertexA.TexCoord, sourceMesh);
                    Vector3 worldB = TransformPosition(current.Instance.Model, vertexB.Position, vertexB.TexCoord, sourceMesh);
                    Vector3 worldC = TransformPosition(current.Instance.Model, vertexC.Position, vertexC.TexCoord, sourceMesh);
                    if (Matrix4x4.Invert(current.InstanceTransform, out Matrix4x4 inverseModel))
                    {
                        positionA = Vector3.Transform(worldA, inverseModel);
                        positionB = Vector3.Transform(worldB, inverseModel);
                        positionC = Vector3.Transform(worldC, inverseModel);
                    }
                    else
                    {
                        positionA = worldA;
                        positionB = worldB;
                        positionC = worldC;
                        current.InstanceTransform = Matrix4x4.Identity;
                    }
                }
                current.Triangles.Add(new NuxTTriangle(positionA, positionB, positionC,
                    ToNumerics(vertexA.Normal), ToNumerics(vertexB.Normal), ToNumerics(vertexC.Normal),
                    ToNumerics(vertexA.TexCoord), ToNumerics(vertexB.TexCoord), ToNumerics(vertexC.TexCoord),
                    current.Material));
            }

            if (current.TriangleIndex + 2 >= triangleLimit)
            {
                NuxTTriangle[] triangles = current.Triangles.ToArray();
                job.Instances.Add(new NuxTSceneInstance(triangles, current.InstanceTransform, current.GeometryKey,
                    current.Material, current.CacheGeometry));
                job.CapturedTriangleCount += triangles.Length;
                if (current.CacheGeometry)
                    _nuxtStaticGeometry[sourceMesh] = new NuxTGeometrySnapshot(current.GeometrySignature, triangles);
                job.CurrentMesh = null;
                job.MeshIndex++;
            }
        }

        if (job.CurrentMesh is null && job.MeshIndex >= job.Meshes.Count)
            BeginNuxTVerification(owner, cameraPosition, job);
        else
            _nuxtPreparationStatus = $"Capturing geometry ({job.CapturedTriangleCount:N0} triangles)";
    }

    private void BeginNuxTVerification(Viewport owner, vec3 cameraPosition, NuxTCaptureJob capture)
    {
        NuxTCaptureJob verification = CreateNuxTScan(owner, cameraPosition, verificationOnly: true);
        verification.ExpectedStamp = capture.ExpectedStamp;
        verification.CaptureToVerify = capture;
        _nuxtCapture = verification;
        _nuxtPreparationStatus = "Verifying scene snapshot";
    }

    private void PollNuxTSceneBuild()
    {
        if (_nuxtSceneBuilder is null)
            return;
        if (_nuxtSceneBuilder.TryTakeCompleted(out NuxTScene scene))
        {
            _nuxtCachedScene = scene;
            InvalidateNuxTViewOutputs(disposeRenderers: true);
            _nuxtPreparationStatus = "Tracing";
        }
        else if (_nuxtSceneBuilder.Status.StartsWith("Error:", StringComparison.Ordinal))
            _nuxtPreparationStatus = _nuxtSceneBuilder.Status;
    }

    private Vector3 TransformPosition(mat4 model, vec3 position, vec2 uv, Mesh mesh)
    {
        Vector3 result = ToNumerics((model * new vec4(position, 1f)).xyz);
        if (mesh.WindActive)
            result += ToNumerics(SnapWindOffset(ToGlm(result), uv, mesh.WindKind));
        return result;
    }

    private static Vector3 TransformNormal(mat4 model, vec3 normal)
    {
        Vector3 transformed = ToNumerics((model * new vec4(normal, 0f)).xyz);
        return transformed.LengthSquared() > 1e-12f ? Vector3.Normalize(transformed) : Vector3.UnitY;
    }

    private static Vector3 TransformDirection(mat4 model, vec3 direction)
    {
        Vector3 transformed = ToNumerics((model * new vec4(direction, 0f)).xyz);
        return transformed.LengthSquared() > 1e-12f ? Vector3.Normalize(transformed) : Vector3.UnitZ;
    }

    private static bool IsStaticNuxTGeometry(Mesh mesh) =>
        !mesh.IsSkinned && !mesh.WindActive && !mesh.HasClothGeometry && !mesh.HasShapeKeys && mesh.GpuBend is null;

    private static long GetNuxTGeometrySignature(Mesh mesh, NuxTCaptureJob job)
    {
        if (job.GeometrySignatures.TryGetValue(mesh, out long cachedSignature))
            return cachedSignature;

        ulong hash = 14695981039346656037UL;
        void Add(uint value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        Add((uint)mesh.GeometryVersion);
        Add((uint)mesh.Vertices.Count);
        Add((uint)mesh.Normals.Count);
        Add((uint)mesh.TexCoords.Count);
        Add((uint)(mesh.Indices?.Length ?? 0));
        foreach (vec3 vertex in mesh.Vertices)
        {
            Add(BitConverter.SingleToUInt32Bits(vertex.x));
            Add(BitConverter.SingleToUInt32Bits(vertex.y));
            Add(BitConverter.SingleToUInt32Bits(vertex.z));
        }
        foreach (vec3 normal in mesh.Normals)
        {
            Add(BitConverter.SingleToUInt32Bits(normal.x));
            Add(BitConverter.SingleToUInt32Bits(normal.y));
            Add(BitConverter.SingleToUInt32Bits(normal.z));
        }
        foreach (vec2 uv in mesh.TexCoords)
        {
            Add(BitConverter.SingleToUInt32Bits(uv.x));
            Add(BitConverter.SingleToUInt32Bits(uv.y));
        }
        if (mesh.Indices is { } indices)
            foreach (uint index in indices)
                Add(index);

        long signature = unchecked((long)hash);
        job.GeometrySignatures.Add(mesh, signature);
        return signature;
    }

    private void PruneNuxTGeometryCache(NuxTCaptureJob job)
    {
        var activeMeshes = new HashSet<Mesh>(job.Meshes.Select(instance => instance.Mesh)
                .Where(IsStaticNuxTGeometry),
            ReferenceEqualityComparer.Instance);
        foreach (Mesh mesh in _nuxtStaticGeometry.Keys.ToArray())
            if (!activeMeshes.Contains(mesh))
                _nuxtStaticGeometry.Remove(mesh);
        _nuxtSceneBuilder?.PruneGeometryCache(activeMeshes.Cast<object>().ToHashSet(ReferenceEqualityComparer.Instance));
    }

    private NuxTMaterial CreateNuxTMaterial(NuxTMeshInstance instance)
    {
        Mesh mesh = instance.Mesh;
        NuxTTexture? albedoTexture = ReadNuxTTexture(mesh.TextureId);
        NuxTTexture? emissionTexture = mesh.EmissionEnabled
            ? ReadNuxTTexture(mesh.EmissionTexture != 0 ? mesh.EmissionTexture : mesh.TextureId)
            : null;
        NuxTTexture? alphaTexture = ReadNuxTTexture(mesh.AlphaMaskTextureId);
        var colorAdjustments = mesh.ColorAdjustments;
        float textureFrameScale = 1f;
        float textureFrameOffset = 0f;
        if (!string.IsNullOrEmpty(mesh.AnimationKey) &&
            TerrainAtlas.AnimatedTextures.TryGetValue(mesh.AnimationKey, out AnimatedTextureInfo animation) &&
            animation.TotalFrames > 0)
        {
            textureFrameScale = 1f / animation.TotalFrames;
            int frameIndex = Mesh.GetAnimatedTextureFrameIndex(animation,
                Mesh.AnimatedTextureTimelineTimeSeconds ?? 0d);
            textureFrameOffset = frameIndex * textureFrameScale;
        }
        return new NuxTMaterial(
            ToNumerics(mesh.RenderAlbedo),
            mesh.EmissionEnabled ? ToNumerics(mesh.EmissionColor) * mesh.EmissionEnergy : Vector3.Zero,
            mesh.PbrEnabled ? mesh.Metallic : 0f,
            mesh.PbrEnabled ? mesh.Roughness : 1f,
            mesh.Alpha,
            mesh.AlphaClipThreshold,
            mesh.AlphaMode == MaterialAlphaMode.Clip,
            albedoTexture,
            emissionTexture,
            instance.Root is null ? Vector2.Zero : ToNumerics(mesh.TextureOffset),
            ToNumerics(mesh.TextureRepeat),
            mesh.TextureMirror.x,
            mesh.TextureMirror.y,
            alphaTexture,
            mesh.EmissionIndirectOnly,
            textureFrameScale,
            textureFrameOffset,
            mesh.Unlit,
            ToNumerics(colorAdjustments.MultiplyHsv),
            ToNumerics(colorAdjustments.AddRgb),
            ToNumerics(colorAdjustments.AddHsv),
            ToNumerics(colorAdjustments.SubtractRgb),
            ToNumerics(colorAdjustments.SubtractHsv),
            !colorAdjustments.IsNeutral);
    }

    private unsafe NuxTTexture? ReadNuxTTexture(uint textureId)
    {
        if (textureId == 0)
            return null;
        long timelineFrame = Timeline.Instance?.CurrentFrame ?? 0;
        if (_nuxtTextureCacheFrame != timelineFrame)
        {
            _nuxtTextureCache.Clear();
            _nuxtTextureCacheBytes = 0;
            _nuxtTextureCacheFrame = timelineFrame;
        }
        if (_nuxtTextureCache.TryGetValue(textureId, out NuxTTexture? cached))
            return cached;
        if (!Gl.IsTexture(textureId))
            return null;

        int previousTexture = Gl.GetInteger(GLEnum.TextureBinding2D);
        int previousPackAlignment = Gl.GetInteger(GLEnum.PackAlignment);
        int previousPackRowLength = Gl.GetInteger(GLEnum.PackRowLength);
        int previousPackSkipPixels = Gl.GetInteger(GLEnum.PackSkipPixels);
        int previousPackSkipRows = Gl.GetInteger(GLEnum.PackSkipRows);
        int previousPackBuffer = Gl.GetInteger(GLEnum.PixelPackBufferBinding);
        try
        {
            Gl.BindTexture(GLEnum.Texture2D, textureId);
            Gl.BindBuffer(GLEnum.PixelPackBuffer, 0);
            Gl.PixelStore(GLEnum.PackAlignment, 1);
            Gl.PixelStore(GLEnum.PackRowLength, 0);
            Gl.PixelStore(GLEnum.PackSkipPixels, 0);
            Gl.PixelStore(GLEnum.PackSkipRows, 0);
            Gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureWidth, out int textureWidth);
            Gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureHeight, out int textureHeight);
            if (textureWidth <= 0 || textureHeight <= 0 || (long)textureWidth * textureHeight * 4 > 32L * 1024 * 1024)
                return null;
            byte[] pixels = new byte[checked(textureWidth * textureHeight * 4)];
            fixed (byte* data = pixels)
                Gl.GetTexImage(GLEnum.Texture2D, 0, GLEnum.Rgba, GLEnum.UnsignedByte, data);
            if (_nuxtTextureCacheBytes + pixels.Length <= NuxTTextureCacheLimit)
            {
                var texture = new NuxTTexture(pixels, textureWidth, textureHeight);
                _nuxtTextureCache.Add(textureId, texture);
                _nuxtTextureCacheBytes += pixels.Length;
                return texture;
            }
            return new NuxTTexture(pixels, textureWidth, textureHeight);
        }
        finally
        {
            Gl.PixelStore(GLEnum.PackAlignment, previousPackAlignment);
            Gl.PixelStore(GLEnum.PackRowLength, previousPackRowLength);
            Gl.PixelStore(GLEnum.PackSkipPixels, previousPackSkipPixels);
            Gl.PixelStore(GLEnum.PackSkipRows, previousPackSkipRows);
            Gl.BindBuffer(GLEnum.PixelPackBuffer, (uint)previousPackBuffer);
            Gl.BindTexture(GLEnum.Texture2D, (uint)previousTexture);
        }
    }

    private unsafe void UploadNuxTFrame(NuxTViewOutput output, NuxTFrame frame)
    {
        int previousTexture = Gl.GetInteger(GLEnum.TextureBinding2D);
        int previousUnpackBuffer = Gl.GetInteger(GLEnum.PixelUnpackBufferBinding);
        int previousUnpackAlignment = Gl.GetInteger(GLEnum.UnpackAlignment);
        int previousUnpackRowLength = Gl.GetInteger(GLEnum.UnpackRowLength);
        int previousUnpackSkipPixels = Gl.GetInteger(GLEnum.UnpackSkipPixels);
        int previousUnpackSkipRows = Gl.GetInteger(GLEnum.UnpackSkipRows);
        try
        {
            Gl.BindBuffer(GLEnum.PixelUnpackBuffer, 0);
            Gl.PixelStore(GLEnum.UnpackAlignment, 1);
            Gl.PixelStore(GLEnum.UnpackRowLength, 0);
            Gl.PixelStore(GLEnum.UnpackSkipPixels, 0);
            Gl.PixelStore(GLEnum.UnpackSkipRows, 0);
            if (output.Texture == 0 || output.Width != frame.Width || output.Height != frame.Height)
            {
                if (output.Texture != 0)
                    Gl.DeleteTexture(output.Texture);
                Gl.GenTextures(1, out output.Texture);
                output.Width = frame.Width;
                output.Height = frame.Height;
                Gl.BindTexture(GLEnum.Texture2D, output.Texture);
                Gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, (uint)frame.Width, (uint)frame.Height, 0,
                    GLEnum.Rgba, GLEnum.UnsignedByte, (void*)0);
                Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Linear);
                Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Linear);
                Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                Gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            }
            else
                Gl.BindTexture(GLEnum.Texture2D, output.Texture);

            fixed (byte* pixels = frame.Rgba)
                Gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, 0, (uint)frame.Width, (uint)frame.Height,
                    GLEnum.Rgba, GLEnum.UnsignedByte, pixels);
        }
        finally
        {
            Gl.PixelStore(GLEnum.UnpackAlignment, previousUnpackAlignment);
            Gl.PixelStore(GLEnum.UnpackRowLength, previousUnpackRowLength);
            Gl.PixelStore(GLEnum.UnpackSkipPixels, previousUnpackSkipPixels);
            Gl.PixelStore(GLEnum.UnpackSkipRows, previousUnpackSkipRows);
            Gl.BindBuffer(GLEnum.PixelUnpackBuffer, (uint)previousUnpackBuffer);
            Gl.BindTexture(GLEnum.Texture2D, (uint)previousTexture);
        }
    }

    private static Vector3 ToNumerics(vec3 value) => new(value.x, value.y, value.z);
    private static Vector2 ToNumerics(vec2 value) => new(value.x, value.y);
    private static Matrix4x4 ToNumerics(mat4 value) => new(
        value.m00, value.m01, value.m02, value.m03,
        value.m10, value.m11, value.m12, value.m13,
        value.m20, value.m21, value.m22, value.m23,
        value.m30, value.m31, value.m32, value.m33);
    private static vec3 ToGlm(Vector3 value) => new(value.X, value.Y, value.Z);
}