#pragma once

#include <memory>
#include <string>
#include <array>
#include <vector>

// Minimal scene-graph node ported from the reference project's SceneObject.
// Only the hierarchy / naming / selection state needed by the scene-tree UI is
// carried over here; the heavy 3D transform and rendering logic from the
// original C# SceneObject is intentionally out of scope for this port.
class SceneObject : public std::enable_shared_from_this<SceneObject> {
public:
    // ── Identity / display ──────────────────────────────────────────────────
    std::string name;
    std::string objectType;
    int objectId = 0;

    // ── UI / selection flags ────────────────────────────────────────────────
    bool hideInSceneTree = false;
    bool isSelectable = true;
    bool isSelected = false;
    bool objectVisible = true;

    // ── Basic editable transform (properties-panel port) ───────────────────
    std::array<float, 3> localPosition{0.0f, 0.0f, 0.0f};
    std::array<float, 3> localRotation{0.0f, 0.0f, 0.0f};
    std::array<float, 3> localScale{1.0f, 1.0f, 1.0f};

    SceneObject() = default;
    explicit SceneObject(std::string objectType);
    virtual ~SceneObject() = default;

    // ── Hierarchy ───────────────────────────────────────────────────────────
    SceneObject* GetParent() const { return parent; }
    const std::vector<std::shared_ptr<SceneObject>>& GetChildren() const { return children; }

    void AddChild(const std::shared_ptr<SceneObject>& child);
    void RemoveChild(const std::shared_ptr<SceneObject>& child);
    bool IsDescendantOf(const SceneObject* ancestor) const;
    void CollectDescendants(std::vector<std::shared_ptr<SceneObject>>& out) const;

    // ── Display ─────────────────────────────────────────────────────────────
    std::string GetDisplayName() const;

    // Assigns a fresh globally-unique object id (used to map DOM rows back to
    // this object). Mirrors SceneObject.AssignObjectId in the reference project.
    void AssignObjectId();

private:
    SceneObject* parent = nullptr;
    std::vector<std::shared_ptr<SceneObject>> children;
};
