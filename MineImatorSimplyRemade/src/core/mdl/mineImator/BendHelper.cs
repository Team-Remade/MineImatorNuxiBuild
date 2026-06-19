using GlmSharp;
using System;
using System.Collections.Generic;

namespace MineImatorSimplyRemade.core.mdl.mineImator;

/// <summary>
/// Identifies which directional half of a part is bent.
/// Matches Modelbench's e_part enum.
/// </summary>
public enum BendPart
{
    Right,
    Left,
    Front,
    Back,
    Upper,
    Lower
}

/// <summary>
/// Bend style setting for character models.
/// </summary>
public enum BendStyle
{
    Realistic,
    Blocky,
    ProjectDefault
}

/// <summary>
/// Holds all bend parameters for a part, derived from MiBend JSON data.
/// All size/offset values are in Minecraft pixels (not world units).
/// </summary>
public struct BendParams
{
    /// <summary>Bend angle in degrees (X, Y, Z axes)</summary>
    public vec3 Angle;

    /// <summary>Bend pivot offset in pixels along the bend axis</summary>
    public float BendOffset;

    /// <summary>Bend region size in pixels (default 4)</summary>
    public float BendSize;

    /// <summary>Whether BendSize was explicitly set in the JSON (vs defaulting to 4)</summary>
    public bool ExplicitBendSize;

    /// <summary>Number of bend segments (default auto-calculated)</summary>
    public float? Detail;

    /// <summary>Which directional half of the part is bent</summary>
    public BendPart Part;

    /// <summary>Which axes are active for bending</summary>
    public bool AxisX, AxisY, AxisZ;

    /// <summary>Whether to invert the bend angle per axis</summary>
    public bool InvertX, InvertY, InvertZ;

    /// <summary>Minimum allowed bend angle per axis (degrees)</summary>
    public vec3 DirectionMin;

    /// <summary>Maximum allowed bend angle per axis (degrees)</summary>
    public vec3 DirectionMax;

    /// <summary>
    /// When true, this part adds its parent's bend angle to its own.
    /// Matches Modelbench's inherit_bend / INHERIT_BEND.
    /// </summary>
    public bool InheritBend;
}

/// <summary>
/// Provides bend math utilities matching Modelbench's GML implementation.
/// Ported from the Godot version in simply-remade-nuxi, adapted to use GlmSharp
/// math types (vec3, mat4) instead of Godot Vector3/Transform3D.
/// </summary>
public static class BendHelper
{
    /// <summary>
    /// Parses a MiBend JSON object into a BendParams struct.
    /// </summary>
    public static BendParams? ParseBend(MiBend bend, float[] partScale, BendStyle bendStyle = BendStyle.ProjectDefault)
    {
        if (bend == null) return null;

        // Parse part direction
        BendPart part = BendPart.Upper;
        if (!string.IsNullOrEmpty(bend.Part))
        {
            switch (bend.Part.ToLowerInvariant())
            {
                case "right":  part = BendPart.Right;  break;
                case "left":   part = BendPart.Left;   break;
                case "front":  part = BendPart.Front;  break;
                case "back":   part = BendPart.Back;   break;
                case "upper":  part = BendPart.Upper;  break;
                case "lower":  part = BendPart.Lower;  break;
            }
        }

        // Parse axes
        bool axisX = false, axisY = false, axisZ = false;
        var axisIndices = new List<int>();

        if (bend.Axis is System.Text.Json.JsonElement axisElem)
        {
            if (axisElem.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                ParseAxisString(axisElem.GetString(), ref axisX, ref axisY, ref axisZ, axisIndices);
            }
            else if (axisElem.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in axisElem.EnumerateArray())
                {
                    if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                        ParseAxisString(item.GetString(), ref axisX, ref axisY, ref axisZ, axisIndices);
                }
            }
        }
        else if (bend.Axis is string axisStr)
        {
            ParseAxisString(axisStr, ref axisX, ref axisY, ref axisZ, axisIndices);
        }

        if (!axisX && !axisY && !axisZ) return null;

        // Parse direction min/max
        vec3 dirMin = new vec3(-180, -180, -180);
        vec3 dirMax = new vec3(180, 180, 180);

        if (bend.DirectionMin != null)
        {
            if (bend.DirectionMin.Length == 1 && axisIndices.Count == 1)
                SetVec3Component(ref dirMin, axisIndices[0], bend.DirectionMin[0]);
            else
                for (int i = 0; i < Math.Min(bend.DirectionMin.Length, axisIndices.Count); i++)
                    SetVec3Component(ref dirMin, axisIndices[i], bend.DirectionMin[i]);
        }

        if (bend.DirectionMax != null)
        {
            if (bend.DirectionMax.Length == 1 && axisIndices.Count == 1)
                SetVec3Component(ref dirMax, axisIndices[0], bend.DirectionMax[0]);
            else
                for (int i = 0; i < Math.Min(bend.DirectionMax.Length, axisIndices.Count); i++)
                    SetVec3Component(ref dirMax, axisIndices[i], bend.DirectionMax[i]);
        }

        // Parse invert
        bool invertX = false, invertY = false, invertZ = false;
        if (bend.Invert != null)
        {
            if (bend.Invert.Length == 1 && axisIndices.Count == 1)
                SetBoolComponent(ref invertX, ref invertY, ref invertZ, axisIndices[0], bend.Invert[0]);
            else
                for (int i = 0; i < Math.Min(bend.Invert.Length, axisIndices.Count); i++)
                    SetBoolComponent(ref invertX, ref invertY, ref invertZ, axisIndices[i], bend.Invert[i]);
        }

        // Parse default angle
        vec3 angle = vec3.Zero;
        if (bend.Angle != null)
        {
            if (bend.Angle.Length == 1 && axisIndices.Count == 1)
                SetVec3Component(ref angle, axisIndices[0], bend.Angle[0]);
            else
                for (int i = 0; i < Math.Min(bend.Angle.Length, axisIndices.Count); i++)
                    SetVec3Component(ref angle, axisIndices[i], bend.Angle[i]);
        }

        // Clamp angle to direction limits and apply invert
        angle = ClampAndInvertAngle(angle, dirMin, dirMax, invertX, invertY, invertZ, axisX, axisY, axisZ);

        // Parse offset, size, and detail (in pixels)
        float offset = bend.Offset ?? 0.0f;
        bool explicitBendSize = bend.Size.HasValue;
        float? detail = bend.Detail;

        // Resolve bend style
        BendStyle effectiveStyle = (bendStyle == BendStyle.ProjectDefault)
            ? MineImatorLoader.ProjectBendStyle
            : bendStyle;

        float defaultBendSize = (effectiveStyle == BendStyle.Realistic) ? 4.0f : 1.0f;
        float size = bend.Size ?? defaultBendSize;

        // Scale offset/size by part scale (matching el_update_part.gml)
        float scaleX = partScale != null && partScale.Length > 0 ? partScale[0] : 1.0f;
        float scaleY = partScale != null && partScale.Length > 1 ? partScale[1] : 1.0f;
        float scaleZ = partScale != null && partScale.Length > 2 ? partScale[2] : 1.0f;

        switch (part)
        {
            case BendPart.Right: case BendPart.Left:
                offset *= scaleX;
                size   *= scaleX;
                break;
            case BendPart.Upper: case BendPart.Lower:
                offset *= scaleY;
                size   *= scaleY;
                break;
            case BendPart.Front: case BendPart.Back:
                offset *= scaleZ;
                size   *= scaleZ;
                break;
        }

        return new BendParams
        {
            Angle           = angle,
            BendOffset      = offset,
            BendSize        = size,
            ExplicitBendSize = explicitBendSize,
            Detail          = detail,
            Part            = part,
            AxisX           = axisX,
            AxisY           = axisY,
            AxisZ           = axisZ,
            InvertX         = invertX,
            InvertY         = invertY,
            InvertZ         = invertZ,
            DirectionMin    = dirMin,
            DirectionMax    = dirMax,
            InheritBend     = (bend.InheritBend ?? 0f) > 0f
        };
    }

    /// <summary>
    /// Computes the bend vector for a given weight (0-1).
    /// X and Z use ease-in-out-quint; Y (height) uses linear weighting.
    /// </summary>
    public static vec3 GetBendVector(vec3 angle, float weight)
    {
        return new vec3(
            angle.x * EaseInOutQuint(weight),
            angle.y * weight,
            angle.z * EaseInOutQuint(weight)
        );
    }

    /// <summary>
    /// Builds the bend transformation matrix for a given bend vector.
    /// Matches Modelbench's model_part_get_bend_matrix() exactly.
    /// Returns a mat4 that performs: Translate(pivot) * RotateYXZ(bend) * Scale(matScale) * Translate(-pivot)
    /// </summary>
    public static mat4 GetBendMatrix(BendParams b, vec3 bendVec, vec3 shapePosition,
        vec3 shapeScale = default, vec3 matrixScale = default)
    {
        if (bendVec.x == 0 && bendVec.y == 0 && bendVec.z == 0 &&
            (matrixScale == default || matrixScale == vec3.Ones))
            return mat4.Identity;

        if (matrixScale == default || matrixScale == vec3.Zero)
            matrixScale = vec3.Ones;

        // Build rotation: RotateYXZ matching GML's matrix_build rotation order
        mat4 rotMat = mat4.Identity;
        rotMat = rotMat * mat4.RotateY(DegToRad(bendVec.y));
        rotMat = rotMat * mat4.RotateX(DegToRad(bendVec.x));
        rotMat = rotMat * mat4.RotateZ(DegToRad(bendVec.z));

        // Apply matrix scale
        mat4 scaleMat = mat4.Scale(matrixScale);
        mat4 rotScaleMat = rotMat * scaleMat;

        // Calculate the bend pivot position in part-local space
        if (shapeScale == vec3.Zero) shapeScale = vec3.Ones;
        vec3 scaledShapePos = new vec3(
            shapePosition.x * shapeScale.x,
            shapePosition.y * shapeScale.y,
            shapePosition.z * shapeScale.z
        );
        vec3 pivotPos = vec3.Zero;
        switch (b.Part)
        {
            case BendPart.Right:
            case BendPart.Left:
                pivotPos.x = b.BendOffset / 16.0f - scaledShapePos.x;
                break;
            case BendPart.Front:
            case BendPart.Back:
                pivotPos.z = b.BendOffset / 16.0f - scaledShapePos.z;
                break;
            case BendPart.Upper:
            case BendPart.Lower:
                pivotPos.y = b.BendOffset / 16.0f - scaledShapePos.y;
                break;
        }

        // Build: Translate(pivot) * RotScale * Translate(-pivot)
        // Which is equivalent to: v' = RotScale*(v - pivot) + pivot
        mat4 tPos  = mat4.Translate(pivotPos);
        mat4 tNeg  = mat4.Translate(-pivotPos);
        return tPos * rotScaleMat * tNeg;
    }

    // ── Easing functions ──────────────────────────────────────────────────────

    public static float EaseInOutQuint(float t)
    {
        float xx2 = t * 2.0f;
        if (t <= 0.0f) return 0.0f;
        if (t >= 1.0f) return 1.0f;
        if (xx2 < 1.0f)
            return 0.5f * xx2 * xx2 * xx2 * xx2 * xx2;
        else
            return 0.5f * ((xx2 - 2.0f) * (xx2 - 2.0f) * (xx2 - 2.0f) * (xx2 - 2.0f) * (xx2 - 2.0f) + 2.0f);
    }

    public static float EaseInCubic(float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);
        return t * t * t;
    }

    /// <summary>
    /// Calculates the number of bend segments for a given bend size.
    /// </summary>
    public static float CalculateSegmentCount(float bendSize, bool sharpBend, float? detail = null)
    {
        if (detail.HasValue) return Math.Max(2, detail.Value);
        if (sharpBend) return 2;
        return Math.Max(bendSize, 2);
    }

    /// <summary>
    /// Calculates anti-pinching scale correction for blocky bending.
    /// </summary>
    public static vec3 GetBendScaleCorrection(float bendStart, float bendEnd, float weight,
        float bendPosition, vec3 bendAngle, BendParams bendParams)
    {
        if (bendPosition > bendStart && bendPosition < bendEnd)
        {
            vec3 bendScale;
            if (weight <= 0.5f)
                bendScale = new vec3(weight * 2, weight * 2, weight * 2);
            else
                bendScale = new vec3((1 - weight) * 2, (1 - weight) * 2, (1 - weight) * 2);

            int bendAxis = -1;
            if (bendParams.AxisX && !bendParams.AxisY && !bendParams.AxisZ)
                bendAxis = 0;
            else if (!bendParams.AxisX && !bendParams.AxisY && bendParams.AxisZ)
                bendAxis = 2;

            if (bendAxis == -1) return vec3.Zero;

            float bendAng = Math.Abs(bendAngle[bendAxis]);
            if (bendAng > 90) bendAng -= (bendAng - 90) * 2;
            float bendPerc = Math.Clamp(bendAng / 90.0f, 0, 1);
            bendScale *= bendPerc;

            bendScale.x = EaseInCubic(bendScale.x);
            bendScale.y = EaseInCubic(bendScale.y);
            bendScale.z = EaseInCubic(bendScale.z);
            bendScale /= 2.5f;
            bendScale[bendAxis] = 0;

            return bendScale;
        }
        return vec3.Zero;
    }

    // ── Math helpers ──────────────────────────────────────────────────────────

    public static float DegToRad(float deg) => deg * MathF.PI / 180f;

    /// <summary>Transforms a vec3 by a mat4 (treats vec3 as a position, w=1).</summary>
    public static vec3 TransformPoint(mat4 m, vec3 v)
    {
        vec4 r = m * new vec4(v, 1f);
        return new vec3(r.x, r.y, r.z);
    }

    /// <summary>Transforms a vec3 direction by a mat4 (w=0, no translation).</summary>
    public static vec3 TransformDirection(mat4 m, vec3 v)
    {
        vec4 r = m * new vec4(v, 0f);
        return new vec3(r.x, r.y, r.z).Normalized;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void ParseAxisString(string axis, ref bool axisX, ref bool axisY, ref bool axisZ,
        List<int> indices)
    {
        switch (axis?.ToLowerInvariant())
        {
            case "x": axisX = true; indices.Add(0); break;
            case "z": axisZ = true; indices.Add(2); break;
            case "y": axisY = true; indices.Add(1); break;
        }
    }

    private static void SetVec3Component(ref vec3 v, int axis, float value)
    {
        switch (axis)
        {
            case 0: v.x = value; break;
            case 1: v.y = value; break;
            case 2: v.z = value; break;
        }
    }

    private static void SetBoolComponent(ref bool bx, ref bool by, ref bool bz, int axis, bool value)
    {
        switch (axis)
        {
            case 0: bx = value; break;
            case 1: by = value; break;
            case 2: bz = value; break;
        }
    }

    private static vec3 ClampAndInvertAngle(vec3 angle, vec3 dirMin, vec3 dirMax,
        bool invertX, bool invertY, bool invertZ, bool axisX, bool axisY, bool axisZ)
    {
        angle.x = Math.Clamp(angle.x, dirMin.x, dirMax.x);
        angle.y = Math.Clamp(angle.y, dirMin.y, dirMax.y);
        angle.z = Math.Clamp(angle.z, dirMin.z, dirMax.z);

        if (invertX) angle.x *= -1;
        if (invertY) angle.y *= -1;
        if (invertZ) angle.z *= -1;

        if (!axisX) angle.x = 0;
        if (!axisY) angle.y = 0;
        if (!axisZ) angle.z = 0;

        return angle;
    }
}
