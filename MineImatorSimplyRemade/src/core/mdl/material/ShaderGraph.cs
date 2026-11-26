using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MineImatorSimplyRemade.core.mdl.material;

// Numeric values are saved in projects and are the instruction codes in shader_graph.glsl.
public enum ShaderNodeKind
{
    Output = 0, Value = 1, Color = 2, Object = 3, UV = 4, Noise = 5, Wave = 6,
    Voronoi = 7, Checker = 8, Ramp = 9, Mix = 10, Add = 11, Multiply = 12, Mapping = 13,
    TextureCoordinate = 14, Geometry = 15, Fresnel = 16, LayerWeight = 17, Time = 18, ObjectInfo = 19,
    Gradient = 20, Magic = 21, Brick = 22, WhiteNoise = 23, ImageTexture = 24,
    Invert = 25, HueSaturation = 26, BrightContrast = 27, Gamma = 28, RgbToBw = 29,
    Math = 30, VectorMath = 31, MapRange = 32, Clamp = 33, SeparateXyz = 34, CombineXyz = 35,
    SeparateColor = 36, CombineColor = 37, VectorRotate = 38, Blackbody = 39,
    FogVolumeOutput = 40, Path = 41
}

public enum ShaderSocketType { Float, Color, Vector }

/// <summary>Implicit value used by an unlinked vector input instead of a constant.</summary>
public enum ShaderAutoInput { None, Position, UV }

/// <summary><see cref="Percent"/> floats are stored as 0..1 and shown as 0..100%.</summary>
public sealed record ShaderSocket(string Name, ShaderSocketType Type, float X = 0, float Y = 0, float Z = 0,
    float Min = -1024, float Max = 1024, ShaderAutoInput Auto = ShaderAutoInput.None, bool Percent = false);

public sealed record ShaderOutputSocket(string Name, ShaderSocketType Type);

public sealed record ShaderNodeChoice(string Label, string[] Values, int Default = 0);

public sealed record ShaderNodeParameter(string Name, float Default, float Min, float Max)
{
    /// <summary>Stored as 0..1 and shown as 0..100%.</summary>
    public bool Percent { get; init; }
}

/// <summary>Static description of a node kind: its sockets, dropdowns and parameters.</summary>
public sealed record ShaderNodeSpec(string Label, string Category, ShaderSocket[] Inputs, ShaderOutputSocket[] Outputs)
{
    public ShaderNodeChoice? Mode { get; init; }
    public ShaderNodeChoice? Option { get; init; }
    public ShaderNodeChoice? Variant { get; init; }
    public string? ClampLabel { get; init; }
    public bool ClampDefault { get; init; }
    public ShaderNodeParameter[] Parameters { get; init; } = [];
    /// <summary>Legacy kinds stay loadable but are not offered in the Add menu.</summary>
    public bool InMenu { get; init; } = true;
}

/// <summary>
/// A graph node. <see cref="Defaults"/> stores three floats per input socket for unlinked values;
/// nodes saved before per-socket values existed are upgraded by <see cref="ShaderGraph.Normalize"/>.
/// </summary>
public sealed record ShaderNode(int Id, ShaderNodeKind Kind, float X, float Y,
    float A = 0, float B = 0, float C = 0, float D = 0)
{
    public int Mode { get; init; }
    public int Option { get; init; }
    public int Variant { get; init; }
    public bool Clamp { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<float> Defaults { get; init; }
    /// <summary>Scene object id a Path node follows; empty until a path is chosen.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; init; }

    public bool Equals(ShaderNode? other) => other is not null && Id == other.Id && Kind == other.Kind &&
        X.Equals(other.X) && Y.Equals(other.Y) && A.Equals(other.A) && B.Equals(other.B) &&
        C.Equals(other.C) && D.Equals(other.D) && Mode == other.Mode && Option == other.Option &&
        Variant == other.Variant && Clamp == other.Clamp && string.Equals(Target, other.Target, StringComparison.Ordinal) &&
        Defaults.AsSpan().SequenceEqual(other.Defaults.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id); hash.Add(Kind); hash.Add(X); hash.Add(Y); hash.Add(A); hash.Add(B); hash.Add(C); hash.Add(D);
        hash.Add(Mode); hash.Add(Option); hash.Add(Variant); hash.Add(Clamp); hash.Add(Target);
        foreach (float value in Defaults.AsSpan()) hash.Add(value);
        return hash.ToHashCode();
    }
}

/// <summary>Connects output socket <see cref="Output"/> of <see cref="Source"/> to an input of <see cref="Target"/>.</summary>
public readonly record struct ShaderLink(int Source, int Target, int Input, int Output = 0);

public sealed record ShaderGraph
{
    public const int MaxNodes = 64;
    public const int MaxLinks = MaxNodes * 4;
    /// <summary>Distinct paths one graph can follow; each Path node picks one of them.</summary>
    public const int MaxPaths = 4;
    internal const int MaxRegisters = 64, MaxDataTexels = 1024;

    public bool Enabled { get; init; }
    public ImmutableArray<ShaderNode> Nodes { get; init; } = [new(1, ShaderNodeKind.Output, 470, 80)];
    public ImmutableArray<ShaderLink> Links { get; init; } = [];

    // ── Node catalogue ────────────────────────────────────────────────────────

    public static readonly string[] MathOperations =
    [
        "Add", "Subtract", "Multiply", "Divide", "Multiply Add", "Power", "Logarithm", "Square Root",
        "Inverse Square Root", "Absolute", "Exponent", "Minimum", "Maximum", "Less Than", "Greater Than",
        "Sign", "Compare", "Smooth Minimum", "Smooth Maximum", "Round", "Floor", "Ceil", "Truncate",
        "Fraction", "Truncated Modulo", "Floored Modulo", "Wrap", "Snap", "Ping-Pong", "Sine", "Cosine",
        "Tangent", "Arcsine", "Arccosine", "Arctangent", "Arctan2", "Hyperbolic Sine", "Hyperbolic Cosine",
        "Hyperbolic Tangent", "To Radians", "To Degrees"
    ];

    public static readonly string[] VectorMathOperations =
    [
        "Add", "Subtract", "Multiply", "Divide", "Multiply Add", "Cross Product", "Project", "Reflect",
        "Refract", "Faceforward", "Dot Product", "Distance", "Length", "Scale", "Normalize", "Absolute",
        "Minimum", "Maximum", "Floor", "Ceil", "Fraction", "Modulo", "Wrap", "Snap", "Sine", "Cosine", "Tangent"
    ];

    public static readonly string[] BlendModes =
    [
        "Mix", "Darken", "Multiply", "Color Burn", "Lighten", "Screen", "Color Dodge", "Add", "Overlay",
        "Soft Light", "Linear Light", "Difference", "Exclusion", "Subtract", "Divide", "Hue", "Saturation",
        "Color", "Value"
    ];

    private static ShaderSocket F(string name, float value = 0, float min = -1024, float max = 1024) =>
        new(name, ShaderSocketType.Float, value, value, value, min, max);
    private static ShaderSocket Rgb(string name, float r, float g, float b) => new(name, ShaderSocketType.Color, r, g, b, 0, 1024);
    private static ShaderSocket Vec(string name, float x = 0, float y = 0, float z = 0) => new(name, ShaderSocketType.Vector, x, y, z);
    private static ShaderSocket Pct(string name, float value) =>
        new(name, ShaderSocketType.Float, value, value, value, 0, 1, Percent: true);
    private static ShaderSocket AutoVector(ShaderAutoInput auto = ShaderAutoInput.Position) =>
        new("Vector", ShaderSocketType.Vector, Auto: auto);
    private static ShaderOutputSocket OutF(string name) => new(name, ShaderSocketType.Float);
    private static ShaderOutputSocket OutC(string name = "Color") => new(name, ShaderSocketType.Color);
    private static ShaderOutputSocket OutV(string name = "Vector") => new(name, ShaderSocketType.Vector);

    private static readonly ShaderNodeChoice ColorModes = new("Mode", ["RGB", "HSV", "HSL"]);

    private static readonly Dictionary<ShaderNodeKind, ShaderNodeSpec> Specs = new()
    {
        [ShaderNodeKind.Output] = new("Principled Material Output", "Output",
        [
            Rgb("Base Color", 0.7f, 0.7f, 0.7f), F("Roughness", 0.5f, 0, 1), F("Metallic", 0, 0, 1),
            Rgb("Emission", 0, 0, 0), F("Bump Height"), F("Alpha", 1, 0, 1), F("IOR", 1.45f, 1, 4),
            F("Transmission", 0, 0, 1), F("Specular", 0.5f, 0, 1), F("Emission Strength", 1, 0, 1024),
            F("Bump Strength", 1, 0, 16), F("Refraction Depth", 0.2f, 0, 16)
        ], []),

        // Turns the object into a fog volume. Voxel Size, Randomness and Top Cutoff are plain settings because the
        // sample position is snapped to the voxel grid before the rest of the graph runs. Ignore Object Scale (the
        // node's Clamp flag) measures that grid and the graph's Position in world blocks instead of object units.
        [ShaderNodeKind.FogVolumeOutput] = new("Fog Volume Output", "Output",
        [
            Rgb("Color", 1, 1, 1), Rgb("Extinction Color", 0, 0, 0), Pct("Density", 0.25f),
            F("Scattering", 1, 0, 16), F("Absorption", 0, 0, 16), F("Anisotropy", 0, -0.99f, 0.99f),
            F("Edge Softness", 0.25f, 0, 16), Rgb("Emission Color", 1, 1, 1), F("Emission Strength", 0, 0, 1024),
            Pct("Path Visibility", 1)
        ], [])
        {
            ClampLabel = "Ignore Object Scale",
            Parameters =
            [
                new("Voxel Size", 0.5f, 0, 16), new("Voxel Size Randomness", 0, 0, 1) { Percent = true },
                new("Top Cutoff", 1, 0, 1) { Percent = true }
            ]
        },

        // Input
        [ShaderNodeKind.Value] = new("Value", "Input", [], [OutF("Value")])
            { Parameters = [new("Value", 0.5f, -1024, 1024)] },
        [ShaderNodeKind.Color] = new("RGB", "Input", [], [OutC()]),
        [ShaderNodeKind.Object] = new("Object Coordinates", "Input", [], [OutV()]) { InMenu = false },
        [ShaderNodeKind.UV] = new("UV Coordinates", "Input", [], [OutV()]) { InMenu = false },
        [ShaderNodeKind.TextureCoordinate] = new("Texture Coordinate", "Input", [],
            [OutV("Generated"), OutV("Normal"), OutV("UV"), OutV("Object"), OutV("Camera"), OutV("Window"), OutV("Reflection")]),
        [ShaderNodeKind.Geometry] = new("Geometry", "Input", [],
            [OutV("Position"), OutV("Normal"), OutV("Incoming"), OutF("Backfacing")]),
        [ShaderNodeKind.Fresnel] = new("Fresnel", "Input", [F("IOR", 1.45f, 1, 16)], [OutF("Fac")]),
        [ShaderNodeKind.LayerWeight] = new("Layer Weight", "Input", [F("Blend", 0.5f, 0, 1)],
            [OutF("Fresnel"), OutF("Facing")]),
        [ShaderNodeKind.Time] = new("Time", "Input", [], [OutF("Seconds"), OutF("Frame")]),
        [ShaderNodeKind.ObjectInfo] = new("Object Info", "Input", [],
            [OutV("Location"), OutC("Color"), OutF("Alpha"), OutF("Random")]),
        // Distance to the part of the chosen path between Start and End. Evaluated by the NuxT path tracer; the
        // viewport has no path data and treats every point as on the path.
        [ShaderNodeKind.Path] = new("Path", "Input",
            [F("Radius", 2, 0, 1024), F("Falloff", 1, 0, 1024), Pct("Start", 0), Pct("End", 1)],
            [OutF("Visibility"), OutF("Distance"), OutF("Progress")]),

        // Texture
        [ShaderNodeKind.Noise] = new("Noise Texture", "Texture",
            [AutoVector(), F("Scale", 5, -1024, 1024), F("Detail", 2, 0, 15), F("Roughness", 0.5f, 0, 1),
             F("Distortion", 0, -1024, 1024), F("W")], [OutF("Fac"), OutC()]),
        [ShaderNodeKind.Wave] = new("Wave Texture", "Texture",
            [AutoVector(), F("Scale", 5), F("Distortion"), F("Detail", 2, 0, 15), F("Detail Scale", 1),
             F("Detail Roughness", 0.5f, 0, 1), F("Phase Offset")], [OutC(), OutF("Fac")])
        {
            Mode = new("Type", ["Bands", "Rings"]),
            Option = new("Direction", ["X", "Y", "Z", "Diagonal / Spherical"]),
            Variant = new("Profile", ["Sine", "Saw", "Triangle"])
        },
        [ShaderNodeKind.Voronoi] = new("Voronoi Texture", "Texture",
            [AutoVector(), F("Scale", 5), F("Randomness", 1, 0, 1), F("Smoothness", 1, 0, 1)],
            [OutF("Distance"), OutC(), OutV("Position")])
        {
            Mode = new("Feature", ["F1", "F2", "Smooth F1", "Distance to Edge"]),
            Option = new("Distance", ["Euclidean", "Manhattan", "Chebyshev"])
        },
        [ShaderNodeKind.Checker] = new("Checker Texture", "Texture",
            [AutoVector(), Rgb("Color1", 0.8f, 0.8f, 0.8f), Rgb("Color2", 0.2f, 0.2f, 0.2f), F("Scale", 5)],
            [OutC(), OutF("Fac")]),
        [ShaderNodeKind.Gradient] = new("Gradient Texture", "Texture", [AutoVector()], [OutC(), OutF("Fac")])
        {
            Mode = new("Type", ["Linear", "Quadratic", "Easing", "Diagonal", "Spherical", "Quadratic Sphere", "Radial"])
        },
        [ShaderNodeKind.Magic] = new("Magic Texture", "Texture",
            [AutoVector(), F("Scale", 5), F("Distortion", 1)], [OutC(), OutF("Fac")])
        {
            Mode = new("Depth", ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10"], 2)
        },
        [ShaderNodeKind.Brick] = new("Brick Texture", "Texture",
            [AutoVector(), Rgb("Color1", 0.8f, 0.8f, 0.8f), Rgb("Color2", 0.2f, 0.2f, 0.2f), Rgb("Mortar", 0, 0, 0),
             F("Scale", 5), F("Mortar Size", 0.02f, 0, 0.125f), F("Bias", 0, -1, 1), F("Brick Width", 0.5f, 0.01f, 100),
             F("Row Height", 0.25f, 0.01f, 100)], [OutC(), OutF("Fac")])
        {
            Parameters = [new("Offset", 0.5f, 0, 1), new("Frequency", 2, 1, 99)]
        },
        [ShaderNodeKind.WhiteNoise] = new("White Noise Texture", "Texture", [AutoVector(), F("W")],
            [OutF("Value"), OutC()]),
        [ShaderNodeKind.ImageTexture] = new("Image Texture", "Texture", [AutoVector(ShaderAutoInput.UV)],
            [OutC(), OutF("Alpha")])
        {
            Mode = new("Extension", ["Repeat", "Extend", "Clip"])
        },

        // Color
        [ShaderNodeKind.Ramp] = new("Color Ramp", "Color",
            [F("Factor", 0.5f, -1024, 1024), Rgb("Color 1", 0, 0, 0), Rgb("Color 2", 1, 1, 1),
             Rgb("Color 3", 1, 1, 1), Rgb("Color 4", 1, 1, 1)], [OutC()])
        {
            Mode = new("Interpolation", ["Linear", "Constant", "Ease"]),
            Option = new("Stops", ["2", "3", "4"])
        },
        [ShaderNodeKind.Mix] = new("Mix Color", "Color",
            [F("Factor", 0.5f, 0, 1), Rgb("A", 0.5f, 0.5f, 0.5f), Rgb("B", 0.5f, 0.5f, 0.5f)], [OutC("Result")])
        {
            Mode = new("Blend", BlendModes), ClampLabel = "Clamp Result"
        },
        [ShaderNodeKind.Invert] = new("Invert Color", "Color", [F("Fac", 1, 0, 1), Rgb("Color", 0, 0, 0)], [OutC()]),
        [ShaderNodeKind.HueSaturation] = new("Hue/Saturation/Value", "Color",
            [F("Hue", 0.5f, 0, 1), F("Saturation", 1, 0, 2), F("Value", 1, 0, 2), F("Fac", 1, 0, 1),
             Rgb("Color", 0.8f, 0.8f, 0.8f)], [OutC()]),
        [ShaderNodeKind.BrightContrast] = new("Brightness/Contrast", "Color",
            [Rgb("Color", 1, 1, 1), F("Bright", 0, -100, 100), F("Contrast", 0, -100, 100)], [OutC()]),
        [ShaderNodeKind.Gamma] = new("Gamma", "Color", [Rgb("Color", 1, 1, 1), F("Gamma", 1, 0.001f, 10)], [OutC()]),
        [ShaderNodeKind.Blackbody] = new("Blackbody", "Color", [F("Temperature", 1500, 800, 12000)], [OutC()]),

        // Vector
        [ShaderNodeKind.Mapping] = new("Mapping", "Vector",
            [AutoVector(), Vec("Location"), Vec("Rotation"), Vec("Scale", 1, 1, 1)], [OutV()])
        {
            Mode = new("Type", ["Point", "Texture", "Vector", "Normal"])
        },
        [ShaderNodeKind.VectorMath] = new("Vector Math", "Vector",
            [Vec("A"), Vec("B"), Vec("C"), F("Scale", 1)], [OutV(), OutF("Value")])
        {
            Mode = new("Operation", VectorMathOperations)
        },
        [ShaderNodeKind.VectorRotate] = new("Vector Rotate", "Vector",
            [AutoVector(), Vec("Center"), Vec("Axis", 0, 0, 1), F("Angle"), Vec("Rotation")], [OutV()])
        {
            Mode = new("Type", ["Axis Angle", "X Axis", "Y Axis", "Z Axis", "Euler XYZ"]), ClampLabel = "Invert"
        },

        // Converter
        [ShaderNodeKind.Math] = new("Math", "Converter", [F("A", 0.5f), F("B", 0.5f), F("C")], [OutF("Value")])
        {
            Mode = new("Operation", MathOperations), ClampLabel = "Clamp"
        },
        [ShaderNodeKind.MapRange] = new("Map Range", "Converter",
            [F("Value", 1), F("From Min"), F("From Max", 1), F("To Min"), F("To Max", 1), F("Steps", 4, 0, 1024)],
            [OutF("Result")])
        {
            Mode = new("Interpolation", ["Linear", "Stepped Linear", "Smooth Step", "Smoother Step"]),
            ClampLabel = "Clamp", ClampDefault = true
        },
        [ShaderNodeKind.Clamp] = new("Clamp", "Converter", [F("Value", 1), F("Min"), F("Max", 1)], [OutF("Result")])
        {
            Mode = new("Type", ["Min Max", "Range"])
        },
        [ShaderNodeKind.RgbToBw] = new("RGB to BW", "Converter", [Rgb("Color", 0.5f, 0.5f, 0.5f)], [OutF("Val")]),
        [ShaderNodeKind.SeparateXyz] = new("Separate XYZ", "Converter", [Vec("Vector")], [OutF("X"), OutF("Y"), OutF("Z")]),
        [ShaderNodeKind.CombineXyz] = new("Combine XYZ", "Converter", [F("X"), F("Y"), F("Z")], [OutV()]),
        [ShaderNodeKind.SeparateColor] = new("Separate Color", "Converter", [Rgb("Color", 0.8f, 0.8f, 0.8f)],
            [OutF("Red"), OutF("Green"), OutF("Blue")]) { Mode = ColorModes },
        [ShaderNodeKind.CombineColor] = new("Combine Color", "Converter", [F("Red"), F("Green"), F("Blue")],
            [OutC()]) { Mode = ColorModes },
        // Kept for graphs saved before the Math node; operate per channel.
        [ShaderNodeKind.Add] = new("Math: Add", "Converter",
            [Rgb("A", 0, 0, 0), Rgb("B", 0, 0, 0)], [OutC("Value")]) { InMenu = false },
        [ShaderNodeKind.Multiply] = new("Math: Multiply", "Converter",
            [Rgb("A", 0, 0, 0), Rgb("B", 1, 1, 1)], [OutC("Value")]) { InMenu = false },
    };

    public static readonly string[] Categories = ["Input", "Texture", "Color", "Vector", "Converter"];

    /// <summary>Output kinds; a graph has exactly one, and choosing another replaces it.</summary>
    public static readonly ShaderNodeKind[] OutputKinds = [ShaderNodeKind.Output, ShaderNodeKind.FogVolumeOutput];

    public static bool IsOutput(ShaderNodeKind kind) => kind is ShaderNodeKind.Output or ShaderNodeKind.FogVolumeOutput;

    public static ShaderNodeSpec Spec(ShaderNodeKind kind) => Specs[kind];

    public static IEnumerable<ShaderNodeKind> MenuKinds(string category) =>
        Enum.GetValues<ShaderNodeKind>().Where(kind => Specs[kind] is { InMenu: true } spec && spec.Category == category);

    public static string Label(ShaderNodeKind kind) => Specs.TryGetValue(kind, out var spec) ? spec.Label : kind.ToString();

    public static ReadOnlySpan<ShaderSocket> InputSockets(ShaderNodeKind kind) => Specs[kind].Inputs;

    public static ReadOnlySpan<ShaderOutputSocket> OutputSockets(ShaderNodeKind kind) => Specs[kind].Outputs;

    public static int InputCount(ShaderNodeKind kind) => Specs[kind].Inputs.Length;

    public static int OutputCount(ShaderNodeKind kind) => Specs[kind].Outputs.Length;

    /// <summary>Index of the named Principled output input, for presets and tests.</summary>
    public static int OutputInput(string name) => Array.FindIndex(Specs[ShaderNodeKind.Output].Inputs, s => s.Name == name);

    /// <summary>Index of the named Fog Volume Output input, for presets and tests.</summary>
    public static int FogInput(string name) => Array.FindIndex(Specs[ShaderNodeKind.FogVolumeOutput].Inputs, s => s.Name == name);

    /// <summary>
    /// Swaps the graph's output node for another kind in place. Links into the old output are dropped because
    /// the two kinds have different sockets.
    /// </summary>
    public static ShaderGraph WithOutputKind(ShaderGraph graph, ShaderNodeKind kind)
    {
        if (!IsOutput(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var current = graph.Nodes.FirstOrDefault(node => IsOutput(node.Kind));
        if (current == null)
            return graph with { Nodes = graph.Nodes.Add(CreateNode(1, kind, 470, 80)) };
        if (current.Kind == kind) return graph;
        return graph with
        {
            Nodes = graph.Nodes.Replace(current, CreateNode(current.Id, kind, current.X, current.Y)),
            Links = graph.Links.RemoveAll(link => link.Target == current.Id)
        };
    }

    public static ShaderNode CreateNode(int id, ShaderNodeKind kind, float x, float y)
    {
        var spec = Specs[kind];
        var node = new ShaderNode(id, kind, x, y)
        {
            Mode = spec.Mode?.Default ?? 0, Option = spec.Option?.Default ?? 0, Variant = spec.Variant?.Default ?? 0,
            Clamp = spec.ClampDefault, Defaults = SpecDefaults(spec)
        };
        return kind switch
        {
            ShaderNodeKind.Color => node with { A = 0.5f, B = 0.5f, C = 0.5f },
            ShaderNodeKind.Value => node with { A = 0.5f },
            ShaderNodeKind.Ramp => node with { A = 0, B = 1, C = 1, D = 1 },
            ShaderNodeKind.Brick => node with { A = 0.5f, B = 2 },
            ShaderNodeKind.FogVolumeOutput => node with { A = 0.5f, B = 0, C = 1 },
            _ => node
        };
    }

    private static ImmutableArray<float> SpecDefaults(ShaderNodeSpec spec)
    {
        var values = new float[spec.Inputs.Length * 3];
        for (int i = 0; i < spec.Inputs.Length; i++)
            (values[i * 3], values[i * 3 + 1], values[i * 3 + 2]) = (spec.Inputs[i].X, spec.Inputs[i].Y, spec.Inputs[i].Z);
        return [..values];
    }

    /// <summary>
    /// Returns the node with a complete per-socket value list. Nodes saved before sockets had values keep
    /// their original look: legacy parameters move into the equivalent socket values.
    /// </summary>
    public static ShaderNode Normalize(ShaderNode node)
    {
        if (!Specs.TryGetValue(node.Kind, out var spec)) return node;
        int length = spec.Inputs.Length * 3;
        if (!node.Defaults.IsDefault && node.Defaults.Length >= length) return node;
        bool legacy = node.Defaults.IsDefaultOrEmpty;
        var values = SpecDefaults(spec).ToArray();
        if (!legacy) node.Defaults.AsSpan().CopyTo(values);
        void Set(int input, float x, float? y = null, float? z = null) =>
            (values[input * 3], values[input * 3 + 1], values[input * 3 + 2]) = (x, y ?? x, z ?? x);
        if (legacy)
        {
            switch (node.Kind)
            {
                case ShaderNodeKind.Noise: Set(1, node.A); break;
                case ShaderNodeKind.Voronoi: Set(1, node.A); node = node with { Mode = 3 }; break;
                case ShaderNodeKind.Checker: Set(1, 1); Set(2, 0); Set(3, node.A); break;
                case ShaderNodeKind.Wave:
                    // The former wave was sin(x * scale + distortion * (noise - 0.5)).
                    Set(1, node.A / 20f); Set(2, node.B * 0.5f); Set(4, 7); Set(6, MathF.PI * 0.5f);
                    break;
                case ShaderNodeKind.Mapping: Set(1, node.B, node.C, node.D); Set(3, node.A); break;
                case ShaderNodeKind.Mix: Set(0, 0); Set(1, 0); Set(2, 1); break;
            }
        }
        return node with { Defaults = [..values] };
    }

    public static (float X, float Y, float Z) InputValue(ShaderNode node, int input)
    {
        node = Normalize(node);
        return (node.Defaults[input * 3], node.Defaults[input * 3 + 1], node.Defaults[input * 3 + 2]);
    }

    public static ShaderNode WithInputValue(ShaderNode node, int input, float x, float y, float z)
    {
        node = Normalize(node);
        var socket = Specs[node.Kind].Inputs[input];
        var values = node.Defaults.ToBuilder();
        values[input * 3] = Math.Clamp(x, socket.Min, socket.Max);
        values[input * 3 + 1] = Math.Clamp(y, socket.Min, socket.Max);
        values[input * 3 + 2] = Math.Clamp(z, socket.Min, socket.Max);
        return node with { Defaults = values.ToImmutable() };
    }

    public static ShaderNode WithInputValue(ShaderNode node, string input, float x, float? y = null, float? z = null)
    {
        int index = Array.FindIndex(Specs[node.Kind].Inputs, s => s.Name == input);
        if (index < 0) throw new ArgumentException($"{Label(node.Kind)} has no input '{input}'.", nameof(input));
        return WithInputValue(node, index, x, y ?? x, z ?? x);
    }

    private static readonly ConditionalWeakTable<ShaderGraph, ShaderGraphProgram> Programs = new();
    [JsonIgnore]
    public string? ValidationError => Program.Error;
    internal ShaderGraphProgram Program => Programs.GetValue(this, static graph => ShaderGraphProgram.Compile(graph));
}

/// <summary>
/// Packs a graph into RGBA32F texels read by shader_graph.glsl.
/// Texel 0: (instruction count, output block texel, flags, 0). Each instruction is
/// (kind, mode, option, inputs | clamp &lt;&lt; 8 | variant &lt;&lt; 9), two output-register maps,
/// (A, B, C, D) parameters and one texel per input: (source, x, y, z). A source &gt;= 0 is a register
/// (+1024 when converted to a scalar), -1 a constant, -2 object position and -3 UV coordinates.
/// The output block holds one texel per output input, followed for a Fog Volume Output by one texel of
/// (voxel size, voxel size randomness, top cutoff, 1 when the volume ignores its object's scale).
/// A Path node stores its path slot in parameter A.
/// </summary>
internal sealed class ShaderGraphProgram
{
    public const int BumpLinked = 1, VolumeOutput = 2;

    public string? Error { get; private init; }
    /// <summary>The output is a Fog Volume Output: the object renders as a medium, not a surface.</summary>
    public bool Volume { get; private init; }
    /// <summary>Scene object ids of the paths used by Path nodes; the slot is the index in this list.</summary>
    public string[] PathTargets { get; private init; } = [];
    public float[] Data { get; private init; } = [];
    public int Count { get; private init; }
    public int OutputStart { get; private init; }
    public int Flags { get; private init; }
    /// <summary>The surface may be partially transparent and must draw in the blended pass.</summary>
    public bool Translucent { get; private init; }
    /// <summary>The surface refracts the scene behind it.</summary>
    public bool Transmissive { get; private init; }
    public int Texels => Data.Length / 4;

    public static ShaderGraphProgram Compile(ShaderGraph graph)
    {
        static ShaderGraphProgram Invalid(string error) => new() { Error = error };
        if (graph.Nodes.IsDefaultOrEmpty || graph.Nodes.Length > ShaderGraph.MaxNodes || graph.Links.IsDefault ||
            graph.Links.Length > ShaderGraph.MaxLinks)
            return Invalid($"A graph must have 1–{ShaderGraph.MaxNodes} nodes and a valid link list.");

        var nodes = new Dictionary<int, ShaderNode>();
        ShaderNode? output = null;
        foreach (var original in graph.Nodes)
        {
            if (original == null || original.Id <= 0 || !Enum.IsDefined(original.Kind)) return Invalid("The graph contains an invalid node.");
            var node = ShaderGraph.Normalize(original);
            if (!nodes.TryAdd(node.Id, node) || !float.IsFinite(node.X) || !float.IsFinite(node.Y) ||
                !float.IsFinite(node.A) || !float.IsFinite(node.B) || !float.IsFinite(node.C) || !float.IsFinite(node.D) ||
                node.Defaults.Any(value => !float.IsFinite(value)))
                return Invalid("The graph contains an invalid node.");
            if (!ShaderGraph.IsOutput(node.Kind)) continue;
            if (output != null) return Invalid("Use exactly one Material Output.");
            output = node;
        }
        if (output == null) return Invalid("The graph is missing its Material Output.");

        var sources = new Dictionary<(int Node, int Input), ShaderLink>();
        foreach (var link in graph.Links)
        {
            if (!nodes.TryGetValue(link.Source, out var from) || ShaderGraph.IsOutput(from.Kind) ||
                (uint)link.Output >= (uint)ShaderGraph.OutputCount(from.Kind) ||
                !nodes.TryGetValue(link.Target, out var to) ||
                (uint)link.Input >= (uint)ShaderGraph.InputCount(to.Kind) ||
                !sources.TryAdd((link.Target, link.Input), link))
                return Invalid("The graph contains an invalid or duplicate input link.");
        }

        var order = new List<ShaderNode>();
        var done = new HashSet<int>();
        var visiting = new HashSet<int>();
        bool Visit(ShaderNode node, bool emit)
        {
            if (done.Contains(node.Id)) return true;
            if (!visiting.Add(node.Id)) return false;
            for (int socket = 0; socket < ShaderGraph.InputCount(node.Kind); socket++)
                if (sources.TryGetValue((node.Id, socket), out var link) && !Visit(nodes[link.Source], emit))
                    return false;
            visiting.Remove(node.Id);
            done.Add(node.Id);
            if (emit && !ShaderGraph.IsOutput(node.Kind)) order.Add(node);
            return true;
        }
        // Emit only nodes that contribute to the surface, then validate any disconnected subgraphs.
        if (!Visit(output, true)) return Invalid("A link would create a cycle.");
        foreach (var node in nodes.Values)
            if (!Visit(node, false)) return Invalid("A link would create a cycle.");

        var emitted = order.Select(node => node.Id).Append(output.Id).ToHashSet();
        var registers = new Dictionary<(int Node, int Output), int>();
        foreach (var link in sources.Values)
            if (emitted.Contains(link.Target) && !registers.ContainsKey((link.Source, link.Output)))
                registers[(link.Source, link.Output)] = registers.Count;
        if (registers.Count > ShaderGraph.MaxRegisters)
            return Invalid($"The graph uses more than {ShaderGraph.MaxRegisters} connected outputs.");

        var data = new List<float> { order.Count, 0, 0, 0 };
        void Inputs(ShaderNode node)
        {
            var sockets = ShaderGraph.InputSockets(node.Kind);
            for (int i = 0; i < sockets.Length; i++)
            {
                var socket = sockets[i];
                if (sources.TryGetValue((node.Id, i), out var link))
                {
                    int register = registers[(link.Source, link.Output)];
                    var sourceType = ShaderGraph.OutputSockets(nodes[link.Source].Kind)[link.Output].Type;
                    bool scalar = socket.Type == ShaderSocketType.Float && sourceType != ShaderSocketType.Float;
                    data.AddRange([register + (scalar ? 1024 : 0), 0, 0, 0]);
                }
                else if (socket.Auto != ShaderAutoInput.None)
                    data.AddRange([socket.Auto == ShaderAutoInput.UV ? -3 : -2, 0, 0, 0]);
                else
                    data.AddRange([-1,
                        Math.Clamp(node.Defaults[i * 3], socket.Min, socket.Max),
                        Math.Clamp(node.Defaults[i * 3 + 1], socket.Min, socket.Max),
                        Math.Clamp(node.Defaults[i * 3 + 2], socket.Min, socket.Max)]);
            }
        }

        var pathTargets = new List<string>();
        foreach (var node in order)
            if (node.Kind == ShaderNodeKind.Path && !string.IsNullOrEmpty(node.Target) && !pathTargets.Contains(node.Target))
                pathTargets.Add(node.Target);
        if (pathTargets.Count > ShaderGraph.MaxPaths)
            return Invalid($"A graph can follow at most {ShaderGraph.MaxPaths} different paths.");

        foreach (var node in order)
        {
            var spec = ShaderGraph.Spec(node.Kind);
            int Choice(ShaderNodeChoice? choice, int value) => choice == null ? 0 : Math.Clamp(value, 0, choice.Values.Length - 1);
            int variant = Choice(spec.Variant, node.Variant);
            data.AddRange([(float)node.Kind, Choice(spec.Mode, node.Mode), Choice(spec.Option, node.Option),
                spec.Inputs.Length | (node.Clamp ? 256 : 0) | (variant << 9)]);
            for (int k = 0; k < 8; k++)
                data.Add(registers.TryGetValue((node.Id, k), out int register) ? register : -1);
            float a = node.A, b = node.B, c = node.C, d = node.D;
            if (node.Kind == ShaderNodeKind.Ramp)
            {
                // The shader walks stops in order, so positions must not decrease.
                a = Math.Clamp(a, 0, 1);
                b = Math.Clamp(b, a, 1);
                c = Math.Clamp(c, b, 1);
                d = Math.Clamp(d, c, 1);
            }
            else if (node.Kind == ShaderNodeKind.Path)
                a = string.IsNullOrEmpty(node.Target) ? -1 : pathTargets.IndexOf(node.Target);
            data.AddRange([Math.Clamp(a, -1024, 1024), Math.Clamp(b, -1024, 1024),
                Math.Clamp(c, -1024, 1024), Math.Clamp(d, -1024, 1024)]);
            Inputs(node);
        }
        int outputStart = data.Count / 4;
        Inputs(output);
        bool volume = output.Kind == ShaderNodeKind.FogVolumeOutput;
        if (volume)
            data.AddRange([Math.Clamp(output.A, 0, 16), Math.Clamp(output.B, 0, 1), Math.Clamp(output.C, 0, 1),
                output.Clamp ? 1 : 0]);
        if (data.Count / 4 > ShaderGraph.MaxDataTexels) return Invalid("The graph is too complex to render.");
        if (volume)
        {
            data[1] = outputStart;
            data[2] = VolumeOutput;
            return new()
            {
                Data = data.ToArray(), Count = order.Count, OutputStart = outputStart, Flags = VolumeOutput,
                Translucent = true, Volume = true, PathTargets = [..pathTargets]
            };
        }

        bool Linked(string name) => sources.ContainsKey((output.Id, ShaderGraph.OutputInput(name)));
        float Value(string name) => output.Defaults[ShaderGraph.OutputInput(name) * 3];
        bool transmissive = Linked("Transmission") || Value("Transmission") > 0;
        data[1] = outputStart;
        int flags = Linked("Bump Height") ? BumpLinked : 0;
        data[2] = flags;
        return new()
        {
            Data = data.ToArray(), Count = order.Count, OutputStart = outputStart, Flags = flags,
            Transmissive = transmissive, Translucent = transmissive || Linked("Alpha") || Value("Alpha") < 1,
            PathTargets = [..pathTargets]
        };
    }
}

public enum ShaderGraphPreset { Water, Magma, BrushedSteel, Copper, Marble, Glass, LavaFlow }

public static class ShaderGraphPresets
{
    public static string Label(ShaderGraphPreset preset) => preset switch
    {
        ShaderGraphPreset.BrushedSteel => "Brushed Steel",
        ShaderGraphPreset.LavaFlow => "Lava Flow",
        _ => preset.ToString()
    };

    /// <summary>
    /// A starter fog volume: noise drives the density, so the medium shows patchy voxel clouds. It is kept out
    /// of <see cref="ShaderGraphPreset"/> because those presets are surface looks that preview on a sphere.
    /// </summary>
    public static ShaderGraph CreateFogVolume()
    {
        var output = ShaderGraph.CreateNode(1, ShaderNodeKind.FogVolumeOutput, 700, 75);
        var noise = ShaderGraph.WithInputValue(ShaderGraph.WithInputValue(
            ShaderGraph.CreateNode(2, ShaderNodeKind.Noise, 225, 60), "Scale", 0.6f), "Detail", 3f);
        var remap = ShaderGraph.WithInputValue(ShaderGraph.WithInputValue(ShaderGraph.WithInputValue(
            ShaderGraph.CreateNode(3, ShaderNodeKind.MapRange, 460, 80), "From Min", 0.3f), "From Max", 0.8f), "To Max", 0.8f);
        return new()
        {
            Enabled = true,
            Nodes = [output, noise, remap],
            Links = [new(2, 3, 0), new(3, 1, ShaderGraph.FogInput("Density"))]
        };
    }

    public static ShaderGraph Create(ShaderGraphPreset preset)
    {
        var nodes = new List<ShaderNode> { ShaderGraph.CreateNode(1, ShaderNodeKind.Output, 700, 75) };
        var links = new List<ShaderLink>();
        int Node(ShaderNodeKind kind, float x, float y, Func<ShaderNode, ShaderNode>? edit = null)
        {
            var node = ShaderGraph.CreateNode(nodes.Count + 1, kind, x, y);
            nodes.Add(edit?.Invoke(node) ?? node);
            return node.Id;
        }
        void Link(int from, int to, int input, int output = 0) => links.Add(new(from, to, input, output));
        void LinkOut(int from, string input, int output = 0) => Link(from, 1, ShaderGraph.OutputInput(input), output);
        void SetOutput(string input, float value) =>
            nodes[0] = ShaderGraph.WithInputValue(nodes[0], input, value);
        static Func<ShaderNode, ShaderNode> Inputs(params (string Name, float Value)[] values) => node =>
        {
            foreach (var (name, value) in values) node = ShaderGraph.WithInputValue(node, name, value);
            return node;
        };
        int Ramp(int factor, (float R, float G, float B) a, (float R, float G, float B) b, float low, float high)
        {
            int ramp = Node(ShaderNodeKind.Ramp, 460, 100, node => ShaderGraph.WithInputValue(
                ShaderGraph.WithInputValue(node with { A = low, B = high }, 1, a.R, a.G, a.B), 2, b.R, b.G, b.B));
            Link(factor, ramp, 0);
            LinkOut(ramp, "Base Color");
            return ramp;
        }
        // Equivalent to the original wave pattern: sin(x * scale + distortion * (noise - 0.5)).
        int Wave(float scale, float distortion) => Node(ShaderNodeKind.Wave, 225, 60, Inputs(
            ("Scale", scale / 20f), ("Distortion", distortion * 0.5f), ("Detail Scale", 7f), ("Phase Offset", MathF.PI * 0.5f)));

        switch (preset)
        {
            case ShaderGraphPreset.Water:
            {
                int waves = Wave(17f, 4.0f);
                Ramp(waves, (0.025f, 0.17f, 0.30f), (0.12f, 0.47f, 0.60f), 0.08f, 0.95f);
                // Ripples travel over time.
                int time = Node(ShaderNodeKind.Time, -220, 0);
                int speed = Node(ShaderNodeKind.Math, 0, -40, node => ShaderGraph.WithInputValue(node with { Mode = 2 }, "B", 2f));
                Link(time, speed, 0);
                Link(speed, waves, 6);
                SetOutput("Roughness", 0.14f);
                SetOutput("Bump Strength", 2f);
                LinkOut(waves, "Bump Height", 1);
                break;
            }
            case ShaderGraphPreset.Magma:
            {
                int cells = Node(ShaderNodeKind.Voronoi, 225, 60, node => ShaderGraph.WithInputValue(node with { Mode = 3 }, "Scale", 10f));
                int ramp = Ramp(cells, (1f, 0.48f, 0.06f), (0.085f, 0.024f, 0.018f), 0.0125f, 0.075f);
                SetOutput("Roughness", 0.82f);
                LinkOut(ramp, "Emission");
                LinkOut(cells, "Bump Height");
                break;
            }
            case ShaderGraphPreset.BrushedSteel:
            {
                int waves = Wave(72f, 0.25f);
                Ramp(waves, (0.46f, 0.50f, 0.54f), (0.74f, 0.79f, 0.85f), 0f, 1f);
                SetOutput("Roughness", 0.44f);
                SetOutput("Metallic", 0.90f);
                break;
            }
            case ShaderGraphPreset.Copper:
            {
                int noise = Node(ShaderNodeKind.Noise, 225, 60, Inputs(("Scale", 8f)));
                Ramp(noise, (0.56f, 0.22f, 0.10f), (0.91f, 0.55f, 0.33f), 0.18f, 0.82f);
                SetOutput("Roughness", 0.32f);
                SetOutput("Metallic", 0.85f);
                break;
            }
            case ShaderGraphPreset.Marble:
            {
                int waves = Wave(12f, 8f);
                Ramp(waves, (0.24f, 0.28f, 0.32f), (0.92f, 0.90f, 0.84f), 0.02f, 0.29f);
                SetOutput("Roughness", 0.32f);
                break;
            }
            case ShaderGraphPreset.Glass:
            {
                int color = Node(ShaderNodeKind.Color, 225, 60, node => node with { A = 0.86f, B = 0.95f, C = 1f });
                LinkOut(color, "Base Color");
                SetOutput("Roughness", 0.05f);
                SetOutput("Transmission", 1f);
                SetOutput("IOR", 1.5f);
                break;
            }
            case ShaderGraphPreset.LavaFlow:
            {
                // Time drives the noise W coordinate, so the crust churns as the timeline plays.
                int time = Node(ShaderNodeKind.Time, -220, 40);
                int slow = Node(ShaderNodeKind.Math, 0, 0, node => ShaderGraph.WithInputValue(node with { Mode = 2 }, "B", 0.35f));
                int noise = Node(ShaderNodeKind.Noise, 225, 60, Inputs(("Scale", 4f), ("Detail", 4f), ("Distortion", 1.5f)));
                int ramp = Node(ShaderNodeKind.Ramp, 460, 100, node =>
                {
                    node = node with { Option = 2, A = 0.35f, B = 0.48f, C = 0.58f, D = 0.7f };
                    node = ShaderGraph.WithInputValue(node, 1, 0.03f, 0.02f, 0.02f);
                    node = ShaderGraph.WithInputValue(node, 2, 0.45f, 0.04f, 0.01f);
                    node = ShaderGraph.WithInputValue(node, 3, 1f, 0.35f, 0.02f);
                    return ShaderGraph.WithInputValue(node, 4, 1f, 0.85f, 0.3f);
                });
                Link(time, slow, 0);
                Link(slow, noise, 5);
                Link(noise, ramp, 0);
                LinkOut(ramp, "Base Color");
                LinkOut(ramp, "Emission");
                LinkOut(noise, "Bump Height");
                SetOutput("Emission Strength", 2.5f);
                SetOutput("Roughness", 0.7f);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(preset));
        }
        return new() { Enabled = true, Nodes = [..nodes], Links = [..links] };
    }
}
