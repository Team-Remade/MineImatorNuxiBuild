using System.Numerics;
using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;

namespace MineImatorSimplyRemade.core.ui;

public static class TextureSheetPicker
{
    public static string Label(string path)
    {
        var manager = ProjectManager.Instance;
        var sheet = manager.FindTextureSheet(path);
        if (sheet == null) return Path.GetFileName(path);
        var (column, row) = Selection(manager, sheet, path);
        return sheet.DisplayName + " - " + Localization.Format("text.panel.properties.column_row", column + 1, row + 1);
    }

    public static bool IsGeneratedTile(string path)
    {
        var manager = ProjectManager.Instance;
        var sheet = manager.FindTextureSheet(path);
        return sheet != null && !string.Equals(manager.GetAssetFullPath(sheet),
            manager.ResolveAssetPath(path), StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveSelection(string path)
    {
        var manager = ProjectManager.Instance;
        var sheet = manager.FindTextureSheet(path);
        return sheet != null && !IsGeneratedTile(path) ? manager.GetTextureSheetTile(sheet, 0, 0) : path;
    }

    public static unsafe bool Render(string id, string path, out string selectedPath)
    {
        selectedPath = path;
        var manager = ProjectManager.Instance;
        var sheet = manager.FindTextureSheet(path);
        if (sheet == null) return false;
        int columns = sheet.TextureSheetWidth / sheet.TextureTileWidth;
        int rows = sheet.TextureSheetHeight / sheet.TextureTileHeight;
        if (columns <= 0 || rows <= 0)
            throw new InvalidDataException("Invalid texture sheet dimensions.");
        var (column, row) = Selection(manager, sheet, path);
        int inputColumn = column + 1, inputRow = row + 1;
        bool changed;
        ImGui.PushID(id);
        ImGui.TextUnformatted(Localization.T("text.texture_sheet.tile"));
        ImGui.SetNextItemWidth(100f * UiTheme.Scale);
        changed = ImGui.InputInt(Localization.Label("text.texture_sheet.column", "Column"), ref inputColumn);
        ImGui.SetNextItemWidth(100f * UiTheme.Scale);
        changed |= ImGui.InputInt(Localization.Label("text.texture_sheet.row", "Row"), ref inputRow);
        if (changed)
        {
            column = Math.Clamp(inputColumn, 1, columns) - 1;
            row = Math.Clamp(inputRow, 1, rows) - 1;
        }
        if (ImGui.Button(Localization.Label("text.texture_sheet.pick", "Pick tile..."), new Vector2(-1, 0)))
            ImGui.OpenPopup("##tiles");
        if (ImGui.BeginPopup("##tiles"))
        {
            uint texture = MineImatorLoader.Instance.LoadTextureFromFile(manager.GetAssetFullPath(sheet));
            if (texture == 0)
                ImGui.TextWrapped(Localization.T("text.texture_sheet.preview_failed"));
            else
            {
                float size = 48f * UiTheme.Scale;
                float stride = size + ImGui.GetStyle().FramePadding.Y * 2 + ImGui.GetStyle().ItemSpacing.Y;
                const int gridColumns = 6;
                int gridRows = (int)(((long)columns * rows + gridColumns - 1) / gridColumns);
                ImGui.BeginChild("##tileGrid", new Vector2(380f, 280f) * UiTheme.Scale);
                float startY = ImGui.GetCursorPosY();
                int first = Math.Clamp((int)(ImGui.GetScrollY() / stride), 0, gridRows - 1);
                int last = Math.Min(gridRows, first + (int)(ImGui.GetWindowHeight() / stride) + 2);
                for (int gridRow = first; gridRow < last; gridRow++)
                {
                    ImGui.SetCursorPosY(startY + gridRow * stride);
                    for (int gridColumn = 0; gridColumn < gridColumns; gridColumn++)
                    {
                        int index = gridRow * gridColumns + gridColumn;
                        if (index >= (long)columns * rows) break;
                        int tileColumn = index % columns, tileRow = index / columns;
                        if (gridColumn > 0) ImGui.SameLine();
                        bool selected = tileColumn == column && tileRow == row;
                        if (selected) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.9f, 0.8f));
                        float aspect = (float)sheet.TextureTileWidth / sheet.TextureTileHeight;
                        Vector2 imageSize = aspect >= 1 ? new Vector2(size, size / aspect) : new Vector2(size * aspect, size);
                        bool clicked = ImGui.ImageButton($"##tile{index}", new ImTextureRef(texId: texture), imageSize,
                            new Vector2((float)tileColumn / columns, (float)tileRow / rows),
                            new Vector2((float)(tileColumn + 1) / columns, (float)(tileRow + 1) / rows));
                        if (selected) ImGui.PopStyleColor();
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip(Localization.Format("text.panel.properties.column_row", tileColumn + 1, tileRow + 1));
                        if (clicked)
                        {
                            column = tileColumn;
                            row = tileRow;
                            changed = true;
                        }
                    }
                }
                ImGui.SetCursorPosY(startY + gridRows * stride);
                ImGui.Dummy(new Vector2(1, 1));
                ImGui.EndChild();
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
        if (changed) selectedPath = manager.GetTextureSheetTile(sheet, column, row);
        return changed;
    }

    private static (int Column, int Row) Selection(ProjectManager manager, ProjectAssetEntry sheet, string path)
    {
        string fullPath = manager.ResolveAssetPath(path);
        foreach (var (key, tilePath) in sheet.TextureSheetTiles)
        {
            if (!string.Equals(manager.ResolveAssetPath(tilePath), fullPath, StringComparison.OrdinalIgnoreCase)) continue;
            string[] indices = key.Split('|')[0].Split(',');
            if (indices.Length == 2 && int.TryParse(indices[0], out int column) && int.TryParse(indices[1], out int row))
                return (column, row);
            throw new InvalidDataException("Invalid texture sheet tile metadata.");
        }
        return (0, 0);
    }
}
