#pragma once

#include <RmlUi/Core.h>

#include <string>
#include <vector>

class SceneTree;

// Floating "Spawn Menu" panel (Categories | Objects | Variants | Preview),
// ported from the reference project's ImGui SpawnMenu.cs.
//
// The reference SpawnMenu.cs is ~5900 lines and its Items / Blocks /
// Characters / Particle Spawners / Scenery / Custom Models categories are
// built on subsystems that do not exist in this build yet (BlockRegistry,
// CharacterRegistry, MinecraftDataLoader, MineImatorLoader, AssimpModelLoader,
// the NBT schematic parser, and the per-object 3-D mesh pipeline — this
// project's SceneObject currently has no transform/geometry at all). Those
// categories are kept in the menu (same names, same order) but rendered as
// "not available" placeholders instead of being faked. Camera, Light, and
// Primitives are ported fully: selecting an object and pressing Spawn (or
// double-clicking) creates a new named SceneObject in the scene tree with
// the same auto-numbering rules ("Cube", "Cube1", "Cube2", ...) as the rest
// of the app.
class SpawnMenu {
public:
    // spawnMenuDocument: the floating spawn-menu-overlay document.
    // viewportDocument: hosts the "spawn-menu-btn" button that toggles this menu.
    void Init(Rml::ElementDocument* spawnMenuDocument, Rml::ElementDocument* viewportDocument, SceneTree* sceneTree);
    void Shutdown();

    // Opens/closes the spawn menu window. Mirrors SpawnMenu.Toggle() in the reference project.
    void Toggle();

private:
    struct Category {
        std::string name;
        std::vector<std::string> objects;
        // False for categories whose C# data sources (Minecraft registries,
        // Assimp/MineImator loaders, NBT parser) are not ported yet.
        bool implemented;
    };

    class SpawnMenuEventListener final : public Rml::EventListener {
    public:
        explicit SpawnMenuEventListener(SpawnMenu* owner) : owner(owner) {}
        void ProcessEvent(Rml::Event& event) override;

    private:
        SpawnMenu* owner;
    };

    void RebuildCategories();
    void RebuildObjects();
    void RebuildVariants();
    void RebuildPreview();

    void SelectCategory(const std::string& categoryName);
    void SelectObject(int index);
    void TrySpawn();

    std::vector<std::string> GetFilteredObjects() const;
    const Category* GetSelectedCategory() const;

    Rml::ElementDocument* document = nullptr;
    Rml::ElementDocument* viewportDocument = nullptr;
    Rml::Element* openButton = nullptr;
    Rml::Element* overlay = nullptr;
    Rml::Element* categoriesContainer = nullptr;
    Rml::Element* objectsContainer = nullptr;
    Rml::Element* variantsContainer = nullptr;
    Rml::Element* previewContainer = nullptr;
    Rml::Element* searchInput = nullptr;
    Rml::Element* clearButton = nullptr;
    Rml::Element* closeButton = nullptr;
    Rml::Element* spawnButton = nullptr;

    SceneTree* sceneTree = nullptr;

    std::vector<Category> categories;
    std::string selectedCategory = "Primitives";
    int selectedObjectIndex = -1;
    std::string searchQuery;
    bool isOpen = false;

    SpawnMenuEventListener eventListener{this};
};
