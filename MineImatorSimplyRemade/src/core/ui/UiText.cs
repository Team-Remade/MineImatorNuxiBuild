using System.Numerics;
using Hexa.NET.ImGui;

namespace MineImatorSimplyRemade.core.ui;

/// <summary>Unformatted equivalents of ImGui's printf-style text helpers.</summary>
internal static class UiText
{
    public static void Wrapped(string text)
    {
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    public static void Colored(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    public static void Disabled(string text) => Colored(ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], text);

    public static void Tooltip(string text)
    {
        if (ImGui.BeginTooltip())
        {
            ImGui.TextUnformatted(text);
            ImGui.EndTooltip();
        }
    }
}
