#include "MenuBar.hpp"

#include <cstdio>

MenuBar::MenuEventListener::MenuEventListener(MenuBar* owner) : owner(owner) {}

void MenuBar::MenuEventListener::ProcessEvent(Rml::Event& event) {
    if (owner == nullptr || owner->fileMenuElement == nullptr || owner->editMenuElement == nullptr || owner->renderMenuElement == nullptr || owner->viewMenuElement == nullptr || owner->helpMenuElement == nullptr) {
        return;
    }

    if (event.GetType() != "click") {
        return;
    }

    Rml::Element* currentElement = event.GetCurrentElement();
    if (currentElement == nullptr) {
        return;
    }

    const Rml::String currentId = currentElement->GetId();
    if (currentId == "file-menu" || currentId == "file-menu-trigger") {
        owner->fileMenuOpen = !owner->fileMenuOpen;
        owner->editMenuOpen = false;
        owner->renderMenuOpen = false;
        owner->viewMenuOpen = false;
        owner->helpMenuOpen = false;
        owner->SyncFileMenuOpenState();
        owner->SyncEditMenuOpenState();
        owner->SyncRenderMenuOpenState();
        owner->SyncViewMenuOpenState();
        owner->SyncHelpMenuOpenState();
        return;
    }

    if (currentId == "edit-menu" || currentId == "edit-menu-trigger") {
        owner->editMenuOpen = !owner->editMenuOpen;
        owner->fileMenuOpen = false;
        owner->renderMenuOpen = false;
        owner->viewMenuOpen = false;
        owner->helpMenuOpen = false;
        owner->SyncEditMenuOpenState();
        owner->SyncFileMenuOpenState();
        owner->SyncRenderMenuOpenState();
        owner->SyncViewMenuOpenState();
        owner->SyncHelpMenuOpenState();
        return;
    }

    if (currentId == "render-menu" || currentId == "render-menu-trigger") {
        owner->renderMenuOpen = !owner->renderMenuOpen;
        owner->fileMenuOpen = false;
        owner->editMenuOpen = false;
        owner->viewMenuOpen = false;
        owner->helpMenuOpen = false;
        owner->SyncRenderMenuOpenState();
        owner->SyncFileMenuOpenState();
        owner->SyncEditMenuOpenState();
        owner->SyncViewMenuOpenState();
        owner->SyncHelpMenuOpenState();
        return;
    }

    if (currentId == "view-menu" || currentId == "view-menu-trigger") {
        owner->viewMenuOpen = !owner->viewMenuOpen;
        owner->fileMenuOpen = false;
        owner->editMenuOpen = false;
        owner->renderMenuOpen = false;
        owner->helpMenuOpen = false;
        owner->SyncViewMenuOpenState();
        owner->SyncFileMenuOpenState();
        owner->SyncEditMenuOpenState();
        owner->SyncRenderMenuOpenState();
        owner->SyncHelpMenuOpenState();
        return;
    }

    if (currentId == "help-menu" || currentId == "help-menu-trigger") {
        owner->helpMenuOpen = !owner->helpMenuOpen;
        owner->fileMenuOpen = false;
        owner->editMenuOpen = false;
        owner->renderMenuOpen = false;
        owner->viewMenuOpen = false;
        owner->SyncHelpMenuOpenState();
        owner->SyncFileMenuOpenState();
        owner->SyncEditMenuOpenState();
        owner->SyncRenderMenuOpenState();
        owner->SyncViewMenuOpenState();
        return;
    }

    Rml::Element* target = event.GetTargetElement();
    if (!owner->IsElementOrDescendantOf(target, owner->fileMenuElement) &&
        !owner->IsElementOrDescendantOf(target, owner->editMenuElement) &&
        !owner->IsElementOrDescendantOf(target, owner->renderMenuElement) &&
        !owner->IsElementOrDescendantOf(target, owner->viewMenuElement) &&
        !owner->IsElementOrDescendantOf(target, owner->helpMenuElement)) {
        owner->fileMenuOpen = false;
        owner->editMenuOpen = false;
        owner->renderMenuOpen = false;
        owner->viewMenuOpen = false;
        owner->helpMenuOpen = false;
        owner->SyncFileMenuOpenState();
        owner->SyncEditMenuOpenState();
        owner->SyncRenderMenuOpenState();
        owner->SyncViewMenuOpenState();
        owner->SyncHelpMenuOpenState();
    }
}

void MenuBar::Init(Rml::ElementDocument* uiDocument) {
    Shutdown();

    this->uiDocument = uiDocument;
    if (this->uiDocument == nullptr) {
        return;
    }

    fileMenuElement = this->uiDocument->GetElementById("file-menu");
    Rml::Element* fileMenuTrigger = this->uiDocument->GetElementById("file-menu-trigger");
    editMenuElement = this->uiDocument->GetElementById("edit-menu");
    Rml::Element* editMenuTrigger = this->uiDocument->GetElementById("edit-menu-trigger");
    renderMenuElement = this->uiDocument->GetElementById("render-menu");
    Rml::Element* renderMenuTrigger = this->uiDocument->GetElementById("render-menu-trigger");
    viewMenuElement = this->uiDocument->GetElementById("view-menu");
    Rml::Element* viewMenuTrigger = this->uiDocument->GetElementById("view-menu-trigger");
    helpMenuElement = this->uiDocument->GetElementById("help-menu");
    Rml::Element* helpMenuTrigger = this->uiDocument->GetElementById("help-menu-trigger");

    if (fileMenuElement == nullptr || fileMenuTrigger == nullptr || editMenuElement == nullptr || editMenuTrigger == nullptr || renderMenuElement == nullptr || renderMenuTrigger == nullptr || viewMenuElement == nullptr || viewMenuTrigger == nullptr || helpMenuElement == nullptr || helpMenuTrigger == nullptr) {
        std::printf("Warning: menu elements not found; using hover-only menu behavior\n");
        fileMenuElement = nullptr;
        fileMenuOpen = false;
        editMenuElement = nullptr;
        editMenuOpen = false;
        renderMenuElement = nullptr;
        renderMenuOpen = false;
        viewMenuElement = nullptr;
        viewMenuOpen = false;
        helpMenuElement = nullptr;
        helpMenuOpen = false;
        return;
    }

    fileMenuOpen = false;
    editMenuOpen = false;
    renderMenuOpen = false;
    viewMenuOpen = false;
    helpMenuOpen = false;
    SyncFileMenuOpenState();
    SyncEditMenuOpenState();
    SyncRenderMenuOpenState();
    SyncViewMenuOpenState();
    SyncHelpMenuOpenState();

    fileMenuElement->AddEventListener("click", &eventListener);
    editMenuElement->AddEventListener("click", &eventListener);
    renderMenuElement->AddEventListener("click", &eventListener);
    viewMenuElement->AddEventListener("click", &eventListener);
    helpMenuElement->AddEventListener("click", &eventListener);
    this->uiDocument->AddEventListener("click", &eventListener);
}

void MenuBar::Shutdown() {
    if (uiDocument != nullptr) {
        uiDocument->RemoveEventListener("click", &eventListener);
    }

    fileMenuElement = nullptr;
    fileMenuOpen = false;
    editMenuElement = nullptr;
    editMenuOpen = false;
    renderMenuElement = nullptr;
    renderMenuOpen = false;
    viewMenuElement = nullptr;
    viewMenuOpen = false;
    helpMenuElement = nullptr;
    helpMenuOpen = false;
    uiDocument = nullptr;
}

void MenuBar::SyncFileMenuOpenState() {
    if (fileMenuElement == nullptr) {
        return;
    }

    if (fileMenuOpen) {
        fileMenuElement->SetClass("open", true);
    } else {
        fileMenuElement->SetClass("open", false);
    }
}

void MenuBar::SyncEditMenuOpenState() {
    if (editMenuElement == nullptr) {
        return;
    }

    if (editMenuOpen) {
        editMenuElement->SetClass("open", true);
    } else {
        editMenuElement->SetClass("open", false);
    }
}

void MenuBar::SyncRenderMenuOpenState() {
    if (renderMenuElement == nullptr) {
        return;
    }

    if (renderMenuOpen) {
        renderMenuElement->SetClass("open", true);
    } else {
        renderMenuElement->SetClass("open", false);
    }
}

void MenuBar::SyncViewMenuOpenState() {
    if (viewMenuElement == nullptr) {
        return;
    }

    if (viewMenuOpen) {
        viewMenuElement->SetClass("open", true);
    } else {
        viewMenuElement->SetClass("open", false);
    }
}

void MenuBar::SyncHelpMenuOpenState() {
    if (helpMenuElement == nullptr) {
        return;
    }

    if (helpMenuOpen) {
        helpMenuElement->SetClass("open", true);
    } else {
        helpMenuElement->SetClass("open", false);
    }
}

bool MenuBar::IsElementOrDescendantOf(Rml::Element* element, Rml::Element* parent) const {
    if (element == nullptr || parent == nullptr) {
        return false;
    }

    Rml::Element* current = element;
    while (current != nullptr) {
        if (current == parent) {
            return true;
        }
        current = current->GetParentNode();
    }

    return false;
}
