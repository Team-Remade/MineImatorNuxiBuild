#pragma once

#include <functional>
#include <memory>
#include <vector>

class SceneObject;

// Editor selection state, ported from the reference project's SelectionManager.
// Gizmo / timeline / project-dirty integrations from the original are omitted
// because those subsystems do not exist in this build yet.
class SelectionManager {
public:
    // ── Singleton ───────────────────────────────────────────────────────────
    static SelectionManager* Instance();
    static void Initialize();
    static void Shutdown();

    // Monotonically-increasing object id source (id 0 means "nothing").
    static int GetNextObjectId();

    // ── Selection state ─────────────────────────────────────────────────────
    const std::vector<std::shared_ptr<SceneObject>>& SelectedObjects() const { return selectedObjects; }

    void SelectObject(const std::shared_ptr<SceneObject>& obj);
    void DeselectObject(const std::shared_ptr<SceneObject>& obj);
    void ToggleSelection(const std::shared_ptr<SceneObject>& obj);
    void ClearSelection();
    bool IsSelected(const SceneObject* obj) const;

    // ── Change notification ─────────────────────────────────────────────────
    // Returns a token that can be passed to RemoveSelectionChanged.
    int AddSelectionChanged(std::function<void()> callback);
    void RemoveSelectionChanged(int token);

private:
    SelectionManager() = default;

    void NotifySelectionChanged();

    std::vector<std::shared_ptr<SceneObject>> selectedObjects;
    std::vector<std::pair<int, std::function<void()>>> selectionChangedCallbacks;
    int nextCallbackToken = 1;
};
