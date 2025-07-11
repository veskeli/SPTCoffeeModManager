using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace SPTCoffeeModManager;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
{
    private readonly string? _modsFolder;
    private readonly string? _clientPath;

    public MainWindow()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule.FileName;
            var exeDir = Path.GetDirectoryName(exePath)!;
            _modsFolder = Path.Combine(exeDir, "user", "mods");
            _clientPath = Path.Combine(exeDir, "SPT.Launcher.exe");
        }
        catch
        {
            MessageBox.Show("SPT.Launcher.exe not found. Please ensure it is in the same directory as this application.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        InitializeComponent();
        Loaded += async (_, _) =>
        {
            // Check for updates
            bool isUpdateAvailable = await UpdaterService.IsUpdateAvailableAsync();
            if (isUpdateAvailable)
            {
                var result = MessageBox.Show(
                    "An update is available for SPT Coffee Mod Manager. Please update to the latest version.",
                    "Update Available",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Information);

                // Handle user response
                if (result == MessageBoxResult.Yes)
                {
                    // Update the application
                    try
                    {
                        await UpdaterService.CheckAndUpdateAsync();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to update: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    return;
                }
                if (result == MessageBoxResult.No)
                {
                    // Close the application
                    Close();
                    return;
                }
            }

            // Check if the client executable exists
            if (!File.Exists(_clientPath))
            {
                MessageBox.Show("SPT.Launcher.exe not found. Please ensure it is in the same directory as this application.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            await RefreshMods();
        };
    }

    private static readonly string[] ServerUrls =
    [
        "https://sptmodmanager.veskeli.org/mods.json",          // Public domain
        "http://192.168.1.109:25569/mods.json",                 // LAN address (change this)
        "http://localhost:25569/mods.json"                      // Same machine
    ];

    private async Task<List<ModEntry>> GetServerModsAsync()
    {
        using var client = new HttpClient();

        foreach (var url in ServerUrls)
        {
            try
            {
                var response = await client.GetStringAsync(url);
                var mods = JsonSerializer.Deserialize<List<ModEntry>>(response, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (mods != null && mods.Any())
                    return mods;
            }
            catch(Exception ex)
            {
                Debug.WriteLine($"Failed to connect to {url}: {ex.Message}");
            }
        }

        return new List<ModEntry>();
    }

    private async Task RefreshMods()
    {
        LaunchOrUpdateButton.IsEnabled = false;
        var serverMods = await GetServerModsAsync();
        var localMods = GetLocalMods();

        if (serverMods.Count == 0)
        {
            ServerStatusText.Text = "Server Offline or Unreachable";
            ServerStatusText.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }

        // Set button state based on mod match
        ServerStatusText.Text = "Server Online";
        ServerStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;

        var statusList = CompareMods(serverMods, localMods);
        ModListView.ItemsSource = statusList;

        bool upToDate = ModsMatch(serverMods, localMods);
        LaunchOrUpdateButton.Content = upToDate ? "Launch" : "Update";
        LaunchOrUpdateButton.IsEnabled = true;
    }

    private List<ModEntry> GetLocalMods()
    {
        var result = new List<ModEntry>();

        if (!Directory.Exists(_modsFolder)) return result;

        foreach (var dir in Directory.GetDirectories(_modsFolder))
        {
            var packagePath = Path.Combine(dir, "package.json");
            if (File.Exists(packagePath))
            {
                try
                {
                    var json = File.ReadAllText(packagePath);
                    using var doc = JsonDocument.Parse(json);
                    var name = doc.RootElement.GetProperty("name").GetString();
                    var version = doc.RootElement.GetProperty("version").GetString();
                    var folderName = Path.GetFileName(dir);

                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(version))
                    {
                        result.Add(new ModEntry { Name = name, Version = version, FolderName = folderName });
                    }
                }
                catch
                {
                    Console.WriteLine($"Failed to parse package.json in {dir}");
                }
            }
        }
        return result;
    }

    private bool ModsMatch(List<ModEntry> serverMods, List<ModEntry> localMods)
    {
        // Check if all server mods exist locally and are up to date
        foreach (var serverMod in serverMods)
        {
            var match = localMods.FirstOrDefault(m => m.Name == serverMod.Name);
            if (match == null || match.Version != serverMod.Version)
                return false;
        }

        // Check if there are any local mods not present on the server (should be removed)
        foreach (var localMod in localMods)
        {
            if (serverMods.All(m => m.Name != localMod.Name))
                return false;
        }

        return true;
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = "Checking...";
        try
        {
            await RefreshMods();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error checking for updates: {ex.Message}");
        }
        await Task.Delay(1000); // 1 second cooldown
        CheckUpdatesButton.Content = "Check for Updates";
        CheckUpdatesButton.IsEnabled = true;
    }

    private async void LaunchOrUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (LaunchOrUpdateButton.Content.ToString() == "Update")
        {
            LaunchOrUpdateButton.IsEnabled = false;
            StatusTextBlock.Text = "Downloading updates...";

            var modsToDownload = ((List<ModStatusEntry>)ModListView.ItemsSource)
                .Where(mod => mod.Status == "Update" || mod.Status == "Not downloaded")
                .ToList();

            var modsToRemove = ((List<ModStatusEntry>)ModListView.ItemsSource)
                .Where(mod => mod.Status == "Removed")
                .ToList();

            var allModsUpdated = await DownloadAndUpdateMods(modsToDownload);

            await RemoveOldModsAsync(modsToRemove);

            StatusTextBlock.Text = allModsUpdated ? "All mods updated successfully." : "Some mods failed to update. Check logs for details.";
            StatusTextBlock.Foreground = allModsUpdated ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.Red;

            LaunchOrUpdateButton.Content = allModsUpdated ? "Play" : "Update";
            LaunchOrUpdateButton.IsEnabled = true;

            // Refresh mod list
            var serverMods = await GetServerModsAsync();
            var localMods = GetLocalMods();
            var statusList = CompareMods(serverMods, localMods);
            ModListView.ItemsSource = statusList;
        }
        else
        {
            LaunchTheGame();
        }
    }

    private async Task RemoveOldModsAsync(List<ModStatusEntry> modsToRemove)
    {
        foreach (var modFolderName in modsToRemove)
        {
            try
            {
                // Use the folder name directly for removal
                if (!string.IsNullOrEmpty(modFolderName.FolderName) && !string.IsNullOrWhiteSpace(modFolderName.FolderName) && _modsFolder != null)
                {
                    string modPath = Path.Combine(_modsFolder, modFolderName.FolderName);
                    if (Directory.Exists(modPath))
                    {
                        await Task.Run(() => Directory.Delete(modPath, true));
                        Debug.WriteLine($"Removed old mod folder: {modFolderName}");
                    }
                    else
                    {
                        Debug.WriteLine($"Mod folder {modFolderName} not found for removal.");
                    }
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Failed to remove {modFolderName}: {ex.Message}";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                Debug.WriteLine($"Error removing mod folder {modFolderName}: {ex.Message}");
            }
        }
    }

    private void LaunchTheGame()
    {
        // Check if the client executable exists
        if (!File.Exists(_clientPath))
        {
            MessageBox.Show("SPT.Launcher.exe not found. Please ensure it is in the same directory as this application.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _clientPath,
            WorkingDirectory = Path.GetDirectoryName(_clientPath),
            UseShellExecute = true
        });
    }

    // Download and update mods based on the provided list
    // Also deletes old mods if found (Ones not in the server list)
    private async Task<bool> DownloadAndUpdateMods(List<ModStatusEntry> modsToDownload)
    {
        bool allModsUpdated = true;
        foreach (var mod in modsToDownload)
        {
            try
            {
                if (string.IsNullOrEmpty(mod.DownloadUrl))
                    throw new Exception($"No download URL for mod {mod.Name}");

                StatusTextBlock.Text = $"Downloading {mod.Name}...";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.LightBlue;

                string tempPath = Path.Combine(Path.GetTempPath(), $"{mod.Name}.zip");
                using var httpClient = new HttpClient();

                var data = await httpClient.GetByteArrayAsync(mod.DownloadUrl);
                File.WriteAllBytes(tempPath, data);

                StatusTextBlock.Text = $"Extracting {mod.Name}...";

                // Extract to temp
                string extractTempPath = Path.Combine(Path.GetTempPath(), $"mod_extract_{Guid.NewGuid()}");
                Directory.CreateDirectory(extractTempPath);
                System.IO.Compression.ZipFile.ExtractToDirectory(tempPath, extractTempPath);
                File.Delete(tempPath);

                // Detect actual mod folder inside extracted data
                string correctModPath = string.Empty;

                // Case 1: root contains package.json
                if (File.Exists(Path.Combine(extractTempPath, "package.json")))
                {
                    correctModPath = extractTempPath;
                }
                else
                {
                    var potentialDirs = Directory.GetDirectories(extractTempPath, "*", SearchOption.AllDirectories);
                    foreach (var dir in potentialDirs)
                    {
                        if (File.Exists(Path.Combine(dir, "package.json")))
                        {
                            correctModPath = dir;
                            break;
                        }
                    }
                }

                if (correctModPath == null)
                    throw new Exception("Mod archive did not contain a valid mod folder with package.json");

                // Decide target folder (use FolderName if provided)
                string targetFolderName = !string.IsNullOrEmpty(mod.FolderName) ? mod.FolderName : mod.Name;
                string targetPath = Path.Combine(_modsFolder, targetFolderName);
                string oldConfigPath = Path.Combine(targetPath, "config");
                string tempConfigPath = Path.Combine(Path.GetTempPath(), $"{mod.Name}_config_backup");

                if (Directory.Exists(oldConfigPath))
                {
                    if (Directory.Exists(tempConfigPath))
                        Directory.Delete(tempConfigPath, true);
                    MoveDirectory(oldConfigPath, tempConfigPath);
                }

                if (Directory.Exists(targetPath))
                    Directory.Delete(targetPath, true);

                Directory.CreateDirectory(targetPath);

                // Copy files to final mod folder
                foreach (var filePath in Directory.GetFiles(correctModPath, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(correctModPath, filePath);
                    var destinationPath = Path.Combine(targetPath, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    File.Copy(filePath, destinationPath, overwrite: true);
                }

                // Restore config if it was moved
                if (Directory.Exists(tempConfigPath))
                {
                    MoveDirectory(tempConfigPath, Path.Combine(targetPath, "config"));
                }

                // Clean up temp files
                Directory.Delete(extractTempPath, true);

                // After successful update:
                mod.Status = "Up to date";
                mod.LocalVersion = mod.ServerVersion;
                // Refresh the ListView to reflect the change
                ModListView.Items.Refresh();
            }
            catch (Exception ex)
            {
                allModsUpdated = false;
                StatusTextBlock.Text = $"Failed to download {mod.Name}: {ex.Message}";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                Debug.WriteLine($"Error updating mod {mod.Name}: {ex.Message}");
                // Show box
                MessageBox.Show($"Failed to update {mod.Name}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        return allModsUpdated;
    }

    private List<ModStatusEntry> CompareMods(List<ModEntry> serverMods, List<ModEntry> localMods)
    {
        var statusList = new List<ModStatusEntry>();

        // Filter out mods with null or empty names to avoid exception
        var serverDict = serverMods
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .ToDictionary(m => m.Name, m => m);

        var localDict = localMods
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .ToDictionary(m => m.Name, m => m);

        // Mods that exist on server
        foreach (var serverMod in serverMods)
        {
            if (string.IsNullOrWhiteSpace(serverMod.Name))
                continue;

            if (localDict.TryGetValue(serverMod.Name, out var localMod))
            {
                var status = localMod.Version == serverMod.Version ? "Up to date" : "Update";
                statusList.Add(new ModStatusEntry
                {
                    Name = serverMod.Name,
                    LocalVersion = localMod.Version,
                    ServerVersion = serverMod.Version,
                    Status = status,
                    DownloadUrl = serverMod.DownloadUrl ?? String.Empty,
                    FolderName = localMod.FolderName // Preserve folder name for manual mods
                });
            }
            else
            {
                statusList.Add(new ModStatusEntry
                {
                    Name = serverMod.Name,
                    LocalVersion = "-",
                    ServerVersion = serverMod.Version,
                    Status = "Not downloaded",
                    DownloadUrl = serverMod.DownloadUrl ?? String.Empty,
                    FolderName = String.Empty // No folder name for not downloaded mods
                });
            }
        }

        // Mods that exist locally but not on server (removed)
        foreach (var localMod in localMods)
        {
            if (string.IsNullOrWhiteSpace(localMod.Name))
                continue;

            if (!serverDict.ContainsKey(localMod.Name))
            {
                statusList.Add(new ModStatusEntry
                {
                    Name = localMod.Name,
                    LocalVersion = localMod.Version,
                    ServerVersion = "-",
                    Status = "Removed",
                    DownloadUrl = String.Empty, // No download URL for removed mods
                    FolderName = localMod.FolderName // Preserve folder name for manual mods
                });
            }
        }

        return statusList;
    }

    // Make the window draggable
    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            this.DragMove();
    }

    // Minimize the window
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }

    // Close the window
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    public static void MoveDirectory(string sourceDir, string destDir)
    {
        if (Path.GetPathRoot(sourceDir) == Path.GetPathRoot(destDir))
        {
            Directory.Move(sourceDir, destDir);
        }
        else
        {
            CopyDirectory(sourceDir, destDir);
            Directory.Delete(sourceDir, true);
        }
    }

    public static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }
}

public class ModEntry
{
    public required string Name { get; set; }
    public required string Version { get; set; }
    public string? DownloadUrl { get; set; }
    public string? FolderName { get; set; } // If the user has manually installed mods, we need to know the folder name to delete it later
}

public class ModStatusEntry
{
    public required string Name { get; set; }
    public required string LocalVersion { get; set; }
    public required string ServerVersion { get; set; }
    public string? Status { get; set; }
    public string? DownloadUrl { get; set; }
    public string? FolderName {get; set; } // If the user has manually installed mods, we need to know the folder name to delete it later
}

