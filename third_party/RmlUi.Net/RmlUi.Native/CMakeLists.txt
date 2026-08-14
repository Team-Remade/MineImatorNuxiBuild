cmake_minimum_required(VERSION 3.14...4.0)

project(RmlUiNative VERSION 1.0 LANGUAGES CXX)

# Third-party FreeType still advertises compatibility with pre-3.5 CMake.
# CMake 4 removed that policy baseline, so select the oldest supported one.
set(CMAKE_POLICY_VERSION_MINIMUM 3.5 CACHE STRING "Oldest dependency policy version")

set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    add_compile_options(/W4 /permissive-)
else()
    add_compile_options(-Wall -Wextra)
endif()

# add external libraries
add_subdirectory("ext/freetype")
if(NOT TARGET Freetype::Freetype)
    add_library(Freetype::Freetype ALIAS freetype)
endif()

set(BUILD_SHARED_LIBS OFF)
add_subdirectory("ext/RmlUi")

add_library(${PROJECT_NAME} SHARED
    "src/Context.cpp"

    "src/Element.cpp"
    "src/ElementDocument.cpp"
    "src/ElementFormControlInput.cpp"
    "src/ElementCustom.cpp"
    "src/ElementInstancer.cpp"

    "src/Variant.cpp"
    "src/VariableDefinition.cpp"
    "src/StructHandle.cpp"
    "src/Dictionary.cpp"
    "src/DataVariable.cpp"
    "src/DataModelHandle.cpp"
    "src/DataModelConstructor.cpp"
    "src/Event.cpp"
    "src/EventInstancer.cpp"
    "src/EventListener.h"
    "src/EventListenerInstancer.cpp"

    "src/Box.cpp"
    "src/RenderManager.cpp"

    "src/RenderInterface.cpp"
    "src/SystemInterface.cpp"
    "src/FileInterface.cpp"
    "src/Plugin.cpp"

    "src/StyleSheetContainer.cpp"
    "src/StyleSheetSpecification.cpp"
    
    "src/XMLAttributes.cpp"
    "src/PropertyDefinition.cpp"
    "src/Property.cpp"
    "src/Rml.cpp"
    "src/Debugger.cpp"
    "src/Util.h"
    "src/Buffer.cpp"
    "include/Buffer.h"
)

include_directories(${PROJECT_NAME}
    PRIVATE include
)

target_compile_definitions(${PROJECT_NAME} PUBLIC "RMLUI_STATIC_LIB")

target_link_libraries(${PROJECT_NAME}
    freetype
    RmlUi::Core
    RmlUi::Debugger
)

install(TARGETS ${PROJECT_NAME}
    RUNTIME DESTINATION .
    LIBRARY DESTINATION .
    ARCHIVE DESTINATION lib
)
