namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public enum BoneConstraintType
{
    InverseKinematics
}

public sealed class BoneConstraint
{
    public bool Enabled { get; set; } = true;
    public BoneConstraintType Type { get; set; } = BoneConstraintType.InverseKinematics;
    public InverseKinematicsSettings InverseKinematics { get; set; } = new();

    public BoneConstraint Clone() => new()
    {
        Enabled = Enabled,
        Type = Type,
        InverseKinematics = InverseKinematics.Clone()
    };
}

public sealed class InverseKinematicsSettings
{
    public string TargetObjectId { get; set; } = "";
    public string PoleObjectId { get; set; } = "";
    /// <summary>Number of real bones, including the constraint owner; internal bend joints do not count.</summary>
    public int ChainLength { get; set; } = 2;
    public int Iterations { get; set; } = 32;
    /// <summary>Rotation of the pole direction around the root-to-target axis, in degrees.</summary>
    public float PoleAngle { get; set; }
    public float Influence { get; set; } = 1f;
    /// <summary>Preserve the constrained bone's pre-IK world orientation while its position follows the chain.</summary>
    public bool LockOrientation { get; set; }
    public List<IkBoneSettings> Bones { get; set; } = new();

    /// <summary>Returns an explicit entry, or null when the default (both controls enabled) applies.</summary>
    public IkBoneSettings? GetBoneSettings(string boneObjectId) =>
        Bones?.FirstOrDefault(bone => bone.BoneObjectId == boneObjectId);

    public InverseKinematicsSettings Clone() => new()
    {
        TargetObjectId = TargetObjectId,
        PoleObjectId = PoleObjectId,
        ChainLength = ChainLength,
        Iterations = Iterations,
        PoleAngle = PoleAngle,
        Influence = Influence,
        LockOrientation = LockOrientation,
        Bones = Bones.Select(bone => bone.Clone()).ToList()
    };
}

public sealed class IkBoneSettings
{
    public string BoneObjectId { get; set; } = "";
    public bool AllowRotation { get; set; } = true;
    public bool AllowBend { get; set; } = true;

    public IkBoneSettings Clone() => new()
    {
        BoneObjectId = BoneObjectId,
        AllowRotation = AllowRotation,
        AllowBend = AllowBend
    };
}
