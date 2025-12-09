using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.AspNetCore.SignalR.Client;

namespace SPTCoffeeModManager;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
{
    private readonly string? _modsFolder;
    private readonly string? _pluginsConfigFolder;
    private readonly string? _clientPath;
    private string _basePath = "";

    // IP/Port of your server console
    private string _serverIp = "127.0.0.1";
    private int _serverPort = 25569;
    private string _sptServerAddress = "http://127.0.0.1:6969";
    private string? _secret = "";

    // store exe directory and config path
    private readonly string? _configPath;

    private HubConnection? _hubConnection;

    // Excluded mod names and folders (fetched from server as well)
    private List<string> _excludedMods = new List<string>
    {
        "spt-common", "spt-core", "spt-custom", "spt-debugging",
        "spt-reflection", "spt-singleplayer", "Fika.Core", "Fika.Headless"
    };
    private List<string> _excludedModFolders = new List<string>
    {
        "spt", "fika"
    };
    private List<string> _excludedConfigs = new List<string>
    {
        "BepInEx.cfg", "com.bepis.bepinex.configurationmanager.cfg",
        "com.fika.core.cfg", "com.fika.headless.cfg"
    };

    public MainWindow()
    {
        try
        {
            var exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!;
            _modsFolder = Path.Combine(exeDir, "BepInEx", "plugins");
            _pluginsConfigFolder = Path.Combine(exeDir, "BepInEx", "config");
            _clientPath = Path.Combine(exeDir, "SPT", "SPT.Launcher.exe");

            // store exe directory and config path
            _configPath = Path.Combine(exeDir, "coffee_manager_server_config.json");

            _basePath = exeDir;
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

            await RefreshPluginConfigs();
            await RefreshMods();

            // if secret is set, validate it on server and update admin status
            if (!string.IsNullOrWhiteSpace(_secret))
            {
                CheckIfSecretIsValid(_secret);
            }

            // Fetch launcher settings
            await FetchLauncherSettings();

            // Initialize server status check timer
            InitializeServerCheckTimer();

            // Check server status on load
            await CheckServerStatus();

            // Check SPT version compatibility
            await CheckSptVersion();

            // Start server notifier
            await InitializeSignalR();
        };
    }

    private async Task InitializeSignalR()
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{BaseUrl}/hub")
            .WithAutomaticReconnect()
            .Build();

        // On various server notifications
        _hubConnection.On<string>("ServerRestarting", (message) =>
        {
            Dispatcher.Invoke(() =>
            {
                Console.WriteLine("[SERVER NOTICE] " + message);
                ServerStatusText.Text = "Restarting...";
                ServerStatusText.Foreground = System.Windows.Media.Brushes.Orange;
            });
        });

        _hubConnection.On<string>("SptServerOffline", (message) =>
        {
            Dispatcher.Invoke(() =>
            {
                Console.WriteLine("[SPT NOTICE] " + message);
                SptServerStatusText.Text = "Offline";
                SptServerStatusText.Foreground = System.Windows.Media.Brushes.Red;
            });
        });

        _hubConnection.On<string>("SptServerOnline", (message) =>
        {
            Dispatcher.Invoke(() =>
            {
                Console.WriteLine("[SPT NOTICE] " + message);
                SptServerStatusText.Text = "Online";
                SptServerStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            });
        });

        _hubConnection.On<string>("SptServerRestarting", (message) =>
        {
            Dispatcher.Invoke(() =>
            {
                Console.WriteLine("[SPT NOTICE] " + message);
                SptServerStatusText.Text = "Restarting...";
                SptServerStatusText.Foreground = System.Windows.Media.Brushes.Orange;
            });
        });

        _hubConnection.On<string>("SptServerUpdating", (message) =>
        {
            Dispatcher.Invoke(() =>
            {
                Console.WriteLine("[SPT NOTICE] " + message);
                SptServerStatusText.Text = "Updating...";
                SptServerStatusText.Foreground = System.Windows.Media.Brushes.Aqua;
            });
        });

        _hubConnection.On<string>("HeadlessOffline", (message) =>
        {
            Dispatcher.Invoke(() =>
            {
                Console.WriteLine("[HEADLESS NOTICE] " + message);
                HeadlessStatusText.Text = "Offline";
                HeadlessStatusText.Foreground = System.Windows.Media.Brushes.Red;
            });
        });

        _hubConnection.On<string>("HeadlessOnline", (message) =>
        {
            Dispatcher.Invoke(() =>
            {
                Console.WriteLine("[HEADLESS NOTICE] " + message);
                HeadlessStatusText.Text = "Online";
                HeadlessStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            });
        });

        // On headless restarted
        _hubConnection.On<string>("HeadlessRestarted", (message) =>
        {
            Dispatcher.Invoke(() =>
            {
                Console.WriteLine("[SERVER NOTICE] " + message);
                HeadlessStatusText.Text = "Restarting...";
                HeadlessStatusText.Foreground = System.Windows.Media.Brushes.Orange;
            });
        });

        try
        {
            await _hubConnection.StartAsync();
            Dispatcher.Invoke(() => Console.WriteLine("Connected to server notifications"));
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => MessageBox.Show("SignalR connection failed:\n" + ex.Message));
        }
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
        var excludedMods = new HashSet<string>(_excludedMods, StringComparer.OrdinalIgnoreCase);
        var excludedModFolders = new HashSet<string>(_excludedModFolders, StringComparer.OrdinalIgnoreCase);

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

            // Don't process excluded or already processed mods (Some mods may contain e.g. Fika combability DLLs)
            //if (excludedMods.Contains(folderName) || processedMods.Contains(folderName))
                //continue;

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

    private async Task CheckSptVersion()
    {
        try
        {
            // Load current spt version
            if (_modsFolder != null)
            {
                using var client = new HttpClient();
                var response = await client.GetAsync($"{BaseUrl}/spt/version");
                var bNotSuccessful = true;

                var sptCoreDll = Path.Combine(_modsFolder, "spt","spt-core.dll");
                if (File.Exists(sptCoreDll))
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(sptCoreDll);
                    CurrentSptVersionText.Text = $"{versionInfo.FileVersion}";
                    CurrentSptVersionText.Foreground = System.Windows.Media.Brushes.Aqua;
                    bNotSuccessful = false;
                }

                if (response.IsSuccessStatusCode)
                {
                    string verText = "0.0.0.0";
                    try
                    {
                        var responseText = await response.Content.ReadAsStringAsync();
                        // Server returns a JSON string like "1.2.3.4"
                        verText = JsonSerializer.Deserialize<string>(responseText) ?? responseText.Trim().Trim('"');
                    }
                    catch
                    {
                        verText = "0.0.0.0";
                    }

                    if (!Version.TryParse(verText, out Version? version))
                    {
                        version = new Version(0, 0, 0, 0);
                    }

                    // Check if the server is newer than local
                    var localSptVersion = new Version(CurrentSptVersionText.Text);
                    if (version > localSptVersion)
                    {
                        CurrentSptVersionText.Text = $"{localSptVersion} (Outdated)";
                        CurrentSptVersionText.Foreground = System.Windows.Media.Brushes.Red;

                        // Set play button to update
                        LaunchOrUpdateButton.Content = "Update SPT";
                        var updateSpt = MessageBox.Show("A new SPT version is required. Would you like to update?", "Update Required", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (updateSpt == MessageBoxResult.Yes)
                        {
                            SptNeedsUpdate();
                        }
                    }
                    else
                    {
                        CurrentSptVersionText.Foreground = System.Windows.Media.Brushes.LightGreen;
                    }

                    bNotSuccessful = false;
                }

                if(bNotSuccessful)
                {
                    CurrentSptVersionText.Text = "Unknown";
                    CurrentSptVersionText.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private void SptNeedsUpdate()
    {
        // Set play button to update
        LaunchOrUpdateButton.Content = "Update SPT";

        // Don't open multiple updater windows
        foreach (Window w in Application.Current.Windows)
        {
            if (w is SPTUpdater)
                return;
        }

        var updateWindow = new SPTUpdater(BaseUrl, _basePath)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        // Hide the main window while updater is visible and show it again when updater closes
        this.Hide();
        updateWindow.Closed += async (_, _) =>
        {
            this.Show();
            // Recheck SPT version after update
            await CheckSptVersion();
            // Check mods again in case SPT update included mod updates and it will update the button state
            await RefreshMods();
        };

        updateWindow.Show();
    }

    private async Task CheckServerStatus()
    {
        try
        {
            using var client = new HttpClient();
            var response = await client.GetAsync($"{BaseUrl}/sptserver/running");
            // If response is successful, server is online
            if (response.IsSuccessStatusCode)
            {
                ServerStatusText.Text = "Online";
                ServerStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;

                // Response returns true and false based on SPT server status so we can use it to update that
                var content = await response.Content.ReadAsStringAsync();
                if (bool.TryParse(content.Trim(), out bool isServerRunning))
                {
                    SptServerStatusText.Text = isServerRunning ? "Online" : "Offline";
                    SptServerStatusText.Foreground = isServerRunning ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.Red;
                }
            }
            else
            {
                ServerStatusText.Text = "Offline";
                ServerStatusText.Foreground = System.Windows.Media.Brushes.Red;
                SptServerStatusText.Text = "Offline";
                SptServerStatusText.Foreground = System.Windows.Media.Brushes.Red;
            }

            // Headless server status
            await CheckHeadlessServerStatus();
        }
        catch
        {
            ServerStatusText.Text = "Offline";
            ServerStatusText.Foreground = System.Windows.Media.Brushes.Red;
            SptServerStatusText.Text = "Offline";
            SptServerStatusText.Foreground = System.Windows.Media.Brushes.Red;
        }
    }

    private async Task CheckHeadlessServerStatus()
    {
        try
        {
            var client = new HttpClient();
            // Get headless status
            var responseHeadless = await client.GetAsync($"{BaseUrl}/headless/running");
            if (responseHeadless.IsSuccessStatusCode)
            {
                var content = await responseHeadless.Content.ReadAsStringAsync();
                if (bool.TryParse(content.Trim(), out bool isHeadlessRunning))
                {
                    HeadlessStatusText.Text = isHeadlessRunning ? "Online" : "Offline";
                    HeadlessStatusText.Foreground = isHeadlessRunning ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.Red;
                }
            }
            else
            {
                HeadlessStatusText.Text = "Offline";
                HeadlessStatusText.Foreground = System.Windows.Media.Brushes.Red;
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Error checking headless server status: {e.Message}");
            HeadlessStatusText.Text = "Offline";
            HeadlessStatusText.Foreground = System.Windows.Media.Brushes.Red;
        }
    }

    private async Task RefreshMods()
    {
        LaunchOrUpdateButton.IsEnabled = false;

        var serverMods = await GetServerModsAsync();
        var localMods = GetLocalMods();

        if (serverMods.Count == 0)
        {
            return;
        }

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

    private async Task<List<ConfigInfo>> GetServerConfigsAsync()
    {
        using var client = new HttpClient();
        try
        {
            var response = await client.GetStringAsync($"{BaseUrl}/ConfigFiles.json");
            var configs = JsonSerializer.Deserialize<List<ConfigInfo>>(response)!;
            return configs ?? new List<ConfigInfo>();
        }
        catch
        {
            return new List<ConfigInfo>();
        }
    }

    private List<ConfigInfo> GetLocalConfigs()
    {
        var configs = new List<ConfigInfo>();

        if (!Directory.Exists(_pluginsConfigFolder))
            return configs;

        var excludedConfigs = new HashSet<string>(_excludedConfigs, StringComparer.OrdinalIgnoreCase);

        var configFiles = Directory.GetFiles(_pluginsConfigFolder, "*.cfg", SearchOption.TopDirectoryOnly);
        foreach (var configFile in configFiles)
        {
            var fileName = Path.GetFileName(configFile);
            if (excludedConfigs.Contains(fileName))
                continue;

            var lastModified = File.GetLastWriteTimeUtc(configFile);

            configs.Add(new ConfigInfo
            {
                FileName = fileName,
                LastModified = lastModified
            });
        }

        return configs;
    }

    private async Task RefreshPluginConfigs()
    {
        SyncStatusText.Text = "Checking...";
        SyncStatusText.Foreground = System.Windows.Media.Brushes.Gray;

        var serverConfigs = await GetServerConfigsAsync();
        var localConfigs = GetLocalConfigs();

        if(serverConfigs.Count == 0)
        {
            SyncStatusText.Text = "Failed to get server configs";
            SyncStatusText.Foreground = System.Windows.Media.Brushes.DarkOrange;
            return;
        }
        if(localConfigs.Count == 0)
        {
            SyncStatusText.Text = "No local configs";
            SyncStatusText.Foreground = System.Windows.Media.Brushes.MediumSlateBlue;
            return;
        }

        if(serverConfigs.Count != localConfigs.Count)
        {
            SyncStatusText.Text = "Configs out of sync";
            SyncStatusText.Foreground = System.Windows.Media.Brushes.CadetBlue;
            return;
        }

        // TODO: Check enforced configs last modified dates

        SyncStatusText.Text = "Configs synced";
        SyncStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
    }

    private async void LaunchOrUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // If SPT needs update, open updater window
            if (LaunchOrUpdateButton.Content.ToString() == "Update SPT")
            {
                SptNeedsUpdate();
                return;
            }

            // Sync configs
            StatusTextBlock.Text = "Syncing config files...";
            var serverConfigs = await GetServerConfigsAsync();
            var localConfigs = GetLocalConfigs();
            SyncStatusText.Text = "Syncing configs...";
            SyncStatusText.Foreground = System.Windows.Media.Brushes.DodgerBlue;

            // Check if configs length differ (some local configs removed or this is first launch) sync all missing configs
            if (localConfigs.Count != serverConfigs.Count)
            {
                foreach (var serverConfig in serverConfigs)
                {
                    var localConfig = localConfigs.FirstOrDefault(c =>
                        string.Equals(c.FileName, serverConfig.FileName, StringComparison.OrdinalIgnoreCase));

                    if (localConfig == null)
                    {
                        try
                        {
                            using var client = new HttpClient();
                            var url = $"{BaseUrl}/configs/{Path.GetFileNameWithoutExtension(serverConfig.FileName)}";
                            var data = await client.GetByteArrayAsync(url);

                            var destPath = Path.Combine(_pluginsConfigFolder!, serverConfig.FileName);
                            await File.WriteAllBytesAsync(destPath, data);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to download config {serverConfig.FileName}: {ex.Message}");
                            MessageBox.Show($"Failed to download config {serverConfig.FileName}: {ex.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                    // Remember to check if enforced and found
                    else if (serverConfig.IsEnforced && localConfig.LastModified != serverConfig.LastModified)
                    {
                        try
                        {
                            using var client = new HttpClient();
                            var url = $"{BaseUrl}/configs/{Path.GetFileNameWithoutExtension(serverConfig.FileName)}";
                            var data = await client.GetByteArrayAsync(url);

                            var destPath = Path.Combine(_pluginsConfigFolder!, serverConfig.FileName);
                            await File.WriteAllBytesAsync(destPath, data);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to download config {serverConfig.FileName}: {ex.Message}");
                            MessageBox.Show($"Failed to download config {serverConfig.FileName}: {ex.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                }
            }
            // Download enforced configs if missing or outdated
            else
            {
                foreach (var serverConfig in serverConfigs.Where(c => c.IsEnforced))
                {
                    var localConfig = localConfigs.FirstOrDefault(c =>
                        string.Equals(c.FileName, serverConfig.FileName, StringComparison.OrdinalIgnoreCase));

                    if (localConfig == null || localConfig.LastModified != serverConfig.LastModified)
                    {
                        try
                        {
                            using var client = new HttpClient();
                            var url = $"{BaseUrl}/configs/{Path.GetFileNameWithoutExtension(serverConfig.FileName)}";
                            var data = await client.GetByteArrayAsync(url);

                            var destPath = Path.Combine(_pluginsConfigFolder!, serverConfig.FileName);
                            await File.WriteAllBytesAsync(destPath, data);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to download config {serverConfig.FileName}: {ex.Message}");
                            MessageBox.Show($"Failed to download config {serverConfig.FileName}: {ex.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                }
            }
            SyncStatusText.Text = "Configs synced";
            SyncStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;

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

                // Small delay before finishing to let user see status and windows catch up
                await Task.Delay(100);
                StatusTextBlock.Text = "Update complete.";
                await Task.Delay(200);

                LaunchOrUpdateButton.Content = "Launch";
                LaunchOrUpdateButton.IsEnabled = true;

                await RefreshMods();
            }
            else
            {
                StatusTextBlock.Text = "Launching the game...";
                LaunchTheGame();
            }

            await Task.Delay(2500);
            StatusTextBlock.Text = "";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during launch/update: {ex.Message}");
            MessageBox.Show($"Error during launch/update: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RemoveMods(List<ModStatusEntry> modsToRemove)
    {
        try
        {
            // Show progress: removing mods
            StatusTextBlock.Text = "Removing old mods...";
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
            // Update mod list UI
            ModListView.Items.Refresh();
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
        client.Timeout = TimeSpan.FromMinutes(120); // 2 hours timeout for large mods

        foreach (var mod in mods)
        {
            try
            {
                if (!Directory.Exists(_modsFolder))
                    return false;

                // Initial UI update
                mod.Status = "Preparing...";
                ModListView.Items.Refresh();
                StatusTextBlock.Text = $"Updating mod: {mod.Name}";
                await Task.Delay(50);

                // Get mod info from server
                var serverMods = await GetServerModsAsync();
                var modInfo = serverMods.FirstOrDefault(m => m.Name == mod.Name);
                if (modInfo == null)
                {
                    Debug.WriteLine($"Mod info not found on server: {mod.Name}");
                    continue;
                }

                var url = $"{BaseUrl}/mods/{modInfo.Name}";

                // --- Streamed download with progress ---
                var tempZip = Path.Combine(Path.GetTempPath(), modInfo.FileName);
                if (File.Exists(tempZip)) File.Delete(tempZip);

                // If download states are not supported, fallback to simple download
                mod.Status = "Downloading...";
                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var canReport = totalBytes > 0;

                    await using var stream = await response.Content.ReadAsStreamAsync();
                    await using var fileStream = File.Create(tempZip);

                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int read;
                    double lastPercent = 0;

                    while ((read = await stream.ReadAsync(buffer)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read));
                        totalRead += read;

                        if (canReport)
                        {
                            double percent = (double)totalRead / totalBytes * 100;
                            if (percent - lastPercent >= 1) // only update every 1%
                            {
                                mod.Status = $"Downloading... {percent:F0}%";
                                StatusTextBlock.Text = $"Updating mod: {mod.Name} - {percent:F0}%";
                                ModListView.Items.Refresh();
                                lastPercent = percent;
                            }
                        }
                    }
                }

                // --- Extracting ---
                mod.Status = "Extracting...";
                ModListView.Items.Refresh();
                StatusTextBlock.Text = $"Updating mod: {mod.Name}";
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
                    // Handle nested mod folders correctly
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
                StatusTextBlock.Text = $"Failed to update mod: {mod.Name}";

                MessageBox.Show($"Failed to update mod {mod.Name}: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        StatusTextBlock.Text = "Mod updates complete.";
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

    private void CheckServerButton_Click(object sender, RoutedEventArgs e)
    {
        // Set status to checking
        ServerStatusText.Text = "Checking...";
        ServerStatusText.Foreground = System.Windows.Media.Brushes.Gray;
        // Check server status
        _ = CheckServerStatus();
    }

    private void ConfigureServerButton_Click(object sender, RoutedEventArgs e)
    {
        var serverConfigWindow = new ServerConfigWindow(_serverIp, _serverPort, _sptServerAddress, _secret!);
        if (serverConfigWindow.ShowDialog() == true)
        {
            _serverIp = serverConfigWindow.ServerIp;
            _serverPort = serverConfigWindow.ServerPort;
            _sptServerAddress = serverConfigWindow.SptServerAddress;
            _secret = serverConfigWindow.SecretKey;

            // Validate secret on server
            _secret = CheckIfSecretIsValid(_secret);

            // Save updated config to exe folder
            SaveConfig();

            // Refresh mods and server status
            _ = RefreshMods();
            _ = CheckServerStatus();
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
                if (!string.IsNullOrWhiteSpace(cfg.Secret))
                    _secret = cfg.Secret;
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

            var cfg = new AppConfig { ServerIp = _serverIp, ServerPort = _serverPort, SptServerAddress = _sptServerAddress, Secret = _secret};
            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save config: {ex.Message}");
        }
    }

    private string? CheckIfSecretIsValid(string? secretKey)
    {
        if (!string.IsNullOrWhiteSpace(secretKey))
        {
            using var client = new HttpClient();
            try
            {
                var url = $"{BaseUrl}/admin/validate?secret={secretKey}";
                try
                {
                    var response = client.GetStringAsync(url).Result?.Trim();
                    if (!string.IsNullOrEmpty(response))
                    {
                        try
                        {
                            // Expecting server to return an AdminConfig JSON; fall back to plain true/false
                            var admin = JsonSerializer.Deserialize<AdminConfig>(response);
                            if (admin != null && admin.IsEnabled)
                            {
                                Debug.WriteLine("Admin secret validated successfully.");
                                if (string.IsNullOrWhiteSpace(admin.Secret))
                                    admin.Secret = secretKey;
                                UpdateAdminStatus(admin);
                            }
                            else
                            {
                                MessageBox.Show("Admin secret is invalid on the server.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                secretKey = ""; // clear invalid secret
                            }
                        }
                        catch
                        {
                            // Fallback: server returned "true" or "false"
                            if (string.Equals(response, "true", StringComparison.OrdinalIgnoreCase))
                            {
                                Debug.WriteLine("Admin secret validated successfully.");
                                UpdateAdminStatus(new AdminConfig { Secret = secretKey, IsEnabled = true });
                            }
                            else
                            {
                                MessageBox.Show("Admin secret is invalid on the server.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                secretKey = ""; // clear invalid secret
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show("Admin secret is invalid on the server.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        secretKey = ""; // clear invalid secret
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to validate admin secret: {ex.Message}");
                    MessageBox.Show($"Failed to validate admin secret: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to validate admin secret: {ex.Message}");
                MessageBox.Show($"Failed to validate admin secret: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        return secretKey;
    }

    private void UpdateAdminStatus(AdminConfig config)
    {
        // Enable or disable admin features based on config
        KillHeadlessButton.Visibility = config.AllowHeadlessClose ? Visibility.Visible : Visibility.Collapsed;
    }

    private void KillServer_Click(object sender, RoutedEventArgs e)
    {
        // Check if secret is set
        if (string.IsNullOrWhiteSpace(_secret))
        {
            MessageBox.Show("Admin secret is not set. Please configure the server settings first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        // Check if the server is online
        if (ServerStatusText.Text != "Online")
        {
            MessageBox.Show("Server is not online. Cannot send shutdown command.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Prompt for confirmation
        var result = MessageBox.Show("Are you sure you want to send shutdown command to the headless server?", "Confirm Shutdown", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        // Check if headless server is running /admin/headless/running with secret
        using var clientCheck = new HttpClient();
        try
        {
            var urlCheck = $"{BaseUrl}/admin/headless/running?secret={_secret}";
            var responseCheck = clientCheck.GetStringAsync(urlCheck).Result?.Trim();
            if (!string.Equals(responseCheck, "true", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Headless server is not running. Cannot send shutdown command.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to check headless server status: {ex.Message}");
            MessageBox.Show($"Failed to check headless server status: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Send command to server /admin/headless/close with secret
        using var client = new HttpClient();
        try
        {
            var url = $"{BaseUrl}/admin/headless/close?secret={_secret}";
            var response = client.GetStringAsync(url).Result?.Trim();

            if (string.Equals(response, "true", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Server shutdown command sent.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (string.Equals(response, "false", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Server cooldown active. Please wait before trying again.", "Info", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show($"Failed to send kill command. Server response: {response}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to send kill command: {ex.Message}");
            MessageBox.Show($"Failed to send kill command: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task FetchLauncherSettings()
    {
        var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher_settings.json");
        if (File.Exists(settingsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(settingsPath);
                var settings = JsonSerializer.Deserialize<LauncherSettings>(json);
                if (settings != null)
                {
                    _excludedMods = settings.ExcludedMods;
                    _excludedModFolders = settings.ExcludedModFolders;
                    _excludedConfigs = settings.ExcludedConfigs;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load launcher settings: {ex.Message}");
            }
        }
    }

    private DispatcherTimer? _serverCheckTimer;

    private void InitializeServerCheckTimer()
    {
        _serverCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _serverCheckTimer.Tick += async (s, e) =>
        {
            // Check server status
            await CheckServerStatus();
        };
        _serverCheckTimer.Start();
    }
}

// new: simple config DTO
public class AppConfig
{
    public string? ServerIp { get; set; }
    public int ServerPort { get; set; }
    public string? SptServerAddress { get; set; }
    public string? Secret { get; set; } // Admin secret for server control
}

public class ModEntry
{
    public required string Name { get; set; }
    public required string Version { get; set; }
    public required string FileName { get; set; }       // Name of the zip file
    public required bool IsFolderMod { get; set; }     // true if folder-based mod
}

public class ConfigInfo
{
    public string FileName { get; set; } = "";
    public DateTime LastModified { get; set; }
    public bool IsEnforced { get; set; } = false; // If true, launcher will get this config file from server on launch
}

public class ModStatusEntry
{
    public required string Name { get; set; }
    public required string LocalVersion { get; set; }
    public required string ServerVersion { get; set; }
    public string? Status { get; set; }
    public required bool IsFolderMod { get; set; }
}

// Admin config so launcher can authenticate admin commands. Array of these in JSON so multiple admins can be set up.
public class AdminConfig
{
    public string Note { get; set; } = ""; // For easy edit, e.g. "My own pc"
    public string Secret { get; set; } = ""; // Like password
    public bool IsEnabled { get; set; } = true; // So can be disabled without deleting
    public bool AllowHeadlessClose { get; set; } = false; // Whether this admin can close headless client
}

// Additional Launcher settings
public class LauncherSettings
{
    // Excluded mods
    public List<string> ExcludedMods { get; set; } = new List<string>();
    // Excluded mods folders
    public List<string> ExcludedModFolders { get; set; } = new List<string>();
    // Excluded config files
    public List<string> ExcludedConfigs { get; set; } = new List<string>();
}
