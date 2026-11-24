using System.Numerics;

namespace MineImatorSimplyRemade.core.graphics;

public readonly record struct NuxTSettings(
    int Samples = 32,
    int MaxBounces = 4,
    float ResolutionScale = 0.5f,
    float Exposure = 1f,
    int Seed = 1,
    bool DenoiserEnabled = false)
{
    public NuxTSettings Normalized() => new(
        Math.Clamp(Samples, 1, 4096),
        Math.Clamp(MaxBounces, 0, 16),
        Math.Clamp(float.IsFinite(ResolutionScale) ? ResolutionScale : 0.5f, 0.1f, 1f),
        Math.Clamp(float.IsFinite(Exposure) ? Exposure : 1f, 0.05f, 16f),
        Seed,
        DenoiserEnabled);
}

public sealed class NuxTTexture(byte[] pixels, int width, int height)
{
    public byte[] Pixels { get; } = pixels.Length == checked(width * height * 4)
        ? pixels
        : throw new ArgumentException("NuxT textures must contain RGBA8 pixels.", nameof(pixels));
    public int Width { get; } = width > 0 ? width : throw new ArgumentOutOfRangeException(nameof(width));
    public int Height { get; } = height > 0 ? height : throw new ArgumentOutOfRangeException(nameof(height));

    public Vector4 Sample(Vector2 uv)
    {
        float u = uv.X - MathF.Floor(uv.X);
        float v = uv.Y - MathF.Floor(uv.Y);
        int x = Math.Clamp((int)(u * Width), 0, Width - 1);
        int y = Math.Clamp((int)(v * Height), 0, Height - 1);
        int offset = (y * Width + x) * 4;
        return new Vector4(
            Pixels[offset] / 255f,
            Pixels[offset + 1] / 255f,
            Pixels[offset + 2] / 255f,
            Pixels[offset + 3] / 255f);
    }
}

public readonly record struct NuxTMaterial(
    Vector3 Albedo,
    Vector3 Emission,
    float Metallic = 0f,
    float Roughness = 1f,
    float Alpha = 1f,
    float AlphaClipThreshold = 0.5f,
    bool AlphaClip = false,
    NuxTTexture? AlbedoTexture = null,
    NuxTTexture? EmissionTexture = null,
    Vector2 TextureOffset = default,
    Vector2 TextureRepeat = default,
    bool MirrorU = false,
    bool MirrorV = false,
    NuxTTexture? AlphaTexture = null,
    bool EmissionIndirectOnly = false,
    float TextureFrameScale = 1f,
    float TextureFrameOffset = 0f,
    bool Unlit = false,
    Vector3 MultiplyHsv = default,
    Vector3 AddRgb = default,
    Vector3 AddHsv = default,
    Vector3 SubtractRgb = default,
    Vector3 SubtractHsv = default,
    bool HasColorAdjustments = false)
{
    public Vector2 TransformUv(Vector2 uv)
    {
        Vector2 repeat = TextureRepeat == default ? Vector2.One : TextureRepeat;
        uv.Y = uv.Y * TextureFrameScale + TextureFrameOffset;
        Vector2 transformed = uv * repeat + TextureOffset;
        if (MirrorU && (MathF.Floor(transformed.X) % 2 != 0))
            transformed.X = MathF.Ceiling(transformed.X) - transformed.X;
        if (MirrorV && (MathF.Floor(transformed.Y) % 2 != 0))
            transformed.Y = MathF.Ceiling(transformed.Y) - transformed.Y;
        return transformed;
    }

    public Vector4 SampleAlbedo(Vector2 uv)
    {
        Vector4 texel = AlbedoTexture?.Sample(TransformUv(uv)) ?? Vector4.One;
        Vector3 color = new(
            Albedo.X * SrgbToLinear(texel.X),
            Albedo.Y * SrgbToLinear(texel.Y),
            Albedo.Z * SrgbToLinear(texel.Z));
        if (HasColorAdjustments)
            color = ApplyColorAdjustments(color);
        return new Vector4(color,
            Math.Clamp(Alpha * texel.W * (AlphaTexture?.Sample(TransformUv(uv)).W ?? 1f), 0f, 1f));
    }

    public Vector3 SampleEmission(Vector2 uv)
    {
        Vector3 color = Emission;
        if (EmissionTexture is { } texture)
        {
            Vector4 texel = texture.Sample(TransformUv(uv));
            color *= new Vector3(SrgbToLinear(texel.X), SrgbToLinear(texel.Y), SrgbToLinear(texel.Z));
        }
        return color;
    }

    private static float SrgbToLinear(float value) => value <= 0.04045f
        ? value / 12.92f
        : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private Vector3 ApplyColorAdjustments(Vector3 color)
    {
        if (MultiplyHsv != Vector3.One)
            color = HsvToRgb(RgbToHsv(Vector3.Max(color, Vector3.Zero)) * MultiplyHsv);
        if (AddRgb != Vector3.Zero)
            color = Vector3.Clamp(color + AddRgb, Vector3.Zero, Vector3.One);
        if (AddHsv != Vector3.Zero)
            color = HsvToRgb(RgbToHsv(Vector3.Max(color, Vector3.Zero)) + AddHsv);
        if (SubtractRgb != Vector3.Zero)
            color = Vector3.Clamp(color - SubtractRgb, Vector3.Zero, Vector3.One);
        if (SubtractHsv != Vector3.Zero)
            color = HsvToRgb(RgbToHsv(Vector3.Max(color, Vector3.Zero)) - SubtractHsv);
        return color;
    }

    private static Vector3 RgbToHsv(Vector3 color)
    {
        float maximum = Math.Max(color.X, Math.Max(color.Y, color.Z));
        float minimum = Math.Min(color.X, Math.Min(color.Y, color.Z));
        float chroma = maximum - minimum;
        float hue = chroma <= 0f ? 0f : maximum == color.X
            ? (color.Y - color.Z) / chroma
            : maximum == color.Y ? (color.Z - color.X) / chroma + 2f : (color.X - color.Y) / chroma + 4f;
        hue = hue / 6f - MathF.Floor(hue / 6f);
        return new Vector3(hue, maximum > 0f ? chroma / maximum : 0f, maximum);
    }

    private static Vector3 HsvToRgb(Vector3 hsv)
    {
        float hue = (hsv.X - MathF.Floor(hsv.X)) * 6f;
        float saturation = Math.Clamp(hsv.Y, 0f, 1f);
        float value = Math.Clamp(hsv.Z, 0f, 1f);
        float chroma = value * saturation;
        float secondary = chroma * (1f - MathF.Abs(hue % 2f - 1f));
        Vector3 rgb = hue switch
        {
            < 1f => new Vector3(chroma, secondary, 0f),
            < 2f => new Vector3(secondary, chroma, 0f),
            < 3f => new Vector3(0f, chroma, secondary),
            < 4f => new Vector3(0f, secondary, chroma),
            < 5f => new Vector3(secondary, 0f, chroma),
            _ => new Vector3(chroma, 0f, secondary)
        };
        return rgb + new Vector3(value - chroma);
    }
}

public readonly record struct NuxTTriangle(
    Vector3 A,
    Vector3 B,
    Vector3 C,
    Vector3 NormalA,
    Vector3 NormalB,
    Vector3 NormalC,
    Vector2 UvA,
    Vector2 UvB,
    Vector2 UvC,
    NuxTMaterial Material);

public readonly record struct NuxTLight(
    Vector3 Position,
    Vector3 Direction,
    Vector3 Color,
    float Energy,
    float Range,
    float SpotInnerCosine = -2f,
    float SpotOuterCosine = -2f,
    bool CastShadows = true);

public readonly record struct NuxTDirectionalLight(Vector3 DirectionToLight, Vector3 Radiance, bool CastShadows = true);

public readonly record struct NuxTRay(Vector3 Origin, Vector3 Direction);

internal readonly record struct NuxTBvhNode(Vector3 Min, Vector3 Max, int Start, int Count, int Left, int Right);

public readonly record struct NuxTSceneInstance(NuxTTriangle[] Triangles, Matrix4x4 Transform,
    object? GeometryKey = null, NuxTMaterial? MaterialOverride = null, bool CacheAcceleration = true);

internal readonly record struct NuxTGpuInstance(Matrix4x4 Transform, Matrix4x4 InverseTransform, int RootNode,
    int MaterialIndex);

internal sealed class NuxTGeometryBuildCache
{
    private readonly object _gate = new();
    private readonly Dictionary<object, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private NuxTScene? _previousScene;

    internal bool TryGet(object key, NuxTTriangle[] inputTriangles, out Entry entry)
    {
        lock (_gate)
            return _entries.TryGetValue(key, out entry!) && ReferenceEquals(entry.InputTriangles, inputTriangles);
    }

    internal void Store(object key, Entry entry)
    {
        lock (_gate)
            _entries[key] = entry;
    }

    internal void Prune(HashSet<object> activeKeys)
    {
        lock (_gate)
            foreach (object key in _entries.Keys.ToArray())
                if (!activeKeys.Contains(key))
                    _entries.Remove(key);
    }

    internal NuxTScene? PreviousScene
    {
        get { lock (_gate) return _previousScene; }
        set { lock (_gate) _previousScene = value; }
    }

    internal sealed record Entry(NuxTTriangle[] InputTriangles, NuxTTriangle[] Triangles, NuxTBvhNode[] Nodes,
        int[] Order, Vector3 Min, Vector3 Max, int Fingerprint);
}

public readonly record struct NuxTCamera(Vector3 Position, Vector3 Forward, Vector3 Up, float VerticalFovRadians)
{
    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Up));

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    public NuxTCamera Normalized()
    {
        Vector3 forward = IsFinite(Forward) && Forward.LengthSquared() > 1e-12f
            ? Vector3.Normalize(Forward)
            : -Vector3.UnitZ;
        Vector3 rightCandidate = Vector3.Cross(forward, Up);
        Vector3 right = IsFinite(rightCandidate) && rightCandidate.LengthSquared() > 1e-12f
            ? Vector3.Normalize(rightCandidate)
            : Vector3.UnitX;
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));
        return this with
        {
            Forward = forward,
            Up = up,
            VerticalFovRadians = Math.Clamp(VerticalFovRadians, 0.01f, MathF.PI - 0.01f)
        };
    }
}

public sealed class NuxTScene
{
    private const float RayEpsilon = 0.0005f;
    private readonly NuxTTriangle[] _triangles;
    private readonly NuxTLight[] _lights;
    private readonly NuxTDirectionalLight[] _directionalLights;
    private readonly int[] _triangleIndices;
    private readonly NuxTBvhNode[] _blasNodes;
    private readonly NuxTBvhNode[] _tlasNodes;
    private readonly int[] _instanceIndices;
    private readonly Geometry[] _geometries;
    private readonly object[] _geometryKeys;
    private readonly NuxTTriangle[][] _geometryTriangles;
    private readonly SceneInstance[] _instances;
    private readonly int _triangleCount;

    public Vector3 EnvironmentColor { get; }
    public Vector3 AmbientLight { get; }
    public int Fingerprint { get; }
    internal object GpuGeometryIdentity { get; }
    internal int BlasBuildCount { get; }
    public int TriangleCount => _triangleCount;
    internal NuxTTriangle[] GpuTriangles => _triangles;
    internal int[] GpuTriangleIndices => _triangleIndices;
    internal NuxTLight[] GpuLights => _lights;
    internal NuxTDirectionalLight[] GpuDirectionalLights => _directionalLights;
    internal int GpuNodeCount => _blasNodes.Length;
    internal int GpuTlasNodeCount => _tlasNodes.Length;
    internal int[] GpuInstanceIndices => _instanceIndices;
    internal int GpuInstanceCount => _instances.Length;
    internal NuxTGpuInstance GetGpuInstance(int index, int materialIndex)
    {
        SceneInstance instance = _instances[index];
        return new NuxTGpuInstance(instance.Transform, instance.InverseTransform,
            _geometries[instance.GeometryIndex].RootNode, materialIndex);
    }

    internal NuxTMaterial? GetGpuMaterialOverride(int index) => _instances[index].MaterialOverride;

    internal NuxTBvhNode GetGpuNode(int index)
    {
        return _blasNodes[index];
    }

    internal NuxTBvhNode GetGpuTlasNode(int index) => _tlasNodes[index];

    public NuxTScene(IEnumerable<NuxTTriangle> triangles, Vector3 environmentColor, IEnumerable<NuxTLight>? lights = null,
        IEnumerable<NuxTDirectionalLight>? directionalLights = null, Vector3 ambientLight = default,
        CancellationToken cancellationToken = default)
        : this([new NuxTSceneInstance(triangles?.ToArray() ?? throw new ArgumentNullException(nameof(triangles)),
                Matrix4x4.Identity)], environmentColor, lights, directionalLights, ambientLight, cancellationToken, null)
    {
    }

    public static NuxTScene CreateInstances(IEnumerable<NuxTSceneInstance> instances, Vector3 environmentColor,
        IEnumerable<NuxTLight>? lights = null, IEnumerable<NuxTDirectionalLight>? directionalLights = null,
        Vector3 ambientLight = default, CancellationToken cancellationToken = default) =>
        new(instances, environmentColor, lights, directionalLights, ambientLight, cancellationToken, null);

    internal static NuxTScene CreateInstances(IEnumerable<NuxTSceneInstance> instances, Vector3 environmentColor,
        IEnumerable<NuxTLight>? lights, IEnumerable<NuxTDirectionalLight>? directionalLights, Vector3 ambientLight,
        CancellationToken cancellationToken, NuxTGeometryBuildCache geometryCache) =>
        new(instances, environmentColor, lights, directionalLights, ambientLight, cancellationToken, geometryCache);

    private NuxTScene(IEnumerable<NuxTSceneInstance> instances, Vector3 environmentColor,
        IEnumerable<NuxTLight>? lights, IEnumerable<NuxTDirectionalLight>? directionalLights, Vector3 ambientLight,
        CancellationToken cancellationToken, NuxTGeometryBuildCache? geometryCache)
    {
        ArgumentNullException.ThrowIfNull(instances);
        _lights = Filter(lights ?? [], light => IsFinite(light.Position) && IsFinite(light.Color) &&
            float.IsFinite(light.Energy) && light.Energy > 0f && float.IsFinite(light.Range) && light.Range > 0f,
            cancellationToken);
        _directionalLights = Filter(directionalLights ?? [], light => IsFinite(light.DirectionToLight) &&
            IsFinite(light.Radiance) && light.DirectionToLight.LengthSquared() > 1e-12f, cancellationToken);
        EnvironmentColor = IsFinite(environmentColor) ? Vector3.Max(Vector3.Zero, environmentColor) : Vector3.Zero;
        AmbientLight = IsFinite(ambientLight) ? Vector3.Max(Vector3.Zero, ambientLight) : Vector3.Zero;
        cancellationToken.ThrowIfCancellationRequested();

        var geometryByKey = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var geometryKeys = new List<object>();
        var geometryTriangles = new List<NuxTTriangle[]>();
        var accelerationEntries = new List<NuxTGeometryBuildCache.Entry?>();
        var accelerationKeys = new List<(object Key, NuxTTriangle[] InputTriangles)?>();
        var pendingInstances = new List<(int GeometryIndex, Matrix4x4 Transform, NuxTMaterial? MaterialOverride)>();
        int visitedInstances = 0;
        foreach (NuxTSceneInstance source in instances)
        {
            if ((visitedInstances++ & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (source.Triangles is null || source.Triangles.Length == 0 || !IsFinite(source.Transform))
                continue;

            Matrix4x4 transform = source.Transform;
            if (!Matrix4x4.Invert(transform, out Matrix4x4 inverse) || !IsFinite(inverse))
            {
                NuxTTriangle[] validTriangles = Filter(source.Triangles, IsValid, cancellationToken);
                if (validTriangles.Length == 0)
                    continue;
                validTriangles = validTriangles.Select(triangle => TransformTriangle(triangle, transform)).ToArray();
                transform = Matrix4x4.Identity;
                object singularGeometryKey = new();
                int singularGeometry = geometryTriangles.Count;
                geometryByKey.Add(singularGeometryKey, singularGeometry);
                geometryKeys.Add(singularGeometryKey);
                geometryTriangles.Add(validTriangles);
                accelerationEntries.Add(null);
                accelerationKeys.Add(null);
                pendingInstances.Add((singularGeometry, transform, source.MaterialOverride));
                continue;
            }

            object geometryKey = source.GeometryKey ?? source.Triangles;
            if (geometryByKey.TryGetValue(geometryKey, out int geometryIndex))
            {
                pendingInstances.Add((geometryIndex, transform, source.MaterialOverride));
                continue;
            }

            NuxTGeometryBuildCache.Entry? cachedGeometry = source.CacheAcceleration && geometryCache != null &&
                geometryCache.TryGet(geometryKey, source.Triangles, out NuxTGeometryBuildCache.Entry foundGeometry)
                ? foundGeometry
                : null;
            NuxTTriangle[] validGeometry = cachedGeometry?.Triangles ?? FilterTriangles(source.Triangles, cancellationToken);
            if (validGeometry.Length == 0)
                continue;
            geometryIndex = geometryTriangles.Count;
            geometryByKey.Add(geometryKey, geometryIndex);
            geometryKeys.Add(geometryKey);
            geometryTriangles.Add(validGeometry);
            accelerationEntries.Add(cachedGeometry);
            accelerationKeys.Add(source.CacheAcceleration ? (geometryKey, source.Triangles) : null);
            pendingInstances.Add((geometryIndex, transform, source.MaterialOverride));
        }

        _geometryKeys = geometryKeys.ToArray();
        _geometryTriangles = geometryTriangles.ToArray();
        NuxTScene? previousScene = geometryCache?.PreviousScene;
        bool reuseGeometryLayout = previousScene is not null &&
                                   previousScene.MatchesGeometryLayout(_geometryKeys, _geometryTriangles);
        int blasBuildCount = 0;
        if (reuseGeometryLayout)
        {
            _triangles = previousScene!._triangles;
            _triangleIndices = previousScene._triangleIndices;
            _blasNodes = previousScene._blasNodes;
            _geometries = previousScene._geometries;
        }
        else
        {
            int totalTriangleCount = 0;
            foreach (NuxTTriangle[] geometry in geometryTriangles)
                totalTriangleCount = checked(totalTriangleCount + geometry.Length);
            _triangles = new NuxTTriangle[totalTriangleCount];
            _triangleIndices = new int[totalTriangleCount];
            var blasNodeList = new List<NuxTBvhNode>();
            _geometries = new Geometry[geometryTriangles.Count];
            int triangleStart = 0;
            for (int geometryIndex = 0; geometryIndex < geometryTriangles.Count; geometryIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NuxTTriangle[] geometry = geometryTriangles[geometryIndex];
                NuxTGeometryBuildCache.Entry? acceleration = accelerationEntries[geometryIndex];
                if (acceleration is null)
                {
                    Vector3[] minimums = new Vector3[geometry.Length];
                    Vector3[] maximums = new Vector3[geometry.Length];
                    for (int i = 0; i < geometry.Length; i++)
                    {
                        NuxTTriangle triangle = geometry[i];
                        minimums[i] = Vector3.Min(triangle.A, Vector3.Min(triangle.B, triangle.C));
                        maximums[i] = Vector3.Max(triangle.A, Vector3.Max(triangle.B, triangle.C));
                    }
                    (NuxTBvhNode[] buildNodes, int[] buildOrder) = NuxTBvhBuilder.Build(minimums, maximums,
                        cancellationToken);
                    blasBuildCount++;
                    acceleration = new NuxTGeometryBuildCache.Entry(geometry, geometry, buildNodes, buildOrder,
                        buildNodes[0].Min, buildNodes[0].Max, CalculateGeometryFingerprint(geometry, cancellationToken));
                    if (accelerationKeys[geometryIndex] is { } cacheKey)
                        geometryCache?.Store(cacheKey.Key, acceleration with { InputTriangles = cacheKey.InputTriangles });
                }

                NuxTBvhNode[] nodes = acceleration.Nodes;
                int[] order = acceleration.Order;
                int nodeStart = blasNodeList.Count;
                int indexStart = triangleStart;
                for (int i = 0; i < order.Length; i++)
                    _triangleIndices[indexStart + i] = triangleStart + order[i];
                foreach (NuxTBvhNode node in nodes)
                    blasNodeList.Add(node.Count > 0
                        ? node with { Start = node.Start + indexStart }
                        : node with { Left = node.Left + nodeStart, Right = node.Right + nodeStart });
                _geometries[geometryIndex] = new Geometry(triangleStart, geometry.Length, nodeStart,
                    nodes.Length == 0 ? -1 : nodeStart, acceleration.Min, acceleration.Max, acceleration.Fingerprint);
                Array.Copy(geometry, 0, _triangles, triangleStart, geometry.Length);
                triangleStart += geometry.Length;
            }

            _blasNodes = blasNodeList.ToArray();
        }

        _instances = BuildInstances(pendingInstances, cancellationToken);
        _triangleCount = _instances.Sum(instance => _geometries[instance.GeometryIndex].TriangleCount);

        Vector3[] instanceMinimums = new Vector3[_instances.Length];
        Vector3[] instanceMaximums = new Vector3[_instances.Length];
        for (int i = 0; i < _instances.Length; i++)
        {
            (instanceMinimums[i], instanceMaximums[i]) = GetTransformedBounds(_geometries[_instances[i].GeometryIndex],
                _instances[i].Transform);
            _instances[i] = _instances[i] with { Min = instanceMinimums[i], Max = instanceMaximums[i] };
        }
        (NuxTBvhNode[] tlasNodes, int[] tlasOrder) = NuxTBvhBuilder.Build(instanceMinimums, instanceMaximums,
            cancellationToken);
        _tlasNodes = tlasNodes;
        _instanceIndices = tlasOrder;
        BlasBuildCount = blasBuildCount;
        GpuGeometryIdentity = new object();
        Fingerprint = CalculateFingerprint(_instances, _geometries, _lights, _directionalLights,
            EnvironmentColor, AmbientLight, cancellationToken);
        if (geometryCache is not null)
            geometryCache.PreviousScene = this;
    }

    public NuxTScene WithLighting(IEnumerable<NuxTLight>? lights, IEnumerable<NuxTDirectionalLight>? directionalLights,
        Vector3 environmentColor, Vector3 ambientLight)
    {
        var scene = new NuxTScene(this, lights, directionalLights, environmentColor, ambientLight);
        return scene;
    }

    private NuxTScene(NuxTScene source, IEnumerable<NuxTLight>? lights,
        IEnumerable<NuxTDirectionalLight>? directionalLights, Vector3 environmentColor, Vector3 ambientLight)
    {
        _triangles = source._triangles;
        _triangleIndices = source._triangleIndices;
        _blasNodes = source._blasNodes;
        _tlasNodes = source._tlasNodes;
        _instanceIndices = source._instanceIndices;
        _geometries = source._geometries;
        _geometryKeys = source._geometryKeys;
        _geometryTriangles = source._geometryTriangles;
        _instances = source._instances;
        _triangleCount = source._triangleCount;
        _lights = Filter(lights ?? [], light => IsFinite(light.Position) && IsFinite(light.Color) &&
            float.IsFinite(light.Energy) && light.Energy > 0f && float.IsFinite(light.Range) && light.Range > 0f,
            CancellationToken.None);
        _directionalLights = Filter(directionalLights ?? [], light => IsFinite(light.DirectionToLight) &&
            IsFinite(light.Radiance) && light.DirectionToLight.LengthSquared() > 1e-12f, CancellationToken.None);
        EnvironmentColor = IsFinite(environmentColor) ? Vector3.Max(Vector3.Zero, environmentColor) : Vector3.Zero;
        AmbientLight = IsFinite(ambientLight) ? Vector3.Max(Vector3.Zero, ambientLight) : Vector3.Zero;
        GpuGeometryIdentity = source.GpuGeometryIdentity;
        BlasBuildCount = 0;
        Fingerprint = CalculateFingerprint(_instances, _geometries, _lights, _directionalLights,
            EnvironmentColor, AmbientLight, CancellationToken.None);
    }

    private bool MatchesGeometryLayout(object[] keys, NuxTTriangle[][] triangles)
    {
        if (keys.Length != _geometryKeys.Length || triangles.Length != _geometryTriangles.Length)
            return false;
        for (int i = 0; i < keys.Length; i++)
            if (!ReferenceEquals(keys[i], _geometryKeys[i]) || !ReferenceEquals(triangles[i], _geometryTriangles[i]))
                return false;
        return true;
    }

    private SceneInstance[] BuildInstances(
        List<(int GeometryIndex, Matrix4x4 Transform, NuxTMaterial? MaterialOverride)> pendingInstances,
        CancellationToken cancellationToken)
    {
        var result = new SceneInstance[pendingInstances.Count];
        for (int i = 0; i < result.Length; i++)
        {
            if ((i & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            var pending = pendingInstances[i];
            Matrix4x4.Invert(pending.Transform, out Matrix4x4 inverse);
            result[i] = new SceneInstance(pending.GeometryIndex, pending.Transform, inverse,
                pending.MaterialOverride, Vector3.Zero, Vector3.Zero);
        }
        return result;
    }

    private static (Vector3 Min, Vector3 Max) GetTransformedBounds(Geometry geometry, Matrix4x4 transform)
    {
        Vector3 minimum = new(float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity);
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 point = new((corner & 1) == 0 ? geometry.Min.X : geometry.Max.X,
                (corner & 2) == 0 ? geometry.Min.Y : geometry.Max.Y,
                (corner & 4) == 0 ? geometry.Min.Z : geometry.Max.Z);
            point = Vector3.Transform(point, transform);
            minimum = Vector3.Min(minimum, point);
            maximum = Vector3.Max(maximum, point);
        }
        return (minimum, maximum);
    }

    private static NuxTTriangle TransformTriangle(NuxTTriangle triangle, Matrix4x4 transform) => triangle with
    {
        A = Vector3.Transform(triangle.A, transform),
        B = Vector3.Transform(triangle.B, transform),
        C = Vector3.Transform(triangle.C, transform)
    };

    private static T[] Filter<T>(IEnumerable<T> source, Predicate<T> predicate, CancellationToken cancellationToken)
    {
        var result = new List<T>();
        int visited = 0;
        foreach (T item in source)
        {
            if ((visited++ & 1023) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (predicate(item))
                result.Add(item);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result.ToArray();
    }

    public Vector3 TraceRay(Vector3 origin, Vector3 direction, int maxBounces, uint seed = 1)
    {
        if (!IsFinite(origin) || !IsFinite(direction) || direction.LengthSquared() < 1e-12f)
            return EnvironmentColor;
        var random = new NuxTRandom(seed);
        return Trace(new NuxTRay(origin, Vector3.Normalize(direction)), Math.Clamp(maxBounces, 0, 16), ref random);
    }

    public byte[] RenderSample(NuxTCamera camera, int width, int height, int sampleIndex, int seed,
        NuxTSettings settings, bool transparentBackground = false)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        settings = settings.Normalized();
        camera = camera.Normalized();
        var pixels = new byte[checked(width * height * 4)];
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 8) };
        Parallel.For(0, height, options, y =>
        {
            for (int x = 0; x < width; x++)
            {
                uint pixelSeed = unchecked((uint)(seed * 747796405 + sampleIndex * 2891336453L + y * 277803737 + x));
                var random = new NuxTRandom(pixelSeed);
                Vector3 radiance = Trace(CreateCameraRay(camera, width, height, x, y, ref random),
                    settings.MaxBounces, ref random, out bool primaryHit);
                int offset = ((y * width) + x) * 4;
                WriteRgba(pixels, offset, transparentBackground && !primaryHit ? Vector3.Zero : radiance,
                    settings.Exposure);
                if (transparentBackground)
                    pixels[offset + 3] = primaryHit ? (byte)255 : (byte)0;
            }
        });
        return pixels;
    }

    internal Vector3 Trace(NuxTRay ray, int maxBounces, ref NuxTRandom random) =>
        Trace(ray, maxBounces, ref random, out _);

    internal Vector3 Trace(NuxTRay ray, int maxBounces, ref NuxTRandom random, out bool primaryHit)
    {
        Vector3 radiance = Vector3.Zero;
        Vector3 throughput = Vector3.One;
        int transparentLayers = 0;
        primaryHit = false;
        for (int bounce = 0; bounce <= maxBounces;)
        {
            if (!TryIntersect(ray, RayEpsilon, float.MaxValue, out var hit))
                return radiance + throughput * EnvironmentColor;

            NuxTMaterial material = hit.Triangle.Material;
            Vector2 uv = hit.Triangle.UvA * hit.Barycentric.X +
                         hit.Triangle.UvB * hit.Barycentric.Y +
                         hit.Triangle.UvC * hit.Barycentric.Z;
            Vector4 surface = material.SampleAlbedo(uv);
            if (material.AlphaClip && surface.W < material.AlphaClipThreshold ||
                !material.AlphaClip && surface.W < 1f && random.NextFloat() > surface.W)
            {
                if (++transparentLayers > 32)
                    return radiance + throughput * EnvironmentColor;
                ray = ray with { Origin = hit.Position + ray.Direction * RayEpsilon };
                continue;
            }
            transparentLayers = 0;
            if (bounce == 0)
                primaryHit = true;

            Vector3 normal =
                hit.Triangle.NormalA * hit.Barycentric.X +
                hit.Triangle.NormalB * hit.Barycentric.Y +
                hit.Triangle.NormalC * hit.Barycentric.Z;
            if (!IsFinite(normal) || normal.LengthSquared() < 1e-12f)
                normal = Vector3.Normalize(Vector3.Cross(hit.Triangle.B - hit.Triangle.A, hit.Triangle.C - hit.Triangle.A));
            else
                normal = Vector3.Normalize(normal);
            if (Vector3.Dot(normal, ray.Direction) > 0f)
                normal = -normal;

            float metallic = Math.Clamp(material.Metallic, 0f, 1f);
            Vector3 albedo = new(surface.X, surface.Y, surface.Z);
            Vector3 emission = material.EmissionIndirectOnly && bounce == 0 ? Vector3.Zero : material.SampleEmission(uv);
            if (material.Unlit)
                return radiance + throughput * (emission + albedo);
            Vector3 diffuse = albedo * (1f - metallic);
            radiance += throughput * (emission + diffuse *
                (AmbientLight + EvaluateDirectLighting(hit.Position, normal, ref random)));
            if (bounce == maxBounces)
                return radiance;

            Vector3 nextDirection;
            if (metallic > 0f && random.NextFloat() < metallic)
            {
                nextDirection = Vector3.Reflect(ray.Direction, normal);
                if (material.Roughness > 0.001f)
                    nextDirection = Vector3.Normalize(nextDirection + RandomInUnitSphere(ref random) * material.Roughness * material.Roughness);
                throughput *= albedo;
            }
            else
            {
                nextDirection = CosineHemisphere(normal, ref random);
                throughput *= albedo;
            }

            if (bounce >= 3)
            {
                float survival = Math.Clamp(Math.Max(throughput.X, Math.Max(throughput.Y, throughput.Z)), 0.05f, 0.95f);
                if (random.NextFloat() > survival)
                    return radiance;
                throughput /= survival;
            }

            ray = new NuxTRay(hit.Position + normal * RayEpsilon, Vector3.Normalize(nextDirection));
            bounce++;
        }
        return radiance;
    }

    private Vector3 EvaluateDirectLighting(Vector3 position, Vector3 normal, ref NuxTRandom random)
    {
        Vector3 result = Vector3.Zero;
        foreach (NuxTDirectionalLight light in _directionalLights)
        {
            Vector3 direction = Vector3.Normalize(light.DirectionToLight);
            float cosine = Math.Max(0f, Vector3.Dot(normal, direction));
            if (cosine <= 0f)
                continue;
            if (light.CastShadows && IsOccluded(new NuxTRay(position + normal * RayEpsilon, direction),
                    float.MaxValue, ref random))
                continue;
            result += light.Radiance * (cosine / MathF.PI);
        }
        foreach (NuxTLight light in _lights)
        {
            Vector3 toLight = light.Position - position;
            float distanceSquared = toLight.LengthSquared();
            if (distanceSquared < 1e-8f || distanceSquared >= light.Range * light.Range)
                continue;
            float distance = MathF.Sqrt(distanceSquared);
            Vector3 direction = toLight / distance;
            float cosine = Math.Max(0f, Vector3.Dot(normal, direction));
            if (cosine <= 0f)
                continue;

            float spot = 1f;
            if (light.SpotOuterCosine > -1f)
            {
                float alignment = Vector3.Dot(Vector3.Normalize(light.Direction), -direction);
                if (alignment <= light.SpotOuterCosine)
                    continue;
                if (light.SpotInnerCosine > light.SpotOuterCosine)
                {
                    float t = Math.Clamp((alignment - light.SpotOuterCosine) /
                        (light.SpotInnerCosine - light.SpotOuterCosine), 0f, 1f);
                    spot = t * t * (3f - 2f * t);
                }
            }

            if (light.CastShadows && IsOccluded(new NuxTRay(position + normal * RayEpsilon, direction),
                    distance - RayEpsilon, ref random))
                continue;

            float rangeFalloff = Math.Clamp(1f - distance / light.Range, 0f, 1f);
            float attenuation = rangeFalloff * rangeFalloff / distanceSquared;
            result += light.Color * (light.Energy * cosine * spot * attenuation / MathF.PI);
        }
        return result;
    }

    private bool IsOccluded(NuxTRay ray, float maxDistance, ref NuxTRandom random)
    {
        for (int layer = 0; layer < 32; layer++)
        {
            if (!TryIntersect(ray, RayEpsilon, maxDistance, out var hit))
                return false;
            NuxTMaterial material = hit.Triangle.Material;
            Vector2 uv = hit.Triangle.UvA * hit.Barycentric.X +
                         hit.Triangle.UvB * hit.Barycentric.Y +
                         hit.Triangle.UvC * hit.Barycentric.Z;
            float alpha = material.SampleAlbedo(uv).W;
            bool transparent = material.AlphaClip
                ? alpha < material.AlphaClipThreshold
                : alpha <= 0f || alpha < 1f && random.NextFloat() > alpha;
            if (!transparent)
                return true;
            ray = ray with { Origin = hit.Position + ray.Direction * RayEpsilon };
        }
        return true;
    }

    private bool TryIntersect(NuxTRay ray, float minDistance, float maxDistance, out NuxTHit closest)
    {
        closest = default;
        if (_tlasNodes.Length == 0)
            return false;

        bool found = false;
        Span<int> stack = stackalloc int[128];
        int stackCount = 0;
        stack[stackCount++] = 0;
        while (stackCount > 0)
        {
            int nodeIndex = stack[--stackCount];
            NuxTBvhNode node = _tlasNodes[nodeIndex];
            if (!IntersectsBounds(node.Min, node.Max, ray, minDistance, found ? closest.Distance : maxDistance))
                continue;
            if (node.Count > 0)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    int instanceIndex = _instanceIndices[i];
                    SceneInstance instance = _instances[instanceIndex];
                    Geometry geometry = _geometries[instance.GeometryIndex];
                    var localRay = new NuxTRay(Vector3.Transform(ray.Origin, instance.InverseTransform),
                        Vector3.TransformNormal(ray.Direction, instance.InverseTransform));
                    if (!TryIntersectBlas(geometry, instance, localRay, minDistance,
                            found ? closest.Distance : maxDistance, ray, out NuxTHit candidate))
                        continue;
                    closest = candidate;
                    found = true;
                }
            }
            else
            {
                if (stackCount + 2 > stack.Length)
                    throw new InvalidOperationException("NuxT BVH exceeds its traversal stack.");
                stack[stackCount++] = node.Left;
                stack[stackCount++] = node.Right;
            }
        }
        return found;
    }

    private bool TryIntersectBlas(Geometry geometry, SceneInstance instance, NuxTRay localRay, float minDistance,
        float maxDistance, NuxTRay worldRay, out NuxTHit closest)
    {
        closest = default;
        if (geometry.RootNode < 0)
            return false;
        bool found = false;
        Span<int> stack = stackalloc int[128];
        int stackCount = 0;
        stack[stackCount++] = geometry.RootNode;
        Matrix4x4 normalTransform = Matrix4x4.Transpose(instance.InverseTransform);
        while (stackCount > 0)
        {
            int nodeIndex = stack[--stackCount];
            NuxTBvhNode node = _blasNodes[nodeIndex];
            if (!IntersectsBounds(node.Min, node.Max, localRay, minDistance, found ? closest.Distance : maxDistance))
                continue;
            if (node.Count > 0)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    NuxTTriangle triangle = _triangles[_triangleIndices[i]];
                    if (!IntersectTriangle(triangle, localRay, minDistance,
                            found ? closest.Distance : maxDistance, out float distance, out Vector3 barycentric))
                        continue;
                    var worldTriangle = new NuxTTriangle(
                        Vector3.Transform(triangle.A, instance.Transform),
                        Vector3.Transform(triangle.B, instance.Transform),
                        Vector3.Transform(triangle.C, instance.Transform),
                        TransformNormal(triangle.NormalA, normalTransform),
                        TransformNormal(triangle.NormalB, normalTransform),
                        TransformNormal(triangle.NormalC, normalTransform),
                        triangle.UvA, triangle.UvB, triangle.UvC, instance.MaterialOverride ?? triangle.Material);
                    closest = new NuxTHit(worldTriangle, distance, worldRay.Origin + worldRay.Direction * distance,
                        barycentric);
                    found = true;
                }
            }
            else
            {
                if (stackCount + 2 > stack.Length)
                    throw new InvalidOperationException("NuxT BLAS exceeds its traversal stack.");
                stack[stackCount++] = node.Left;
                stack[stackCount++] = node.Right;
            }
        }
        return found;
    }

    private static Vector3 TransformNormal(Vector3 normal, Matrix4x4 transform)
    {
        Vector3 transformed = Vector3.TransformNormal(normal, transform);
        return IsFinite(transformed) && transformed.LengthSquared() > 1e-12f
            ? Vector3.Normalize(transformed)
            : Vector3.UnitY;
    }

    private static bool IsValid(NuxTTriangle triangle) =>
        IsFinite(triangle.A) && IsFinite(triangle.B) && IsFinite(triangle.C) &&
        Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).LengthSquared() > 1e-16f;

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IntersectsBounds(Vector3 min, Vector3 max, NuxTRay ray, float near, float far)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            float origin = Component(ray.Origin, axis);
            float direction = Component(ray.Direction, axis);
            float low = Component(min, axis);
            float high = Component(max, axis);
            if (MathF.Abs(direction) < 1e-20f)
            {
                if (origin < low || origin > high) return false;
                continue;
            }
            float inverse = 1f / direction;
            float first = (low - origin) * inverse;
            float second = (high - origin) * inverse;
            if (first > second) (first, second) = (second, first);
            near = Math.Max(near, first);
            far = Math.Min(far, second);
            if (far < near) return false;
        }
        return true;
    }

    private static bool IntersectTriangle(NuxTTriangle triangle, NuxTRay ray, float minDistance, float maxDistance,
        out float distance, out Vector3 barycentric)
    {
        Vector3 edge1 = triangle.B - triangle.A;
        Vector3 edge2 = triangle.C - triangle.A;
        Vector3 p = Vector3.Cross(ray.Direction, edge2);
        float determinant = Vector3.Dot(edge1, p);
        if (MathF.Abs(determinant) < 1e-8f)
        {
            distance = 0;
            barycentric = default;
            return false;
        }
        float inverse = 1f / determinant;
        Vector3 fromA = ray.Origin - triangle.A;
        float u = Vector3.Dot(fromA, p) * inverse;
        if (u < 0f || u > 1f)
        {
            distance = 0;
            barycentric = default;
            return false;
        }
        Vector3 q = Vector3.Cross(fromA, edge1);
        float v = Vector3.Dot(ray.Direction, q) * inverse;
        if (v < 0f || u + v > 1f)
        {
            distance = 0;
            barycentric = default;
            return false;
        }
        distance = Vector3.Dot(edge2, q) * inverse;
        barycentric = new Vector3(1f - u - v, u, v);
        return distance >= minDistance && distance <= maxDistance;
    }

    internal static NuxTRay CreateCameraRay(NuxTCamera camera, int width, int height, int x, int y, ref NuxTRandom random)
    {
        float aspect = width / (float)height;
        float halfHeight = MathF.Tan(camera.VerticalFovRadians * 0.5f);
        float px = ((x + random.NextFloat()) / width * 2f - 1f) * aspect * halfHeight;
        float py = ((y + random.NextFloat()) / height * 2f - 1f) * halfHeight;
        return new NuxTRay(camera.Position, Vector3.Normalize(camera.Forward + camera.Right * px + camera.Up * py));
    }

    private static Vector3 CosineHemisphere(Vector3 normal, ref NuxTRandom random)
    {
        float r1 = random.NextFloat();
        float r2 = random.NextFloat();
        float radius = MathF.Sqrt(r1);
        float angle = MathF.Tau * r2;
        Vector3 tangent = Vector3.Normalize(MathF.Abs(normal.Y) < 0.999f
            ? Vector3.Cross(Vector3.UnitY, normal)
            : Vector3.Cross(Vector3.UnitX, normal));
        Vector3 bitangent = Vector3.Cross(normal, tangent);
        return Vector3.Normalize(tangent * (radius * MathF.Cos(angle)) +
                                 bitangent * (radius * MathF.Sin(angle)) +
                                 normal * MathF.Sqrt(1f - r1));
    }

    private static Vector3 RandomInUnitSphere(ref NuxTRandom random)
    {
        float z = random.NextFloat() * 2f - 1f;
        float angle = random.NextFloat() * MathF.Tau;
        float radius = MathF.Pow(random.NextFloat(), 1f / 3f) * MathF.Sqrt(1f - z * z);
        return new Vector3(radius * MathF.Cos(angle), radius * MathF.Sin(angle), z);
    }

    internal static void WriteRgba(byte[] pixels, int offset, Vector3 color, float exposure)
    {
        color = Vector3.Max(Vector3.Zero, color * exposure);
        color = new Vector3(ToneMap(color.X), ToneMap(color.Y), ToneMap(color.Z));
        pixels[offset] = ToSrgbByte(color.X);
        pixels[offset + 1] = ToSrgbByte(color.Y);
        pixels[offset + 2] = ToSrgbByte(color.Z);
        pixels[offset + 3] = 255;
    }

    private static float ToneMap(float value) => value / (1f + value);

    private static byte ToSrgbByte(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        float srgb = value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;
        return (byte)Math.Clamp((int)MathF.Round(srgb * 255f), 0, 255);
    }

    private static float Component(Vector3 vector, int axis) => axis switch { 0 => vector.X, 1 => vector.Y, _ => vector.Z };

    private static int CalculateFingerprint(SceneInstance[] instances, Geometry[] geometries, NuxTLight[] lights,
        NuxTDirectionalLight[] directionalLights, Vector3 environment,
        Vector3 ambient, CancellationToken cancellationToken)
    {
        var hash = new HashCode();
        hash.Add(environment);
        hash.Add(ambient);
        foreach (Geometry geometry in geometries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.Add(geometry.TriangleStart);
            hash.Add(geometry.TriangleCount);
            hash.Add(geometry.Fingerprint);
        }
        foreach (SceneInstance instance in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.Add(instance.GeometryIndex);
            hash.Add(instance.Transform);
            hash.Add(instance.MaterialOverride);
        }
        foreach (NuxTLight light in lights)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.Add(light);
        }
        foreach (NuxTDirectionalLight light in directionalLights)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.Add(light);
        }
        return hash.ToHashCode();
    }

    private static int CalculateGeometryFingerprint(NuxTTriangle[] triangles,
        CancellationToken cancellationToken)
    {
        var hash = new HashCode();
        for (int i = 0; i < triangles.Length; i++)
        {
            if ((i & 1023) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            NuxTTriangle triangle = triangles[i];
            hash.Add(triangle.A); hash.Add(triangle.B); hash.Add(triangle.C);
            hash.Add(triangle.NormalA); hash.Add(triangle.NormalB); hash.Add(triangle.NormalC);
            hash.Add(triangle.UvA); hash.Add(triangle.UvB); hash.Add(triangle.UvC);
            hash.Add(triangle.Material);
        }
        return hash.ToHashCode();
    }

    private static NuxTTriangle[] FilterTriangles(NuxTTriangle[] triangles, CancellationToken cancellationToken)
    {
        int invalidCount = 0;
        for (int i = 0; i < triangles.Length; i++)
        {
            if ((i & 2047) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (!IsValid(triangles[i]))
                invalidCount++;
        }
        if (invalidCount == 0)
            return triangles;

        var validTriangles = new NuxTTriangle[triangles.Length - invalidCount];
        int write = 0;
        for (int i = 0; i < triangles.Length; i++)
            if (IsValid(triangles[i]))
                validTriangles[write++] = triangles[i];
        return validTriangles;
    }

    private static bool IsFinite(Matrix4x4 matrix) =>
        float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14) &&
        float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24) &&
        float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34) &&
        float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);

    private readonly record struct Geometry(int TriangleStart, int TriangleCount, int NodeStart, int RootNode,
        Vector3 Min, Vector3 Max, int Fingerprint);

    private readonly record struct SceneInstance(int GeometryIndex, Matrix4x4 Transform, Matrix4x4 InverseTransform,
        NuxTMaterial? MaterialOverride, Vector3 Min, Vector3 Max);

    private readonly record struct NuxTHit(NuxTTriangle Triangle, float Distance, Vector3 Position, Vector3 Barycentric);
}

public sealed class NuxTProgressiveRenderer : IDisposable
{
    private const int MaxRenderPixels = 8_388_608;
    private const int MaxPublishedFrames = 128;
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private NuxTFrame? _latestFrame;
    private NuxTFrame? _completedFrame;
    private Task? _worker;
    private int _requestKey;
    private int _generation;
    private int _completedSamples;
    private string _status = "Idle";
    private bool _disposed;

    public int CompletedSamples { get { lock (_gate) return _completedSamples; } }
    public string Status { get { lock (_gate) return _status; } }

    public void StartOrUpdate(NuxTScene scene, NuxTCamera camera, int width, int height, NuxTSettings settings,
        bool transparentBackground = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        if (width <= 0 || height <= 0) return;
        long pixelCount = (long)width * height;
        if (pixelCount > MaxRenderPixels)
        {
            float scale = MathF.Sqrt(MaxRenderPixels / (float)pixelCount);
            width = Math.Max(1, (int)(width * scale));
            height = Math.Max(1, (int)(height * scale));
        }
        settings = settings.Normalized();
        camera = camera.Normalized();
        int key = HashCode.Combine(scene.Fingerprint, camera, width, height, settings, transparentBackground);
        lock (_gate)
        {
            if (key == _requestKey && _worker is { IsCompleted: false }) return;
            if (key == _requestKey && _completedSamples >= settings.Samples) return;
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            _requestKey = key;
            int generation = ++_generation;
            _completedSamples = 0;
            _latestFrame = null;
            _completedFrame = null;
            _status = "Tracing";
            CancellationToken token = _cancellation.Token;
            _worker = Task.Run(() => RenderProgressively(scene, camera, width, height, settings,
                transparentBackground, token, generation), token);
        }
    }

    public NuxTFrame RenderToCompletion(NuxTScene scene, NuxTCamera camera, int width, int height,
        NuxTSettings settings, bool transparentBackground = false)
    {
        StartOrUpdate(scene, camera, width, height, settings, transparentBackground);
        settings = settings.Normalized();
        Task? worker;
        lock (_gate)
        {
            if (_completedFrame is { } completed && completed.Samples >= settings.Samples)
                return completed;
            worker = _worker;
        }
        worker?.GetAwaiter().GetResult();
        lock (_gate)
        {
            if (_completedFrame is { } completed && completed.Samples >= settings.Samples)
                return completed;
            throw new InvalidOperationException($"NuxT final render did not complete: {_status}.");
        }
    }

    public bool TryTakeLatestFrame(out NuxTFrame frame)
    {
        lock (_gate)
        {
            if (_latestFrame is null)
            {
                frame = default;
                return false;
            }
            frame = _latestFrame.Value;
            _latestFrame = null;
            return true;
        }
    }

    private void RenderProgressively(NuxTScene scene, NuxTCamera camera, int width, int height,
        NuxTSettings settings, bool transparentBackground, CancellationToken token, int generation)
    {
        var accumulation = new Vector4[checked(width * height)];
        int publishInterval = Math.Max(1, (settings.Samples + MaxPublishedFrames - 1) / MaxPublishedFrames);
        int completed = 0;
        string? denoiserStatus = null;
        try
        {
            for (int sample = 0; sample < settings.Samples; sample++)
            {
                token.ThrowIfCancellationRequested();
                var options = new ParallelOptions
                {
                    CancellationToken = token,
                    MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 8)
                };
                Parallel.For(0, height, options, y =>
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * width + x;
                        uint pixelSeed = unchecked((uint)((long)settings.Seed * 747796405L +
                            (long)sample * 2891336453L + (long)y * 277803737L + x));
                        var random = new NuxTRandom(pixelSeed);
                        NuxTRay ray = NuxTScene.CreateCameraRay(camera, width, height, x, y, ref random);
                        Vector3 radiance = scene.Trace(ray, settings.MaxBounces, ref random, out bool primaryHit);
                        if (!transparentBackground || primaryHit)
                            accumulation[index] += new Vector4(radiance, 1f);
                    }
                });

                completed++;
                lock (_gate)
                {
                    if (token.IsCancellationRequested || generation != _generation) return;
                    _completedSamples = completed;
                }
                if (completed % publishInterval != 0 && completed != settings.Samples)
                    continue;

                float[]? denoisedLinearRgb = null;
                if (completed == settings.Samples && settings.DenoiserEnabled)
                {
                    lock (_gate)
                    {
                        if (token.IsCancellationRequested || generation != _generation) return;
                        _status = "Denoising · CPU";
                    }

                    float[] linearRgb = new float[checked(accumulation.Length * 3)];
                    for (int i = 0; i < accumulation.Length; i++)
                    {
                        Vector4 accumulated = accumulation[i];
                        float colorSamples = transparentBackground ? accumulated.W : completed;
                        int colorOffset = i * 3;
                        if (colorSamples > 0f)
                        {
                            linearRgb[colorOffset] = accumulated.X / colorSamples;
                            linearRgb[colorOffset + 1] = accumulated.Y / colorSamples;
                            linearRgb[colorOffset + 2] = accumulated.Z / colorSamples;
                        }
                    }

                    if (NuxTDenoiser.TryDenoiseLinearRgb(linearRgb, width, height,
                            out float[] filteredLinearRgb, out string? denoiserError))
                    {
                        denoisedLinearRgb = filteredLinearRgb;
                        denoiserStatus = "Complete · CPU · Denoised";
                    }
                    else
                        denoiserStatus = $"Complete · CPU · Denoiser unavailable: {denoiserError}";
                }

                byte[] rgba = new byte[checked(width * height * 4)];
                for (int i = 0; i < accumulation.Length; i++)
                {
                    Vector4 accumulated = accumulation[i];
                    float colorSamples = transparentBackground ? accumulated.W : completed;
                    int colorOffset = i * 3;
                    Vector3 color = denoisedLinearRgb is not null
                        ? new Vector3(denoisedLinearRgb[colorOffset], denoisedLinearRgb[colorOffset + 1],
                            denoisedLinearRgb[colorOffset + 2])
                        : colorSamples > 0f
                            ? new Vector3(accumulated.X, accumulated.Y, accumulated.Z) / colorSamples
                            : Vector3.Zero;
                    int offset = i * 4;
                    NuxTScene.WriteRgba(rgba, offset, color, settings.Exposure);
                    if (transparentBackground)
                        rgba[offset + 3] = (byte)Math.Clamp((int)MathF.Round(accumulated.W / completed * 255f), 0, 255);
                }
                lock (_gate)
                {
                    if (token.IsCancellationRequested || generation != _generation) return;
                    var frame = new NuxTFrame(width, height, completed, rgba);
                    _latestFrame = frame;
                    if (completed == settings.Samples)
                    {
                        _completedFrame = frame;
                        _status = denoiserStatus ?? "Complete";
                    }
                }
            }
            lock (_gate)
                if (!token.IsCancellationRequested && generation == _generation)
                    _status = denoiserStatus ?? "Complete";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            lock (_gate)
                if (!token.IsCancellationRequested && generation == _generation)
                    _status = $"Error: {exception.Message}";
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
            _latestFrame = null;
            _status = "Disposed";
        }
    }
}

public readonly record struct NuxTFrame(int Width, int Height, int Samples, byte[] Rgba);

public sealed class NuxTSceneBuildCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly NuxTGeometryBuildCache _geometryCache = new();
    private CancellationTokenSource? _cancellation;
    private Task<NuxTScene>? _buildTask;
    private string _status = "Idle";
    private bool _disposed;

    public string Status { get { lock (_gate) return _status; } }

    public void Start(IEnumerable<NuxTTriangle> triangles, Vector3 environmentColor,
        IEnumerable<NuxTLight>? lights = null, IEnumerable<NuxTDirectionalLight>? directionalLights = null,
        Vector3 ambientLight = default)
    {
        ArgumentNullException.ThrowIfNull(triangles);
        StartBuild(token => new NuxTScene(triangles, environmentColor, lights, directionalLights, ambientLight, token));
    }

    public void StartInstances(IEnumerable<NuxTSceneInstance> instances, Vector3 environmentColor,
        IEnumerable<NuxTLight>? lights = null, IEnumerable<NuxTDirectionalLight>? directionalLights = null,
        Vector3 ambientLight = default)
    {
        ArgumentNullException.ThrowIfNull(instances);
        NuxTSceneInstance[] snapshot = instances.ToArray();
        StartBuild(token => NuxTScene.CreateInstances(snapshot, environmentColor, lights, directionalLights,
            ambientLight, token, _geometryCache));
    }

    internal void PruneGeometryCache(HashSet<object> activeKeys) => _geometryCache.Prune(activeKeys);

    private void StartBuild(Func<CancellationToken, NuxTScene> build)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            CancelCurrent();
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            CancellationToken token = cancellation.Token;
            _status = "Building scene";
            _buildTask = Task.Run(() => build(token), token);
        }
    }

    public bool TryTakeCompleted(out NuxTScene scene)
    {
        lock (_gate)
        {
            if (_buildTask is not { IsCompleted: true } completed)
            {
                scene = null!;
                return false;
            }

            _buildTask = null;
            if (completed.IsCanceled)
            {
                _status = "Cancelled";
                scene = null!;
                return false;
            }
            if (completed.IsFaulted)
            {
                _status = $"Error: {completed.Exception?.GetBaseException().Message ?? "Scene build failed."}";
                scene = null!;
                return false;
            }

            scene = completed.GetAwaiter().GetResult();
            _status = "Ready";
            return true;
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            CancelCurrent();
            _status = "Cancelled";
        }
    }

    private void CancelCurrent()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _buildTask = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            CancelCurrent();
            _status = "Disposed";
        }
    }
}

public struct NuxTRandom(uint seed)
{
    private uint _state = seed == 0 ? 0xA341316Cu : seed;

    public uint NextUInt()
    {
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return _state;
    }

    public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);
}