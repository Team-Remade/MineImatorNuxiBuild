#pragma once

#include <RmlUi/Core.h>

#include <map>
#include <memory>
#include <set>
#include <string>
#include <vector>

class SceneObject;

// RmlUi scene-tree panel, ported from the reference project's ImGui SceneTree.
//
// Supported features
//   • Recursive tree built from the root object list + SceneObject children
//   • Multi-selection (Ctrl+click toggle, Shift+click range)
//   • Inline rename (double-click a row)
//   • Right-click context menu (Rename / Duplicate / Delete)
//   • Drag-and-drop reparenting (drop on a row = child, drop on blank = root)
//   • Expand / collapse via the row arrow
//   • Live search filtering
class SceneTree {
public:
    void Init(Rml::ElementDocument* document);
    void Shutdown();

    // Rebuilds the whole tree DOM from the current model state.
    void Rebuild();

    // Root-level scene objects (equivalent to Viewport.SceneObjects).
    const std::vector<std::shared_ptr<SceneObject>>& RootObjects() const { return rootObjects; }
    std::shared_ptr<SceneObject> AddRootObject(const std::string& objectType);

    // Adds a new root-level object of the given engine type, auto-numbering its
    // display name from baseName (e.g. "Cube", "Cube1", "Cube2", ...) using the
    // same naming rules as DuplicateObject, then selects it. Used by SpawnMenu.
    std::shared_ptr<SceneObject> AddSpawnedObject(const std::string& objectType, const std::string& baseName);

private:
    class TreeEventListener final : public Rml::EventListener {
    public:
        explicit TreeEventListener(SceneTree* owner) : owner(owner) {}
        void ProcessEvent(Rml::Event& event) override;

    private:
        SceneTree* owner;
    };

    // ── DOM construction ────────────────────────────────────────────────────
    void BuildNode(Rml::Element* parent, const std::shared_ptr<SceneObject>& obj, int depth,
                   const std::set<const SceneObject*>* visibilityFilter);
    void RegisterNode(const std::shared_ptr<SceneObject>& obj);

    // ── Event handling ──────────────────────────────────────────────────────
    void OnRowClick(Rml::Element* row, bool ctrl, bool shift);
    void OnRowRightClick(Rml::Element* row, float mouseX, float mouseY);
    void BeginRename(Rml::Element* row);
    void CommitRename(Rml::Element* input);
    void HandleDrop(Rml::Element* dragElement, Rml::Element* targetRow);

    // ── Context menu ────────────────────────────────────────────────────────
    void ShowContextMenu(const std::shared_ptr<SceneObject>& target, float mouseX, float mouseY);
    void CloseContextMenu();

    // ── Model operations ────────────────────────────────────────────────────
    bool ReparentObject(const std::shared_ptr<SceneObject>& obj, const std::shared_ptr<SceneObject>& newParent);
    void DeleteObject(const std::shared_ptr<SceneObject>& obj);
    std::shared_ptr<SceneObject> DuplicateObject(const std::shared_ptr<SceneObject>& original, bool selectDuplicate);

    // ── Selection helpers ───────────────────────────────────────────────────
    void HandleClickSelection(const std::shared_ptr<SceneObject>& obj, bool ctrl, bool shift);
    void FlattenVisibleTree(std::vector<std::shared_ptr<SceneObject>>& out,
                            const std::set<const SceneObject*>* visibilityFilter) const;

    // ── Search helpers ──────────────────────────────────────────────────────
    bool PopulateFilterVisibleSet(const std::shared_ptr<SceneObject>& obj, const std::string& term,
                                  std::set<const SceneObject*>& visible) const;

    // ── Naming helpers ──────────────────────────────────────────────────────
    std::string GetBaseName(const std::string& name) const;
    int GetNextAvailableNameNumber(const std::string& baseName) const;

    // ── Lookup ──────────────────────────────────────────────────────────────
    std::shared_ptr<SceneObject> FindById(int id) const;
    static int RowNodeId(Rml::Element* element);

    Rml::ElementDocument* document = nullptr;
    Rml::Element* container = nullptr;
    Rml::Element* searchInput = nullptr;
    Rml::Element* contextMenu = nullptr;

    std::vector<std::shared_ptr<SceneObject>> rootObjects;
    std::map<int, std::shared_ptr<SceneObject>> nodesById;
    std::set<int> collapsedNodes;

    std::string searchQuery;
    std::shared_ptr<SceneObject> lastClickedObject;
    std::shared_ptr<SceneObject> contextMenuTarget;
    std::shared_ptr<SceneObject> draggingObject;
    int renamingNodeId = 0;

    int selectionChangedToken = 0;

    TreeEventListener eventListener{this};
};
