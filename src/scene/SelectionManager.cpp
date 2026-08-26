#include "SelectionManager.hpp"

#include "SceneObject.hpp"

#include <algorithm>

namespace {
    SelectionManager* g_instance = nullptr;
    int g_nextObjectId = 1;
}

SelectionManager* SelectionManager::Instance() {
    return g_instance;
}

void SelectionManager::Initialize() {
    if (g_instance == nullptr) {
        g_instance = new SelectionManager();
    }
}

void SelectionManager::Shutdown() {
    delete g_instance;
    g_instance = nullptr;
}

int SelectionManager::GetNextObjectId() {
    return g_nextObjectId++;
}

void SelectionManager::SelectObject(const std::shared_ptr<SceneObject>& obj) {
    if (!obj) {
        return;
    }
    if (IsSelected(obj.get())) {
        return;
    }
    if (!obj->isSelectable) {
        return;
    }

    selectedObjects.push_back(obj);
    obj->isSelected = true;
    NotifySelectionChanged();
}

void SelectionManager::DeselectObject(const std::shared_ptr<SceneObject>& obj) {
    if (!obj) {
        return;
    }

    const auto it = std::find(selectedObjects.begin(), selectedObjects.end(), obj);
    if (it == selectedObjects.end()) {
        return;
    }

    (*it)->isSelected = false;
    selectedObjects.erase(it);
    NotifySelectionChanged();
}

void SelectionManager::ToggleSelection(const std::shared_ptr<SceneObject>& obj) {
    if (!obj) {
        return;
    }

    if (IsSelected(obj.get())) {
        DeselectObject(obj);
    } else {
        SelectObject(obj);
    }
}

void SelectionManager::ClearSelection() {
    if (selectedObjects.empty()) {
        return;
    }

    for (const auto& obj : selectedObjects) {
        obj->isSelected = false;
    }
    selectedObjects.clear();
    NotifySelectionChanged();
}

bool SelectionManager::IsSelected(const SceneObject* obj) const {
    if (obj == nullptr) {
        return false;
    }
    return std::any_of(selectedObjects.begin(), selectedObjects.end(),
        [obj](const std::shared_ptr<SceneObject>& candidate) { return candidate.get() == obj; });
}

int SelectionManager::AddSelectionChanged(std::function<void()> callback) {
    const int token = nextCallbackToken++;
    selectionChangedCallbacks.emplace_back(token, std::move(callback));
    return token;
}

void SelectionManager::RemoveSelectionChanged(int token) {
    selectionChangedCallbacks.erase(
        std::remove_if(selectionChangedCallbacks.begin(), selectionChangedCallbacks.end(),
            [token](const std::pair<int, std::function<void()>>& entry) { return entry.first == token; }),
        selectionChangedCallbacks.end());
}

void SelectionManager::NotifySelectionChanged() {
    // Copy so a callback that unsubscribes doesn't invalidate iteration.
    const auto callbacks = selectionChangedCallbacks;
    for (const auto& entry : callbacks) {
        if (entry.second) {
            entry.second();
        }
    }
}
