using System.Buffers.Binary;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;

namespace MineImatorSimplyRemade.core;

public static class MinecraftSkinDownloader
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20),
        MaxResponseContentBufferSize = 1024 * 1024
    };

    public static bool IsValidUsername(string username) =>
        username.Length is >= 1 and <= 16 &&
        username.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');

    public static async Task<string> DownloadAsync(string username, string directory, HttpClient? client = null)
    {
        username = username.Trim();
        if (!IsValidUsername(username))
            throw new ArgumentException("Enter a Java Edition username (1-16 letters, numbers or underscores).", nameof(username));

        client ??= Client;
        using var profile = await GetJsonAsync(client,
            $"https://api.mojang.com/users/profiles/minecraft/{username}").ConfigureAwait(false);
        string id = RequiredString(profile.RootElement, "id");
        if (!Guid.TryParseExact(id, "N", out _))
            throw new InvalidDataException("Minecraft returned an invalid player ID.");

        using var session = await GetJsonAsync(client,
            $"https://sessionserver.mojang.com/session/minecraft/profile/{id}").ConfigureAwait(false);
        if (!session.RootElement.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Minecraft returned no skin information.");

        string? textureValue = null;
        foreach (var property in properties.EnumerateArray())
        {
            if (RequiredString(property, "name") == "textures")
            {
                textureValue = RequiredString(property, "value");
                break;
            }
        }
        if (textureValue == null)
            throw new InvalidDataException("This player has no uploaded skin.");

        using var textures = JsonDocument.Parse(Convert.FromBase64String(textureValue));
        if (textures.RootElement.ValueKind != JsonValueKind.Object ||
            !textures.RootElement.TryGetProperty("textures", out var textureMap) ||
            textureMap.ValueKind != JsonValueKind.Object ||
            !textureMap.TryGetProperty("SKIN", out var skin))
            throw new InvalidDataException("This player has no uploaded skin.");

        string url = RequiredString(skin, "url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !uri.Host.Equals("textures.minecraft.net", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            !uri.AbsolutePath.StartsWith("/texture/", StringComparison.Ordinal) ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("Minecraft returned an invalid skin URL.");

        using var response = await client.GetAsync(new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        ValidateSkin(bytes);

        // Content-addressed files keep existing project textures stable when a player changes skins.
        string path = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + ".png");
        Directory.CreateDirectory(directory);
        if (!File.Exists(path))
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
        return path;
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
            throw new InvalidDataException("No Minecraft Java Edition player was found with that username.");
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Minecraft is rate limiting requests. Please try again later.");
        response.EnsureSuccessStatusCode();
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        if (document.RootElement.ValueKind == JsonValueKind.Object)
            return document;
        document.Dispose();
        throw new InvalidDataException("Minecraft returned an invalid profile.");
    }

    private static string RequiredString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString()))
            return value.GetString()!;
        throw new InvalidDataException($"Minecraft returned an invalid '{name}' value.");
    }

    private static void ValidateSkin(byte[] bytes)
    {
        if (bytes.Length is < 24 or > 1024 * 1024 ||
            !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)) != 64 ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)) is not (32 or 64))
            throw new InvalidDataException("Minecraft returned an invalid skin PNG (expected 64x64 or 64x32).");
        try
        {
            using var image = new MagickImage(bytes);
        }
        catch (MagickException exception)
        {
            throw new InvalidDataException("Minecraft returned a damaged skin PNG.", exception);
        }
    }
}
