using System.Text.Json.Serialization;

namespace SPTCoffeeModManager;

using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

public class UpdaterService
{
    private const string UpdateJsonUrl = "https://raw.githubusercontent.com/veskeli/SPTCoffeeModManager/main/latest.json";

    public static async Task CheckAndUpdateAsync()
    {
        using var http = new HttpClient();
        var json = await http.GetStringAsync(UpdateJsonUrl);
        var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(json);

        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
        var latestVersion = new Version(updateInfo.Version);

        if (latestVersion <= currentVersion) return;

        // Download update zip
        var tempZip = Path.Combine(Path.GetTempPath(), "ModManagerUpdate.zip");
        using var stream = await http.GetStreamAsync(updateInfo.Url);
        using var file = File.Create(tempZip);
        await stream.CopyToAsync(file);

        // Extract and overwrite
        string baseDir = AppContext.BaseDirectory;
        ZipFile.ExtractToDirectory(tempZip, baseDir, overwriteFiles: true);

        // Restart app
        string exePath = Path.Combine(baseDir, Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName));
        Process.Start(exePath);
        Environment.Exit(0);
    }

    public static async Task<bool> IsUpdateAvailableAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        Console.WriteLine("Checking for updates...");
        try
        {
            Console.WriteLine("Requesting update JSON...");
            var json = await http.GetStringAsync(UpdateJsonUrl);
            Console.WriteLine("Received JSON: " + json);

            var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(json);
            Console.WriteLine("Deserialized update info.");

            if (string.IsNullOrWhiteSpace(updateInfo?.Version))
            {
                Console.WriteLine("Update info is missing the version field.");
                return false;
            }

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            var latestVersion = new Version(updateInfo.Version);

            Console.WriteLine($"Current: {currentVersion}, Latest: {latestVersion}");
            Console.WriteLine($"Is update available? {latestVersion > currentVersion}");
            return latestVersion > currentVersion;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Update check failed: {ex.Message}");
            return false;
        }
    }

    private class UpdateInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; }
        [JsonPropertyName("url")]
        public string Url { get; set; }
    }
}