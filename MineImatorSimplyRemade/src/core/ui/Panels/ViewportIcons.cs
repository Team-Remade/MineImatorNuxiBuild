using System.Numerics;
using Hexa.NET.ImGui;
using Silk.NET.OpenGL;

namespace MineImatorSimplyRemade.core.ui.Panels;

internal static class ViewportIcons
{
    public static uint Overlays { get; private set; }
    public static uint Thirds { get; private set; }
    public static uint Particles { get; private set; }
    public static uint Preview { get; private set; }
    public static uint PopOut { get; private set; }
    public static uint Dock { get; private set; }

    private static bool _initialized;

    public static void Initialize(GL gl)
    {
        if (_initialized) return;

        // Material Design Icons by Pictogrammers, via Iconify (Apache-2.0).
        Overlays = Load("layers-outline");
        Thirds = Load("grid");
        Particles = Load("creation");
        Preview = Load("picture-in-picture-bottom-right");
        PopOut = Load("open-in-new");
        Dock = Load("dock-window");
        _initialized = true;

        uint Load(string name)
        {
            try
            {
                return SvgLoader.LoadTexture(gl, $"MineImatorSimplyRemade.assets.img.button.mdi--{name}.svg", 32);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load viewport icon '{name}': {ex.Message}");
                return 0;
            }
        }
    }

    public static unsafe bool Button(string id, uint texture, string label, float height = 0f, string? tooltip = null)
    {
        if (height <= 0f) height = ImGui.GetFrameHeight();
        string itemLabel = label + "###" + id;
        bool clicked;
        if (texture == 0)
        {
            clicked = ImGui.Button(itemLabel, new Vector2(0f, height));
        }
        else
        {
            float padding = MathF.Min(4f * UiTheme.Scale, height * 0.25f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(padding));
            clicked = ImGui.ImageButton(itemLabel, new ImTextureRef(texId: (ulong)texture),
                new Vector2(height - padding * 2f), Vector2.Zero, Vector2.One,
                Vector4.Zero, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
            ImGui.PopStyleVar();
        }

        if (ImGui.IsItemHovered() || ImGui.IsItemFocused())
            ImGui.SetTooltip(tooltip ?? label);
        return clicked;
    }
}
