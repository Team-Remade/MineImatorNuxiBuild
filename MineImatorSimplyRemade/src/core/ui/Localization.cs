using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace MineImatorSimplyRemade.core.ui;

/// <summary>
/// Resolves dotted translation keys from data/languages catalogs loaded at runtime.
/// </summary>
public static class Localization
{
    public const string DefaultLanguage = "en_us";

    public sealed record LanguageInfo(string Code, string Name);

    private sealed record Catalog(LanguageInfo Info, Dictionary<string, string> Strings);
    private sealed record LanguageSelection(Catalog Current, Catalog English, Dictionary<string, string> EnglishKeys);
    private static readonly object CatalogLock = new();
    private static readonly Dictionary<string, Catalog> Catalogs = new(StringComparer.OrdinalIgnoreCase);
    private static LanguageSelection _selection = null!;
    private static int _revision;
    internal static int Revision => Volatile.Read(ref _revision);
    public static IReadOnlyList<LanguageInfo> AvailableLanguages { get; private set; } = [];
    public static string CurrentLanguage => Volatile.Read(ref _selection)?.Current.Info.Code ?? DefaultLanguage;
    public static string LanguagesDirectory => Path.Combine(MinecraftDataLoader.GetBasePath(), "data", "languages");
    public static string CurrentLanguageName => Volatile.Read(ref _selection).Current.Info.Name;

    static Localization() => RefreshLanguages();

    internal static bool IsTranslationKey(string key)
    {
        if (!key.StartsWith("text.", StringComparison.Ordinal))
            return false;
        bool segmentHasCharacter = false;
        foreach (char character in key.AsSpan(5))
        {
            if (character == '.')
            {
                if (!segmentHasCharacter)
                    return false;
                segmentHasCharacter = false;
            }
            else if (character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
                segmentHasCharacter = true;
            else
                return false;
        }
        return segmentHasCharacter;
    }

    private static Catalog ParseCatalog(JsonElement root, string code)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(name.GetString()) || name.GetString()!.Contains("##", StringComparison.Ordinal) ||
            !root.TryGetProperty("strings", out var strings) || strings.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected a nonempty name and a strings object.");

        Dictionary<string, string> translations = new(StringComparer.Ordinal);
        foreach (JsonProperty entry in strings.EnumerateObject())
        {
            if (!IsTranslationKey(entry.Name) || entry.Value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(entry.Value.GetString()) ||
                !translations.TryAdd(entry.Name, entry.Value.GetString()!))
                throw new JsonException("Translation keys must be unique dotted text identifiers with nonempty string values.");
        }
        if (translations.Count == 0)
            throw new JsonException("The strings object must contain at least one translation.");
        return new(new(code, name.GetString()!), translations);
    }

    public static void RefreshLanguages()
    {
        lock (CatalogLock)
            RefreshLanguagesLocked();
    }

    private static void RefreshLanguagesLocked()
    {
        string previousLanguage = CurrentLanguage;
        Catalogs.Clear();
        Catalogs[DefaultLanguage] = new(new(DefaultLanguage, "English (US)"), new(StringComparer.Ordinal));

        try
        {
            if (Directory.Exists(LanguagesDirectory))
            {
                foreach (string path in Directory.GetFiles(LanguagesDirectory)
                             .Where(path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(path => path, StringComparer.Ordinal))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(File.ReadAllText(path));
                        string code = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                        Catalogs[code] = ParseCatalog(document.RootElement, code);
                    }
                    catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
                    {
                        Debug.WriteLine($"Skipping language file '{path}': {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Unable to discover languages: {ex.Message}");
        }

        AvailableLanguages = Catalogs.Values.Select(catalog => catalog.Info)
            .OrderBy(info => info.Code == DefaultLanguage ? 0 : 1)
            .ThenBy(info => info.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        SetLanguage(previousLanguage);
    }

    public static void SetLanguage(string? code)
    {
        lock (CatalogLock)
        {
            // Older preference files stored the English display name rather than its filename.
            Catalog current = code != null && Catalogs.TryGetValue(code, out var catalog)
                ? catalog
                : Catalogs[DefaultLanguage];
            Catalog english = Catalogs[DefaultLanguage];
            // Prefer shared keys when different contexts have the same wording.
            var englishKeys = english.Strings
                .OrderBy(entry => entry.Key.StartsWith("text.common.", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .GroupBy(entry => entry.Value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);
            // Publish catalogs and their index together; background progress text can translate
            // safely while the UI refreshes or changes its language selection.
            Volatile.Write(ref _selection, new(current, english, englishKeys));
            Interlocked.Increment(ref _revision);
        }
    }

    public static string T(string key) => Translate(Volatile.Read(ref _selection), key);

    private static string Translate(LanguageSelection selection, string key)
    {
        if (selection.Current.Strings.TryGetValue(key, out string? translated))
            return translated;
        return DefaultText(selection, key);
    }

    private static string DefaultText(LanguageSelection selection, string key) =>
        selection.English.Strings.TryGetValue(key, out string? english) ? english : key;

    public static string DisplayText(string english)
    {
        LanguageSelection selection = Volatile.Read(ref _selection);
        return selection.EnglishKeys.TryGetValue(english, out string? key) ? Translate(selection, key) : english;
    }

    public static string Label(string key, string englishLabel) => ComposeLabel(T(key), englishLabel);

    public static string DisplayLabel(string englishLabel)
    {
        int hidden = englishLabel.IndexOf("##", StringComparison.Ordinal);
        string visible = hidden < 0 ? englishLabel : englishLabel[..hidden];
        return ComposeLabel(DisplayText(visible), englishLabel);
    }

    public static string ComposeLabel(string translated, string englishLabel)
    {
        int hidden = englishLabel.IndexOf("##", StringComparison.Ordinal);
        if (hidden == 0)
            return englishLabel;

        int stable = englishLabel.IndexOf("###", StringComparison.Ordinal);
        string id = stable < 0 ? englishLabel : englishLabel[(stable + 3)..];
        // Catalog text must never change the widget's identity, even if it contains ImGui markers.
        int marker = translated.IndexOf("##", StringComparison.Ordinal);
        if (marker >= 0)
            translated = translated[..marker];
        return translated + "###" + id;
    }

    public static string Format(string key, params object?[] args)
    {
        LanguageSelection selection = Volatile.Read(ref _selection);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, Translate(selection, key), args);
        }
        catch (FormatException)
        {
            try
            {
                return string.Format(CultureInfo.CurrentCulture, DefaultText(selection, key), args);
            }
            catch (FormatException)
            {
                return key;
            }
        }
    }

    internal static string MigrateWindowSettings(string settings, params string[] englishWindowNames)
    {
        // ImGui's ### identity differs from the old bare title. Keep existing
        // window positions and dock assignments when introducing stable labels.
        foreach (string name in englishWindowNames)
            settings = settings.Replace("[Window][" + name + "]", "[Window][" + DisplayLabel(name) + "]", StringComparison.Ordinal);
        return settings;
    }
}
