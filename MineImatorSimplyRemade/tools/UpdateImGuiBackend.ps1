param(
    [string]$CacheDirectory = [System.IO.Path]::GetTempPath(),
    [string]$Generator = 'Visual Studio 18 2026'
)

$ErrorActionPreference = 'Stop'

# Matches Hexa.NET.ImGui 2.2.9 / Backends 1.0.18 (Dear ImGui 1.92.2b).
$revision = '7a459f54179587dc923749fed44ae34f78c370d3'
$imguiRevision = '1f7f1f54af38b0350d8c0008b096a9af6de299c7'
$source = Join-Path $CacheDirectory "mi-nuxi-imgui-$revision"
$build = Join-Path $source 'build-no-vulkan'
$projectDirectory = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

if (-not (Test-Path -LiteralPath $CacheDirectory -PathType Container)) {
    throw "Cache directory does not exist: $CacheDirectory"
}
if (-not (Test-Path -LiteralPath $source)) {
    & git clone --no-checkout https://github.com/HexaEngine/cimgui_impl.git $source
    if ($LASTEXITCODE -ne 0) { throw 'ImGui backend source download failed.' }
}
& git -C $source checkout --detach $revision
if ($LASTEXITCODE -ne 0) { throw 'Cannot check out the pinned ImGui backend revision.' }
& git -C $source submodule update --init --recursive --depth 1
if ($LASTEXITCODE -ne 0) { throw 'Cannot restore the pinned Dear ImGui submodule.' }
if ((& git -C (Join-Path $source 'imgui') rev-parse HEAD) -ne $imguiRevision) {
    throw 'Unexpected Dear ImGui revision. Do not mix native backend ABIs.'
}

# The editor UI is OpenGL, including when Veldrid renders the scene with Vulkan.
# Keep WCHAR32 in sync with the existing cimgui.dll and ImGuiImplGLFW.dll.
& cmake -S $source -B $build -G $Generator -A x64 `
    -DBACKENDS_BASE_ONLY=ON -DIMGUI_WCHAR32=ON -DBACKENDS_VULKAN=OFF
if ($LASTEXITCODE -ne 0) { throw 'ImGui backend configuration failed.' }
& cmake --build $build --config Release --target cimgui_impl --parallel
if ($LASTEXITCODE -ne 0) { throw 'ImGui backend build failed.' }

& cmake -E copy_if_different (Join-Path $build 'Release\ImGuiImpl.dll') (Join-Path $projectDirectory 'ImGuiImpl.dll')
if ($LASTEXITCODE -ne 0) { throw 'Cannot update the bundled ImGui backend.' }

Get-FileHash -LiteralPath (Join-Path $projectDirectory 'ImGuiImpl.dll') -Algorithm SHA256
