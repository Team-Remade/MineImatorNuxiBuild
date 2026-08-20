#include "EmbeddedFileInterface.hpp"

#include <algorithm>
#include <cstdio>
#include <cstring>
#include <memory>
#include <vector>

static const unsigned char embeddedNotoSans[] = {
#include "assets/NotoSans.ttf.h"
};

static const unsigned char embeddedMainMenuRml[] = {
#include "assets/ui/menubar/main_menu.rml.h"
};

static const unsigned char embeddedViewportRml[] = {
#include "assets/ui/panels/viewport.rml.h"
};

static const unsigned char embeddedTimelineRml[] = {
#include "assets/ui/panels/timeline.rml.h"
};

static const unsigned char embeddedSceneTreeRml[] = {
#include "assets/ui/panels/scene_tree.rml.h"
};

static const unsigned char embeddedPropertiesRml[] = {
#include "assets/ui/panels/properties.rml.h"
};

static const unsigned char embeddedMenubarRcss[] = {
#include "assets/ui/styles/menubar.rcss.h"
};

static const unsigned char embeddedViewportRcss[] = {
#include "assets/ui/styles/viewport.rcss.h"
};

static const unsigned char embeddedTimelineRcss[] = {
#include "assets/ui/styles/timeline.rcss.h"
};

static const unsigned char embeddedSceneTreeRcss[] = {
#include "assets/ui/styles/scene_tree.rcss.h"
};

static const unsigned char embeddedPropertiesRcss[] = {
#include "assets/ui/styles/properties.rcss.h"
};

EmbeddedFileInterface::EmbeddedFileInterface()
    : assets{
        {"assets/NotoSans.ttf", {embeddedNotoSans, sizeof(embeddedNotoSans)}},
        {"assets/ui/menubar/main_menu.rml", {embeddedMainMenuRml, sizeof(embeddedMainMenuRml)}},
        {"assets/ui/panels/viewport.rml", {embeddedViewportRml, sizeof(embeddedViewportRml)}},
        {"assets/ui/panels/timeline.rml", {embeddedTimelineRml, sizeof(embeddedTimelineRml)}},
        {"assets/ui/panels/scene_tree.rml", {embeddedSceneTreeRml, sizeof(embeddedSceneTreeRml)}},
        {"assets/ui/panels/properties.rml", {embeddedPropertiesRml, sizeof(embeddedPropertiesRml)}},
        {"assets/ui/styles/menubar.rcss", {embeddedMenubarRcss, sizeof(embeddedMenubarRcss)}},
        {"assets/ui/styles/viewport.rcss", {embeddedViewportRcss, sizeof(embeddedViewportRcss)}},
        {"assets/ui/styles/timeline.rcss", {embeddedTimelineRcss, sizeof(embeddedTimelineRcss)}},
        {"assets/ui/styles/scene_tree.rcss", {embeddedSceneTreeRcss, sizeof(embeddedSceneTreeRcss)}},
        {"assets/ui/styles/properties.rcss", {embeddedPropertiesRcss, sizeof(embeddedPropertiesRcss)}}
    } {
}

Rml::FileHandle EmbeddedFileInterface::Open(const Rml::String& path) {
    const std::string normalizedPath = NormalizePath(path.c_str());
    auto assetIt = assets.find(normalizedPath);
    if (assetIt == assets.end()) {
        std::fprintf(stderr, "Embedded asset not found: %s (normalized: %s)\n", path.c_str(), normalizedPath.c_str());
        return 0;
    }

    auto file = std::make_unique<OpenFile>();
    file->asset = &assetIt->second;
    file->position = 0;
    return reinterpret_cast<Rml::FileHandle>(file.release());
}

void EmbeddedFileInterface::Close(Rml::FileHandle file) {
    delete reinterpret_cast<OpenFile*>(file);
}

size_t EmbeddedFileInterface::Read(void* buffer, size_t size, Rml::FileHandle file) {
    OpenFile* openFile = reinterpret_cast<OpenFile*>(file);
    if (!openFile || !buffer || size == 0) {
        return 0;
    }

    const size_t remaining = openFile->asset->size - openFile->position;
    const size_t bytesToRead = std::min(size, remaining);
    if (bytesToRead == 0) {
        return 0;
    }

    std::memcpy(buffer, openFile->asset->data + openFile->position, bytesToRead);
    openFile->position += bytesToRead;
    return bytesToRead;
}

bool EmbeddedFileInterface::Seek(Rml::FileHandle file, long offset, int origin) {
    OpenFile* openFile = reinterpret_cast<OpenFile*>(file);
    if (!openFile) {
        return false;
    }

    long long base = 0;
    switch (origin) {
        case SEEK_SET: base = 0; break;
        case SEEK_CUR: base = static_cast<long long>(openFile->position); break;
        case SEEK_END: base = static_cast<long long>(openFile->asset->size); break;
        default: return false;
    }

    const long long nextPosition = base + static_cast<long long>(offset);
    if (nextPosition < 0 || static_cast<size_t>(nextPosition) > openFile->asset->size) {
        return false;
    }

    openFile->position = static_cast<size_t>(nextPosition);
    return true;
}

size_t EmbeddedFileInterface::Tell(Rml::FileHandle file) {
    OpenFile* openFile = reinterpret_cast<OpenFile*>(file);
    return openFile ? openFile->position : 0;
}

std::string EmbeddedFileInterface::NormalizePath(const std::string& path) const {
    std::string normalized = path;
    std::replace(normalized.begin(), normalized.end(), '\\', '/');

    std::vector<std::string> components;
    size_t componentStart = 0;
    while (componentStart <= normalized.size()) {
        const size_t componentEnd = normalized.find('/', componentStart);
        const std::string component = normalized.substr(componentStart, componentEnd - componentStart);

        if (!component.empty() && component != ".") {
            if (component == "..") {
                if (!components.empty()) {
                    components.pop_back();
                }
            } else {
                components.push_back(component);
            }
        }

        if (componentEnd == std::string::npos) {
            break;
        }
        componentStart = componentEnd + 1;
    }

    normalized.clear();
    for (const std::string& component : components) {
        if (!normalized.empty()) {
            normalized += '/';
        }
        normalized += component;
    }

    if (normalized != "assets" && normalized.rfind("assets/", 0) != 0) {
        normalized = "assets/" + normalized;
    }

    return normalized;
}

const unsigned char* GetEmbeddedNotoSansData() {
    return embeddedNotoSans;
}

size_t GetEmbeddedNotoSansSize() {
    return sizeof(embeddedNotoSans);
}
