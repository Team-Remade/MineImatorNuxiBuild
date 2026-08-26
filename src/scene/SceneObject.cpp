#include "SceneObject.hpp"

#include "SelectionManager.hpp"

#include <algorithm>

SceneObject::SceneObject(std::string objectType) : objectType(std::move(objectType)) {}

void SceneObject::AddChild(const std::shared_ptr<SceneObject>& child) {
    if (!child || child.get() == this) {
        return;
    }

    if (child->parent != nullptr) {
        child->parent->RemoveChild(child);
    }

    child->parent = this;
    children.push_back(child);
}

void SceneObject::RemoveChild(const std::shared_ptr<SceneObject>& child) {
    if (!child) {
        return;
    }

    const auto it = std::find(children.begin(), children.end(), child);
    if (it != children.end()) {
        (*it)->parent = nullptr;
        children.erase(it);
    }
}

bool SceneObject::IsDescendantOf(const SceneObject* ancestor) const {
    const SceneObject* current = parent;
    while (current != nullptr) {
        if (current == ancestor) {
            return true;
        }
        current = current->parent;
    }
    return false;
}

void SceneObject::CollectDescendants(std::vector<std::shared_ptr<SceneObject>>& out) const {
    for (const auto& child : children) {
        out.push_back(child);
        child->CollectDescendants(out);
    }
}

std::string SceneObject::GetDisplayName() const {
    if (!name.empty()) {
        return name;
    }
    if (!objectType.empty()) {
        return objectType;
    }
    return "Object";
}

void SceneObject::AssignObjectId() {
    objectId = SelectionManager::GetNextObjectId();
}
