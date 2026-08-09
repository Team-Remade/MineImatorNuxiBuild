using GlmSharp;

namespace MineImatorSimplyRemadeNuxi.core.objs.sceneObjects;

public enum CameraEffectType
{
    CameraShake = 0,
    FilmGrain = 1
}

public class FilmGrainSettings
{
    public float Strength = 0.15f;
    public float Saturation = 0.5f;
    public float Size = 1f;
}

public enum CameraShakeMode
{
    Rotational = 0,
    Positional = 1,
    Both = 2
}

public class CameraShakeSettings
{
    public CameraShakeMode Mode = CameraShakeMode.Both;

    // Overall shake multiplier applied to all shake axes.
    public float Trauma = 1f;

    // For rotational shake these are radians. For positional shake these are world units.
    public vec3 Strength = new vec3(0.03f, 0.03f, 0.03f);

    // Per-axis angular/noise speed multipliers.
    public vec3 Speed = new vec3(3f, 3.5f, 2.5f);

    // Per-axis phase offsets for deterministic shake pattern variation.
    public vec3 Offset = vec3.Zero;
}

public class CameraEffect
{
    public CameraEffectType Type = CameraEffectType.CameraShake;
    public CameraShakeSettings Shake = new CameraShakeSettings();
    public FilmGrainSettings FilmGrain = new FilmGrainSettings();
}
