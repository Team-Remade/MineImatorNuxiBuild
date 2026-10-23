# Mine Imator Nuxi

For all your Minecraft animation needs!

## Dev environment

Requirements

- Dotnet 10.0+
- A C++ compiler (MSVC, GCC, or Clang)
- OpenGL 3.3+ compatible graphics driver

## Setup

1. Clone the repo
2. Run `cd ./MineImatorNuxiBuild`
3. Run `dotnet restore`
4. Run `dotnet build`
5. Run `cd ./MineImatorSimplyRemade`
6. Run `dotnet run`

## Experimental Veldrid scene renderer

OpenGL remains the default renderer. To render the main scene through Veldrid,
run `dotnet run -- --renderer=vulkan` or, on Windows,
`dotnet run -- --renderer=d3d11` from `MineImatorSimplyRemade`.
Use `--renderer=opengl` to explicitly select the default path.

This is an incremental migration, not a GL-free renderer. Veldrid renders the
main viewport's geometry into an offscreen target and submits real GPU draw
commands. Its output is read back and uploaded into the existing OpenGL UI.
The window, asset uploads, editor overlays, camera previews, and exports still
use OpenGL. Advanced scene lighting and post-processing are not yet ported;
editor overlays do not have the Veldrid scene depth buffer. The bridge incurs
GPU synchronization and CPU-transfer overhead and is not a performance upgrade.
The viewport labels the experimental backend, and initialization or rendering
failures are logged before falling back to OpenGL.

## Mine-imator Minecraft assets

The bundled 26.3 catalog includes 129 additional character models from the
Mine-imator Minecraft data archive.
Equipment models and special-block/prop models are available as standalone
objects in the **Equipment** and **Props** spawn categories, with model
previews and compatible texture variants. Equipment is not automatically
attached to a character yet.

To import assets, use Python 3.9+ and the matching ZIP and `.midata`
files from a local Mine-imator installation:

```powershell
python .\MineImatorSimplyRemade\tools\ImportMinecraftCharacters\import_characters.py "D:\path\to\Data\Minecraft\26.3.zip"
```

Existing models (including their texture manifests), textures, and animation
loops are skipped by default. This preserves customized or manually moved
assets `characters/vanilla`.
Use `--replace-existing` to regenerate existing archive models and manifests
and replace shared textures and loops. This intentionally replaces custom edits
to matching assets; it does not move or delete unrelated files.

Add `--check` for a read-only check of assets that would be imported. Combine it
with `--replace-existing` to compare existing assets against generated output
too; customized models may intentionally fail that comparison.

## Contributing

Pull requests are welcome. For major changes, please open an issue first
to discuss what you would like to change.

## License

[MIT](https://choosealicense.com/licenses/mit/)

[Discord](https://discord.gg/eswvppFuAD)
