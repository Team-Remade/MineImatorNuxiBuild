using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MineImatorSimplyRemade.core.mdl.material;

// Numeric values are the instruction codes in shader_graph.glsl.
public enum ShaderNodeKind
{
    Output = 0, Value = 1, Color = 2, Object = 3, UV = 4, Noise = 5, Wave = 6,
    Voronoi = 7, Checker = 8, Ramp = 9, Mix = 10, Add = 11, Multiply = 12, Mapping = 13
}

public sealed record ShaderNode(int Id, ShaderNodeKind Kind, float X, float Y,
    float A = 0, float B = 0, float C = 0, float D = 0);

public readonly record struct ShaderLink(int Source, int Target, int Input);

public sealed record ShaderGraph
{
    public const int MaxNodes = 32;
    public bool Enabled { get; init; }
    public ImmutableArray<ShaderNode> Nodes { get; init; } = [new(1, ShaderNodeKind.Output, 470, 80)];
    public ImmutableArray<ShaderLink> Links { get; init; } = [];

    public static string Label(ShaderNodeKind kind) => kind switch
    {
        ShaderNodeKind.Output => "Principled Material Output",
        ShaderNodeKind.Object => "Object Coordinates",
        ShaderNodeKind.UV => "UV Coordinates",
        ShaderNodeKind.Noise => "Noise Texture",
        ShaderNodeKind.Wave => "Wave Texture",
        ShaderNodeKind.Voronoi => "Voronoi Texture",
        ShaderNodeKind.Checker => "Checker Texture",
        ShaderNodeKind.Ramp => "Color Ramp",
        ShaderNodeKind.Mix => "Mix Color",
        ShaderNodeKind.Add => "Math: Add",
        ShaderNodeKind.Multiply => "Math: Multiply",
        _ => kind.ToString()
    };

    private static readonly string[] SurfaceInputs = ["Base Color", "Roughness", "Metallic", "Emission", "Bump Height"];
    private static readonly string[] VectorInput = ["Vector"];
    private static readonly string[] ColorInputs = ["Factor", "Color A", "Color B"];
    private static readonly string[] MathInputs = ["A", "B"];

    public static ReadOnlySpan<string> Inputs(ShaderNodeKind kind) => kind switch
    {
        ShaderNodeKind.Output => SurfaceInputs,
        ShaderNodeKind.Noise or ShaderNodeKind.Wave or ShaderNodeKind.Voronoi or ShaderNodeKind.Checker
            or ShaderNodeKind.Mapping => VectorInput,
        ShaderNodeKind.Ramp or ShaderNodeKind.Mix => ColorInputs,
        ShaderNodeKind.Add or ShaderNodeKind.Multiply => MathInputs,
        _ => []
    };

    public static ShaderNode CreateNode(int id, ShaderNodeKind kind, float x, float y) => kind switch
    {
        ShaderNodeKind.Color => new(id, kind, x, y, 0.5f, 0.5f, 0.5f),
        ShaderNodeKind.Value => new(id, kind, x, y, 0.5f),
        ShaderNodeKind.Noise or ShaderNodeKind.Wave or ShaderNodeKind.Voronoi or ShaderNodeKind.Checker
            => new(id, kind, x, y, 5f),
        ShaderNodeKind.Mapping => new(id, kind, x, y, 1f),
        ShaderNodeKind.Ramp => new(id, kind, x, y, 0.25f, 0.75f),
        _ => new(id, kind, x, y)
    };

    private static readonly ConditionalWeakTable<ShaderGraph, ShaderGraphProgram> Programs = new();
    [JsonIgnore]
    public string? ValidationError => Program.Error;
    internal ShaderGraphProgram Program => Programs.GetValue(this, static graph => ShaderGraphProgram.Compile(graph));
}

internal sealed class ShaderGraphProgram
{
    public string? Error { get; private init; }
    public float[] Instructions { get; private init; } = [];
    public float[] Parameters { get; private init; } = [];
    public int[] Outputs { get; private init; } = [];
    public int Count => Instructions.Length / 4;

    public static ShaderGraphProgram Compile(ShaderGraph graph)
    {
        static ShaderGraphProgram Invalid(string error) => new() { Error = error };
        if (graph.Nodes.IsDefaultOrEmpty || graph.Nodes.Length > ShaderGraph.MaxNodes || graph.Links.IsDefault ||
            graph.Links.Length > ShaderGraph.MaxNodes * 3)
            return Invalid($"A graph must have 1–{ShaderGraph.MaxNodes} nodes and a valid link list.");

        var nodes = new Dictionary<int, ShaderNode>();
        ShaderNode? output = null;
        foreach (var node in graph.Nodes)
        {
            if (node == null || node.Id <= 0 || !nodes.TryAdd(node.Id, node) ||
                !Enum.IsDefined(node.Kind) || !float.IsFinite(node.X) || !float.IsFinite(node.Y) ||
                !float.IsFinite(node.A) || !float.IsFinite(node.B) ||
                !float.IsFinite(node.C) || !float.IsFinite(node.D))
                return Invalid("The graph contains an invalid node.");
            if (node.Kind != ShaderNodeKind.Output) continue;
            if (output != null) return Invalid("Use exactly one Material Output.");
            output = node;
        }
        if (output == null) return Invalid("The graph is missing its Material Output.");

        var sources = new Dictionary<(int Node, int Input), int>();
        foreach (var link in graph.Links)
        {
            if (!nodes.TryGetValue(link.Source, out var from) || from.Kind == ShaderNodeKind.Output ||
                !nodes.TryGetValue(link.Target, out var to) ||
                (uint)link.Input >= (uint)ShaderGraph.Inputs(to.Kind).Length ||
                !sources.TryAdd((link.Target, link.Input), link.Source))
                return Invalid("The graph contains an invalid or duplicate input link.");
        }

        var instructions = new List<float>();
        var parameters = new List<float>();
        var registers = new Dictionary<int, int>();
        var visiting = new HashSet<int>();
        int Input(ShaderNode node, int socket) =>
            sources.TryGetValue((node.Id, socket), out int source) && registers.TryGetValue(source, out int index)
                ? index : -1;

        bool Visit(ShaderNode node, bool emit)
        {
            if (registers.ContainsKey(node.Id)) return true;
            if (!visiting.Add(node.Id)) return false;
            for (int socket = 0; socket < ShaderGraph.Inputs(node.Kind).Length; socket++)
                if (sources.TryGetValue((node.Id, socket), out int source) && !Visit(nodes[source], emit))
                    return false;
            visiting.Remove(node.Id);
            registers.Add(node.Id, emit ? instructions.Count / 4 : -1);
            if (emit)
            {
                instructions.AddRange([(float)node.Kind, Input(node, 0), Input(node, 1), Input(node, 2)]);
                parameters.AddRange([Math.Clamp(node.A, -1024, 1024), Math.Clamp(node.B, -1024, 1024),
                    Math.Clamp(node.C, -1024, 1024), Math.Clamp(node.D, -1024, 1024)]);
            }
            return true;
        }

        // Emit only nodes that contribute to the surface, then validate any disconnected subgraphs.
        var outputs = new int[ShaderGraph.Inputs(ShaderNodeKind.Output).Length];
        for (int i = 0; i < outputs.Length; i++)
        {
            if (sources.TryGetValue((output.Id, i), out int source) && !Visit(nodes[source], true))
                return Invalid("A link would create a cycle.");
            outputs[i] = Input(output, i);
        }
        foreach (var node in graph.Nodes)
            if (!Visit(node, false)) return Invalid("A link would create a cycle.");
        return new() { Instructions = instructions.ToArray(), Parameters = parameters.ToArray(), Outputs = outputs };
    }
}

public enum ShaderGraphPreset { Water, Magma, BrushedSteel, Copper, Marble }

public static class ShaderGraphPresets
{
    public static ShaderGraph Create(ShaderGraphPreset preset)
    {
        var nodes = new List<ShaderNode> { new(1, ShaderNodeKind.Output, 700, 75) };
        var links = new List<ShaderLink>();
        int Node(ShaderNodeKind kind, float x, float y, float a, float b = 0, float c = 0)
        {
            int id = nodes.Count + 1;
            nodes.Add(new(id, kind, x, y, a, b, c));
            return id;
        }
        void Link(int from, int to, int input) => links.Add(new(from, to, input));
        int Value(float value, float y) => Node(ShaderNodeKind.Value, 460, y, value);
        int Ramp(int factor, (float R, float G, float B) a, (float R, float G, float B) b, float low, float high)
        {
            int ramp = Node(ShaderNodeKind.Ramp, 460, 100, low, high);
            Link(factor, ramp, 0);
            Link(Node(ShaderNodeKind.Color, 0, 170, a.R, a.G, a.B), ramp, 1);
            Link(Node(ShaderNodeKind.Color, 0, 300, b.R, b.G, b.B), ramp, 2);
            Link(ramp, 1, 0);
            return ramp;
        }

        switch (preset)
        {
            case ShaderGraphPreset.Water:
            {
                int waves = Node(ShaderNodeKind.Wave, 225, 60, 17f, 4.0f);
                Ramp(waves, (0.025f, 0.17f, 0.30f), (0.12f, 0.47f, 0.60f), 0.08f, 0.95f);
                Link(Value(0.14f, 290), 1, 1);
                Link(waves, 1, 4);
                break;
            }
            case ShaderGraphPreset.Magma:
            {
                int cells = Node(ShaderNodeKind.Voronoi, 225, 60, 10f);
                int ramp = Ramp(cells, (1f, 0.48f, 0.06f), (0.085f, 0.024f, 0.018f), 0.025f, 0.15f);
                Link(Value(0.82f, 290), 1, 1);
                Link(ramp, 1, 3);
                Link(cells, 1, 4);
                break;
            }
            case ShaderGraphPreset.BrushedSteel:
            {
                int waves = Node(ShaderNodeKind.Wave, 225, 60, 72f, 0.25f);
                Ramp(waves, (0.46f, 0.50f, 0.54f), (0.74f, 0.79f, 0.85f), 0f, 1f);
                Link(Value(0.44f, 290), 1, 1);
                Link(Value(0.90f, 390), 1, 2);
                break;
            }
            case ShaderGraphPreset.Copper:
            {
                int noise = Node(ShaderNodeKind.Noise, 225, 60, 8f);
                Ramp(noise, (0.56f, 0.22f, 0.10f), (0.91f, 0.55f, 0.33f), 0.18f, 0.82f);
                Link(Value(0.32f, 290), 1, 1);
                Link(Value(0.85f, 390), 1, 2);
                break;
            }
            case ShaderGraphPreset.Marble:
            {
                int waves = Node(ShaderNodeKind.Wave, 225, 60, 12f, 8f);
                Ramp(waves, (0.24f, 0.28f, 0.32f), (0.92f, 0.90f, 0.84f), 0.02f, 0.29f);
                Link(Value(0.32f, 290), 1, 1);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(preset));
        }
        return new() { Enabled = true, Nodes = [..nodes], Links = [..links] };
    }
}
