using Hexa.NET.ImGui;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemadeNuxi.core.objs;

namespace MineImatorSimplyRemade.core.ui;

internal sealed class ObjectNameEditor
{
    private SceneObject? _object;
    private string _buffer = "";
    private bool _editing;
    private int _lastRenderedFrame = -1;

    public bool Render(SceneObject obj, string label, bool requestFocus = false)
    {
        _lastRenderedFrame = ImGui.GetFrameCount();
        if (_object != obj)
        {
            Commit();
            _object = obj;
        }

        // Refresh idle fields after a rename in another panel or undo/redo.
        if (!_editing)
            _buffer = obj.GetDisplayName();

        if (requestFocus)
            ImGui.SetKeyboardFocusHere();

        ImGui.PushID(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj));
        // Without EnterReturnsTrue the binding keeps the managed draft current while typing.
        ImGui.InputText(label, ref _buffer, 256, ImGuiInputTextFlags.AutoSelectAll);
        bool finished = ImGui.IsItemDeactivated();
        _editing = ImGui.IsItemActive() || finished;
        if (finished)
        {
            if (!ImGui.IsKeyPressed(ImGuiKey.Escape))
                Commit();
            _editing = false;
        }
        ImGui.PopID();
        return finished;
    }

    public bool CommitIfHidden()
    {
        if (!_editing || _lastRenderedFrame == ImGui.GetFrameCount())
            return false;

        Commit();
        return true;
    }

    public void Commit()
    {
        if (_editing && _object != null)
        {
            string name = _buffer.Trim();
            if (name.Length > 0 && name != _object.GetDisplayName())
            {
                _object.Name = name;
                ProjectManager.Instance.SetDirty(true);
            }
        }
        _editing = false;
    }
}
