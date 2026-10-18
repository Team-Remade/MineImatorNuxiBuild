using System.Numerics;
using Hexa.NET.ImGui;

namespace MineImatorSimplyRemade.core.ui;

/// <summary>
/// Provides a modern, cohesive design system and theme management for Dear ImGui.
/// Transforms default ImGui into a polished, professional creative studio interface
/// with refined geometry metrics, smooth roundings, balanced paddings, curated palettes,
/// and harmonious accent lighting.
/// </summary>
public static class UiTheme
{
    public enum ThemePreset
    {
        Darker,     // Midnight / Deep Charcoal (Default pro creative suite look)
        Dark,       // Studio Dark (Neutral balanced dark theme)
        Light,      // Clean Light (Crisp modern studio light)
        Nord,       // Arctic / Slate Blue (Refined cool-toned palette)
        PureBlack   // OLED / High-Contrast Black
    }

    public enum AccentPreset
    {
        Purple,
        Blue,
        SkyBlue,
        Cyan,
        Green,
        Lime,
        Yellow,
        Orange,
        Red,
        Pink,
        Indigo,
        Custom
    }

    /// <summary>
    /// Applies modern UI geometry, metric paddings, smooth roundings, and clean borders.
    /// </summary>
    public static void ApplyModernMetrics(ImGuiStylePtr style)
    {
        // Smooth rounded corners for a modern tactile feel
        style.WindowRounding = 8.0f;
        style.ChildRounding = 6.0f;
        style.FrameRounding = 5.0f;
        style.PopupRounding = 8.0f;
        style.ScrollbarRounding = 9.0f;
        style.GrabRounding = 4.0f;
        style.TabRounding = 6.0f;

        // Comfortable, breathable padding and spacing
        style.WindowPadding = new Vector2(10.0f, 10.0f);
        style.FramePadding = new Vector2(8.0f, 5.0f);
        style.ItemSpacing = new Vector2(8.0f, 6.0f);
        style.ItemInnerSpacing = new Vector2(6.0f, 4.0f);
        style.CellPadding = new Vector2(6.0f, 4.0f);
        style.TouchExtraPadding = Vector2.Zero;

        // Modern indentation & sleek scrollbars
        style.IndentSpacing = 16.0f;
        style.ScrollbarSize = 12.0f;
        style.GrabMinSize = 10.0f;

        // Clean, subtle borders
        style.WindowBorderSize = 1.0f;
        style.ChildBorderSize = 1.0f;
        style.PopupBorderSize = 1.0f;
        style.FrameBorderSize = 0.0f;
        style.TabBorderSize = 0.0f;

        // Alignment & visual layout
        style.WindowTitleAlign = new Vector2(0.0f, 0.5f);
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        style.SelectableTextAlign = new Vector2(0.0f, 0.5f);
        style.SeparatorTextAlign = new Vector2(0.0f, 0.5f);
        style.SeparatorTextPadding = new Vector2(12.0f, 6.0f);
        style.SeparatorTextBorderSize = 1.0f;
        style.DockingSeparatorSize = 2.0f;
    }

    /// <summary>
    /// Loads a high-quality system UI font (e.g. Segoe UI, Inter, Roboto, Arial) if available,
    /// or falls back to the default Dear ImGui font.
    /// </summary>
    public static unsafe void SetupDefaultFont(ImGuiIOPtr io)
    {
        string fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string[] candidateFontNames =
        [
            "segoeui.ttf",
            "segoeuib.ttf",
            "arial.ttf",
            "tahoma.ttf",
            "DejaVuSans.ttf",
            "Ubuntu-R.ttf",
            "FreeSans.ttf"
        ];

        const float targetFontSize = 15.0f;

        if (!string.IsNullOrWhiteSpace(fontsDirectory) && Directory.Exists(fontsDirectory))
        {
            foreach (string candidate in candidateFontNames)
            {
                string fullPath = Path.Combine(fontsDirectory, candidate);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        io.Fonts.AddFontFromFileTTF(fullPath, targetFontSize);
                        return;
                    }
                    catch
                    {
                        // Fall back to trying next candidate font
                    }
                }
            }
        }

        // Fallback to built-in default font
        io.Fonts.AddFontDefault();
    }

    /// <summary>
    /// Converts an accent preset into an RGBA vector.
    /// </summary>
    public static Vector4 GetAccentColorVector(AccentPreset accent, Vector4? customColor = null)
    {
        return accent switch
        {
            AccentPreset.Purple => new Vector4(0.58f, 0.36f, 0.96f, 1.0f),    // #9333ea / vibrant violet
            AccentPreset.Blue => new Vector4(0.23f, 0.51f, 0.96f, 1.0f),      // #3b82f6 / royal electric blue
            AccentPreset.SkyBlue => new Vector4(0.05f, 0.65f, 0.95f, 1.0f),   // #0ea5e9 / azure sky
            AccentPreset.Cyan => new Vector4(0.04f, 0.73f, 0.83f, 1.0f),      // #06b6d4 / cyan teal
            AccentPreset.Green => new Vector4(0.10f, 0.75f, 0.52f, 1.0f),     // #10b981 / emerald
            AccentPreset.Lime => new Vector4(0.52f, 0.80f, 0.09f, 1.0f),      // #84cc16 / fresh lime
            AccentPreset.Yellow => new Vector4(0.92f, 0.70f, 0.03f, 1.0f),    // #eab308 / warm amber
            AccentPreset.Orange => new Vector4(0.98f, 0.45f, 0.09f, 1.0f),    // #f97316 / vibrant orange
            AccentPreset.Red => new Vector4(0.94f, 0.27f, 0.27f, 1.0f),       // #ef4444 / crimson rose
            AccentPreset.Pink => new Vector4(0.93f, 0.28f, 0.60f, 1.0f),      // #ec4899 / hot magenta
            AccentPreset.Indigo => new Vector4(0.39f, 0.40f, 0.95f, 1.0f),    // #6366f1 / deep indigo
            AccentPreset.Custom => customColor ?? new Vector4(0.58f, 0.36f, 0.96f, 1.0f),
            _ => new Vector4(0.58f, 0.36f, 0.96f, 1.0f)
        };
    }

    /// <summary>
    /// Applies a complete theme preset and accent color to the current ImGui style context.
    /// </summary>
    public static void ApplyTheme(ThemePreset theme, AccentPreset accent, Vector4? customAccent = null)
    {
        var style = ImGui.GetStyle();
        ApplyModernMetrics(style);

        Vector4 accentVec = GetAccentColorVector(accent, customAccent);

        switch (theme)
        {
            case ThemePreset.Light:
                ApplyLightPalette(style, accentVec);
                break;

            case ThemePreset.Dark:
                ApplyStudioDarkPalette(style, accentVec);
                break;

            case ThemePreset.Nord:
                ApplyNordPalette(style, accentVec);
                break;

            case ThemePreset.PureBlack:
                ApplyPureBlackPalette(style, accentVec);
                break;

            case ThemePreset.Darker:
            default:
                ApplyMidnightDarkerPalette(style, accentVec);
                break;
        }

        // Apply harmonious accent highlights
        ApplyAccentHighlights(style, accentVec);
    }

    private static void ApplyMidnightDarkerPalette(ImGuiStylePtr style, Vector4 accent)
    {
        // Deep Charcoal / Midnight Base
        style.Colors[(int)ImGuiCol.Text]                  = new Vector4(0.93f, 0.94f, 0.96f, 1.00f);
        style.Colors[(int)ImGuiCol.TextDisabled]          = new Vector4(0.48f, 0.51f, 0.58f, 1.00f);
        style.Colors[(int)ImGuiCol.WindowBg]              = new Vector4(0.07f, 0.07f, 0.09f, 1.00f);
        style.Colors[(int)ImGuiCol.ChildBg]               = new Vector4(0.09f, 0.09f, 0.11f, 1.00f);
        style.Colors[(int)ImGuiCol.PopupBg]               = new Vector4(0.10f, 0.10f, 0.13f, 0.98f);
        style.Colors[(int)ImGuiCol.Border]                = new Vector4(0.18f, 0.18f, 0.22f, 0.85f);
        style.Colors[(int)ImGuiCol.BorderShadow]          = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        
        // Input frames
        style.Colors[(int)ImGuiCol.FrameBg]               = new Vector4(0.13f, 0.13f, 0.16f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgHovered]        = new Vector4(0.18f, 0.18f, 0.22f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgActive]         = new Vector4(0.22f, 0.22f, 0.27f, 1.00f);
        
        // Title bars & Menus
        style.Colors[(int)ImGuiCol.TitleBg]               = new Vector4(0.07f, 0.07f, 0.09f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgActive]         = new Vector4(0.09f, 0.09f, 0.12f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgCollapsed]      = new Vector4(0.06f, 0.06f, 0.08f, 0.75f);
        style.Colors[(int)ImGuiCol.MenuBarBg]             = new Vector4(0.07f, 0.07f, 0.09f, 1.00f);
        
        // Scrollbars
        style.Colors[(int)ImGuiCol.ScrollbarBg]           = new Vector4(0.07f, 0.07f, 0.09f, 0.50f);
        style.Colors[(int)ImGuiCol.ScrollbarGrab]         = new Vector4(0.20f, 0.20f, 0.25f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered]  = new Vector4(0.28f, 0.28f, 0.35f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive]   = new Vector4(0.35f, 0.35f, 0.44f, 1.00f);
        
        // Headers & Trees
        style.Colors[(int)ImGuiCol.Header]                = new Vector4(0.14f, 0.14f, 0.18f, 0.90f);
        style.Colors[(int)ImGuiCol.HeaderHovered]         = new Vector4(0.20f, 0.20f, 0.25f, 0.90f);
        style.Colors[(int)ImGuiCol.HeaderActive]          = new Vector4(0.25f, 0.25f, 0.32f, 1.00f);
        
        // Separators & Grips
        style.Colors[(int)ImGuiCol.Separator]             = new Vector4(0.16f, 0.16f, 0.20f, 0.80f);
        style.Colors[(int)ImGuiCol.SeparatorHovered]      = accent with { W = 0.80f };
        style.Colors[(int)ImGuiCol.SeparatorActive]       = accent;
        style.Colors[(int)ImGuiCol.ResizeGrip]            = new Vector4(0.20f, 0.20f, 0.25f, 0.40f);
        style.Colors[(int)ImGuiCol.ResizeGripHovered]     = accent with { W = 0.70f };
        style.Colors[(int)ImGuiCol.ResizeGripActive]      = accent;
        
        // Tables
        style.Colors[(int)ImGuiCol.TableHeaderBg]         = new Vector4(0.11f, 0.11f, 0.14f, 1.00f);
        style.Colors[(int)ImGuiCol.TableBorderStrong]     = new Vector4(0.18f, 0.18f, 0.22f, 1.00f);
        style.Colors[(int)ImGuiCol.TableBorderLight]      = new Vector4(0.14f, 0.14f, 0.18f, 0.80f);
        style.Colors[(int)ImGuiCol.TableRowBg]            = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        style.Colors[(int)ImGuiCol.TableRowBgAlt]         = new Vector4(1.00f, 1.00f, 1.00f, 0.02f);
        
        // Overlays & Highlights
        style.Colors[(int)ImGuiCol.DockingEmptyBg]        = new Vector4(0.05f, 0.05f, 0.07f, 1.00f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg]      = new Vector4(0.00f, 0.00f, 0.00f, 0.65f);
    }

    private static void ApplyStudioDarkPalette(ImGuiStylePtr style, Vector4 accent)
    {
        // Studio Neutral Dark (Blender / Modern IDE aesthetic)
        style.Colors[(int)ImGuiCol.Text]                  = new Vector4(0.92f, 0.93f, 0.95f, 1.00f);
        style.Colors[(int)ImGuiCol.TextDisabled]          = new Vector4(0.52f, 0.55f, 0.62f, 1.00f);
        style.Colors[(int)ImGuiCol.WindowBg]              = new Vector4(0.12f, 0.13f, 0.15f, 1.00f);
        style.Colors[(int)ImGuiCol.ChildBg]               = new Vector4(0.14f, 0.15f, 0.17f, 1.00f);
        style.Colors[(int)ImGuiCol.PopupBg]               = new Vector4(0.15f, 0.16f, 0.19f, 0.98f);
        style.Colors[(int)ImGuiCol.Border]                = new Vector4(0.22f, 0.24f, 0.28f, 0.85f);
        style.Colors[(int)ImGuiCol.BorderShadow]          = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        
        style.Colors[(int)ImGuiCol.FrameBg]               = new Vector4(0.18f, 0.19f, 0.23f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgHovered]        = new Vector4(0.23f, 0.25f, 0.29f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgActive]         = new Vector4(0.28f, 0.30f, 0.36f, 1.00f);
        
        style.Colors[(int)ImGuiCol.TitleBg]               = new Vector4(0.10f, 0.11f, 0.13f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgActive]         = new Vector4(0.14f, 0.15f, 0.18f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgCollapsed]      = new Vector4(0.09f, 0.10f, 0.11f, 0.75f);
        style.Colors[(int)ImGuiCol.MenuBarBg]             = new Vector4(0.10f, 0.11f, 0.13f, 1.00f);
        
        style.Colors[(int)ImGuiCol.ScrollbarBg]           = new Vector4(0.10f, 0.11f, 0.13f, 0.50f);
        style.Colors[(int)ImGuiCol.ScrollbarGrab]         = new Vector4(0.25f, 0.27f, 0.32f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered]  = new Vector4(0.33f, 0.36f, 0.42f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive]   = new Vector4(0.42f, 0.45f, 0.53f, 1.00f);
        
        style.Colors[(int)ImGuiCol.Header]                = new Vector4(0.19f, 0.21f, 0.25f, 0.90f);
        style.Colors[(int)ImGuiCol.HeaderHovered]         = new Vector4(0.26f, 0.28f, 0.34f, 0.90f);
        style.Colors[(int)ImGuiCol.HeaderActive]          = new Vector4(0.32f, 0.35f, 0.42f, 1.00f);
        
        style.Colors[(int)ImGuiCol.Separator]             = new Vector4(0.21f, 0.23f, 0.27f, 0.85f);
        style.Colors[(int)ImGuiCol.SeparatorHovered]      = accent with { W = 0.80f };
        style.Colors[(int)ImGuiCol.SeparatorActive]       = accent;
        style.Colors[(int)ImGuiCol.ResizeGrip]            = new Vector4(0.25f, 0.27f, 0.32f, 0.40f);
        style.Colors[(int)ImGuiCol.ResizeGripHovered]     = accent with { W = 0.70f };
        style.Colors[(int)ImGuiCol.ResizeGripActive]      = accent;
        
        style.Colors[(int)ImGuiCol.TableHeaderBg]         = new Vector4(0.16f, 0.17f, 0.20f, 1.00f);
        style.Colors[(int)ImGuiCol.TableBorderStrong]     = new Vector4(0.22f, 0.24f, 0.28f, 1.00f);
        style.Colors[(int)ImGuiCol.TableBorderLight]      = new Vector4(0.18f, 0.19f, 0.23f, 0.80f);
        style.Colors[(int)ImGuiCol.TableRowBg]            = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        style.Colors[(int)ImGuiCol.TableRowBgAlt]         = new Vector4(1.00f, 1.00f, 1.00f, 0.02f);
        
        style.Colors[(int)ImGuiCol.DockingEmptyBg]        = new Vector4(0.09f, 0.10f, 0.12f, 1.00f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg]      = new Vector4(0.00f, 0.00f, 0.00f, 0.60f);
    }

    private static void ApplyLightPalette(ImGuiStylePtr style, Vector4 accent)
    {
        // Crisp Modern Studio Light
        style.Colors[(int)ImGuiCol.Text]                  = new Vector4(0.10f, 0.12f, 0.16f, 1.00f);
        style.Colors[(int)ImGuiCol.TextDisabled]          = new Vector4(0.48f, 0.52f, 0.58f, 1.00f);
        style.Colors[(int)ImGuiCol.WindowBg]              = new Vector4(0.95f, 0.96f, 0.97f, 1.00f);
        style.Colors[(int)ImGuiCol.ChildBg]               = new Vector4(1.00f, 1.00f, 1.00f, 1.00f);
        style.Colors[(int)ImGuiCol.PopupBg]               = new Vector4(1.00f, 1.00f, 1.00f, 0.98f);
        style.Colors[(int)ImGuiCol.Border]                = new Vector4(0.82f, 0.84f, 0.88f, 0.90f);
        style.Colors[(int)ImGuiCol.BorderShadow]          = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        
        style.Colors[(int)ImGuiCol.FrameBg]               = new Vector4(0.89f, 0.91f, 0.94f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgHovered]        = new Vector4(0.83f, 0.86f, 0.90f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgActive]         = new Vector4(0.78f, 0.81f, 0.86f, 1.00f);
        
        style.Colors[(int)ImGuiCol.TitleBg]               = new Vector4(0.90f, 0.91f, 0.94f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgActive]         = new Vector4(0.86f, 0.88f, 0.92f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgCollapsed]      = new Vector4(0.92f, 0.93f, 0.95f, 0.75f);
        style.Colors[(int)ImGuiCol.MenuBarBg]             = new Vector4(0.91f, 0.92f, 0.95f, 1.00f);
        
        style.Colors[(int)ImGuiCol.ScrollbarBg]           = new Vector4(0.94f, 0.95f, 0.97f, 0.60f);
        style.Colors[(int)ImGuiCol.ScrollbarGrab]         = new Vector4(0.76f, 0.79f, 0.83f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered]  = new Vector4(0.66f, 0.69f, 0.74f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive]   = new Vector4(0.55f, 0.58f, 0.64f, 1.00f);
        
        style.Colors[(int)ImGuiCol.Header]                = new Vector4(0.88f, 0.90f, 0.93f, 0.90f);
        style.Colors[(int)ImGuiCol.HeaderHovered]         = new Vector4(0.82f, 0.85f, 0.89f, 0.90f);
        style.Colors[(int)ImGuiCol.HeaderActive]          = new Vector4(0.76f, 0.79f, 0.84f, 1.00f);
        
        style.Colors[(int)ImGuiCol.Separator]             = new Vector4(0.82f, 0.84f, 0.88f, 0.80f);
        style.Colors[(int)ImGuiCol.SeparatorHovered]      = accent with { W = 0.80f };
        style.Colors[(int)ImGuiCol.SeparatorActive]       = accent;
        style.Colors[(int)ImGuiCol.ResizeGrip]            = new Vector4(0.70f, 0.73f, 0.78f, 0.40f);
        style.Colors[(int)ImGuiCol.ResizeGripHovered]     = accent with { W = 0.70f };
        style.Colors[(int)ImGuiCol.ResizeGripActive]      = accent;
        
        style.Colors[(int)ImGuiCol.TableHeaderBg]         = new Vector4(0.90f, 0.92f, 0.94f, 1.00f);
        style.Colors[(int)ImGuiCol.TableBorderStrong]     = new Vector4(0.80f, 0.82f, 0.86f, 1.00f);
        style.Colors[(int)ImGuiCol.TableBorderLight]      = new Vector4(0.87f, 0.89f, 0.92f, 0.80f);
        style.Colors[(int)ImGuiCol.TableRowBg]            = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        style.Colors[(int)ImGuiCol.TableRowBgAlt]         = new Vector4(0.00f, 0.00f, 0.00f, 0.02f);
        
        style.Colors[(int)ImGuiCol.DockingEmptyBg]        = new Vector4(0.90f, 0.91f, 0.93f, 1.00f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg]      = new Vector4(0.00f, 0.00f, 0.00f, 0.45f);
    }

    private static void ApplyNordPalette(ImGuiStylePtr style, Vector4 accent)
    {
        // Nord Arctic Slate
        style.Colors[(int)ImGuiCol.Text]                  = new Vector4(0.93f, 0.94f, 0.96f, 1.00f);
        style.Colors[(int)ImGuiCol.TextDisabled]          = new Vector4(0.55f, 0.60f, 0.68f, 1.00f);
        style.Colors[(int)ImGuiCol.WindowBg]              = new Vector4(0.18f, 0.20f, 0.25f, 1.00f);
        style.Colors[(int)ImGuiCol.ChildBg]               = new Vector4(0.21f, 0.24f, 0.29f, 1.00f);
        style.Colors[(int)ImGuiCol.PopupBg]               = new Vector4(0.22f, 0.25f, 0.31f, 0.98f);
        style.Colors[(int)ImGuiCol.Border]                = new Vector4(0.30f, 0.34f, 0.42f, 0.85f);
        style.Colors[(int)ImGuiCol.BorderShadow]          = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        
        style.Colors[(int)ImGuiCol.FrameBg]               = new Vector4(0.26f, 0.30f, 0.37f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgHovered]        = new Vector4(0.32f, 0.36f, 0.44f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgActive]         = new Vector4(0.37f, 0.42f, 0.51f, 1.00f);
        
        style.Colors[(int)ImGuiCol.TitleBg]               = new Vector4(0.15f, 0.17f, 0.21f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgActive]         = new Vector4(0.18f, 0.21f, 0.26f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgCollapsed]      = new Vector4(0.15f, 0.17f, 0.21f, 0.75f);
        style.Colors[(int)ImGuiCol.MenuBarBg]             = new Vector4(0.15f, 0.17f, 0.21f, 1.00f);
        
        style.Colors[(int)ImGuiCol.ScrollbarBg]           = new Vector4(0.16f, 0.18f, 0.22f, 0.50f);
        style.Colors[(int)ImGuiCol.ScrollbarGrab]         = new Vector4(0.30f, 0.34f, 0.42f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered]  = new Vector4(0.38f, 0.43f, 0.52f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive]   = new Vector4(0.48f, 0.53f, 0.63f, 1.00f);
        
        style.Colors[(int)ImGuiCol.Header]                = new Vector4(0.26f, 0.30f, 0.37f, 0.90f);
        style.Colors[(int)ImGuiCol.HeaderHovered]         = new Vector4(0.33f, 0.38f, 0.46f, 0.90f);
        style.Colors[(int)ImGuiCol.HeaderActive]          = new Vector4(0.40f, 0.46f, 0.55f, 1.00f);
        
        style.Colors[(int)ImGuiCol.Separator]             = new Vector4(0.28f, 0.32f, 0.39f, 0.85f);
        style.Colors[(int)ImGuiCol.SeparatorHovered]      = accent with { W = 0.80f };
        style.Colors[(int)ImGuiCol.SeparatorActive]       = accent;
        style.Colors[(int)ImGuiCol.ResizeGrip]            = new Vector4(0.30f, 0.34f, 0.42f, 0.40f);
        style.Colors[(int)ImGuiCol.ResizeGripHovered]     = accent with { W = 0.70f };
        style.Colors[(int)ImGuiCol.ResizeGripActive]      = accent;
        
        style.Colors[(int)ImGuiCol.TableHeaderBg]         = new Vector4(0.22f, 0.25f, 0.31f, 1.00f);
        style.Colors[(int)ImGuiCol.TableBorderStrong]     = new Vector4(0.30f, 0.34f, 0.42f, 1.00f);
        style.Colors[(int)ImGuiCol.TableBorderLight]      = new Vector4(0.25f, 0.28f, 0.35f, 0.80f);
        style.Colors[(int)ImGuiCol.TableRowBg]            = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        style.Colors[(int)ImGuiCol.TableRowBgAlt]         = new Vector4(1.00f, 1.00f, 1.00f, 0.02f);
        
        style.Colors[(int)ImGuiCol.DockingEmptyBg]        = new Vector4(0.14f, 0.16f, 0.20f, 1.00f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg]      = new Vector4(0.00f, 0.00f, 0.00f, 0.60f);
    }

    private static void ApplyPureBlackPalette(ImGuiStylePtr style, Vector4 accent)
    {
        // OLED High-Contrast Pure Black
        style.Colors[(int)ImGuiCol.Text]                  = new Vector4(0.96f, 0.96f, 0.98f, 1.00f);
        style.Colors[(int)ImGuiCol.TextDisabled]          = new Vector4(0.45f, 0.45f, 0.50f, 1.00f);
        style.Colors[(int)ImGuiCol.WindowBg]              = new Vector4(0.00f, 0.00f, 0.00f, 1.00f);
        style.Colors[(int)ImGuiCol.ChildBg]               = new Vector4(0.04f, 0.04f, 0.05f, 1.00f);
        style.Colors[(int)ImGuiCol.PopupBg]               = new Vector4(0.06f, 0.06f, 0.08f, 0.98f);
        style.Colors[(int)ImGuiCol.Border]                = new Vector4(0.16f, 0.16f, 0.20f, 0.90f);
        style.Colors[(int)ImGuiCol.BorderShadow]          = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        
        style.Colors[(int)ImGuiCol.FrameBg]               = new Vector4(0.09f, 0.09f, 0.11f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgHovered]        = new Vector4(0.15f, 0.15f, 0.18f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgActive]         = new Vector4(0.20f, 0.20f, 0.24f, 1.00f);
        
        style.Colors[(int)ImGuiCol.TitleBg]               = new Vector4(0.00f, 0.00f, 0.00f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgActive]         = new Vector4(0.05f, 0.05f, 0.06f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgCollapsed]      = new Vector4(0.00f, 0.00f, 0.00f, 0.75f);
        style.Colors[(int)ImGuiCol.MenuBarBg]             = new Vector4(0.00f, 0.00f, 0.00f, 1.00f);
        
        style.Colors[(int)ImGuiCol.ScrollbarBg]           = new Vector4(0.00f, 0.00f, 0.00f, 0.50f);
        style.Colors[(int)ImGuiCol.ScrollbarGrab]         = new Vector4(0.18f, 0.18f, 0.22f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered]  = new Vector4(0.26f, 0.26f, 0.32f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive]   = new Vector4(0.34f, 0.34f, 0.42f, 1.00f);
        
        style.Colors[(int)ImGuiCol.Header]                = new Vector4(0.10f, 0.10f, 0.13f, 0.90f);
        style.Colors[(int)ImGuiCol.HeaderHovered]         = new Vector4(0.16f, 0.16f, 0.20f, 0.90f);
        style.Colors[(int)ImGuiCol.HeaderActive]          = new Vector4(0.22f, 0.22f, 0.28f, 1.00f);
        
        style.Colors[(int)ImGuiCol.Separator]             = new Vector4(0.14f, 0.14f, 0.18f, 0.85f);
        style.Colors[(int)ImGuiCol.SeparatorHovered]      = accent with { W = 0.80f };
        style.Colors[(int)ImGuiCol.SeparatorActive]       = accent;
        style.Colors[(int)ImGuiCol.ResizeGrip]            = new Vector4(0.16f, 0.16f, 0.20f, 0.40f);
        style.Colors[(int)ImGuiCol.ResizeGripHovered]     = accent with { W = 0.70f };
        style.Colors[(int)ImGuiCol.ResizeGripActive]      = accent;
        
        style.Colors[(int)ImGuiCol.TableHeaderBg]         = new Vector4(0.07f, 0.07f, 0.09f, 1.00f);
        style.Colors[(int)ImGuiCol.TableBorderStrong]     = new Vector4(0.16f, 0.16f, 0.20f, 1.00f);
        style.Colors[(int)ImGuiCol.TableBorderLight]      = new Vector4(0.10f, 0.10f, 0.13f, 0.80f);
        style.Colors[(int)ImGuiCol.TableRowBg]            = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        style.Colors[(int)ImGuiCol.TableRowBgAlt]         = new Vector4(1.00f, 1.00f, 1.00f, 0.02f);
        
        style.Colors[(int)ImGuiCol.DockingEmptyBg]        = new Vector4(0.00f, 0.00f, 0.00f, 1.00f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg]      = new Vector4(0.00f, 0.00f, 0.00f, 0.75f);
    }

    private static void ApplyAccentHighlights(ImGuiStylePtr style, Vector4 accent)
    {
        // Interactive Buttons
        style.Colors[(int)ImGuiCol.Button]                = accent with { W = 0.72f };
        style.Colors[(int)ImGuiCol.ButtonHovered]         = accent with { W = 0.90f };
        style.Colors[(int)ImGuiCol.ButtonActive]          = new Vector4(accent.X * 0.85f, accent.Y * 0.85f, accent.Z * 0.85f, 1.00f);
        
        // Checks, Grabs & Sliders
        style.Colors[(int)ImGuiCol.CheckMark]             = accent;
        style.Colors[(int)ImGuiCol.SliderGrab]            = accent with { W = 0.85f };
        style.Colors[(int)ImGuiCol.SliderGrabActive]      = accent;
        
        // Tab Controls
        style.Colors[(int)ImGuiCol.Tab]                   = new Vector4(accent.X * 0.35f, accent.Y * 0.35f, accent.Z * 0.35f, 0.35f);
        style.Colors[(int)ImGuiCol.TabHovered]            = accent with { W = 0.65f };
        style.Colors[(int)ImGuiCol.TabSelected]           = accent with { W = 0.88f };
        style.Colors[(int)ImGuiCol.TabDimmed]             = new Vector4(accent.X * 0.20f, accent.Y * 0.20f, accent.Z * 0.20f, 0.20f);
        style.Colors[(int)ImGuiCol.TabDimmedSelected]     = accent with { W = 0.45f };

        // Docking & Nav
        style.Colors[(int)ImGuiCol.DockingPreview]        = accent with { W = 0.45f };
        style.Colors[(int)ImGuiCol.TextSelectedBg]        = accent with { W = 0.35f };
        style.Colors[(int)ImGuiCol.DragDropTarget]        = accent;
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] = accent;

        // Plot lines & Histograms
        style.Colors[(int)ImGuiCol.PlotLines]             = accent;
        style.Colors[(int)ImGuiCol.PlotLinesHovered]      = new Vector4(accent.X * 1.15f, accent.Y * 1.15f, accent.Z * 1.15f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotHistogram]         = accent with { W = 0.85f };
        style.Colors[(int)ImGuiCol.PlotHistogramHovered]  = accent;
    }

    /// <summary>
    /// Renders a modern styled section header with an accent color indicator bar.
    /// </summary>
    public static void RenderSectionHeader(string title, string? subtitle = null)
    {
        var drawList = ImGui.GetWindowDrawList();
        Vector2 cursor = ImGui.GetCursorScreenPos();
        float lineHeight = ImGui.GetTextLineHeight();
        float barWidth = 3.0f;
        float barHeight = lineHeight + 2.0f;

        Vector4 accent = ImGui.GetStyle().Colors[(int)ImGuiCol.Button];
        uint accentColor = ImGui.ColorConvertFloat4ToU32(accent with { W = 1.0f });

        drawList.AddRectFilled(
            new Vector2(cursor.X, cursor.Y - 1.0f),
            new Vector2(cursor.X + barWidth, cursor.Y + barHeight - 1.0f),
            accentColor,
            2.0f);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + barWidth + 6.0f);
        ImGui.TextColored(new Vector4(1.0f, 1.0f, 1.0f, 1.0f), title);

        if (!string.IsNullOrEmpty(subtitle))
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"— {subtitle}");
        }

        ImGui.Spacing();
    }

    /// <summary>
    /// Renders a sleek pill-shaped badge (tag or status indicator).
    /// </summary>
    public static void RenderBadge(string text, Vector4? backgroundColor = null, Vector4? textColor = null)
    {
        var style = ImGui.GetStyle();
        Vector2 textSize = ImGui.CalcTextSize(text);
        Vector2 padding = new Vector2(6.0f, 2.0f);
        Vector2 badgeSize = new Vector2(textSize.X + padding.X * 2.0f, textSize.Y + padding.Y * 2.0f);

        Vector2 pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        Vector4 bg = backgroundColor ?? style.Colors[(int)ImGuiCol.FrameBgHovered];
        Vector4 fg = textColor ?? style.Colors[(int)ImGuiCol.Text];

        uint bgCol = ImGui.ColorConvertFloat4ToU32(bg);
        uint fgCol = ImGui.ColorConvertFloat4ToU32(fg);

        drawList.AddRectFilled(pos, pos + badgeSize, bgCol, badgeSize.Y * 0.5f);
        drawList.AddText(pos + padding, fgCol, text);

        ImGui.Dummy(badgeSize);
    }
}
