#include "PropertiesPanel.hpp"

#include "../scene/SceneObject.hpp"
#include "../scene/SelectionManager.hpp"

#include <RmlUi/Core/Elements/ElementFormControlInput.h>

#include <charconv>
#include <cmath>

void PropertiesPanel::Init(Rml::ElementDocument* documentIn) {
    document = documentIn;
    if (document == nullptr) {
        return;
    }

    emptyState = document->GetElementById("properties-empty");
    objectState = document->GetElementById("properties-object");
    objectName = document->GetElementById("properties-object-name");

    // Use bubbling phase for click so button activations are delivered
    // consistently across RmlUi controls.
    document->AddEventListener("click", &eventListener, false);
    if (Rml::Element* dropdown = document->GetElementById("prop-dropdown-transform")) {
        dropdown->AddEventListener("click", &eventListener, false);
        dropdown->AddEventListener("mousedown", &eventListener, false);
    }
    if (Rml::Element* dropdown = document->GetElementById("prop-dropdown-position")) {
        dropdown->AddEventListener("click", &eventListener, false);
        dropdown->AddEventListener("mousedown", &eventListener, false);
    }
    if (Rml::Element* dropdown = document->GetElementById("prop-dropdown-rotation")) {
        dropdown->AddEventListener("click", &eventListener, false);
        dropdown->AddEventListener("mousedown", &eventListener, false);
    }
    if (Rml::Element* dropdown = document->GetElementById("prop-dropdown-scale")) {
        dropdown->AddEventListener("click", &eventListener, false);
        dropdown->AddEventListener("mousedown", &eventListener, false);
    }
    document->AddEventListener("mousedown", &eventListener, true);
    document->AddEventListener("mousemove", &eventListener, true);
    document->AddEventListener("mouseup", &eventListener, true);
    document->AddEventListener("change", &eventListener, true);
    document->AddEventListener("blur", &eventListener, true);
    if (SelectionManager::Instance() != nullptr) {
        selectionChangedToken = SelectionManager::Instance()->AddSelectionChanged([this]() {
            RefreshFromSelection();
        });
    }

    RefreshFromSelection();
}

void PropertiesPanel::Shutdown() {
    if (document != nullptr) {
        document->RemoveEventListener("click", &eventListener, false);
        if (Rml::Element* dropdown = document->GetElementById("prop-dropdown-transform")) {
            dropdown->RemoveEventListener("click", &eventListener, false);
            dropdown->RemoveEventListener("mousedown", &eventListener, false);
        }
        if (Rml::Element* dropdown = document->GetElementById("prop-dropdown-position")) {
            dropdown->RemoveEventListener("click", &eventListener, false);
            dropdown->RemoveEventListener("mousedown", &eventListener, false);
        }
        if (Rml::Element* dropdown = document->GetElementById("prop-dropdown-rotation")) {
            dropdown->RemoveEventListener("click", &eventListener, false);
            dropdown->RemoveEventListener("mousedown", &eventListener, false);
        }
        if (Rml::Element* dropdown = document->GetElementById("prop-dropdown-scale")) {
            dropdown->RemoveEventListener("click", &eventListener, false);
            dropdown->RemoveEventListener("mousedown", &eventListener, false);
        }
        document->RemoveEventListener("mousedown", &eventListener, true);
        document->RemoveEventListener("mousemove", &eventListener, true);
        document->RemoveEventListener("mouseup", &eventListener, true);
        document->RemoveEventListener("change", &eventListener, true);
        document->RemoveEventListener("blur", &eventListener, true);
    }

    EndInputDrag();

    if (SelectionManager::Instance() != nullptr && selectionChangedToken != 0) {
        SelectionManager::Instance()->RemoveSelectionChanged(selectionChangedToken);
    }
    selectionChangedToken = 0;

    objectName = nullptr;
    objectState = nullptr;
    emptyState = nullptr;
    document = nullptr;
}

void PropertiesPanel::PropertiesEventListener::ProcessEvent(Rml::Event& event) {
    if (owner == nullptr) {
        return;
    }

    const Rml::String type = event.GetType();
    Rml::Element* clickTarget = event.GetTargetElement();
    Rml::Element* currentElement = event.GetCurrentElement();
    if (currentElement != nullptr && currentElement != owner->document) {
        clickTarget = currentElement;
    }
    if (type == "mousedown") {
        const bool handled = owner->HandleClick(clickTarget);
        if (handled) {
            event.StopPropagation();
            event.StopImmediatePropagation();
            return;
        }
        if (owner->BeginInputDrag(event.GetTargetElement(), event.GetParameter<float>("mouse_x", 0.0f))) {
            event.StopPropagation();
            event.StopImmediatePropagation();
        }
    } else if (type == "mousemove") {
        owner->UpdateInputDrag(event.GetParameter<float>("mouse_x", 0.0f));
    } else if (type == "mouseup") {
        owner->EndInputDrag();
    } else if (type == "change" || type == "blur") {
        owner->HandleInputCommit(event.GetTargetElement());
    }
}

bool PropertiesPanel::HandleClick(Rml::Element* target) {
    if (target == nullptr) {
        return false;
    }

    const auto setSubgroupState = [this](const char* groupId, bool open) {
        if (document == nullptr || groupId == nullptr) {
            return;
        }

        Rml::Element* group = document->GetElementById(groupId);
        if (group == nullptr) {
            return;
        }

        group->SetClass("property-subgroup-open", open);
        group->SetClass("property-subgroup-closed", !open);
    };

    Rml::Element* current = target;
    while (current != nullptr) {
        const Rml::String id = current->GetId();
        if (id == "prop-dropdown-transform" || id == "prop-group-transform") {
            ToggleGroup("prop-group-transform");
            return true;
        }
        if (id == "prop-dropdown-position" || id == "prop-group-position") {
            Rml::Element* group = document != nullptr ? document->GetElementById("prop-group-position") : nullptr;
            const bool willOpen = group == nullptr || !group->IsClassSet("property-subgroup-open");
            setSubgroupState("prop-group-position", willOpen);
            return true;
        }
        if (id == "prop-dropdown-rotation" || id == "prop-group-rotation") {
            Rml::Element* group = document != nullptr ? document->GetElementById("prop-group-rotation") : nullptr;
            const bool willOpen = group == nullptr || !group->IsClassSet("property-subgroup-open");
            setSubgroupState("prop-group-rotation", willOpen);
            return true;
        }
        if (id == "prop-dropdown-scale" || id == "prop-group-scale") {
            Rml::Element* group = document != nullptr ? document->GetElementById("prop-group-scale") : nullptr;
            const bool willOpen = group == nullptr || !group->IsClassSet("property-subgroup-open");
            setSubgroupState("prop-group-scale", willOpen);
            return true;
        }
        current = current->GetParentNode();
    }
    return false;
}

void PropertiesPanel::RefreshFromSelection() {
    SelectionManager* selection = SelectionManager::Instance();
    if (selection == nullptr) {
        return;
    }

    const auto& selected = selection->SelectedObjects();
    const bool hasSelection = !selected.empty() && selected.front() != nullptr;

    if (emptyState != nullptr) {
        emptyState->SetProperty("display", hasSelection ? "none" : "block");
    }
    if (objectState != nullptr) {
        objectState->SetProperty("display", hasSelection ? "block" : "none");
    }

    if (!hasSelection) {
        return;
    }

    const std::shared_ptr<SceneObject>& obj = selected.front();
    if (objectName != nullptr) {
        objectName->SetInnerRML(obj->GetDisplayName());
    }

    SetTransformInput("prop-pos-x", obj->localPosition[0]);
    SetTransformInput("prop-pos-y", obj->localPosition[1]);
    SetTransformInput("prop-pos-z", obj->localPosition[2]);

    SetTransformInput("prop-rot-x", obj->localRotation[0]);
    SetTransformInput("prop-rot-y", obj->localRotation[1]);
    SetTransformInput("prop-rot-z", obj->localRotation[2]);

    SetTransformInput("prop-scale-x", obj->localScale[0]);
    SetTransformInput("prop-scale-y", obj->localScale[1]);
    SetTransformInput("prop-scale-z", obj->localScale[2]);
}

void PropertiesPanel::HandleInputCommit(Rml::Element* target) {
    if (target == nullptr) {
        return;
    }

    auto* input = dynamic_cast<Rml::ElementFormControlInput*>(target);
    if (input == nullptr) {
        return;
    }

    SelectionManager* selection = SelectionManager::Instance();
    if (selection == nullptr || selection->SelectedObjects().empty() || selection->SelectedObjects().front() == nullptr) {
        return;
    }

    float parsed = 0.0f;
    if (!TryParseFloat(input->GetValue(), parsed)) {
        RefreshFromSelection();
        return;
    }

    ApplyInputValueById(input->GetId(), parsed);

    RefreshFromSelection();
}

bool PropertiesPanel::BeginInputDrag(Rml::Element* target, float mouseX) {
    EndInputDrag();

    if (target == nullptr) {
        return false;
    }

    auto* input = dynamic_cast<Rml::ElementFormControlInput*>(target);
    if (input == nullptr || input->GetClassNames().find("property-input") == Rml::String::npos) {
        return false;
    }

    float startValue = 0.0f;
    if (!TryParseFloat(input->GetValue(), startValue)) {
        return false;
    }

    dragState.input = input;
    dragState.startMouseX = mouseX;
    dragState.startValue = startValue;
    input->SetClass("property-input-dragging", true);
    return true;
}

void PropertiesPanel::UpdateInputDrag(float mouseX) {
    if (dragState.input == nullptr) {
        return;
    }

    const float delta = mouseX - dragState.startMouseX;
    if (std::fabs(delta) < 0.01f) {
        return;
    }

    const float sensitivity = 0.02f;
    const float newValue = dragState.startValue + delta * sensitivity;
    if (ApplyInputValueById(dragState.input->GetId(), newValue)) {
        SetTransformInput(dragState.input->GetId().c_str(), newValue);
    }
}

void PropertiesPanel::EndInputDrag() {
    if (dragState.input != nullptr) {
        dragState.input->SetClass("property-input-dragging", false);
    }
    dragState = {};
}

void PropertiesPanel::ToggleGroup(const char* groupId, const char* openClass, const char* closedClass) const {
    if (document == nullptr || groupId == nullptr || openClass == nullptr || closedClass == nullptr) {
        return;
    }

    Rml::Element* group = document->GetElementById(groupId);
    if (group == nullptr) {
        return;
    }

    const bool isOpen = group->IsClassSet(openClass);
    group->SetClass(openClass, !isOpen);
    group->SetClass(closedClass, isOpen);
}

bool PropertiesPanel::ApplyInputValueById(const Rml::String& id, float value) const {
    SelectionManager* selection = SelectionManager::Instance();
    if (selection == nullptr || selection->SelectedObjects().empty() || selection->SelectedObjects().front() == nullptr) {
        return false;
    }

    const std::shared_ptr<SceneObject>& obj = selection->SelectedObjects().front();

    if (id == "prop-pos-x") obj->localPosition[0] = value;
    else if (id == "prop-pos-y") obj->localPosition[1] = value;
    else if (id == "prop-pos-z") obj->localPosition[2] = value;
    else if (id == "prop-rot-x") obj->localRotation[0] = value;
    else if (id == "prop-rot-y") obj->localRotation[1] = value;
    else if (id == "prop-rot-z") obj->localRotation[2] = value;
    else if (id == "prop-scale-x") obj->localScale[0] = value;
    else if (id == "prop-scale-y") obj->localScale[1] = value;
    else if (id == "prop-scale-z") obj->localScale[2] = value;
    else return false;

    return true;
}

void PropertiesPanel::SetTransformInput(const char* id, float value) const {
    if (document == nullptr || id == nullptr) {
        return;
    }

    auto* input = dynamic_cast<Rml::ElementFormControlInput*>(document->GetElementById(id));
    if (input == nullptr) {
        return;
    }

    char buffer[64];
    std::snprintf(buffer, sizeof(buffer), "%.3f", value);
    input->SetValue(buffer);
}

bool PropertiesPanel::TryParseFloat(const Rml::String& text, float& outValue) {
    const char* begin = text.c_str();
    const char* end = begin + text.size();
    auto result = std::from_chars(begin, end, outValue);
    return result.ec == std::errc() && result.ptr == end;
}
