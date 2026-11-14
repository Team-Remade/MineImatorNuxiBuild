using GlmSharp;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

/// <summary>Saved local-space positions, indexed by timeline frame from zero.</summary>
public sealed class ClothBake
{
    public const long MaxBytes = 256L * 1024 * 1024;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public float Framerate { get; init; }
    public ClothSettings Settings { get; init; } = new();
    public int[] MeshVertexCounts { get; init; } = [];
    public float[][] Frames { get; init; } = [];

    public void Validate()
    {
        if (!Guid.TryParseExact(Id, "N", out _) || !float.IsFinite(Framerate) || Framerate is < 1 or > 120 ||
            Settings == null || MeshVertexCounts == null || MeshVertexCounts.Length == 0 ||
            MeshVertexCounts.Any(n => n < 0) || Frames == null || Frames.Length == 0)
            throw new InvalidDataException("Invalid saved cloth bake.");
        Settings.Validate();
        long vertices = MeshVertexCounts.Sum(n => (long)n);
        if (vertices is <= 0 or > ClothSimulation.MaxVertices ||
            vertices * 3 * sizeof(float) * Frames.Length > MaxBytes)
            throw new InvalidDataException("Saved cloth bake exceeds the supported size.");
        int length = checked((int)vertices * 3);
        foreach (var frame in Frames)
            if (frame == null || frame.Length != length || frame.Any(v => !float.IsFinite(v)))
                throw new InvalidDataException("Saved cloth bake contains invalid frame geometry.");
    }

    public vec3[] GetMeshVertices(int frame, int mesh)
    {
        int offset = 0;
        for (int i = 0; i < mesh; i++) offset += MeshVertexCounts[i] * 3;
        var positions = new vec3[MeshVertexCounts[mesh]];
        var data = Frames[frame];
        for (int i = 0; i < positions.Length; i++, offset += 3)
            positions[i] = new(data[offset], data[offset + 1], data[offset + 2]);
        return positions;
    }
}
