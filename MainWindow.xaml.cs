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

    public MainWindow()
    {
        try
        {
            var exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!;
            _modsFolder = Path.Combine(exeDir, "user", "mods");
            _clientPath = Path.Combine(exeDir, "SPT", "SPT.Launcher.exe");
        }
        catch
        {
            MessageBox.Show("SPT.Launcher.exe not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        InitializeComponent();
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
            var response = await client.GetStringAsync($"{BaseUrl}/mods");
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

        foreach (var dll in Directory.GetFiles(_modsFolder, "*.dll"))
        {
            try
            {
                var fileVersion = FileVersionInfo.GetVersionInfo(dll).FileVersion ?? "0";
                mods.Add(new ModEntry
                {
                    Name = Path.GetFileNameWithoutExtension(dll),
                    Version = fileVersion
                });
            }
            catch { }
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
                var url = $"{BaseUrl}/mods/{mod.Name}";
                var data = await client.GetByteArrayAsync(url);

                // Save temp zip
                var tempZip = Path.Combine(Path.GetTempPath(), $"{mod.Name}.zip");
                await File.WriteAllBytesAsync(tempZip, data);

                // Extract
                var extractPath = Path.Combine(_modsFolder, mod.Name);
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);

                ZipFile.ExtractToDirectory(tempZip, extractPath);
                File.Delete(tempZip);

                mod.Status = "Up to date";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update {mod.Name}: {ex.Message}");
                success = false;
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
        var serverConfigWindow = new ServerConfigWindow(ServerIP, ServerPort);
        if (serverConfigWindow.ShowDialog() == true)
        {
            ServerIP = serverConfigWindow.ServerIP;
            ServerPort = serverConfigWindow.ServerPort;
            _ = RefreshMods();
        }
    }
}

public class ModEntry
{
    public required string Name { get; set; }
    public required string Version { get; set; }
}

public class ModStatusEntry
{
    public required string Name { get; set; }
    public required string LocalVersion { get; set; }
    public required string ServerVersion { get; set; }
    public string? Status { get; set; }
}

