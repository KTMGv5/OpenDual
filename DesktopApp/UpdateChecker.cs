using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace OpenDualHub
{
    public class GithubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("assets")]
        public GithubAsset[] Assets { get; set; } = Array.Empty<GithubAsset>();
    }

    public class UpdateInfo
    {
        public bool UpdateAvailable { get; set; }
        public string CurrentVersion { get; set; } = "1.6.6";
        public string LatestVersion { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public string ReleaseUrl { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public long FileSize { get; set; }
    }

    public class UpdateChecker
    {
        public const string CurrentAppVersion = "1.6.6";
        private const string RepoApiUrl = "https://api.github.com/repos/KTMGv5/OpenDual/releases/latest";
        private static readonly HttpClient HttpClient = new HttpClient();

        static UpdateChecker()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"OpenDualHub/{CurrentAppVersion} (Windows; x64)");
            HttpClient.Timeout = TimeSpan.FromSeconds(15);
        }

        public async Task<UpdateInfo> CheckForUpdateAsync(CancellationToken ct = default)
        {
            var info = new UpdateInfo
            {
                CurrentVersion = CurrentAppVersion,
                UpdateAvailable = false
            };

            try
            {
                var response = await HttpClient.GetAsync(RepoApiUrl, ct);
                if (!response.IsSuccessStatusCode)
                    return info;

                var json = await response.Content.ReadAsStringAsync(ct);
                var release = JsonSerializer.Deserialize<GithubRelease>(json);
                if (release == null) return info;

                // Strip 'v' from tag (e.g. "v1.4.1" -> "1.4.1")
                string cleanRemoteVer = release.TagName.TrimStart('v', 'V').Trim();
                info.LatestVersion = cleanRemoteVer;
                info.ReleaseNotes = release.Body;
                info.ReleaseUrl = release.HtmlUrl;

                if (Version.TryParse(cleanRemoteVer, out var remoteVer) &&
                    Version.TryParse(CurrentAppVersion, out var localVer))
                {
                    if (remoteVer > localVer)
                    {
                        info.UpdateAvailable = true;

                        // Find installer asset
                        var setupAsset = Array.Find(release.Assets, a =>
                            a.Name.Equals("OpenDualSetup.exe", StringComparison.OrdinalIgnoreCase) ||
                            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                        if (setupAsset != null)
                        {
                            info.DownloadUrl = setupAsset.BrowserDownloadUrl;
                            info.FileSize = setupAsset.Size;
                        }
                    }
                }
            }
            catch
            {
                // Network error or rate limiting; return info with UpdateAvailable = false
            }

            return info;
        }

        public async Task<string?> DownloadInstallerAsync(string downloadUrl, IProgress<int>? progress, CancellationToken ct = default)
        {
            try
            {
                string tempPath = Path.Combine(Path.GetTempPath(), "OpenDualSetup_Update.exe");
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }

                using var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int read;

                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read, ct);
                    totalRead += read;
                    if (totalBytes > 0 && progress != null)
                    {
                        int pct = (int)((totalRead * 100) / totalBytes);
                        progress.Report(pct);
                    }
                }

                return tempPath;
            }
            catch
            {
                return null;
            }
        }

        public static void LaunchInstallerAndExit(string installerPath, bool silent = false)
        {
            if (!File.Exists(installerPath)) return;

            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = silent ? "--silent" : "",
                UseShellExecute = true
            };

            Process.Start(psi);
            System.Windows.Application.Current?.Shutdown();
        }
    }
}
