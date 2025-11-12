using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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

    // IP/Port of your server console
    private string _serverIp = "127.0.0.1";
    private int _serverPort = 25569;
    private string _sptServerAddress = "http://127.0.0.1:6969";

    // store exe directory and config path
    private readonly string? _configPath;

    public MainWindow()
    {
        try
        {
            var exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!;
            _modsFolder = Path.Combine(exeDir, "BepInEx", "Plugins");
            _clientPath = Path.Combine(exeDir, "SPT", "SPT.Launcher.exe");

            // store exe directory and config path
            _configPath = Path.Combine(exeDir, "coffee_manager_server_config.json");
        }
        catch
        {
            MessageBox.Show("SPT.Launcher.exe not found. Please make sure to run from `SPT` root folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        InitializeComponent();

        // Load saved server config if present
        LoadConfig();

        Loaded += async (_, _) =>
        {
            if (!File.Exists(_clientPath))
            {
                MessageBox.Show("SPT.Launcher.exe not found. Please make sure to run from `SPT` root folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            await RefreshMods();
        };
    }

    private string BaseUrl => $"http://{_serverIp}:{_serverPort}";

    private async Task<List<ModEntry>> GetServerModsAsync()
    {
        using var client = new HttpClient();
        try
        {
            var response = await client.GetStringAsync($"{BaseUrl}/PluginVersions.json");
            var mods = JsonSerializer.Deserialize<List<ModEntry>>(response)!;
            return mods ?? new List<ModEntry>();
        }
        catch
        {
            return new List<ModEntry>();
        }
    }

    private List<ModEntry> GetLocalMods()
    {
        var mods = new List<ModEntry>();

        if (!Directory.Exists(_modsFolder))
            return mods;

        // Excluded mod names and folders (same as server)
        var excludedMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "spt-common", "spt-core", "spt-custom", "spt-debugging",
            "spt-reflection", "spt-singleplayer", "Fika.Core", "Fika.Headless"
        };

        var excludedModFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "spt", "fika"
        };

        var processedMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Folder-based mods
        foreach (var modFolder in Directory.GetDirectories(_modsFolder))
        {
            var folderName = Path.GetFileName(modFolder);
            if (excludedModFolders.Contains(folderName))
                continue;

            var dllFiles = Directory.GetFiles(modFolder, "*.dll", SearchOption.AllDirectories);
            if (dllFiles.Length == 0)
                continue;

            if (excludedMods.Contains(folderName) || processedMods.Contains(folderName))
                continue;

            var version = FileVersionInfo.GetVersionInfo(dllFiles[0]).FileVersion ?? "0";

            mods.Add(new ModEntry
            {
                Name = folderName,
                Version = version,
                FileName = folderName + ".zip",
                IsFolderMod = true
            });

            processedMods.Add(folderName);
        }

        // Single DLL mods
        foreach (var dll in Directory.GetFiles(_modsFolder, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var modName = Path.GetFileNameWithoutExtension(dll);

            if (excludedMods.Contains(modName) || processedMods.Contains(modName))
                continue;

            var version = FileVersionInfo.GetVersionInfo(dll).FileVersion ?? "0";

            mods.Add(new ModEntry
            {
                Name = modName,
                Version = version,
                FileName = modName + ".zip",
                IsFolderMod = false
            });

            processedMods.Add(modName);
        }

        return mods;
    }

    private async Task RefreshMods()
    {
        LaunchOrUpdateButton.IsEnabled = false;

        var serverMods = await GetServerModsAsync();
        var localMods = GetLocalMods();

        if (serverMods.Count == 0)
        {
            ServerStatusText.Text = "Server Offline";
            ServerStatusText.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }

        ServerStatusText.Text = "Server Online";
        ServerStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;

        var statusList = CompareMods(serverMods, localMods);
        ModListView.ItemsSource = statusList;

        bool upToDate = ModsMatch(serverMods, localMods);
        LaunchOrUpdateButton.Content = upToDate ? "Launch" : "Update";
        LaunchOrUpdateButton.IsEnabled = true;
    }

    private List<ModStatusEntry> CompareMods(List<ModEntry> serverMods, List<ModEntry> localMods)
    {
        var statusList = new List<ModStatusEntry>();

        // Create dictionaries for quick lookup
        var localDict = localMods.ToDictionary(m => m.Name, m => m, StringComparer.OrdinalIgnoreCase);
        var serverDict = serverMods.ToDictionary(m => m.Name, m => m, StringComparer.OrdinalIgnoreCase);

        // Check server mods against local mods
        foreach (var serverMod in serverMods)
        {
            localDict.TryGetValue(serverMod.Name, out var localMod);

            string status;
            if (localMod == null)
                status = "Not installed";
            else
                status = localMod.Version == serverMod.Version ? "Up to date" : "Update";

            statusList.Add(new ModStatusEntry
            {
                Name = serverMod.Name,
                LocalVersion = localMod?.Version ?? "-",
                ServerVersion = serverMod.Version,
                Status = status,
                IsFolderMod = serverMod.IsFolderMod
            });
        }

        // Check for local mods not on server
        foreach (var localMod in localMods)
        {
            if (!serverDict.ContainsKey(localMod.Name))
            {
                statusList.Add(new ModStatusEntry
                {
                    Name = localMod.Name,
                    LocalVersion = localMod.Version,
                    ServerVersion = "-",
                    Status = "Removed",
                    IsFolderMod = localMod.IsFolderMod
                });
            }
        }

        return statusList;
    }

    private bool ModsMatch(List<ModEntry> serverMods, List<ModEntry> localMods)
    {
        // Check if local and server mod counts match
        if (localMods.Count != serverMods.Count)
            return false;

        // Check if all server mods are present locally with matching versions
        foreach (var serverMod in serverMods)
        {
            var localMod = localMods.FirstOrDefault(m => m.Name == serverMod.Name);
            if (localMod == null || localMod.Version != serverMod.Version)
                return false;
        }
        return true;
    }

    private async void LaunchOrUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (LaunchOrUpdateButton.Content.ToString() == "Update")
        {
            LaunchOrUpdateButton.IsEnabled = false;
            StatusTextBlock.Text = "Updating mods...";
            var modsToUpdate = ((List<ModStatusEntry>)ModListView.ItemsSource)
                .Where(m => m.Status == "Update" || m.Status == "Not installed")
                .ToList();

            // Download and update mods
            var allUpdated = await DownloadAndUpdateMods(modsToUpdate);
            StatusTextBlock.Text = allUpdated ? "All mods updated" : "Some mods failed";

            // Remove mods marked as "Removed"
            var modsToRemove = ((List<ModStatusEntry>)ModListView.ItemsSource)
                .Where(m => m.Status == "Removed")
                .ToList();
            await RemoveMods(modsToRemove);

            LaunchOrUpdateButton.Content = "Launch";
            LaunchOrUpdateButton.IsEnabled = true;

            await RefreshMods();
        }
        else
        {
            LaunchTheGame();
        }
    }

    private async Task RemoveMods(List<ModStatusEntry> modsToRemove)
    {
        try
        {
            await Task.Run(() =>
            {
                foreach (var mod in modsToRemove)
                {
                    if (mod.IsFolderMod)
                    {
                        var modFolder = Path.Combine(_modsFolder!, mod.Name);
                        if (Directory.Exists(modFolder))
                            Directory.Delete(modFolder, true);
                    }
                    else
                    {
                        var dllFile = Path.Combine(_modsFolder!, mod.Name + ".dll");
                        if (File.Exists(dllFile))
                            File.Delete(dllFile);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to remove mods: {ex.Message}");
            MessageBox.Show($"Failed to remove some mods: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<bool> DownloadAndUpdateMods(List<ModStatusEntry> mods)
    {
        bool success = true;
        using var client = new HttpClient();

        foreach (var mod in mods)
        {
            try
            {
                if (!Directory.Exists(_modsFolder))
                    return false;

                // Show progress: downloading
                mod.Status = "Downloading...";
                ModListView.Items.Refresh();
                await Task.Delay(50); // let UI update

                // Fetch mod info from server
                var serverMods = await GetServerModsAsync();
                var modInfo = serverMods.FirstOrDefault(m => m.Name == mod.Name);
                if (modInfo == null)
                {
                    Debug.WriteLine($"Mod info not found on server: {mod.Name}");
                    continue;
                }

                var url = $"{BaseUrl}/mods/{modInfo.Name}";
                var data = await client.GetByteArrayAsync(url);

                // Save temp zip
                var tempZip = Path.Combine(Path.GetTempPath(), modInfo.FileName);
                await File.WriteAllBytesAsync(tempZip, data);

                // Extracting
                mod.Status = "Extracting...";
                ModListView.Items.Refresh();
                await Task.Delay(50);

                // Extract to temp folder
                var extractPath = Path.Combine(Path.GetTempPath(), $"{mod.Name}_extract_{Guid.NewGuid():N}");
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);

                ZipFile.ExtractToDirectory(tempZip, extractPath);
                File.Delete(tempZip);

                // Ensure Plugins folder exists
                Directory.CreateDirectory(_modsFolder);

                if (modInfo.IsFolderMod)
                {
                    // Detect correct root folder after extraction
                    string srcFolder = extractPath;
                    var subDirs = Directory.GetDirectories(extractPath);
                    if (subDirs.Length == 1 &&
                        File.Exists(Path.Combine(subDirs[0], $"{mod.Name}.dll")))
                    {
                        srcFolder = subDirs[0];
                    }

                    var destFolder = Path.Combine(_modsFolder, mod.Name);
                    if (Directory.Exists(destFolder))
                        Directory.Delete(destFolder, true);

                    mod.Status = "Installing...";
                    ModListView.Items.Refresh();
                    await Task.Delay(50);

                    CopyDirectory(srcFolder, destFolder);
                }
                else
                {
                    var dllFile = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories).FirstOrDefault();
                    if (dllFile != null)
                    {
                        mod.Status = "Installing...";
                        ModListView.Items.Refresh();
                        await Task.Delay(50);

                        var destFile = Path.Combine(_modsFolder, Path.GetFileName(dllFile));
                        if (File.Exists(destFile))
                            File.Delete(destFile);
                        File.Copy(dllFile, destFile, overwrite: true);
                    }
                }

                // Cleanup
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);

                // Done
                mod.Status = "Up to date";
                ModListView.Items.Refresh();
                await Task.Delay(50);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update {mod.Name}: {ex.Message}");
                success = false;

                mod.Status = "Failed";
                ModListView.Items.Refresh();

                MessageBox.Show($"Failed to update mod {mod.Name}: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        return success;
    }

    /// <summary>
    /// Recursively copies a directory and all contents.
    /// </summary>
    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }

    private void LaunchTheGame()
    {
        if (!File.Exists(_clientPath))
        {
            MessageBox.Show("SPT.Launcher.exe not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _clientPath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(_clientPath)
        });
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
        CheckUpdatesButton.Content = "Check for mod updates";
        CheckUpdatesButton.IsEnabled = true;
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

    private void CheckServerButton_Click(object sender, RoutedEventArgs e)
    {
        // Set status to checking
        ServerStatusText.Text = "Checking...";
        ServerStatusText.Foreground = System.Windows.Media.Brushes.Gray;
        // Refresh mods to check server status
        _ = RefreshMods();
    }

    private void ConfigureServerButton_Click(object sender, RoutedEventArgs e)
    {
        var serverConfigWindow = new ServerConfigWindow(_serverIp, _serverPort);
        if (serverConfigWindow.ShowDialog() == true)
        {
            _serverIp = serverConfigWindow.ServerIp;
            _serverPort = serverConfigWindow.ServerPort;

            // Save updated config to exe folder
            SaveConfig();

            _ = RefreshMods();
        }
    }

    // new: load config from file
    private void LoadConfig()
    {
        try
        {
            if (string.IsNullOrEmpty(_configPath) || !File.Exists(_configPath))
                return;

            var json = File.ReadAllText(_configPath);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json);
            if (cfg != null)
            {
                if (!string.IsNullOrWhiteSpace(cfg.ServerIp))
                    _serverIp = cfg.ServerIp;
                if (cfg.ServerPort > 0)
                    _serverPort = cfg.ServerPort;
                if (!string.IsNullOrWhiteSpace(cfg.SptServerAddress))
                    _sptServerAddress = cfg.SptServerAddress;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load config: {ex.Message}");
        }
    }

    // new: save config to file
    private void SaveConfig()
    {
        try
        {
            if (string.IsNullOrEmpty(_configPath))
                return;

            var cfg = new AppConfig { ServerIp = _serverIp, ServerPort = _serverPort, SptServerAddress = _sptServerAddress};
            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save config: {ex.Message}");
        }
    }
}

// new: simple config DTO
public class AppConfig
{
    public string? ServerIp { get; set; }
    public int ServerPort { get; set; }
    public string? SptServerAddress { get; set; }
}

public class ModEntry
{
    public required string Name { get; set; }
    public required string Version { get; set; }
    public required string FileName { get; set; }       // Name of the zip file
    public required bool IsFolderMod { get; set; }     // true if folder-based mod
}

public class ModStatusEntry
{
    public required string Name { get; set; }
    public required string LocalVersion { get; set; }
    public required string ServerVersion { get; set; }
    public string? Status { get; set; }
    public required bool IsFolderMod { get; set; }
}
