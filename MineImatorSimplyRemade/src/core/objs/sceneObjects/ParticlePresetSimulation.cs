using System.Text.Json.Nodes;
using GlmSharp;
using static MineImatorSimplyRemadeNuxi.core.objs.sceneObjects.ParticlePreset;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public sealed class ParticlePresetSimulation
{
    public sealed class Particle
    {
        public int TypeIndex;
        public vec3 Position, Velocity, Acceleration, VelocityMultiplier;
        public vec3 Rotation, AngularVelocity, AngularAcceleration, AngularMultiplier;
        public vec3 Direction, StartColor, Color, MixColor;
        public mat4 RotationPrefix = mat4.Identity;
        public float DirectionSpeed, DirectionAcceleration, DirectionMultiplier;
        public float Scale, ScaleSpeed, Alpha, AlphaSpeed, SpriteAngle, SpriteAngleSpeed;
        public float Age, Lifetime, AnimationSpeed, MixTime;
        public int Frame, StillFrame;
        public int Ticks;
    }

    public const int SafetyLimit = 10000;
    public const float TimeStep = 1f / 60f;
    private readonly ParticlePreset _preset;
    private readonly Random _random;
    private readonly double[] _spawnRemainders;
    private double _remainder;
    private bool _fired;
    public List<Particle> Particles { get; } = [];
    public vec3? AttractorPosition { get; set; }
    public float AttractorForce { get; set; }

    public ParticlePresetSimulation(ParticlePreset preset, int seed)
    {
        _preset = preset;
        _random = new Random(seed);
        _spawnRemainders = new double[preset.Types.Length];
    }

    public void Advance(float seconds, bool emitting, vec3 origin, mat4 rotation)
    {
        if (!float.IsFinite(seconds) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        _remainder += seconds;
        const double fixedStep = 1d / 60d;
        while (_remainder + 1e-7 >= fixedStep)
        {
            Tick(emitting, origin, rotation);
            _remainder -= fixedStep;
        }
    }

    private void Tick(bool emitting, vec3 origin, mat4 rotation)
    {
        JsonObject settings = _preset.Settings;
        bool constant = Flag(settings, "spawn_constant", true);
        if (emitting && (constant || !_fired))
        {
            for (int type = 0; type < _preset.Types.Length; type++)
            {
                double count = Math.Max(0, Number(settings, "spawn_amount")) *
                    Math.Max(0, Number(_preset.Types[type], "spawn_rate", 1));
                if (constant)
                {
                    // Mine-imator's continuous amount is particles per minute, not per second.
                    _spawnRemainders[type] += count / 3600d;
                    count = Math.Floor(_spawnRemainders[type] + 1e-8);
                    _spawnRemainders[type] -= count;
                }
                int amount = (int)Math.Min(count, SafetyLimit);
                for (int index = 0; index < amount; index++)
                {
                    int limit = Flag(settings, "destroy_at_amount")
                        ? (int)Math.Clamp(Number(settings, "destroy_at_amount_val", 200), 1, SafetyLimit)
                        : SafetyLimit;
                    if (Particles.Count >= limit)
                        Particles.RemoveAt(0);
                    Particles.Add(Spawn(type, origin, rotation));
                }
            }
            _fired = true;
        }

        for (int index = Particles.Count - 1; index >= 0; index--)
        {
            Particle p = Particles[index];
            JsonObject type = _preset.Types[p.TypeIndex];
            p.Age = ++p.Ticks / 60f;
            bool expired = Flag(settings, "destroy_at_time", true) && p.Age >= p.Lifetime;
            expired |= Animate(p, type) && Flag(settings, "destroy_at_animation_finish", true);
            if (!expired)
            {
                p.Position += (p.Velocity + p.Direction * p.DirectionSpeed) * TimeStep;
                p.Velocity = (p.Velocity + p.Acceleration * TimeStep) * p.VelocityMultiplier;
                p.DirectionSpeed = (p.DirectionSpeed + p.DirectionAcceleration * TimeStep) * p.DirectionMultiplier;
                p.Rotation += p.AngularVelocity * TimeStep;
                p.AngularVelocity = (p.AngularVelocity + p.AngularAcceleration * TimeStep) * p.AngularMultiplier;
                if (AttractorPosition is { } attractor)
                {
                    vec3 delta = attractor - (Flag(type, "orbit") ? p.Position : origin);
                    float force = Math.Max(0, AttractorForce);
                    for (int axis = 0; axis < 3; axis++)
                        p.Velocity[axis] += Math.Clamp(delta[axis], -force, force) * TimeStep;
                }
                p.Scale += p.ScaleSpeed * TimeStep;
                p.Alpha += p.AlphaSpeed * TimeStep;
                p.SpriteAngle += p.SpriteAngleSpeed * TimeStep;
                if (Flag(type, "color_mix_enabled"))
                    p.Color = p.StartColor + (p.MixColor - p.StartColor) * Math.Clamp(p.Age / p.MixTime, 0, 1);
                expired = p.Scale <= 0 || p.Alpha <= 0;
                if (Collide(p, type, origin) && Number(settings, "destroy_at_bounding_box") != 0)
                    expired = true;
            }
            if (expired) Particles.RemoveAt(index);
        }
    }

    private Particle Spawn(int typeIndex, vec3 origin, mat4 rotation)
    {
        JsonObject type = _preset.Types[typeIndex];
        JsonObject settings = _preset.Settings;
        vec3 angle = SampleVector(type, "angle", "angle_extend", _random);
        mat4 directionRotation = DegreesRotation(angle);
        vec3 direction = (rotation * directionRotation * new vec4(0, 1, 0, 0)).xyz.Normalized;
        var p = new Particle
        {
            TypeIndex = typeIndex,
            Position = origin + (Flag(settings, "spawn_region_use") && Flag(type, "spawn_region", true)
                ? SpawnOffset() : vec3.Zero),
            Velocity = SampleVector(type, "spd", "spd_extend", _random) / 16f,
            Acceleration = SampleVector(type, "spd_add", "spd_extend", _random) * (60f / 16f),
            VelocityMultiplier = Multiplier(SampleVector(type, "spd_mul", "spd_extend", _random, 1)),
            Rotation = SampleVector(type, "rot", "rot_extend", _random),
            RotationPrefix = Flag(type, "rot_spawner_angle") ? rotation : mat4.Identity,
            AngularVelocity = SampleVector(type, "rot_spd", "rot_spd_extend", _random),
            AngularAcceleration = SampleVector(type, "rot_spd_add", "rot_spd_extend", _random) * 60f,
            AngularMultiplier = Multiplier(SampleVector(type, "rot_spd_mul", "rot_spd_extend", _random, 1)),
            Direction = direction,
            DirectionSpeed = Sample(type, "angle_speed", _random) / 16f,
            DirectionAcceleration = Sample(type, "angle_speed_add", _random) * (60f / 16f),
            DirectionMultiplier = Multiplier(Sample(type, "angle_speed_mul", _random, 1)),
            Scale = Sample(type, "scale", _random, 1),
            ScaleSpeed = Sample(type, "scale_add", _random),
            Alpha = Sample(type, "alpha", _random, 1),
            AlphaSpeed = Sample(type, "alpha_add", _random),
            SpriteAngle = Sample(type, "sprite_angle", _random),
            SpriteAngleSpeed = Sample(type, "sprite_angle_add", _random),
            Lifetime = Math.Max(TimeStep, Sample(settings, "destroy_at_time_seconds", _random, 10)),
            AnimationSpeed = Math.Max(0, Sample(type, "sprite_animation_speed", _random, 5)),
            StartColor = SampleColor(type, "color", _random),
            MixColor = SampleColor(type, "color_mix", _random),
            MixTime = Math.Max(TimeStep, Sample(type, "color_mix_time", _random, 3))
        };
        p.Color = p.StartColor;
        if (Text(type, "temp_type", "template") == "template")
        {
            int frames = Sprite(Text(type, "sprite_template", "generic")).Frames;
            p.StillFrame = Flag(type, "sprite_template_random_frame") ? _random.Next(frames) : 0;
        }
        return p;
    }

    public static mat4 DegreesRotation(vec3 degrees) => mat4.RotateY(degrees.y * MathF.PI / 180f) *
        mat4.RotateX(degrees.x * MathF.PI / 180f) * mat4.RotateZ(degrees.z * MathF.PI / 180f);

    private static float Multiplier(float value) => MathF.Pow(Math.Max(0, value), 1f / 32f);
    private static vec3 Multiplier(vec3 value) =>
        new(Multiplier(value.x), Multiplier(value.y), Multiplier(value.z));

    private vec3 SpawnOffset()
    {
        JsonObject settings = _preset.Settings;
        if (Text(settings, "spawn_region_type", "box") == "sphere")
        {
            vec3 point;
            do
                point = new vec3(Range(-1, 1, _random), Range(-1, 1, _random), Range(-1, 1, _random));
            while (point.LengthSqr > 1);
            return point * Math.Max(0, Number(settings, "spawn_region_sphere_radius")) / 16f;
        }
        vec3 size = RegionSize();
        return new vec3(Range(-size.x, size.x, _random), Range(-size.y, size.y, _random),
            Range(-size.z, size.z, _random)) / 32f;
    }

    private vec3 RegionSize() => Text(_preset.Settings, "spawn_region_type", "box") == "cube"
        ? new vec3(Math.Max(0, Number(_preset.Settings, "spawn_region_cube_size")))
        : Vector(_preset.Settings, "spawn_region_box_size");

    private static bool Animate(Particle p, JsonObject type)
    {
        if (Text(type, "temp_type", "template") != "template") return false;
        int frames = Sprite(Text(type, "sprite_template", "generic")).Frames;
        if (Flag(type, "sprite_template_still_frame") || frames == 1)
        {
            p.Frame = p.StillFrame;
            return false;
        }
        float progress = p.Age * p.AnimationSpeed / (frames - 1);
        int onEnd = (int)Number(type, "sprite_animation_onend");
        bool finished = progress >= 1 && onEnd == 0;
        progress = onEnd switch
        {
            1 => progress % 1,
            2 => 1 - MathF.Abs(progress % 2 - 1),
            _ => Math.Min(1, progress)
        };
        if (Flag(type, "sprite_template_reverse")) progress = 1 - progress;
        p.Frame = Math.Clamp((int)MathF.Round(progress * (frames - 1)), 0, frames - 1);
        return finished;
    }

    private bool Collide(Particle p, JsonObject type, vec3 origin)
    {
        JsonObject settings = _preset.Settings;
        string bounds = Text(settings, "bounding_box_type", "none");
        if (bounds == "none" || !Flag(type, "bounding_box", true)) return false;
        vec3 normal = vec3.Zero;
        bool hit = false;
        if (bounds == "ground")
        {
            float ground = Number(settings, "bounding_box_ground_z") / 16f;
            if (p.Position.y < ground)
            {
                p.Position.y = ground;
                normal = vec3.UnitY;
                hit = true;
            }
        }
        else if (bounds == "spawn" && !Flag(settings, "spawn_region_use"))
            return false;
        else if (bounds == "spawn" && Text(settings, "spawn_region_type") == "sphere")
        {
            float radius = Math.Max(0, Number(settings, "spawn_region_sphere_radius")) / 16f;
            vec3 offset = p.Position - origin;
            if (offset.Length > radius)
            {
                normal = -offset.Normalized;
                p.Position = origin - normal * radius;
                hit = true;
            }
        }
        else
        {
            vec3 min = bounds == "spawn" ? origin - RegionSize() / 32f :
                Vector(settings, "bounding_box_custom_start") / 16f +
                (Flag(settings, "bounding_box_relative") ? origin : vec3.Zero);
            vec3 max = bounds == "spawn" ? origin + RegionSize() / 32f :
                Vector(settings, "bounding_box_custom_end") / 16f +
                (Flag(settings, "bounding_box_relative") ? origin : vec3.Zero);
            for (int axis = 0; axis < 3; axis++)
            {
                float lower = Math.Min(min[axis], max[axis]), upper = Math.Max(min[axis], max[axis]);
                if (p.Position[axis] < lower || p.Position[axis] > upper)
                {
                    normal[axis] = p.Position[axis] < lower ? 1 : -1;
                    p.Position[axis] = Math.Clamp(p.Position[axis], lower, upper);
                    hit = true;
                }
            }
        }
        if (hit && Flag(type, "bounce", true))
        {
            normal = normal.Normalized;
            float factor = Math.Max(0, Number(type, "bounce_factor", 0.5f));
            p.Velocity -= (1 + factor) * vec3.Dot(p.Velocity, normal) * normal;
            p.Direction -= 2 * vec3.Dot(p.Direction, normal) * normal;
            p.DirectionSpeed *= factor;
            p.AngularVelocity *= -0.5f;
        }
        return hit;
    }
}
