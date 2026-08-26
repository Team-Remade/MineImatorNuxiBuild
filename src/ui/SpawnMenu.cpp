#include "SpawnMenu.hpp"

#include "SceneTree.hpp"

#include <RmlUi/Core/Elements/ElementFormControlInput.h>

#include <algorithm>
#include <cctype>

namespace {
    std::string ToLower(std::string value) {
        std::transform(value.begin(), value.end(), value.begin(),
            [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        return value;
    }
}

// ── Lifecycle ────────────────────────────────────────────────────────────────

void SpawnMenu::Init(Rml::ElementDocument* spawnMenuDocument, Rml::ElementDocument* viewportDocument, SceneTree* sceneTree) {
    Shutdown();

    this->document = spawnMenuDocument;
    this->viewportDocument = viewportDocument;
    this->sceneTree = sceneTree;

    // Category → object list, same names/order as the reference SpawnMenu.cs
    // constructor. Camera / Light / Primitives map onto this project's actual
    // SceneObject model and are fully spawnable; the remaining categories
    // depend on subsystems (BlockRegistry, CharacterRegistry,
    // MinecraftDataLoader, MineImatorLoader, AssimpModelLoader, the NBT
    // schematic parser) that have not been ported yet, so they are shown but
    // marked unavailable rather than faked.
    categories = {
        {"Camera", {"Camera"}, true},
        {"Light", {"Point Light", "Spot Light"}, true},
        {"Primitives", {"Empty", "Cube", "Sphere", "Cylinder", "Cone", "Torus", "Plane", "Capsule", "Text Mesh"}, true},
        {"Items", {}, false},
        {"Blocks", {}, false},
        {"Characters", {}, false},
        {"Particle Spawners", {"Particle Spawner"}, false},
        {"Scenery", {"Load schematic..."}, false},
        {"Custom Models", {"Load..."}, false},
    };

    if (this->document != nullptr) {
        overlay = this->document->GetElementById("spawn-menu-overlay");
        categoriesContainer = this->document->GetElementById("spawn-menu-categories");
        objectsContainer = this->document->GetElementById("spawn-menu-objects");
        variantsContainer = this->document->GetElementById("spawn-menu-variants");
        previewContainer = this->document->GetElementById("spawn-menu-preview");
        searchInput = this->document->GetElementById("spawn-menu-search");
        clearButton = this->document->GetElementById("spawn-menu-clear");
        closeButton = this->document->GetElementById("spawn-menu-close");
        spawnButton = this->document->GetElementById("spawn-menu-spawn-btn");

        if (overlay != nullptr) overlay->AddEventListener("click", &eventListener);
        if (searchInput != nullptr) {
            searchInput->AddEventListener("change", &eventListener);
            searchInput->AddEventListener("keyup", &eventListener);
        }
        if (clearButton != nullptr) clearButton->AddEventListener("click", &eventListener);
        if (closeButton != nullptr) closeButton->AddEventListener("click", &eventListener);
        if (spawnButton != nullptr) spawnButton->AddEventListener("click", &eventListener);
        if (categoriesContainer != nullptr) categoriesContainer->AddEventListener("click", &eventListener);
        if (objectsContainer != nullptr) {
            objectsContainer->AddEventListener("click", &eventListener);
            objectsContainer->AddEventListener("dblclick", &eventListener);
        }
    }

    if (this->viewportDocument != nullptr) {
        openButton = this->viewportDocument->GetElementById("spawn-menu-btn");
        if (openButton != nullptr) openButton->AddEventListener("click", &eventListener);
    }

    RebuildCategories();
    RebuildObjects();
    RebuildVariants();
    RebuildPreview();
}

void SpawnMenu::Shutdown() {
    if (overlay != nullptr) overlay->RemoveEventListener("click", &eventListener);
    if (searchInput != nullptr) {
        searchInput->RemoveEventListener("change", &eventListener);
        searchInput->RemoveEventListener("keyup", &eventListener);
    }
    if (clearButton != nullptr) clearButton->RemoveEventListener("click", &eventListener);
    if (closeButton != nullptr) closeButton->RemoveEventListener("click", &eventListener);
    if (spawnButton != nullptr) spawnButton->RemoveEventListener("click", &eventListener);
    if (categoriesContainer != nullptr) categoriesContainer->RemoveEventListener("click", &eventListener);
    if (objectsContainer != nullptr) {
        objectsContainer->RemoveEventListener("click", &eventListener);
        objectsContainer->RemoveEventListener("dblclick", &eventListener);
    }
    if (openButton != nullptr) openButton->RemoveEventListener("click", &eventListener);

    document = nullptr;
    viewportDocument = nullptr;
    openButton = nullptr;
    overlay = nullptr;
    categoriesContainer = nullptr;
    objectsContainer = nullptr;
    variantsContainer = nullptr;
    previewContainer = nullptr;
    searchInput = nullptr;
    clearButton = nullptr;
    closeButton = nullptr;
    spawnButton = nullptr;
    sceneTree = nullptr;
}

// ── Toggle (mirrors SpawnMenu.Toggle in the reference project) ──────────────

void SpawnMenu::Toggle() {
    isOpen = !isOpen;
    if (overlay != nullptr) {
        overlay->SetClass("open", isOpen);
    }

    if (document != nullptr) {
        if (isOpen) {
            // Modal + pulled to front so the menu always renders above (and
            // takes input priority over) the other docked panel documents,
            // instead of relying on RmlUi's click-driven auto-focus, which
            // otherwise leaves stale panel borders drawn on top until the
            // user happens to click inside the menu.
            document->PullToFront();
            document->Show(Rml::ModalFlag::Modal, Rml::FocusFlag::Document);
        } else {
            document->Hide();
        }
    }
}

// ── Category helpers ─────────────────────────────────────────────────────────

const SpawnMenu::Category* SpawnMenu::GetSelectedCategory() const {
    for (const Category& category : categories) {
        if (category.name == selectedCategory) {
            return &category;
        }
    }
    return nullptr;
}

std::vector<std::string> SpawnMenu::GetFilteredObjects() const {
    const Category* category = GetSelectedCategory();
    if (category == nullptr) {
        return {};
    }

    if (searchQuery.empty()) {
        return category->objects;
    }

    const std::string lowered = ToLower(searchQuery);
    std::vector<std::string> filtered;
    for (const std::string& object : category->objects) {
        if (ToLower(object).find(lowered) != std::string::npos) {
            filtered.push_back(object);
        }
    }
    return filtered;
}

// ── DOM construction ─────────────────────────────────────────────────────────

void SpawnMenu::RebuildCategories() {
    if (categoriesContainer == nullptr) {
        return;
    }

    while (Rml::Element* child = categoriesContainer->GetChild(0)) {
        categoriesContainer->RemoveChild(child);
    }

    for (const Category& category : categories) {
        Rml::ElementPtr item = document->CreateElement("div");
        item->SetClass("spawn-menu-item", true);
        item->SetClass("selected", category.name == selectedCategory);
        item->SetAttribute("data-category", category.name);
        item->SetInnerRML(category.name);
        categoriesContainer->AppendChild(std::move(item));
    }
}

void SpawnMenu::RebuildObjects() {
    if (objectsContainer == nullptr) {
        return;
    }

    while (Rml::Element* child = objectsContainer->GetChild(0)) {
        objectsContainer->RemoveChild(child);
    }

    const Category* category = GetSelectedCategory();
    if (category == nullptr) {
        return;
    }

    if (!category->implemented) {
        Rml::ElementPtr empty = document->CreateElement("div");
        empty->SetClass("spawn-menu-empty", true);
        empty->SetInnerRML("(not available in this build)");
        objectsContainer->AppendChild(std::move(empty));
        return;
    }

    const std::vector<std::string> filtered = GetFilteredObjects();
    for (int i = 0; i < static_cast<int>(filtered.size()); ++i) {
        Rml::ElementPtr item = document->CreateElement("div");
        item->SetClass("spawn-menu-item", true);
        item->SetClass("selected", selectedObjectIndex == i);
        item->SetAttribute("data-object-index", i);
        item->SetInnerRML(filtered[static_cast<size_t>(i)]);
        objectsContainer->AppendChild(std::move(item));
    }

    if (filtered.empty()) {
        Rml::ElementPtr empty = document->CreateElement("div");
        empty->SetClass("spawn-menu-empty", true);
        empty->SetInnerRML("(no matches)");
        objectsContainer->AppendChild(std::move(empty));
    }
}

void SpawnMenu::RebuildVariants() {
    if (variantsContainer == nullptr) {
        return;
    }

    while (Rml::Element* child = variantsContainer->GetChild(0)) {
        variantsContainer->RemoveChild(child);
    }

    // No category currently exposes per-object variants (Blocks/Characters
    // variant lists depend on the un-ported Minecraft data registries).
    Rml::ElementPtr empty = document->CreateElement("div");
    empty->SetClass("spawn-menu-empty", true);
    empty->SetInnerRML("(not available)");
    variantsContainer->AppendChild(std::move(empty));
}

void SpawnMenu::RebuildPreview() {
    if (previewContainer == nullptr) {
        return;
    }

    while (Rml::Element* child = previewContainer->GetChild(0)) {
        previewContainer->RemoveChild(child);
    }

    const std::vector<std::string> filtered = GetFilteredObjects();
    if (selectedObjectIndex < 0 || selectedObjectIndex >= static_cast<int>(filtered.size())) {
        Rml::ElementPtr placeholder = document->CreateElement("div");
        placeholder->SetInnerRML("Select an object<br/>to see a preview.");
        previewContainer->AppendChild(std::move(placeholder));
        return;
    }

    // This build has no per-object 3-D mesh pipeline (SceneObject carries no
    // transform/geometry yet), so the live rotating FBO preview from the
    // reference SpawnMenu.cs is represented here by the object's name instead
    // of a fabricated 3-D render.
    Rml::ElementPtr name = document->CreateElement("div");
    name->SetClass("spawn-menu-preview-name", true);
    name->SetInnerRML(filtered[static_cast<size_t>(selectedObjectIndex)]);
    previewContainer->AppendChild(std::move(name));
}

// ── Selection ────────────────────────────────────────────────────────────────

void SpawnMenu::SelectCategory(const std::string& categoryName) {
    if (selectedCategory == categoryName) {
        return;
    }
    selectedCategory = categoryName;
    selectedObjectIndex = -1;
    RebuildCategories();
    RebuildObjects();
    RebuildVariants();
    RebuildPreview();
}

void SpawnMenu::SelectObject(int index) {
    selectedObjectIndex = index;
    RebuildObjects();
    RebuildPreview();
}

// ── Spawning (mirrors SpawnMenu.TrySpawn / SpawnObject) ─────────────────────

void SpawnMenu::TrySpawn() {
    if (sceneTree == nullptr) {
        return;
    }

    const Category* category = GetSelectedCategory();
    if (category == nullptr || !category->implemented) {
        return;
    }

    const std::vector<std::string> filtered = GetFilteredObjects();
    if (selectedObjectIndex < 0 || selectedObjectIndex >= static_cast<int>(filtered.size())) {
        return;
    }

    const std::string objectName = filtered[static_cast<size_t>(selectedObjectIndex)];

    if (category->name == "Camera") {
        sceneTree->AddSpawnedObject("Camera", objectName);
    } else if (category->name == "Light") {
        sceneTree->AddSpawnedObject("Light", objectName);
    } else if (category->name == "Primitives") {
        sceneTree->AddSpawnedObject(objectName, objectName);
    }
}

// ── Event dispatch ──────────────────────────────────────────────────────────

void SpawnMenu::SpawnMenuEventListener::ProcessEvent(Rml::Event& event) {
    if (owner == nullptr) {
        return;
    }

    Rml::Element* target = event.GetTargetElement();
    Rml::Element* current = event.GetCurrentElement();
    const Rml::String type = event.GetType();

    if (type == "click" && current == owner->openButton) {
        owner->Toggle();
        return;
    }

    if (type == "click" && current == owner->closeButton) {
        if (owner->isOpen) {
            owner->Toggle();
        }
        return;
    }

    // Clicking the dimmed backdrop (not the window itself) closes the menu.
    if (type == "click" && current == owner->overlay && target == owner->overlay) {
        if (owner->isOpen) {
            owner->Toggle();
        }
        return;
    }

    if (type == "click" && current == owner->clearButton) {
        owner->searchQuery.clear();
        if (owner->searchInput != nullptr) {
            owner->searchInput->SetAttribute("value", "");
        }
        owner->selectedObjectIndex = -1;
        owner->RebuildObjects();
        owner->RebuildPreview();
        return;
    }

    if (type == "click" && current == owner->spawnButton) {
        owner->TrySpawn();
        return;
    }

    if ((type == "change" || type == "keyup") && current == owner->searchInput) {
        Rml::String value;
        if (auto* control = rmlui_dynamic_cast<Rml::ElementFormControlInput*>(current)) {
            value = control->GetValue();
        } else {
            value = current->GetAttribute<Rml::String>("value", "");
        }
        if (value != owner->searchQuery) {
            owner->searchQuery = value;
            owner->selectedObjectIndex = -1;
            owner->RebuildObjects();
            owner->RebuildPreview();
        }
        return;
    }

    if (type == "click" && target != nullptr && target->HasAttribute("data-category")) {
        owner->SelectCategory(target->GetAttribute<Rml::String>("data-category", ""));
        return;
    }

    if (target != nullptr && target->HasAttribute("data-object-index")) {
        const int index = target->GetAttribute<int>("data-object-index", -1);
        if (type == "click") {
            owner->SelectObject(index);
            return;
        }
        if (type == "dblclick") {
            owner->SelectObject(index);
            owner->TrySpawn();
            return;
        }
    }
}
