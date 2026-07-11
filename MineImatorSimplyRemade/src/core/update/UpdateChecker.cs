using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO.Compression;
using System.Reflection;
using MineImatorSimplyRemade;

namespace MineImatorSimplyRemade.core.update;

public class UpdateChecker
{
    private const string GitHubApiUrl = "https://api.github.com/repos/Team-Remade/MineImatorNuxiBuild/releases";
    private const string GitHubRepoUrl = "https://github.com/Team-Remade/MineImatorNuxiBuild/releases";
    private static readonly HttpClient HttpClient = new();
    
    private static string UpdateStateFilePath => Path.Combine(main.ApplicationLocalDirectoryPath, "update-state.json");
    private static string DownloadsDirectory => Path.Combine(main.ApplicationLocalDirectoryPath, "downloads");

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool PreRelease { get; set; }

        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = [];
    }

    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public class UpdateState
    {
        public string? AvailableVersion { get; set; }
        public string? AvailableVersionName { get; set; }
        public string? ChangeLog { get; set; }
        public string? DownloadUrl { get; set; }
        public DateTime LastCheckTime { get; set; }
        public bool DismissedVersion { get; set; }
        public string? DismissedVersionNumber { get; set; }
        public bool DownloadedUpdatePath { get; set; }
        public string? DownloadedUpdateFilePath { get; set; }
    }

    public class UpdateCheckResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public bool UpdateAvailable { get; set; }
        public string? AvailableVersion { get; set; }
        public string? AvailableVersionName { get; set; }
        public string? ChangeLog { get; set; }
        public string? DownloadUrl { get; set; }
    }

    public static string GetCurrentVersion()
    {
        // Read from the csproj Version property
        var version = typeof(main).Assembly.GetName().Version;
        return version?.ToString() ?? "0.1.0";
    }

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        try
        {
            // Add User-Agent header (required by GitHub API)
            if (!HttpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                HttpClient.DefaultRequestHeaders.Add("User-Agent", "MineImatorSimplyRemade-UpdateChecker");
            }

            var response = await HttpClient.GetAsync(GitHubApiUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    Message = $"GitHub API returned: {response.StatusCode}"
                };
            }

            var content = await response.Content.ReadAsStringAsync();
            var releases = (List<GitHubRelease>)JsonSerializer.Deserialize(content, typeof(List<GitHubRelease>), AppJsonContext.Default);

            if (releases == null || releases.Count == 0)
            {
                return new UpdateCheckResult
                {
                    Success = true,
                    UpdateAvailable = false,
                    Message = "No releases found"
                };
            }

            // Get the latest non-draft, non-prerelease release
            var latestRelease = releases.FirstOrDefault(r => !r.Draft && !r.PreRelease);
            
            if (latestRelease == null)
            {
                // If no stable release, try prerelease
                latestRelease = releases.FirstOrDefault(r => !r.Draft);
            }

            if (latestRelease == null)
            {
                return new UpdateCheckResult
                {
                    Success = true,
                    UpdateAvailable = false,
                    Message = "No suitable releases found"
                };
            }

            var currentVersion = GetCurrentVersion();
            var availableVersion = NormalizeVersionString(latestRelease.TagName);
            
            var updateState = LoadUpdateState();
            var result = new UpdateCheckResult
            {
                Success = true,
                AvailableVersion = availableVersion,
                AvailableVersionName = latestRelease.Name,
                ChangeLog = latestRelease.Body,
                UpdateAvailable = IsNewerVersion(currentVersion, availableVersion)
            };

            // Find the appropriate asset for this platform
            var asset = FindSuitableAsset(latestRelease.Assets);
            if (asset != null)
            {
                result.DownloadUrl = asset.DownloadUrl;
                updateState.AvailableVersion = availableVersion;
                updateState.AvailableVersionName = latestRelease.Name;
                updateState.ChangeLog = latestRelease.Body;
                updateState.DownloadUrl = asset.DownloadUrl;
                updateState.LastCheckTime = DateTime.UtcNow;
                
                // Don't override dismissed version if it's the same
                if (updateState.DismissedVersionNumber != availableVersion)
                {
                    updateState.DismissedVersion = false;
                }

                SaveUpdateState(updateState);
                result.Message = result.UpdateAvailable ? "Update available" : "Already on latest version";
            }
            else
            {
                result.Message = "No suitable download asset found for this platform";
            }

            return result;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                Success = false,
                Message = $"Error checking for updates: {ex.Message}"
            };
        }
    }

    public static UpdateState LoadUpdateState()
    {
        try
        {
            if (!File.Exists(UpdateStateFilePath))
                return new UpdateState();

            var json = File.ReadAllText(UpdateStateFilePath);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.UpdateState) ?? new UpdateState();
        }
        catch
        {
            return new UpdateState();
        }
    }

    public static void SaveUpdateState(UpdateState state)
    {
        try
        {
            Directory.CreateDirectory(main.ApplicationLocalDirectoryPath);
            var json = JsonSerializer.Serialize(state, AppJsonContext.Default.UpdateState);
            File.WriteAllText(UpdateStateFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateChecker] Error saving update state: {ex.Message}");
        }
    }

    public static void DismissUpdate(string version)
    {
        var state = LoadUpdateState();
        state.DismissedVersion = true;
        state.DismissedVersionNumber = version;
        SaveUpdateState(state);
    }

    public static async Task<(bool success, string filePath)> DownloadUpdateAsync(string downloadUrl, Action<long, long>? progressCallback = null)
    {
        try
        {
            Directory.CreateDirectory(DownloadsDirectory);

            var fileName = Path.GetFileName(downloadUrl.Split('?')[0]);
            var filePath = Path.Combine(DownloadsDirectory, fileName);

            using (var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                if (!response.IsSuccessStatusCode)
                {
                    return (false, $"Download failed: {response.StatusCode}");
                }

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1 && progressCallback != null;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var totalRead = 0L;
                    var buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;

                        if (canReportProgress)
                        {
                            progressCallback!(totalRead, totalBytes);
                        }
                    }
                }

                // Update state to mark download complete
                var state = LoadUpdateState();
                state.DownloadedUpdatePath = true;
                state.DownloadedUpdateFilePath = filePath;
                SaveUpdateState(state);

                return (true, filePath);
            }
        }
        catch (Exception ex)
        {
            return (false, $"Error downloading update: {ex.Message}");
        }
    }

    public static void LaunchInstallerOrExecutable(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Update file not found: {filePath}");

            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            };

            System.Diagnostics.Process.Start(processInfo);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateChecker] Error launching update: {ex.Message}");
        }
    }

    public static bool HasDownloadedUpdate()
    {
        var state = LoadUpdateState();
        return state.DownloadedUpdatePath && File.Exists(state.DownloadedUpdateFilePath);
    }

    public static string? GetDownloadedUpdatePath()
    {
        var state = LoadUpdateState();
        if (state.DownloadedUpdatePath && File.Exists(state.DownloadedUpdateFilePath))
            return state.DownloadedUpdateFilePath;
        return null;
    }

    public static void ClearDownloadedUpdate()
    {
        try
        {
            var state = LoadUpdateState();
            if (state.DownloadedUpdateFilePath != null && File.Exists(state.DownloadedUpdateFilePath))
            {
                File.Delete(state.DownloadedUpdateFilePath);
            }
            state.DownloadedUpdatePath = false;
            state.DownloadedUpdateFilePath = null;
            SaveUpdateState(state);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateChecker] Error clearing downloaded update: {ex.Message}");
        }
    }

    private static string NormalizeVersionString(string version)
    {
        // Remove 'v' prefix if present
        return version.StartsWith("v", StringComparison.OrdinalIgnoreCase) 
            ? version[1..] 
            : version;
    }

    private static bool IsNewerVersion(string currentVersion, string availableVersion)
    {
        try
        {
            var current = ParseVersion(currentVersion);
            var available = ParseVersion(availableVersion);
            
            if (available.major != current.major)
                return available.major > current.major;
            if (available.minor != current.minor)
                return available.minor > current.minor;
            return available.patch > current.patch;
        }
        catch
        {
            return false;
        }
    }

    private static (int major, int minor, int patch) ParseVersion(string version)
    {
        var parts = version.Split('.');
        int.TryParse(parts.ElementAtOrDefault(0) ?? "0", out var major);
        int.TryParse(parts.ElementAtOrDefault(1) ?? "0", out var minor);
        int.TryParse(parts.ElementAtOrDefault(2) ?? "0", out var patch);
        return (major, minor, patch);
    }

    private static GitHubAsset? FindSuitableAsset(List<GitHubAsset> assets)
    {
        // Look for Windows executable or installer
        if (OperatingSystem.IsWindows())
        {
            // First try for .exe files
            var exe = assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (exe != null) return exe;

            // Then try for .msi or .zip
            var msi = assets.FirstOrDefault(a => a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
            if (msi != null) return msi;

            var zip = assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (zip != null) return zip;
        }
        else if (OperatingSystem.IsLinux())
        {
            // Look for Linux packages
            var deb = assets.FirstOrDefault(a => a.Name.EndsWith(".deb", StringComparison.OrdinalIgnoreCase));
            if (deb != null) return deb;

            var rpm = assets.FirstOrDefault(a => a.Name.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase));
            if (rpm != null) return rpm;

            var tar = assets.FirstOrDefault(a => a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase));
            if (tar != null) return tar;
        }
        else if (OperatingSystem.IsMacOS())
        {
            // Look for macOS packages
            var dmg = assets.FirstOrDefault(a => a.Name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase));
            if (dmg != null) return dmg;

            var tar = assets.FirstOrDefault(a => a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase));
            if (tar != null) return tar;
        }

        // Fallback: return first asset
        return assets.FirstOrDefault();
    }

    /// <summary>
    /// Install an update while the app is running by:
    /// 1. Downloading the release
    /// 2. Extracting it to a temp location
    /// 3. Backing up the running executable
    /// 4. Copying new files to the app directory
    /// The app must be restarted for the update to take effect.
    /// </summary>
    public static async Task<(bool success, string message, bool needsRestart)> InstallUpdateWhileRunningAsync(
        string downloadUrl, 
        Action<long, long>? progressCallback = null)
    {
        try
        {
            Console.WriteLine("[UpdateChecker] Starting in-place update installation...");

            // Get running executable path and directory
            var runningExePath = GetRunningExecutablePath();
            var appDirectory = Path.GetDirectoryName(runningExePath) ?? throw new InvalidOperationException("Could not determine app directory");
            var exeName = Path.GetFileName(runningExePath);

            Console.WriteLine($"[UpdateChecker] Running exe: {runningExePath}");
            Console.WriteLine($"[UpdateChecker] App directory: {appDirectory}");

            // Create temp extraction directory
            var tempExtractDir = Path.Combine(main.ApplicationLocalDirectoryPath, "update-temp-extract");
            if (Directory.Exists(tempExtractDir))
                Directory.Delete(tempExtractDir, recursive: true);
            Directory.CreateDirectory(tempExtractDir);

            Console.WriteLine($"[UpdateChecker] Temp extract dir: {tempExtractDir}");

            // Download the update
            var fileName = Path.GetFileName(downloadUrl.Split('?')[0]);
            var downloadPath = Path.Combine(tempExtractDir, fileName);

            Console.WriteLine($"[UpdateChecker] Downloading update ({fileName})...");

            using (var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                if (!response.IsSuccessStatusCode)
                {
                    return (false, $"Download failed: {response.StatusCode}", false);
                }

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1 && progressCallback != null;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var totalRead = 0L;
                    var buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;

                        if (canReportProgress)
                        {
                            progressCallback!(totalRead, totalBytes);
                        }
                    }
                }
            }

            Console.WriteLine($"[UpdateChecker] Download complete: {new FileInfo(downloadPath).Length} bytes");

            // Extract the downloaded file
            var extractedDir = Path.Combine(tempExtractDir, "extracted");
            Directory.CreateDirectory(extractedDir);

            if (downloadPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[UpdateChecker] Extracting ZIP...");
                ZipFile.ExtractToDirectory(downloadPath, extractedDir, overwriteFiles: true);
            }
            else
            {
                return (false, "Only ZIP files are supported for now", false);
            }

            Console.WriteLine($"[UpdateChecker] Extraction complete");

            // Find the executable in the extracted content
            var extractedExe = FindExecutableInExtracted(extractedDir, exeName);
            if (extractedExe == null)
            {
                return (false, $"Could not find {exeName} in extracted release", false);
            }

            Console.WriteLine($"[UpdateChecker] Found extracted exe: {extractedExe}");

            // Backup the running executable
            var backupPath = runningExePath + ".bak";
            Console.WriteLine($"[UpdateChecker] Backing up current exe to: {backupPath}");

            try
            {
                if (File.Exists(backupPath))
                    File.Delete(backupPath);

                File.Move(runningExePath, backupPath, overwrite: true);
                Console.WriteLine($"[UpdateChecker] Backup successful");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to backup executable: {ex.Message}", false);
            }

            // Copy new executable and supporting files
            Console.WriteLine($"[UpdateChecker] Copying new files to app directory...");

            try
            {
                // Copy the extracted exe
                File.Copy(extractedExe, runningExePath, overwrite: true);
                Console.WriteLine($"[UpdateChecker] Copied new executable");

                // Copy other files from extracted directory (maintaining structure)
                CopyDirectoryContents(extractedDir, appDirectory, exeName);
                Console.WriteLine($"[UpdateChecker] Copied all files");
            }
            catch (Exception ex)
            {
                // Try to restore from backup
                try
                {
                    Console.WriteLine($"[UpdateChecker] Installation failed, attempting to restore from backup...");
                    if (File.Exists(backupPath))
                    {
                        File.Move(backupPath, runningExePath, overwrite: true);
                    }
                }
                catch { }

                return (false, $"Failed to copy new files: {ex.Message}", false);
            }

            // Clean up temp directory
            try
            {
                Directory.Delete(tempExtractDir, recursive: true);
                Console.WriteLine($"[UpdateChecker] Cleaned up temp directory");
            }
            catch { }

            Console.WriteLine($"[UpdateChecker] Update installation successful!");
            return (true, "Update installed successfully. Please restart the application to apply changes.", true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateChecker] Installation error: {ex}");
            return (false, $"Update installation failed: {ex.Message}", false);
        }
    }

    /// <summary>
    /// Get the path to the currently running executable
    /// </summary>
    private static string GetRunningExecutablePath()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var location = assembly.Location;

        if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
            return location;

        // Fallback for single-file publish
        var processModule = System.Diagnostics.Process.GetCurrentProcess().MainModule;
        if (processModule?.FileName != null && File.Exists(processModule.FileName))
            return processModule.FileName;

        throw new InvalidOperationException("Could not determine running executable path");
    }

    /// <summary>
    /// Search for the executable in the extracted directory structure
    /// </summary>
    private static string? FindExecutableInExtracted(string extractedDir, string exeName)
    {
        // First try direct match
        var directPath = Path.Combine(extractedDir, exeName);
        if (File.Exists(directPath))
            return directPath;

        // Search in subdirectories (e.g., might be in a 'bin' folder)
        foreach (var file in Directory.EnumerateFiles(extractedDir, exeName, SearchOption.AllDirectories))
        {
            return file;
        }

        // Try case-insensitive search
        foreach (var file in Directory.EnumerateFiles(extractedDir, "*.*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).Equals(exeName, StringComparison.OrdinalIgnoreCase) && 
                (file.EndsWith(".exe") || file.EndsWith(".dll") || !file.Contains(".")))
            {
                return file;
            }
        }

        return null;
    }

    /// <summary>
    /// Copy directory contents, excluding the executable (which is handled separately)
    /// </summary>
    private static void CopyDirectoryContents(string sourceDir, string targetDir, string exeNameToSkip)
    {
        var sourceInfo = new DirectoryInfo(sourceDir);

        foreach (var file in sourceInfo.GetFiles())
        {
            // Skip the executable - it's already been copied
            if (file.Name.Equals(exeNameToSkip, StringComparison.OrdinalIgnoreCase))
                continue;

            var targetPath = Path.Combine(targetDir, file.Name);
            file.CopyTo(targetPath, overwrite: true);
        }

        foreach (var subDir in sourceInfo.GetDirectories())
        {
            var targetSubDir = Path.Combine(targetDir, subDir.Name);
            Directory.CreateDirectory(targetSubDir);
            CopyDirectoryContents(subDir.FullName, targetSubDir, exeNameToSkip);
        }
    }
}
