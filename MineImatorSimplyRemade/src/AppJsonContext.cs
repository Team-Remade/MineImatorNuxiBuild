using System.Text.Json.Serialization;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;

namespace MineImatorSimplyRemade;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for all types that are
/// deserialized at runtime.  Using a context instead of reflection-based
/// <see cref="System.Text.Json.JsonSerializer"/> overloads keeps the code
/// compatible with trimmed / AOT-compiled publish profiles and suppresses
/// IL2026 warnings.
/// </summary>
[JsonSourceGenerationOptions(
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
[JsonSerializable(typeof(RecentProjectsState))]
[JsonSerializable(typeof(FfmpegBootstrap.FfmpegBootstrapState))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
