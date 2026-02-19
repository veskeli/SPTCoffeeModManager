using System.Windows.Controls;

namespace SPTCoffeeModManager.Tabs;

/// <summary>
/// Interaction logic for SettingsTab.xaml
/// </summary>
public partial class SettingsTab : UserControl
{
    public SettingsTab()
    {
        InitializeComponent();
    }

    // Expose controls to parent window for easy access
    public Button ConfigureServerButtonRef => ConfigureServerButton;
}


