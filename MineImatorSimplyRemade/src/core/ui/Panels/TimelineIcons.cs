using System;
using MineImatorSimplyRemade.core;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui.Panels;

/// <summary>
/// Loads the timeline transport-button SVG icons as OpenGL textures.
/// Call <see cref="Initialize"/> once the GL context is available.
/// </summary>
public static class TimelineIcons
{
    public static uint Play       { get; private set; }
    public static uint Pause      { get; private set; }
    public static uint Stop       { get; private set; }
    public static uint StepBack   { get; private set; }
    public static uint StepForward{ get; private set; }
    public static uint JumpStart  { get; private set; }
    public static uint JumpEnd    { get; private set; }
    public static uint AutoKey    { get; private set; }
    public static uint Loop       { get; private set; }
    public static uint Ghost      { get; private set; }

    public static bool IsLoaded   { get; private set; }

    private const string Prefix = "MineImatorSimplyRemade.assets.img.button.";

    public static void Initialize(GL gl, int iconSize = 20)
    {
        if (IsLoaded) return;

        Play        = Load(gl, Prefix + "mdi--play.svg",              iconSize);
        Pause       = Load(gl, Prefix + "material-symbols--pause.svg",iconSize);
        Stop        = Load(gl, Prefix + "material-symbols--stop.svg", iconSize);
        StepBack    = Load(gl, Prefix + "mdi--step-backward.svg",     iconSize);
        StepForward = Load(gl, Prefix + "mdi--step-forward.svg",      iconSize);
        JumpStart   = Load(gl, Prefix + "vaadin--step-backward.svg",  iconSize);
        JumpEnd     = Load(gl, Prefix + "vaadin--step-forward.svg",   iconSize);
        AutoKey     = Load(gl, Prefix + "bi--dot.svg",                iconSize);
        Loop        = Load(gl, Prefix + "ic--outline-loop.svg",       iconSize);
        Ghost       = Load(gl, Prefix + "icon-park-solid--ghost.svg", iconSize);

        IsLoaded = true;
    }

    private static uint Load(GL gl, string resourceName, int size)
    {
        try   { return SvgLoader.LoadTexture(gl, resourceName, size); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load {resourceName}: {ex.Message}");
            return 0;
        }
    }
}
