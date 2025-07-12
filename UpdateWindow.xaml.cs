using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SPTCoffeeModManager;

public partial class UpdateWindow : Window
{
    public UpdateWindow()
    {
        InitializeComponent();
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

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Updating... Please wait.";
        StatusText.Foreground = new SolidColorBrush(Colors.White);

        try
        {
            await UpdaterService.CheckAndUpdateAsync(status =>
            {
                Dispatcher.Invoke(() => StatusText.Text = status);
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Update failed. Please try again later.";
            StatusText.Foreground = new SolidColorBrush(Colors.Red);
            MessageBox.Show($"Failed to check for updates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void SetStatusText(string text)
    {
        StatusText.Text = text;
        StatusText.Foreground = new SolidColorBrush(Colors.White);
    }
}