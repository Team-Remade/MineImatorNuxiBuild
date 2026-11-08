using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core.ui;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemade.core.window;

internal static partial class Program
{
    private static unsafe void UiScaleTests(Fixture f)
    {
        var legacy = JsonSerializer.Deserialize("{}", AppJsonContext.Default.PreferencesState)!;
        Near(1f, legacy.UiScale, "Legacy preferences keep 100% scale");
        var state = new PreferencesState { UiScale = 1.75f };
        string json = JsonSerializer.Serialize(state, AppJsonContext.Default.PreferencesState);
        Near(1.75f, JsonSerializer.Deserialize(json, AppJsonContext.Default.PreferencesState)!.UiScale,
            "User scale persists through source-generated JSON");
        Near(1f, new PreferencesState().UiScale, "Restored default scale");

        var panel = new PreferencesPanel();
        float notified = 0f;
        panel.UiScaleChanged += value => notified = value;
        foreach (var (input, expected) in new (float, float)[]
        {
            (0.5f, 0.5f), (2f, 2f), (0f, 0.5f), (3f, 2f),
            (float.NaN, 1f), (float.PositiveInfinity, 1f)
        })
        {
            panel.ApplyUiScale(input);
            Near(expected, panel.UiScale, "Scale limits and invalid preference normalization");
            Near(expected, notified, "Scale changes notify window listeners");
        }

        var previous = ImGui.GetCurrentContext();
        var main = ImGui.CreateContext();
        var camera = ImGui.CreateContext();
        try
        {
            foreach (var context in new[] { main, camera })
            {
                ImGui.SetCurrentContext(context);
                var io = ImGui.GetIO();
                io.IniFilename = null;
                io.LogFilename = null;
                io.DisplaySize = new System.Numerics.Vector2(1200, 900);
                io.DeltaTime = 1f / 60f;
                io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
                io.ConfigDpiScaleFonts = true;
                UiTheme.SetupDefaultFont(io);
                UiTheme.ApplyModernMetrics(ImGui.GetStyle());
                UiTheme.ApplyTheme(UiTheme.ThemePreset.Nord, UiTheme.AccentPreset.Green);
                var style = ImGui.GetStyle();
                var baseline = *style.Handle;
                var accent = style.Colors[(int)ImGuiCol.CheckMark];
                style.Alpha = 0.8f;

                // Exercise window sizing without creating a GLFW window or GPU backend.
                var window = (Window)RuntimeHelpers.GetUninitializedObject(typeof(Window));
                var fields = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(Window).GetField("_unscaledStyle", fields)!.SetValue(window, baseline);
                var apply = typeof(Window).GetMethod("ApplyDpiScale", fields)!;
                float baseFontSize = 0f;
                foreach (var (user, dpi) in new (float, float)[]
                {
                    (1f, 1f), (0.5f, 1f), (1.25f, 1.5f), (2f, 2f),
                    (0.5f, 2f), (2f, 1f), (1f, 1f), (1f, 1f)
                })
                {
                    window.UiScale = user;
                    ImGui.GetMainViewport().DpiScale = dpi;
                    apply.Invoke(window, [dpi]);
                    Near(MathF.Floor(baseline.WindowPadding.X * user * dpi), style.WindowPadding.X,
                        "Padding scale without cumulative rounding drift");
                    Near(MathF.Floor(baseline.ScrollbarSize * user * dpi), style.ScrollbarSize, "Scrollbar scale");
                    Near(user, style.FontScaleMain, "User font scale remains separate from DPI");
                    Near(dpi, style.FontScaleDpi, "Monitor font scale");
                    Equal(accent, style.Colors[(int)ImGuiCol.CheckMark], "Scale preserves theme and accent");
                    Near(0.8f, style.Alpha, "Scale preserves opacity");
                    ImGui.NewFrame();
                    ImGui.Begin("UI scale regression");
                    if (baseFontSize == 0f)
                        baseFontSize = ImGui.GetFontSize();
                    Near(MathF.Floor(baseFontSize * user * dpi + 0.5f), ImGui.GetFontSize(),
                        "Rendered font size rounds to the nearest pixel");
                    Near(user * dpi, UiTheme.Scale, "Explicit widget dimensions use both scales");
                    ImGui.TextUnformatted("UI scale");
                    ImGui.End();
                    ImGui.Render();
                }
                if (context == camera)
                {
                    window.UiScale = 2f;
                    apply.Invoke(window, [1.5f]);
                    Near(2f, style.FontScaleMain, "Independent camera scale");
                }
            }
            ImGui.SetCurrentContext(main);
            Near(1f, ImGui.GetStyle().FontScaleMain, "Camera context changes do not corrupt main style");
        }
        finally
        {
            ImGui.DestroyContext(camera);
            ImGui.DestroyContext(main);
            ImGui.SetCurrentContext(previous);
        }
    }
}
