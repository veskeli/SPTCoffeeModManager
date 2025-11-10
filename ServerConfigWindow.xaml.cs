using System.Windows;
using System.Windows.Input;

namespace SPTCoffeeModManager;

public partial class ServerConfigWindow : Window
{
    public string ServerIP { get; private set; }
    public int ServerPort { get; private set; }

    public ServerConfigWindow(string serverIP = "", int serverPort = 0)
    {
        InitializeComponent();

        // Populate fields with incoming values
        AddressTextBox.Text = serverIP;
        PortTextBox.Text = serverPort > 0 ? serverPort.ToString() : string.Empty;
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

        ServerIP = ip;
        ServerPort = port;

        // Setting DialogResult closes the dialog and makes ShowDialog() return true.
        this.DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
    }
}