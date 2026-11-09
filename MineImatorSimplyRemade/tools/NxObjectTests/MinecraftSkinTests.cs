using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ImageMagick;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using MineImatorSimplyRemadeNuxi.core.objs;

internal static partial class Program
{
    private const string SkinPlayerId = "069a79f444e94726a5befca90e38aaf5";

    private static void MinecraftSkinDownloads(Fixture f)
    {
        using var image = new MagickImage(MagickColors.Red, 64, 64);
        byte[] png = image.ToByteArray(MagickFormat.Png);
        using var handler = new SkinHttpHandler(request => SkinResponse(request, png));
        using var client = new HttpClient(handler);
        string path = MinecraftSkinDownloader.DownloadAsync("  Test_Player  ", f.Root, client).GetAwaiter().GetResult();
        Check(File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(png), "Downloaded PNG persists unchanged");
        Check(handler.Requests.Count == 3 && handler.Requests[0].AbsolutePath.EndsWith("/Test_Player"),
            "Trimmed username resolves through Mojang");
        Check(handler.Requests.All(uri => uri.Scheme == "https"), "All requests, including HTTP texture URLs, use HTTPS");
        string second = MinecraftSkinDownloader.DownloadAsync("Test_Player", f.Root, client).GetAwaiter().GetResult();
        Check(second == path && Directory.GetFiles(f.Root).Length == 1, "Identical skins share immutable storage");
        using var changedImage = new MagickImage(MagickColors.Blue, 64, 64);
        byte[] changed = changedImage.ToByteArray(MagickFormat.Png);
        using var changedHandler = new SkinHttpHandler(request => SkinResponse(request, changed));
        using var changedClient = new HttpClient(changedHandler);
        string newPath = MinecraftSkinDownloader.DownloadAsync("Test_Player", f.Root, changedClient).GetAwaiter().GetResult();
        Check(newPath != path && File.ReadAllBytes(path).SequenceEqual(png), "Changed skin does not replace a saved texture");
        using var legacy = new MagickImage(MagickColors.Green, 64, 32);
        using var legacyHandler = new SkinHttpHandler(request => SkinResponse(request, legacy.ToByteArray(MagickFormat.Png)));
        using var legacyClient = new HttpClient(legacyHandler);
        Check(File.Exists(MinecraftSkinDownloader.DownloadAsync("Legacy", f.Root, legacyClient).GetAwaiter().GetResult()),
            "Legacy 64x32 skins are accepted");

        foreach (string invalid in new[] { "", "../player", "Name With Space", "12345678901234567", "pläyer" })
        {
            int calls = handler.Requests.Count;
            RejectSkin<ArgumentException>(() => MinecraftSkinDownloader.DownloadAsync(invalid, f.Root, client).GetAwaiter().GetResult());
            Check(handler.Requests.Count == calls, "Invalid username never reaches the network");
        }
    }

    private static void MinecraftSkinFailures(Fixture f)
    {
        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.NoContent,
            HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
        {
            using var handler = new SkinHttpHandler(_ => new HttpResponseMessage(status));
            using var client = new HttpClient(handler);
            RejectSkin<Exception>(() => MinecraftSkinDownloader.DownloadAsync("Player", f.Root, client).GetAwaiter().GetResult(),
                exception => exception is InvalidDataException or HttpRequestException);
        }
        using var image = new MagickImage(MagickColors.Red, 64, 64);
        byte[] png = image.ToByteArray(MagickFormat.Png);
        foreach (string url in new[] { "https://example.com/texture/test", "file:///C:/test.png",
            "https://textures.minecraft.net:8443/texture/test", "https://textures.minecraft.net/other/test" })
        {
            using var handler = new SkinHttpHandler(request => SkinResponse(request, png, url));
            using var client = new HttpClient(handler);
            RejectSkin<InvalidDataException>(() => MinecraftSkinDownloader.DownloadAsync("Player", f.Root, client).GetAwaiter().GetResult());
            Check(handler.Requests.Count == 2, "Untrusted texture URLs are rejected before download");
        }
        using var wrongSize = new MagickImage(MagickColors.Red, 32, 32);
        foreach (byte[] invalidPng in new[] { new byte[] { 1, 2, 3 }, png[..24], wrongSize.ToByteArray(MagickFormat.Png) })
        {
            using var handler = new SkinHttpHandler(request => SkinResponse(request, invalidPng));
            using var client = new HttpClient(handler);
            RejectSkin<InvalidDataException>(() => MinecraftSkinDownloader.DownloadAsync("Player", f.Root, client).GetAwaiter().GetResult());
        }
        foreach (string profile in new[] { "[]", "{}", """{"id":"invalid"}""" })
        {
            using var handler = new SkinHttpHandler(_ => JsonSkinResponse(profile));
            using var client = new HttpClient(handler);
            RejectSkin<InvalidDataException>(() => MinecraftSkinDownloader.DownloadAsync("Player", f.Root, client).GetAwaiter().GetResult());
        }
        foreach (string session in new[]
        {
            "{}", """{"properties":[]}""",
            """{"properties":[{"name":"textures","value":"not base64!"}]}""",
            SkinSession("""{"textures":{}}"""),
            SkinSession("[]")
        })
        {
            using var handler = new SkinHttpHandler(request => request.RequestUri!.Host == "api.mojang.com"
                ? JsonSkinResponse($$"""{"id":"{{SkinPlayerId}}"}""") : JsonSkinResponse(session));
            using var client = new HttpClient(handler);
            RejectSkin<Exception>(() => MinecraftSkinDownloader.DownloadAsync("Player", f.Root, client).GetAwaiter().GetResult(),
                exception => exception is InvalidDataException or FormatException);
        }
        Check(Directory.GetFiles(f.Root).Length == 0, "Failed downloads leave no cached PNGs");
    }

    private static void MinecraftSkinSelection(Fixture f)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var catalog = (List<CharacterEntry>)typeof(CharacterRegistry).GetField("_characters",
            BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var original = catalog.ToArray();
        try
        {
            catalog.Clear();
            catalog.Add(new CharacterEntry
            {
                Name = "Steve", TextureVariants = [new() { Name = "Default" }, new() { Name = "Custom", IsCustom = true }]
            });
            var spawn = new SpawnMenu();
            void Set(string name, object value) => typeof(SpawnMenu).GetField(name, flags)!.SetValue(spawn, value);
            object? Get(string name) => typeof(SpawnMenu).GetField(name, flags)!.GetValue(spawn);
            void Complete() => typeof(SpawnMenu).GetMethod("CompleteSkinDownload", flags)!.Invoke(spawn, null);
            Set("_selectedCategory", "Characters");
            Set("_selectedObjectIndex", 0);
            Set("_skinDownloadEntry", catalog[0]);
            var pending = new TaskCompletionSource<string>();
            Set("_skinDownloadTask", pending.Task);
            Complete();
            Check((int)Get("_selectedCharTextureIndex")! == -1, "Pending download does not alter current skin");
            string path = f.File("skin.png", "fixture");
            pending.SetResult(path);
            Complete();
            Check((int)Get("_selectedCharTextureIndex")! == 1 && (string)Get("_customCharTexturePath")! == path,
                "Successful download selects Custom and its persistent path");
            Check((string?)typeof(SpawnMenu).GetMethod("ResolveCharacterTextureOverride", flags)!.Invoke(spawn, [catalog[0]]) == path,
                "Preview resolves the downloaded texture");
            Check((bool)typeof(SpawnMenu).GetMethod("CanSpawn", flags)!.Invoke(spawn, null)!,
                "Downloaded custom skin is spawnable");

            Set("_skinDownloadEntry", catalog[0]);
            Set("_skinDownloadTask", Task.FromResult("stale.png"));
            typeof(SpawnMenu).GetMethod("ResetSkinDownloadSelection", flags)!.Invoke(spawn, null);
            Complete();
            Check((string)Get("_customCharTexturePath")! == path, "Changing selection discards stale results");

            foreach (Exception error in new Exception[] { new HttpRequestException("offline"),
                new InvalidDataException("missing player"), new TaskCanceledException("timeout") })
            {
                Set("_skinDownloadRevision", Get("_characterSelectionRevision")!);
                Set("_skinDownloadEntry", catalog[0]);
                Set("_skinDownloadTask", Task.FromException<string>(error));
                Complete();
                Check((string)Get("_skinDownloadError")! == error.Message && (string)Get("_customCharTexturePath")! == path,
                    "Network, profile and timeout errors are surfaced without losing the previous skin");
            }
            Check(Get("_skinDownloadTask") == null, "Failure allows retry");
        }
        finally
        {
            catalog.Clear();
            catalog.AddRange(original);
        }
    }

    private static void MinecraftSkinProjectStorage(Fixture f)
    {
        using var projectFixture = new Fixture();
        var manager = BackupProject(projectFixture, "skins");
        var spawn = new SpawnMenu { ProjectManager = manager };
        var resolve = typeof(SpawnMenu).GetMethod("ResolveTexturePathForProject",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        using var image = new MagickImage(MagickColors.Red, 64, 64);
        byte[] png = image.ToByteArray(MagickFormat.Png);
        using var handler = new SkinHttpHandler(request => SkinResponse(request, png));
        using var client = new HttpClient(handler);
        string cache = MinecraftSkinDownloader.DownloadAsync("Player", f.Root, client).GetAwaiter().GetResult();
        string copied = (string)resolve.Invoke(spawn, [cache, false])!;
        Check(Path.GetDirectoryName(copied) == manager.ImagesFolder &&
            File.ReadAllBytes(copied).SequenceEqual(png), "Skin copied into project images");
        Check((string)resolve.Invoke(spawn, [cache, false])! == copied &&
            manager.Manifest.Assets.Count == 1, "Repeated skin imports reuse the project image");
        var saved = JsonSerializer.Deserialize(File.ReadAllText(manager.ProjectFilePath),
            AppJsonContext.Default.ProjectManifest)!;
        var asset = saved.Assets.Single();
        Check(asset.StoredInProject && !Path.IsPathRooted(asset.RelativePath), "Manifest records a project-local image");
        File.Delete(cache);
        Check(File.Exists(copied), "Project skin is independent of the download cache");
        Check((string)resolve.Invoke(spawn, [copied, false])! == copied &&
            manager.Manifest.Assets.Count == 1, "Project-local skin is not copied again");
    }

    private static HttpResponseMessage SkinResponse(HttpRequestMessage request, byte[] png,
        string url = "http://textures.minecraft.net/texture/test")
    {
        return request.RequestUri!.Host switch
        {
            "api.mojang.com" => JsonSkinResponse($$"""{"id":"{{SkinPlayerId}}"}"""),
            "sessionserver.mojang.com" => JsonSkinResponse(SkinSession(
                JsonSerializer.Serialize(new { textures = new { SKIN = new { url } } }))),
            "textures.minecraft.net" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) },
            _ => throw new InvalidOperationException("Unexpected network request")
        };
    }

    private static void MinecraftSkinGraphics(SpawnMenu spawn, Viewport viewport, Fixture f,
        Func<byte[]> readPixels)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var catalog = (List<CharacterEntry>)typeof(CharacterRegistry).GetField("_characters",
            BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var original = catalog.ToArray();
        using var image = new MagickImage(MagickColors.Red, 64, 64);
        using var handler = new SkinHttpHandler(request => SkinResponse(request, image.ToByteArray(MagickFormat.Png)));
        using var client = new HttpClient(handler);
        string path = MinecraftSkinDownloader.DownloadAsync("Player", f.Root, client).GetAwaiter().GetResult();
        void Set(string name, object value) => typeof(SpawnMenu).GetField(name, flags)!.SetValue(spawn, value);
        var manager = ProjectManager.Instance;
        string originalFolder = manager.ProjectFolder, originalFile = manager.ProjectFilePath;
        var originalManifest = manager.Manifest;
        bool originalDirty = manager.IsDirty;
        var originalManager = spawn.ProjectManager;
        string projectFolder = Path.Combine(f.Root, "skin-project");
        Directory.CreateDirectory(projectFolder);
        void SetProject(string folder)
        {
            typeof(ProjectManager).GetProperty(nameof(ProjectManager.ProjectFolder))!.SetValue(manager, folder);
            typeof(ProjectManager).GetProperty(nameof(ProjectManager.ProjectFilePath))!.SetValue(manager,
                Path.Combine(folder, "skins.nxProj"));
        }
        var snapshots = new List<ProjectSceneObjectEntry>();
        try
        {
            SetProject(projectFolder);
            typeof(ProjectManager).GetProperty(nameof(ProjectManager.Manifest))!.SetValue(manager, new ProjectManifest());
            spawn.ProjectManager = manager;
            foreach (string model in new[] { "steve", "alex" })
            {
                string modelPath = Path.Combine(DataAssetRemapper.Current.DataRoot, "minecraft", "versions", "26.3",
                    "characters", "vanilla", model, model + ".mimodel");
                catalog.Clear();
                catalog.Add(new CharacterEntry
                {
                    Name = model, FilePath = modelPath,
                    TextureVariants = [new() { Name = "Default" }, new() { Name = "Custom", IsCustom = true }]
                });
                Set("_selectedCategory", "Characters");
                Set("_selectedObjectIndex", 0);
                Set("_skinDownloadRevision", typeof(SpawnMenu).GetField("_characterSelectionRevision", flags)!.GetValue(spawn)!);
                Set("_skinDownloadEntry", catalog[0]);
                Set("_skinDownloadTask", Task.FromResult(path));
                typeof(SpawnMenu).GetMethod("CompleteSkinDownload", flags)!.Invoke(spawn, null);
                typeof(SpawnMenu).GetMethod("UpdatePreview", flags)!.Invoke(spawn, [0d]);
                var preview = (SceneObject)typeof(SpawnMenu).GetField("_previewCharacter", flags)!.GetValue(spawn)!;
                uint texture = MineImatorLoader.Instance.LoadTextureFromFile(path, makeSquare: true);
                Check(texture != 0 && preview.GetMeshInstancesRecursively().Any(mesh => mesh.TextureId == texture),
                    $"{model} preview uses the downloaded skin texture");
                byte[] pixels = readPixels();
                int redPixels = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                    if (pixels[i] > 80 && pixels[i] > pixels[i + 1] * 2 && pixels[i] > pixels[i + 2] * 2)
                        redPixels++;
                Check(redPixels > 100, $"{model} visibly renders the downloaded red skin ({redPixels} pixels)");

                Set("_placeWithCursor", false);
                typeof(SpawnMenu).GetMethod("TrySpawnCharacter", flags)!.Invoke(spawn, null);
                var spawned = viewport.SceneObjects.Last();
                try
                {
                    string projectSkin = spawned.TextureOverridePath;
                    uint projectTexture = MineImatorLoader.Instance.LoadTextureFromFile(projectSkin, makeSquare: true);
                    Check(Path.GetDirectoryName(projectSkin) == manager.ImagesFolder &&
                        File.ReadAllBytes(projectSkin).SequenceEqual(File.ReadAllBytes(path)) &&
                        spawned.GetMeshInstancesRecursively().Any(mesh => mesh.TextureId == projectTexture),
                        $"{model} spawn uses the project copy of the downloaded texture: path={projectSkin}, " +
                        $"expected folder={manager.ImagesFolder}, texture={projectTexture}, " +
                        $"mesh textures={string.Join(",", spawned.GetMeshInstancesRecursively().Select(mesh => mesh.TextureId))}");
                    Check(spawned.GetAllDescendants().All(child => child.TextureOverridePath == projectSkin),
                        $"{model} descendants retain project-local skin references");
                    var snapshot = ProjectSceneSerializer.SerializeObjectForLibrary(spawned);
                    Check(!Path.IsPathRooted(snapshot.TextureOverridePath) &&
                        snapshot.TextureOverridePath == Path.Combine("images", Path.GetFileName(path)),
                        $"{model} saved object uses a portable relative skin reference");
                    snapshots.Add(snapshot);
                }
                finally
                {
                    viewport.SceneObjects.Remove(spawned);
                    foreach (var mesh in spawned.GetMeshInstancesRecursively()) mesh.Dispose();
                }
            }
            Check(manager.Manifest.Assets.Count(asset => asset.AssetType == ProjectAssetType.Image) == 1,
                "Alex and Steve share one project copy of the same downloaded skin");
            manager.Manifest.SceneObjects = snapshots;
            manager.SaveManifest();
            string movedFolder = Path.Combine(f.Root, "transferred-skin-project");
            Directory.Move(projectFolder, movedFolder);
            File.Delete(path);
            SetProject(movedFolder);
            var transferred = JsonSerializer.Deserialize(File.ReadAllText(manager.ProjectFilePath),
                AppJsonContext.Default.ProjectManifest)!;
            foreach (var snapshot in transferred.SceneObjects)
            {
                var restored = ProjectSceneSerializer.SpawnObjectFromEntry(snapshot, viewport, spawn)!;
                try
                {
                    string movedSkin = Path.Combine(movedFolder, snapshot.TextureOverridePath);
                    uint movedTexture = MineImatorLoader.Instance.LoadTextureFromFile(movedSkin, makeSquare: true);
                    Check(restored.TextureOverridePath == movedSkin && movedTexture != 0 &&
                        restored.GetMeshInstancesRecursively().Any(mesh => mesh.TextureId == movedTexture),
                        "Transferred project reloads its skin without the original project or download cache");
                }
                finally
                {
                    viewport.SceneObjects.Remove(restored);
                    foreach (var mesh in restored.GetMeshInstancesRecursively()) mesh.Dispose();
                }
            }
            Console.WriteLine("PASS Downloaded skins copied on spawn, deduplicated and reloaded after project transfer without cache");
        }
        finally
        {
            typeof(ProjectManager).GetProperty(nameof(ProjectManager.ProjectFolder))!.SetValue(manager, originalFolder);
            typeof(ProjectManager).GetProperty(nameof(ProjectManager.ProjectFilePath))!.SetValue(manager, originalFile);
            typeof(ProjectManager).GetProperty(nameof(ProjectManager.Manifest))!.SetValue(manager, originalManifest);
            manager.SetDirty(originalDirty);
            spawn.ProjectManager = originalManager;
            catalog.Clear();
            catalog.AddRange(original);
            Set("_placeWithCursor", true);
            Set("_selectedCharTextureIndex", -1);
            Set("_customCharTexturePath", "");
        }
    }

    private static string SkinSession(string textures) => JsonSerializer.Serialize(new
    {
        properties = new[] { new { name = "textures", value = Convert.ToBase64String(Encoding.UTF8.GetBytes(textures)) } }
    });

    private static HttpResponseMessage JsonSkinResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private static void RejectSkin<T>(Action run, Func<Exception, bool>? predicate = null) where T : Exception
    {
        try { run(); }
        catch (T exception) when (predicate == null || predicate(exception)) { return; }
        throw new InvalidOperationException("Invalid skin download was accepted");
    }

    private sealed class SkinHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }
}
