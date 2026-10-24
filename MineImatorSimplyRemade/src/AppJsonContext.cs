using System.Text.Json;
using System.Text.Json.Serialization;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemade.core.update;

namespace MineImatorSimplyRemade;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for all types that are
/// deserialized at runtime.  Using a context instead of reflection-based
/// <see cref="System.Text.Json.JsonSerializer"/> overloads keeps the code
/// compatible with trimmed / AOT-compiled publish profiles and suppresses
/// IL2026 warnings.
/// </summary>
[JsonSourceGenerationOptions(
    MaxDepth = AppJsonContext.MaxDepth,
    // Generated fast-path writers recurse with large stack frames for scene entries.
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = true,
    // MiObject / MiModel files use numbers stored as JSON strings in some fields;
    // the individual properties already carry [JsonNumberHandling] attributes, so
    // this global flag is not required — but it's harmless and avoids surprises.
    UseStringEnumConverter = false)]
[JsonSerializable(typeof(NuxManifest))]
[JsonSerializable(typeof(TexturesNuxManifest))]
[JsonSerializable(typeof(MiObject))]
[JsonSerializable(typeof(MiModel))]
[JsonSerializable(typeof(ProjectManifest))]
[JsonSerializable(typeof(ProjectSceneObjectEntry))]
[JsonSerializable(typeof(DataAssetRemapRules))]
[JsonSerializable(typeof(ProjectAudioTrack))]
[JsonSerializable(typeof(RecentProjectsState))]
[JsonSerializable(typeof(PreferencesState))]
[JsonSerializable(typeof(UpdateChecker.UpdateState))]
[JsonSerializable(typeof(List<UpdateChecker.GitHubRelease>))]
internal partial class AppJsonContext : JsonSerializerContext
{
    // Each scene/model hierarchy level adds both an object and a children array.
    internal const int MaxDepth = 1024;
    internal static JsonDocumentOptions DocumentOptions => new() { MaxDepth = MaxDepth };
}
