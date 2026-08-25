#pragma once

#include <RmlUi/Core.h>

#include <cstddef>
#include <string>
#include <unordered_map>

struct EmbeddedAsset {
    const unsigned char* data;
    size_t size;
};

class EmbeddedFileInterface : public Rml::FileInterface {
public:
    EmbeddedFileInterface();

    Rml::FileHandle Open(const Rml::String& path) override;
    void Close(Rml::FileHandle file) override;
    size_t Read(void* buffer, size_t size, Rml::FileHandle file) override;
    bool Seek(Rml::FileHandle file, long offset, int origin) override;
    size_t Tell(Rml::FileHandle file) override;
    size_t Length(Rml::FileHandle file) override;

private:
    struct OpenFile {
        const EmbeddedAsset* asset = nullptr;
        size_t position = 0;
    };

    std::string NormalizePath(const std::string& path) const;

    const std::unordered_map<std::string, EmbeddedAsset> assets;
};

const unsigned char* GetEmbeddedNotoSansData();
size_t GetEmbeddedNotoSansSize();