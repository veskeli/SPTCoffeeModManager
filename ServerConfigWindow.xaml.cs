using System.Windows;
using System.Windows.Input;

namespace SPTCoffeeModManager;

public partial class ServerConfigWindow : Window
{
    public string ServerIp { get; private set; }
    public int ServerPort { get; private set; }
    public string SptServerAddress { get; private set; }
    public string SecretKey {get; private set; }

    public ServerConfigWindow(string serverIp = "", int serverPort = 0, string sptServerAddressTextBox = "", string secret = "")
    {
        InitializeComponent();

        // Initialize properties with defaults when incoming values are empty/invalid
        ServerIp = string.IsNullOrWhiteSpace(serverIp) ? "127.0.0.1" : serverIp;
        ServerPort = serverPort > 0 ? serverPort : 25569;
        SptServerAddress = string.IsNullOrWhiteSpace(sptServerAddressTextBox) ? "http://127.0.0.1:6969" : sptServerAddressTextBox;
        SecretKey = string.IsNullOrWhiteSpace(secret) ? "" : secret;

        // Populate fields with incoming values
        AddressTextBox.Text = serverIp;
        PortTextBox.Text = serverPort > 0 ? serverPort.ToString() : string.Empty;
        SptServerAddressTextBox.Text = sptServerAddressTextBox;
        SecretKeyTextBox.Password = secret;
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

    // Close the window (treat as cancel)
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var ip = AddressTextBox.Text.Trim();
        if (!int.TryParse(PortTextBox.Text.Trim(), out var port))
        {
            MessageBox.Show(this, "Please enter a valid port number.", "Invalid Port", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ServerIp = ip;
        ServerPort = port;
        SptServerAddress = SptServerAddressTextBox.Text.Trim();
        SecretKey = SecretKeyTextBox.Password;

        // Setting DialogResult closes the dialog and makes ShowDialog() return true.
        this.DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
    }

    private void ToggleSecretKeyVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        // Toggle SecretKeyTextBox text visibility (dots vs plain text)
        SecretKeyTextBox.Visibility = SecretKeyTextBox.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }
}