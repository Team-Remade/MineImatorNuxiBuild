namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public sealed record ClothVertex(int Mesh, int Vertex);

public sealed record ClothVertexGroup
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Vertex group";
    public ClothVertex[] Vertices { get; init; } = [];
    public bool Pinned { get; init; }
    public string SeamObjectId { get; init; } = "";
    public string SeamGroupId { get; init; } = "";
    public bool ReverseSeam { get; init; }

    public bool Equals(ClothVertexGroup? other) => other != null && Id == other.Id && Name == other.Name &&
        Pinned == other.Pinned && SeamObjectId == other.SeamObjectId && SeamGroupId == other.SeamGroupId &&
        ReverseSeam == other.ReverseSeam && (ReferenceEquals(Vertices, other.Vertices) ||
            Vertices != null && other.Vertices != null && Vertices.SequenceEqual(other.Vertices));
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id); hash.Add(Name); hash.Add(Pinned); hash.Add(SeamObjectId); hash.Add(SeamGroupId); hash.Add(ReverseSeam);
        if (Vertices != null) foreach (var vertex in Vertices) hash.Add(vertex);
        return hash.ToHashCode();
    }
}

public sealed record ClothSettings
{
    public float Gravity { get; init; } = 9.81f;
    public float Damping { get; init; } = 0.02f;
    public float Stretch { get; init; } = 0.95f;
    public float Bend { get; init; } = 0.2f;
    public float Thickness { get; init; } = 0.01f;
    public int Substeps { get; init; } = 4;
    public int Iterations { get; init; } = 8;
    public bool Collisions { get; init; } = true;
    public bool SelfCollision { get; init; } = true;
    public ClothVertexGroup[] Groups { get; init; } = [];

    public bool Equals(ClothSettings? other) => other != null && Gravity == other.Gravity && Damping == other.Damping &&
        Stretch == other.Stretch && Bend == other.Bend && Thickness == other.Thickness && Substeps == other.Substeps &&
        Iterations == other.Iterations && Collisions == other.Collisions && SelfCollision == other.SelfCollision &&
        (ReferenceEquals(Groups, other.Groups) || Groups != null && other.Groups != null && Groups.SequenceEqual(other.Groups));
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Gravity); hash.Add(Damping); hash.Add(Stretch); hash.Add(Bend); hash.Add(Thickness);
        hash.Add(Substeps); hash.Add(Iterations); hash.Add(Collisions); hash.Add(SelfCollision);
        if (Groups != null) foreach (var group in Groups) hash.Add(group);
        return hash.ToHashCode();
    }

    public void Validate()
    {
        if (!float.IsFinite(Gravity) || MathF.Abs(Gravity) > 1000 ||
            !float.IsFinite(Damping) || Damping is < 0 or > 1 ||
            !float.IsFinite(Stretch) || Stretch is < 0 or > 1 ||
            !float.IsFinite(Bend) || Bend is < 0 or > 1 ||
            !float.IsFinite(Thickness) || Thickness is < 0.0001f or > 1 ||
            Substeps is < 1 or > 16 || Iterations is < 1 or > 32 || Groups == null || Groups.Length > 256)
            throw new InvalidDataException("Invalid cloth settings: use finite values within the editor's ranges.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in Groups)
        {
            if (group == null || !Guid.TryParseExact(group.Id, "N", out _) || !ids.Add(group.Id) ||
                string.IsNullOrWhiteSpace(group.Name) || group.Vertices == null || group.Vertices.Length > 12000 ||
                group.Vertices.Any(v => v == null || v.Mesh < 0 || v.Vertex < 0) ||
                group.Vertices.Distinct().Count() != group.Vertices.Length ||
                group.SeamObjectId == null || group.SeamGroupId == null ||
                (group.SeamObjectId.Length == 0) != (group.SeamGroupId.Length == 0))
                throw new InvalidDataException("Cloth vertex groups require unique IDs, names, valid vertices and complete seam references.");
        }
    }
}
