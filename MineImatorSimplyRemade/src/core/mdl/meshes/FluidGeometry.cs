using GlmSharp;
using MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

namespace MineImatorSimplyRemade.core.mdl.meshes;

public static class FluidGeometry
{
    private static readonly int[][] Tetrahedra =
    [
        [0, 5, 1, 6], [0, 1, 2, 6], [0, 2, 3, 6],
        [0, 3, 7, 6], [0, 7, 4, 6], [0, 4, 5, 6]
    ];
    private static readonly int[] CornerX = [0, 1, 1, 0, 0, 1, 1, 0];
    private static readonly int[] CornerY = [0, 0, 1, 1, 0, 0, 1, 1];
    private static readonly int[] CornerZ = [0, 0, 0, 0, 1, 1, 1, 1];

    public static void Populate(Mesh mesh, FluidSettings settings, IReadOnlyList<FluidParticle> particles)
    {
        mesh.Vertices.Clear();
        mesh.Normals.Clear();
        mesh.TexCoords.Clear();
        mesh.Indices = null;
        if (settings.RenderMode == FluidRenderMode.Particles)
            AddParticles(mesh, settings, particles);
        else
            AddSurface(mesh, settings, particles);
    }

    private static void AddParticles(Mesh mesh, FluidSettings settings, IReadOnlyList<FluidParticle> particles)
    {
        const int segments = 8, rings = 4;
        foreach (FluidParticle particle in particles)
            for (int ring = 0; ring < rings; ring++)
            for (int segment = 0; segment < segments; segment++)
            {
                vec3 a = SpherePoint(particle.Position, settings.ParticleRadius, segment, ring, segments, rings);
                vec3 b = SpherePoint(particle.Position, settings.ParticleRadius, segment + 1, ring, segments, rings);
                vec3 c = SpherePoint(particle.Position, settings.ParticleRadius, segment, ring + 1, segments, rings);
                vec3 d = SpherePoint(particle.Position, settings.ParticleRadius, segment + 1, ring + 1, segments, rings);
                AddTriangle(mesh, a, b, c);
                AddTriangle(mesh, b, d, c);
            }
    }

    private static vec3 SpherePoint(vec3 center, float radius, int segment, int ring, int segments, int rings)
    {
        float longitude = MathF.Tau * segment / segments;
        float latitude = MathF.PI * ring / rings;
        vec3 normal = new(
            MathF.Sin(latitude) * MathF.Cos(longitude),
            MathF.Cos(latitude),
            MathF.Sin(latitude) * MathF.Sin(longitude));
        return center + normal * radius;
    }

    private static void AddSurface(Mesh mesh, FluidSettings settings, IReadOnlyList<FluidParticle> particles)
    {
        int count = settings.SurfaceResolution + 1;
        var field = new float[count, count, count];
        vec3 min = -settings.Bounds;
        vec3 step = settings.Bounds * 2f / settings.SurfaceResolution;
        float radius2 = settings.SurfaceRadius * settings.SurfaceRadius;
        foreach (FluidParticle particle in particles)
        {
            vec3 relativeMin = (particle.Position - new vec3(settings.SurfaceRadius) - min) / step;
            vec3 relativeMax = (particle.Position + new vec3(settings.SurfaceRadius) - min) / step;
            int x0 = Math.Clamp((int)MathF.Floor(relativeMin.x), 0, count - 1);
            int x1 = Math.Clamp((int)MathF.Ceiling(relativeMax.x), 0, count - 1);
            int y0 = Math.Clamp((int)MathF.Floor(relativeMin.y), 0, count - 1);
            int y1 = Math.Clamp((int)MathF.Ceiling(relativeMax.y), 0, count - 1);
            int z0 = Math.Clamp((int)MathF.Floor(relativeMin.z), 0, count - 1);
            int z1 = Math.Clamp((int)MathF.Ceiling(relativeMax.z), 0, count - 1);
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                vec3 position = min + new vec3(x, y, z) * step;
                float distance2 = (position - particle.Position).LengthSqr;
                if (distance2 >= radius2) continue;
                float falloff = 1f - distance2 / radius2;
                field[x, y, z] += falloff * falloff * falloff;
            }
        }

        var cube = new GridPoint[8];
        for (int z = 0; z < settings.SurfaceResolution; z++)
        for (int y = 0; y < settings.SurfaceResolution; y++)
        for (int x = 0; x < settings.SurfaceResolution; x++)
        {
            for (int i = 0; i < 8; i++)
            {
                int gx = x + CornerX[i], gy = y + CornerY[i], gz = z + CornerZ[i];
                vec3 normal = new(
                    field[Math.Max(0, gx - 1), gy, gz] - field[Math.Min(count - 1, gx + 1), gy, gz],
                    field[gx, Math.Max(0, gy - 1), gz] - field[gx, Math.Min(count - 1, gy + 1), gz],
                    field[gx, gy, Math.Max(0, gz - 1)] - field[gx, gy, Math.Min(count - 1, gz + 1)]);
                cube[i] = new(min + new vec3(gx, gy, gz) * step, field[gx, gy, gz], normal);
            }
            foreach (int[] tetra in Tetrahedra)
                AddTetrahedron(mesh, cube, tetra, settings.SurfaceThreshold);
        }
    }

    private static void AddTetrahedron(Mesh mesh, GridPoint[] cube, int[] tetra, float threshold)
    {
        var inside = tetra.Where(index => cube[index].Value >= threshold).ToArray();
        if (inside.Length is 0 or 4) return;
        if (inside.Length is 1 or 3)
        {
            int isolated = inside.Length == 1 ? inside[0] : tetra.First(index => cube[index].Value < threshold);
            var points = tetra.Where(index => index != isolated)
                .Select(index => Intersect(cube[isolated], cube[index], threshold)).ToArray();
            AddOrientedTriangle(mesh, points[0].Position, points[1].Position, points[2].Position,
                points[0].Normal, points[1].Normal, points[2].Normal);
            return;
        }

        int a = inside[0], b = inside[1];
        var outside = tetra.Where(index => index != a && index != b).ToArray();
        int c = outside[0], d = outside[1];
        GridPoint ac = Intersect(cube[a], cube[c], threshold);
        GridPoint ad = Intersect(cube[a], cube[d], threshold);
        GridPoint bc = Intersect(cube[b], cube[c], threshold);
        GridPoint bd = Intersect(cube[b], cube[d], threshold);
        AddOrientedTriangle(mesh, ac.Position, ad.Position, bc.Position, ac.Normal, ad.Normal, bc.Normal);
        AddOrientedTriangle(mesh, ad.Position, bd.Position, bc.Position, ad.Normal, bd.Normal, bc.Normal);
    }

    private static GridPoint Intersect(GridPoint a, GridPoint b, float threshold)
    {
        float t = (threshold - a.Value) / (b.Value - a.Value);
        t = float.IsFinite(t) ? Math.Clamp(t, 0f, 1f) : 0.5f;
        vec3 position = a.Position + (b.Position - a.Position) * t;
        vec3 normal = a.Normal + (b.Normal - a.Normal) * t;
        return new(position, threshold, normal);
    }

    private static void AddOrientedTriangle(Mesh mesh, vec3 a, vec3 b, vec3 c,
        vec3 normalA, vec3 normalB, vec3 normalC)
    {
        vec3 geometric = vec3.Cross(b - a, c - a);
        vec3 averageNormal = normalA + normalB + normalC;
        bool reverse = vec3.Dot(geometric, averageNormal) < 0;
        if (reverse)
        {
            (b, c) = (c, b);
            (normalB, normalC) = (normalC, normalB);
        }
        AddTriangle(mesh, a, b, c, normalA, normalB, normalC);
    }

    private static void AddTriangle(Mesh mesh, vec3 a, vec3 b, vec3 c,
        vec3? normalA = null, vec3? normalB = null, vec3? normalC = null)
    {
        vec3 normal = vec3.Cross(b - a, c - a);
        if (normal.LengthSqr > 1e-12f) normal = normal.Normalized; else normal = vec3.UnitY;
        mesh.Vertices.Add(a); mesh.Vertices.Add(b); mesh.Vertices.Add(c);
        mesh.Normals.Add(normalA is { } na && na.LengthSqr > 1e-12f ? na.Normalized : normal);
        mesh.Normals.Add(normalB is { } nb && nb.LengthSqr > 1e-12f ? nb.Normalized : normal);
        mesh.Normals.Add(normalC is { } nc && nc.LengthSqr > 1e-12f ? nc.Normalized : normal);
        mesh.TexCoords.Add(vec2.Zero); mesh.TexCoords.Add(vec2.Zero); mesh.TexCoords.Add(vec2.Zero);
    }

    private readonly record struct GridPoint(vec3 Position, float Value, vec3 Normal);
}
