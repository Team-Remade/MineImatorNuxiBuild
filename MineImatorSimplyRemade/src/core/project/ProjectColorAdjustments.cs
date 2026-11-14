using GlmSharp;
using MineImatorSimplyRemade.core.mdl.material.materials;

namespace MineImatorSimplyRemade.core.project;

public class ProjectColorAdjustments
{
    public ProjectVec3 MultiplyHsv { get; set; } = new() { X = 1f, Y = 1f, Z = 1f };
    public ProjectVec3 AddRgb { get; set; } = new();
    public ProjectVec3 AddHsv { get; set; } = new();
    public ProjectVec3 SubtractRgb { get; set; } = new();
    public ProjectVec3 SubtractHsv { get; set; } = new();

    public MaterialColorAdjustments ToRuntime() => new()
    {
        MultiplyHsv = Read(MultiplyHsv),
        AddRgb = Read(AddRgb),
        AddHsv = Read(AddHsv),
        SubtractRgb = Read(SubtractRgb),
        SubtractHsv = Read(SubtractHsv)
    };

    public static ProjectColorAdjustments FromRuntime(MaterialColorAdjustments value) => new()
    {
        MultiplyHsv = Write(value.MultiplyHsv),
        AddRgb = Write(value.AddRgb),
        AddHsv = Write(value.AddHsv),
        SubtractRgb = Write(value.SubtractRgb),
        SubtractHsv = Write(value.SubtractHsv)
    };

    private static vec3 Read(ProjectVec3 value) => new(value.X, value.Y, value.Z);
    private static ProjectVec3 Write(vec3 value) => new() { X = value.x, Y = value.y, Z = value.z };
}
