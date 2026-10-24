using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.ui;

namespace MineImatorSimplyRemade;

internal static class ResourcePackStackSelector
{
    public static bool Render(string id, ref string selection, string label, IReadOnlyList<string>? availableIds = null)
    {
        availableIds ??= MinecraftDataLoader.GetAvailableResourcePackIds().ToList();
        var packs = MinecraftDataLoader.GetResourcePackStack(selection).ToList();
        bool changed = false;

        ImGui.PushID(id);
        ImGui.TextUnformatted(Localization.DisplayText(label));
        UiText.Wrapped("Highest priority first. The top pack wins conflicts; lower packs and Default fill missing resources. New packs are added at the bottom.");

        ImGui.BeginDisabled(packs.Count == 0);
        if (ImGui.Button(Localization.Label("text.common.default", "Default")))
        {
            packs.Clear();
            changed = true;
        }
        ImGui.EndDisabled();

        if (packs.Count == 0)
            UiText.Disabled(Localization.T("text.common.default"));

        for (int i = 0; i < packs.Count; i++)
        {
            ImGui.PushID(i);
            bool available = availableIds.Contains(packs[i], StringComparer.OrdinalIgnoreCase);
            UiText.Wrapped($"{i + 1}. {packs[i]}{(available ? "" : " (missing)")}");

            ImGui.BeginDisabled(i == 0);
            bool moveUp = ImGui.Button("Up");
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(i == packs.Count - 1);
            bool moveDown = ImGui.Button("Down");
            ImGui.EndDisabled();
            ImGui.SameLine();
            bool remove = ImGui.Button("Remove");
            ImGui.PopID();

            if (remove)
                packs.RemoveAt(i);
            else if (moveUp)
                (packs[i - 1], packs[i]) = (packs[i], packs[i - 1]);
            else if (moveDown)
                (packs[i], packs[i + 1]) = (packs[i + 1], packs[i]);
            else
                continue;

            changed = true;
            break;
        }

        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##addPack", "Add resource pack..."))
        {
            bool hasOptions = false;
            foreach (string pack in availableIds)
            {
                if (string.IsNullOrWhiteSpace(pack) || packs.Contains(pack, StringComparer.OrdinalIgnoreCase))
                    continue;

                hasOptions = true;
                if (ImGui.Selectable(pack))
                {
                    packs.Add(pack);
                    changed = true;
                }
            }

            if (!hasOptions)
                UiText.Disabled("No additional resource packs available.");
            ImGui.EndCombo();
        }
        ImGui.PopID();

        if (changed)
            selection = MinecraftDataLoader.NormalizeResourcePackStack(string.Join(';', packs));
        return changed;
    }
}
