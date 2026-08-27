#include "SceneTree.hpp"

#include "../scene/SceneObject.hpp"
#include "../scene/SelectionManager.hpp"

#include <RmlUi/Core/Elements/ElementFormControlInput.h>

#include <algorithm>
#include <cctype>
#include <functional>

namespace {
    std::string ToLower(std::string value) {
        std::transform(value.begin(), value.end(), value.begin(),
            [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        return value;
    }

    std::string Trim(const std::string& value) {
        size_t start = 0;
        size_t end = value.size();
        while (start < end && std::isspace(static_cast<unsigned char>(value[start]))) {
            ++start;
        }
        while (end > start && std::isspace(static_cast<unsigned char>(value[end - 1]))) {
            --end;
        }
        return value.substr(start, end - start);
    }
}

int SceneTree::RowNodeId(Rml::Element* element) {
    Rml::Element* current = element;
    while (current != nullptr) {
        if (current->HasAttribute("data-node-id")) {
            return current->GetAttribute<int>("data-node-id", 0);
        }
        current = current->GetParentNode();
    }
    return 0;
}

std::shared_ptr<SceneObject> SceneTree::FindById(int id) const {
    const auto it = nodesById.find(id);
    return it != nodesById.end() ? it->second : nullptr;
}

void SceneTree::Init(Rml::ElementDocument* document) {
    Shutdown();

    this->document = document;
    if (this->document == nullptr) {
        return;
    }

    container = this->document->GetElementById("scene-tree-items");
    searchInput = this->document->GetElementById("scene-tree-search");

    if (SelectionManager::Instance() != nullptr) {
        selectionChangedToken = SelectionManager::Instance()->AddSelectionChanged([this]() { Rebuild(); });
    }

    // Seed a small default scene so the panel is populated on first launch.
    // Objects live at the tree root; there is no wrapping "Scene" node.
    // The work camera used for fly controls is intentionally NOT added here –
    // it is an internal viewport camera and must not appear in the scene tree.
    if (rootObjects.empty()) {
        AddRootObject("Cube");
    }

    if (searchInput != nullptr) {
        searchInput->AddEventListener("change", &eventListener);
        searchInput->AddEventListener("keyup", &eventListener);
    }
    if (container != nullptr) {
        container->AddEventListener("click", &eventListener);
        container->AddEventListener("dblclick", &eventListener);
        container->AddEventListener("mousedown", &eventListener);
        container->AddEventListener("dragstart", &eventListener);
        container->AddEventListener("dragover", &eventListener);
        container->AddEventListener("dragout", &eventListener);
        container->AddEventListener("dragend", &eventListener);
        container->AddEventListener("dragdrop", &eventListener);
    }
    this->document->AddEventListener("mousedown", &eventListener);

    Rebuild();
}

void SceneTree::Shutdown() {
    if (SelectionManager::Instance() != nullptr && selectionChangedToken != 0) {
        SelectionManager::Instance()->RemoveSelectionChanged(selectionChangedToken);
    }
    selectionChangedToken = 0;

    CloseContextMenu();

    if (searchInput != nullptr) {
        searchInput->RemoveEventListener("change", &eventListener);
        searchInput->RemoveEventListener("keyup", &eventListener);
    }
    if (container != nullptr) {
        container->RemoveEventListener("click", &eventListener);
        container->RemoveEventListener("dblclick", &eventListener);
        container->RemoveEventListener("mousedown", &eventListener);
        container->RemoveEventListener("dragstart", &eventListener);
        container->RemoveEventListener("dragover", &eventListener);
        container->RemoveEventListener("dragout", &eventListener);
        container->RemoveEventListener("dragend", &eventListener);
        container->RemoveEventListener("dragdrop", &eventListener);
    }
    if (document != nullptr) {
        document->RemoveEventListener("mousedown", &eventListener);
    }

    document = nullptr;
    container = nullptr;
    searchInput = nullptr;
    contextMenu = nullptr;
}

std::shared_ptr<SceneObject> SceneTree::AddRootObject(const std::string& objectType) {
    auto obj = std::make_shared<SceneObject>(objectType);
    obj->AssignObjectId();
    rootObjects.push_back(obj);
    return obj;
}

std::shared_ptr<SceneObject> SceneTree::AddSpawnedObject(const std::string& objectType, const std::string& baseNameIn) {
    auto obj = std::make_shared<SceneObject>(objectType);
    obj->AssignObjectId();

    const std::string baseName = GetBaseName(baseNameIn);
    const int nextNum = GetNextAvailableNameNumber(baseName);
    obj->name = nextNum > 1 ? baseName + std::to_string(nextNum) : baseName;

    rootObjects.push_back(obj);

    if (SelectionManager::Instance() != nullptr) {
        SelectionManager::Instance()->ClearSelection();
        SelectionManager::Instance()->SelectObject(obj);
    } else {
        Rebuild();
    }

    return obj;
}

// ── DOM construction ────────────────────────────────────────────────────────

void SceneTree::RegisterNode(const std::shared_ptr<SceneObject>& obj) {
    nodesById[obj->objectId] = obj;
}

void SceneTree::Rebuild() {
    if (container == nullptr) {
        return;
    }

    CloseContextMenu();
    dragOverRow = nullptr;
    nodesById.clear();

    while (Rml::Element* child = container->GetChild(0)) {
        container->RemoveChild(child);
    }

    const std::string term = Trim(searchQuery);
    std::set<const SceneObject*> visibleSet;
    const std::set<const SceneObject*>* visibilityFilter = nullptr;
    if (!term.empty()) {
        const std::string lowered = ToLower(term);
        for (const auto& root : rootObjects) {
            PopulateFilterVisibleSet(root, lowered, visibleSet);
        }
        visibilityFilter = &visibleSet;
    }

    int rendered = 0;
    for (const auto& obj : rootObjects) {
        if (visibilityFilter != nullptr && visibilityFilter->find(obj.get()) == visibilityFilter->end()) {
            continue;
        }
        BuildNode(container, obj, 0, visibilityFilter);
        ++rendered;
    }

    if (visibilityFilter != nullptr && rendered == 0) {
        Rml::ElementPtr empty = document->CreateElement("div");
        empty->SetClass("tree-empty", true);
        empty->SetInnerRML("No scene objects match the current search.");
        container->AppendChild(std::move(empty));
    }
}

void SceneTree::BuildNode(Rml::Element* parent, const std::shared_ptr<SceneObject>& obj, int depth,
                          const std::set<const SceneObject*>* visibilityFilter) {
    if (obj->hideInSceneTree) {
        return;
    }

    RegisterNode(obj);

    bool hasChildren = false;
    for (const auto& child : obj->GetChildren()) {
        if (child->hideInSceneTree) {
            continue;
        }
        if (visibilityFilter != nullptr && visibilityFilter->find(child.get()) == visibilityFilter->end()) {
            continue;
        }
        hasChildren = true;
        break;
    }

    // Search always forces branches open so matches are visible.
    const bool collapsed = visibilityFilter == nullptr && collapsedNodes.count(obj->objectId) != 0;
    const bool selected = SelectionManager::Instance() != nullptr && SelectionManager::Instance()->IsSelected(obj.get());
    const bool renaming = renamingNodeId == obj->objectId;

    Rml::ElementPtr rowPtr = document->CreateElement("div");
    Rml::Element* row = rowPtr.get();
    row->SetClass("tree-row", true);
    if (depth == 0) {
        row->SetClass("tree-root", true);
    } else {
        row->SetClass("tree-child", true);
    }
    if (selected) {
        row->SetClass("selected", true);
    }
    row->SetAttribute("data-node-id", obj->objectId);
    row->SetProperty("padding-left", Rml::CreateString("%fdp", 7.0f + depth * 16.0f));
    // "drag-drop" (not plain "drag") is required so that dropping this row on
    // another element generates a "dragdrop" event on the element underneath.
    row->SetProperty("drag", "drag-drop");

    // Expand / collapse arrow.
    Rml::ElementPtr arrow = document->CreateElement("span");
    arrow->SetClass("tree-arrow", true);
    arrow->SetAttribute("data-arrow", obj->objectId);
    if (hasChildren) {
        arrow->SetInnerRML(collapsed ? "&#9656;" : "&#9662;");
    } else {
        arrow->SetInnerRML("");
    }
    row->AppendChild(std::move(arrow));

    if (renaming) {
        Rml::ElementPtr input = document->CreateElement("input");
        input->SetAttribute("type", "text");
        input->SetAttribute("value", obj->GetDisplayName());
        input->SetClass("tree-rename-input", true);
        input->SetAttribute("data-rename-id", obj->objectId);
        Rml::Element* inputElement = input.get();
        row->AppendChild(std::move(input));
        inputElement->AddEventListener("blur", &eventListener);
        inputElement->AddEventListener("keydown", &eventListener);
        inputElement->Focus();
    } else {
        Rml::ElementPtr label = document->CreateElement("span");
        label->SetClass("tree-label", true);
        label->SetInnerRML(obj->GetDisplayName());
        row->AppendChild(std::move(label));
    }

    parent->AppendChild(std::move(rowPtr));

    if (hasChildren && !collapsed) {
        Rml::ElementPtr childrenContainer = document->CreateElement("div");
        childrenContainer->SetClass("tree-children", true);
        Rml::Element* childrenElement = childrenContainer.get();
        parent->AppendChild(std::move(childrenContainer));

        // Snapshot children so a mutation during iteration is safe.
        std::vector<std::shared_ptr<SceneObject>> childSnapshot(obj->GetChildren().begin(), obj->GetChildren().end());
        for (const auto& child : childSnapshot) {
            if (child->hideInSceneTree) {
                continue;
            }
            if (visibilityFilter != nullptr && visibilityFilter->find(child.get()) == visibilityFilter->end()) {
                continue;
            }
            BuildNode(childrenElement, child, depth + 1, visibilityFilter);
        }
    }
}

// ── Event dispatch ──────────────────────────────────────────────────────────

void SceneTree::TreeEventListener::ProcessEvent(Rml::Event& event) {
    if (owner == nullptr) {
        return;
    }

    Rml::Element* target = event.GetTargetElement();
    Rml::Element* current = event.GetCurrentElement();
    const Rml::String type = event.GetType();

    // Search box (live filtering on both keyup and change/commit).
    if ((type == "change" || type == "keyup") && current == owner->searchInput) {
        const Rml::String value = current->GetAttribute<Rml::String>("value", "");
        if (value != owner->searchQuery) {
            owner->searchQuery = value;
            owner->Rebuild();
        }
        return;
    }

    // Rename input events.
    if (target != nullptr && target->HasAttribute("data-rename-id")) {
        if (type == "blur") {
            owner->CommitRename(target);
            return;
        }
        if (type == "keydown") {
            const int key = event.GetParameter<int>("key_identifier", 0);
            if (key == Rml::Input::KI_RETURN || key == Rml::Input::KI_NUMPADENTER) {
                owner->CommitRename(target);
            } else if (key == Rml::Input::KI_ESCAPE) {
                owner->renamingNodeId = 0;
                owner->Rebuild();
            }
            return;
        }
    }

    // Close the context menu when clicking elsewhere.
    if (type == "mousedown" && current == owner->document) {
        if (owner->suppressNextMenuClose) {
            owner->suppressNextMenuClose = false;
            return;
        }
        if (owner->contextMenu != nullptr) {
            Rml::Element* walker = target;
            bool insideMenu = false;
            while (walker != nullptr) {
                if (walker == owner->contextMenu) {
                    insideMenu = true;
                    break;
                }
                walker = walker->GetParentNode();
            }
            if (!insideMenu) {
                owner->CloseContextMenu();
            }
        }
        return;
    }

    // Context menu item clicks.
    if (target != nullptr && target->HasAttribute("data-menu-action")) {
        if (type == "click") {
            const Rml::String action = target->GetAttribute<Rml::String>("data-menu-action", "");
            auto menuTarget = owner->contextMenuTarget;
            owner->CloseContextMenu();
            if (menuTarget != nullptr) {
                if (action == "duplicate") {
                    owner->DuplicateObject(menuTarget, true);
                } else if (action == "delete") {
                    owner->DeleteObject(menuTarget);
                    owner->Rebuild();
                } else if (action == "rename") {
                    owner->renamingNodeId = menuTarget->objectId;
                    owner->Rebuild();
                }
            }
        }
        return;
    }

    // Drag-and-drop reparenting — visual feedback.
    // These fire on the dragged element (dragstart/dragend) and on whatever
    // element the cursor passes over while dragging (dragover/dragout), so
    // the user can actually see that a drag is happening and where it will land.
    if (type == "dragstart") {
        target->SetClass("dragging", true);
        return;
    }
    if (type == "dragend") {
        target->SetClass("dragging", false);
        owner->ClearDropHighlight();
        return;
    }
    if (type == "dragover") {
        Rml::Element* row = target;
        while (row != nullptr && !row->HasAttribute("data-node-id")) {
            row = row->GetParentNode();
        }
        if (row != nullptr) {
            owner->ClearDropHighlight();
            row->SetClass("drag-over", true);
            owner->dragOverRow = row;
        }
        return;
    }
    if (type == "dragout") {
        Rml::Element* row = target;
        while (row != nullptr && !row->HasAttribute("data-node-id")) {
            row = row->GetParentNode();
        }
        if (row != nullptr && row == owner->dragOverRow) {
            row->SetClass("drag-over", false);
            owner->dragOverRow = nullptr;
        }
        return;
    }

    // Drag-and-drop reparenting.
    if (type == "dragdrop") {
        owner->ClearDropHighlight();
        Rml::Element* dragElement = static_cast<Rml::Element*>(event.GetParameter<void*>("drag_element", nullptr));
        owner->HandleDrop(dragElement, target);
        return;
    }

    // Everything below operates on a tree row.
    const int nodeId = RowNodeId(target);
    if (nodeId == 0) {
        return;
    }
    Rml::Element* row = target;
    while (row != nullptr && !row->HasAttribute("data-node-id")) {
        row = row->GetParentNode();
    }
    if (row == nullptr) {
        return;
    }

    // Arrow click toggles expand/collapse.
    if (type == "click" && target->HasAttribute("data-arrow")) {
        if (owner->collapsedNodes.count(nodeId) != 0) {
            owner->collapsedNodes.erase(nodeId);
        } else {
            owner->collapsedNodes.insert(nodeId);
        }
        owner->Rebuild();
        return;
    }

    if (type == "click") {
        const bool ctrl = event.GetParameter<bool>("ctrl_key", false);
        const bool shift = event.GetParameter<bool>("shift_key", false);
        owner->OnRowClick(row, ctrl, shift);
        return;
    }

    if (type == "dblclick") {
        owner->BeginRename(row);
        return;
    }

    if (type == "mousedown") {
        const int button = event.GetParameter<int>("button", 0);
        if (button == 1) {
            const float mouseX = event.GetParameter<float>("mouse_x", 0.0f);
            const float mouseY = event.GetParameter<float>("mouse_y", 0.0f);
            owner->OnRowRightClick(row, mouseX, mouseY);
        }
        return;
    }
}

void SceneTree::OnRowClick(Rml::Element* row, bool ctrl, bool shift) {
    auto obj = FindById(RowNodeId(row));
    if (obj == nullptr) {
        return;
    }
    HandleClickSelection(obj, ctrl, shift);
}

void SceneTree::OnRowRightClick(Rml::Element* row, float mouseX, float mouseY) {
    auto obj = FindById(RowNodeId(row));
    if (obj == nullptr) {
        return;
    }

    if (SelectionManager::Instance() != nullptr && !SelectionManager::Instance()->IsSelected(obj.get())) {
        SelectionManager::Instance()->ClearSelection();
        SelectionManager::Instance()->SelectObject(obj);
    }
    ShowContextMenu(obj, mouseX, mouseY);
}

void SceneTree::HandleClickSelection(const std::shared_ptr<SceneObject>& obj, bool ctrl, bool shift) {
    SelectionManager* selection = SelectionManager::Instance();
    if (selection == nullptr) {
        return;
    }

    if (ctrl) {
        selection->ToggleSelection(obj);
        lastClickedObject = obj;
    } else if (shift && lastClickedObject != nullptr) {
        std::vector<std::shared_ptr<SceneObject>> flat;
        const std::string term = Trim(searchQuery);
        std::set<const SceneObject*> visibleSet;
        const std::set<const SceneObject*>* filter = nullptr;
        if (!term.empty()) {
            const std::string lowered = ToLower(term);
            for (const auto& root : rootObjects) {
                PopulateFilterVisibleSet(root, lowered, visibleSet);
            }
            filter = &visibleSet;
        }
        FlattenVisibleTree(flat, filter);

        const auto startIt = std::find(flat.begin(), flat.end(), lastClickedObject);
        const auto endIt = std::find(flat.begin(), flat.end(), obj);
        if (startIt != flat.end() && endIt != flat.end()) {
            auto low = startIt;
            auto high = endIt;
            if (low > high) {
                std::swap(low, high);
            }
            for (auto it = low; it <= high; ++it) {
                if (!selection->IsSelected(it->get())) {
                    selection->SelectObject(*it);
                }
            }
        }
        lastClickedObject = obj;
    } else {
        selection->ClearSelection();
        selection->SelectObject(obj);
        lastClickedObject = obj;
    }
}

void SceneTree::FlattenVisibleTree(std::vector<std::shared_ptr<SceneObject>>& out,
                                   const std::set<const SceneObject*>* visibilityFilter) const {
    std::function<void(const std::shared_ptr<SceneObject>&)> flatten =
        [&](const std::shared_ptr<SceneObject>& obj) {
            if (obj->hideInSceneTree) {
                return;
            }
            if (visibilityFilter != nullptr && visibilityFilter->find(obj.get()) == visibilityFilter->end()) {
                return;
            }
            out.push_back(obj);
            for (const auto& child : obj->GetChildren()) {
                flatten(child);
            }
        };
    for (const auto& root : rootObjects) {
        flatten(root);
    }
}

// ── Rename ──────────────────────────────────────────────────────────────────

void SceneTree::BeginRename(Rml::Element* row) {
    const int nodeId = RowNodeId(row);
    if (nodeId == 0) {
        return;
    }
    renamingNodeId = nodeId;
    Rebuild();
}

void SceneTree::CommitRename(Rml::Element* input) {
    const int nodeId = input->GetAttribute<int>("data-rename-id", 0);
    auto obj = FindById(nodeId);
    if (obj != nullptr) {
        Rml::String value;
        if (auto* control = rmlui_dynamic_cast<Rml::ElementFormControlInput*>(input)) {
            value = control->GetValue();
        } else {
            value = input->GetAttribute<Rml::String>("value", "");
        }
        const std::string trimmed = Trim(value);
        if (!trimmed.empty()) {
            obj->name = trimmed;
        }
    }
    renamingNodeId = 0;
    Rebuild();
}

// ── Context menu ──────────────────────────────────────────────────────────────

void SceneTree::ShowContextMenu(const std::shared_ptr<SceneObject>& target, float mouseX, float mouseY) {
    CloseContextMenu();
    if (document == nullptr) {
        return;
    }

    contextMenuTarget = target;

    Rml::ElementPtr menuPtr = document->CreateElement("div");
    menuPtr->SetClass("scene-tree-context-menu", true);
    menuPtr->SetProperty("left", Rml::CreateString("%fpx", mouseX));
    menuPtr->SetProperty("top", Rml::CreateString("%fpx", mouseY));

    const char* labels[] = {"Rename", "Duplicate", "Delete"};
    const char* actions[] = {"rename", "duplicate", "delete"};
    for (int i = 0; i < 3; ++i) {
        Rml::ElementPtr item = document->CreateElement("div");
        item->SetClass("context-menu-item", true);
        item->SetAttribute("data-menu-action", actions[i]);
        item->SetInnerRML(labels[i]);
        Rml::Element* itemElement = item.get();
        menuPtr->AppendChild(std::move(item));
        itemElement->AddEventListener("click", &eventListener);
    }

    contextMenu = document->AppendChild(std::move(menuPtr));
    suppressNextMenuClose = true;
}

void SceneTree::CloseContextMenu() {
    if (contextMenu != nullptr && document != nullptr) {
        document->RemoveChild(contextMenu);
    }
    contextMenu = nullptr;
    contextMenuTarget = nullptr;
}

// ── Drag-and-drop ─────────────────────────────────────────────────────────────

void SceneTree::ClearDropHighlight() {
    if (dragOverRow != nullptr) {
        dragOverRow->SetClass("drag-over", false);
        dragOverRow = nullptr;
    }
}

void SceneTree::HandleDrop(Rml::Element* dragElement, Rml::Element* targetRow) {
    if (dragElement == nullptr) {
        return;
    }
    auto dragged = FindById(RowNodeId(dragElement));
    if (dragged == nullptr) {
        return;
    }

    const int targetId = RowNodeId(targetRow);
    if (targetId == 0) {
        // Dropped on blank area -> unparent to root.
        if (dragged->GetParent() != nullptr) {
            ReparentObject(dragged, nullptr);
        }
        return;
    }

    auto target = FindById(targetId);
    if (target == nullptr || target == dragged || target->IsDescendantOf(dragged.get())) {
        return;
    }
    ReparentObject(dragged, target);
}

// ── Model operations ──────────────────────────────────────────────────────────

bool SceneTree::ReparentObject(const std::shared_ptr<SceneObject>& obj, const std::shared_ptr<SceneObject>& newParent) {
    if (obj == nullptr || obj == newParent ||
        (newParent != nullptr && newParent->IsDescendantOf(obj.get()))) {
        return false;
    }

    SceneObject* currentParent = obj->GetParent();
    if (currentParent == newParent.get()) {
        return false;
    }

    // Detach from current owner (keep a strong ref alive across the move).
    std::shared_ptr<SceneObject> keepAlive = obj;
    if (currentParent != nullptr) {
        currentParent->RemoveChild(obj);
    } else {
        rootObjects.erase(std::remove(rootObjects.begin(), rootObjects.end(), obj), rootObjects.end());
    }

    if (newParent != nullptr) {
        newParent->AddChild(obj);
    } else if (std::find(rootObjects.begin(), rootObjects.end(), obj) == rootObjects.end()) {
        rootObjects.push_back(obj);
    }

    Rebuild();
    return true;
}

void SceneTree::DeleteObject(const std::shared_ptr<SceneObject>& obj) {
    if (obj == nullptr) {
        return;
    }

    if (SelectionManager::Instance() != nullptr) {
        SelectionManager::Instance()->DeselectObject(obj);
    }

    std::shared_ptr<SceneObject> keepAlive = obj;
    if (obj->GetParent() != nullptr) {
        obj->GetParent()->RemoveChild(obj);
    } else {
        rootObjects.erase(std::remove(rootObjects.begin(), rootObjects.end(), obj), rootObjects.end());
    }
}

std::shared_ptr<SceneObject> SceneTree::DuplicateObject(const std::shared_ptr<SceneObject>& original, bool selectDuplicate) {
    if (original == nullptr) {
        return nullptr;
    }

    std::function<std::shared_ptr<SceneObject>(const std::shared_ptr<SceneObject>&)> clone =
        [&](const std::shared_ptr<SceneObject>& src) {
            auto copy = std::make_shared<SceneObject>();
            copy->AssignObjectId();
            copy->objectType = src->objectType;
            copy->hideInSceneTree = src->hideInSceneTree;
            copy->isSelectable = src->isSelectable;
            copy->objectVisible = src->objectVisible;
            for (const auto& child : src->GetChildren()) {
                copy->AddChild(clone(child));
            }
            return copy;
        };

    auto duplicate = clone(original);

    const std::string baseName = GetBaseName(original->GetDisplayName());
    const int nextNum = GetNextAvailableNameNumber(baseName);
    duplicate->name = nextNum > 1 ? baseName + std::to_string(nextNum) : baseName;

    if (original->GetParent() != nullptr) {
        original->GetParent()->AddChild(duplicate);
    } else {
        rootObjects.push_back(duplicate);
    }

    if (selectDuplicate && SelectionManager::Instance() != nullptr) {
        SelectionManager::Instance()->ClearSelection();
        SelectionManager::Instance()->SelectObject(duplicate);
    } else {
        Rebuild();
    }

    return duplicate;
}

// ── Search helpers ────────────────────────────────────────────────────────────

bool SceneTree::PopulateFilterVisibleSet(const std::shared_ptr<SceneObject>& obj, const std::string& term,
                                         std::set<const SceneObject*>& visible) const {
    if (obj->hideInSceneTree) {
        return false;
    }

    const bool selfMatches = ToLower(obj->GetDisplayName()).find(term) != std::string::npos;
    bool childMatches = false;
    for (const auto& child : obj->GetChildren()) {
        childMatches = PopulateFilterVisibleSet(child, term, visible) || childMatches;
    }

    if (selfMatches || childMatches) {
        visible.insert(obj.get());
        return true;
    }
    return false;
}

// ── Naming helpers ────────────────────────────────────────────────────────────

std::string SceneTree::GetBaseName(const std::string& name) const {
    int i = static_cast<int>(name.size()) - 1;
    while (i >= 0 && std::isdigit(static_cast<unsigned char>(name[i]))) {
        --i;
    }
    if (i >= 0 && i < static_cast<int>(name.size()) - 1) {
        return name.substr(0, i + 1);
    }
    return name;
}

int SceneTree::GetNextAvailableNameNumber(const std::string& baseName) const {
    std::set<int> used;

    std::function<void(const std::shared_ptr<SceneObject>&)> scan =
        [&](const std::shared_ptr<SceneObject>& node) {
            const std::string n = node->GetDisplayName();
            if (n == baseName) {
                used.insert(1);
            } else if (n.size() > baseName.size() && n.compare(0, baseName.size(), baseName) == 0) {
                const std::string suffix = n.substr(baseName.size());
                bool allDigits = !suffix.empty();
                for (char c : suffix) {
                    if (!std::isdigit(static_cast<unsigned char>(c))) {
                        allDigits = false;
                        break;
                    }
                }
                if (allDigits) {
                    used.insert(std::stoi(suffix));
                }
            }
            for (const auto& child : node->GetChildren()) {
                scan(child);
            }
        };

    for (const auto& root : rootObjects) {
        scan(root);
    }

    int next = 1;
    while (used.count(next) != 0) {
        ++next;
    }
    return next;
}
