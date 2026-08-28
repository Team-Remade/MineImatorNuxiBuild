#pragma once

#include <RmlUi/Core.h>

class SceneObject;
namespace Rml {
    class ElementFormControlInput;
}

class PropertiesPanel {
public:
    void Init(Rml::ElementDocument* document);
    void Shutdown();

private:
    struct DragState {
        Rml::ElementFormControlInput* input = nullptr;
        float startMouseX = 0.0f;
        float startValue = 0.0f;
    };

    class PropertiesEventListener final : public Rml::EventListener {
    public:
        explicit PropertiesEventListener(PropertiesPanel* owner) : owner(owner) {}
        void ProcessEvent(Rml::Event& event) override;

    private:
        PropertiesPanel* owner;
    };

    bool HandleClick(Rml::Element* target);
    void RefreshFromSelection();
    void HandleInputCommit(Rml::Element* target);
    bool BeginInputDrag(Rml::Element* target, float mouseX);
    void UpdateInputDrag(float mouseX);
    void EndInputDrag();
    void ToggleGroup(const char* groupId, const char* openClass = "property-group-open", const char* closedClass = "property-group-closed") const;
    bool ApplyInputValueById(const Rml::String& id, float value) const;
    void SetTransformInput(const char* id, float value) const;
    static bool TryParseFloat(const Rml::String& text, float& outValue);

    Rml::ElementDocument* document = nullptr;
    Rml::Element* emptyState = nullptr;
    Rml::Element* objectState = nullptr;
    Rml::Element* objectName = nullptr;
    int selectionChangedToken = 0;
    DragState dragState;

    PropertiesEventListener eventListener{this};
};
