#pragma once

#include <RmlUi/Core.h>

class MenuBar {
public:
    void Init(Rml::ElementDocument* uiDocument);
    void Shutdown();

private:
    class MenuEventListener final : public Rml::EventListener {
    public:
        explicit MenuEventListener(MenuBar* owner);
        void ProcessEvent(Rml::Event& event) override;

    private:
        MenuBar* owner;
    };

    void SyncFileMenuOpenState();
    void SyncEditMenuOpenState();
    void SyncRenderMenuOpenState();
    void SyncViewMenuOpenState();
    void SyncHelpMenuOpenState();
    bool IsElementOrDescendantOf(Rml::Element* element, Rml::Element* parent) const;

    Rml::ElementDocument* uiDocument = nullptr;
    Rml::Element* fileMenuElement = nullptr;
    bool fileMenuOpen = false;
    Rml::Element* editMenuElement = nullptr;
    bool editMenuOpen = false;
    Rml::Element* renderMenuElement = nullptr;
    bool renderMenuOpen = false;
    Rml::Element* viewMenuElement = nullptr;
    bool viewMenuOpen = false;
    Rml::Element* helpMenuElement = nullptr;
    bool helpMenuOpen = false;
    MenuEventListener eventListener{this};
};
