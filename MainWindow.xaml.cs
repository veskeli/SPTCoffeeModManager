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
    private readonly string _modsFolder;
    private readonly string _clientPath;

    // IP/Port of your server console
    private string ServerIP = "127.0.0.1";
    private int ServerPort = 25569;

    // store exe directory and config path
    private readonly string _exeDir;
    private readonly string _configPath;

    public MainWindow()
    {
        try
        {
            var exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!;
            _modsFolder = Path.Combine(exeDir, "BepInEx", "Plugins");
            _clientPath = Path.Combine(exeDir, "SPT", "SPT.Launcher.exe");

            // store exe directory and config path
            _exeDir = exeDir;
            _configPath = Path.Combine(_exeDir, "coffee_manager_server_config.json");
        }
        catch
        {
            MessageBox.Show("SPT.Launcher.exe not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show("SPT.Launcher.exe not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            await RefreshMods();
        };
    }

    private string BaseUrl => $"http://{ServerIP}:{ServerPort}";

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
        if (!Directory.Exists(_modsFolder)) return mods;

        foreach (var dir in Directory.GetDirectories(_modsFolder))
        {
            var dllFiles = Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly);
            if (dllFiles.Length == 0) continue;

            var version = FileVersionInfo.GetVersionInfo(dllFiles[0]).FileVersion ?? "0";
            var name = Path.GetFileName(dir);

            mods.Add(new ModEntry
            {
                Name = name,
                Version = version,
                FileName = name + ".zip",
                IsFolderMod = true
            });
        }

        // Add DLLs directly in Plugins folder
        foreach (var dll in Directory.GetFiles(_modsFolder, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var version = FileVersionInfo.GetVersionInfo(dll).FileVersion ?? "0";
            var name = Path.GetFileNameWithoutExtension(dll);

            mods.Add(new ModEntry
            {
                Name = name,
                Version = version,
                FileName = name + ".zip",
                IsFolderMod = false
            });
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

        var localDict = localMods.ToDictionary(m => m.Name, m => m);

        foreach (var serverMod in serverMods)
        {
            localDict.TryGetValue(serverMod.Name, out var localMod);
            var status = (localMod != null && localMod.Version == serverMod.Version) ? "Up to date" : "Update";

            statusList.Add(new ModStatusEntry
            {
                Name = serverMod.Name,
                LocalVersion = localMod?.Version ?? "-",
                ServerVersion = serverMod.Version,
                Status = status
            });
        }

        return statusList;
    }

    private bool ModsMatch(List<ModEntry> serverMods, List<ModEntry> localMods)
    {
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
                .Where(m => m.Status == "Update")
                .ToList();

            var allUpdated = await DownloadAndUpdateMods(modsToUpdate);
            StatusTextBlock.Text = allUpdated ? "All mods updated" : "Some mods failed";
            LaunchOrUpdateButton.Content = "Launch";
            LaunchOrUpdateButton.IsEnabled = true;

            await RefreshMods();
        }
        else
        {
            LaunchTheGame();
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
                // Get PluginVersions.json to know FileName and IsFolderMod
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

                // Extract to temp folder
                var extractPath = Path.Combine(Path.GetTempPath(), $"{mod.Name}_extract_{Guid.NewGuid():N}");
                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                ZipFile.ExtractToDirectory(tempZip, extractPath);
                File.Delete(tempZip);

                // Determine Plugins folder
                Directory.CreateDirectory(_modsFolder);

                if (modInfo.IsFolderMod)
                {
                    try
                    {
                        // Move extracted folder to Plugins/<FolderName>
                        var srcFolder = Directory.GetDirectories(extractPath).FirstOrDefault() ?? extractPath;
                        var destFolder = Path.Combine(_modsFolder, mod.Name);
                        if (Directory.Exists(destFolder)) Directory.Delete(destFolder, true);
                        Directory.Move(srcFolder, destFolder);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to move folder mod {mod.Name}: {ex.Message}");
                        MessageBox.Show($"Failed to update folder mod {mod.Name}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        throw;
                    }
                }
                else
                {
                    // Single DLL: move directly into Plugins
                    var dllFile = Directory.GetFiles(extractPath, "*.dll", SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (dllFile != null)
                    {
                        var destFile = Path.Combine(_modsFolder, Path.GetFileName(dllFile));
                        if (File.Exists(destFile)) File.Delete(destFile);
                        File.Move(dllFile, destFile);
                    }
                }

                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);

                mod.Status = "Up to date";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update {mod.Name}: {ex.Message}");
                success = false;
                MessageBox.Show($"Failed to update mod {mod.Name}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        ModListView.Items.Refresh();
        return success;
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
        CheckUpdatesButton.Content = "Check for Updates";
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
        ServerStatusText.Foreground = System.Windows.Media.Brushes.Yellow;
        // Refresh mods to check server status
        _ = RefreshMods();
    }

    private void ConfigureServerButton_Click(object sender, RoutedEventArgs e)
    {
        var serverConfigWindow = new ServerConfigWindow(ServerIP, ServerPort);
        if (serverConfigWindow.ShowDialog() == true)
        {
            ServerIP = serverConfigWindow.ServerIP;
            ServerPort = serverConfigWindow.ServerPort;

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
                if (!string.IsNullOrWhiteSpace(cfg.ServerIP))
                    ServerIP = cfg.ServerIP;
                if (cfg.ServerPort > 0)
                    ServerPort = cfg.ServerPort;
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

            var cfg = new AppConfig { ServerIP = ServerIP, ServerPort = ServerPort };
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
    public string? ServerIP { get; set; }
    public int ServerPort { get; set; }
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
}
