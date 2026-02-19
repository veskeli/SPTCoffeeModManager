using System.Windows.Controls;
using System.Windows;
using System.ComponentModel;

namespace SPTCoffeeModManager.Tabs;

/// <summary>
/// Interaction logic for HomeTab.xaml
/// </summary>
public partial class HomeTab : UserControl
{
    public HomeTab()
    {
        InitializeComponent();

        // Show controls in the XAML designer but keep them collapsed at runtime.
        // Some editors (or their previewers) don't honor d:Visibility, so explicitly set design-time visibility here.
        var isDesign = DesignerProperties.GetIsInDesignMode(this);
        AdminPanel.Visibility = isDesign ? Visibility.Visible : Visibility.Collapsed;
        KillHeadlessButton.Visibility = isDesign ? Visibility.Visible : Visibility.Collapsed;
    }

    // Expose controls to parent window for easy access
    public TextBlock ServerStatusTextBlock => ServerStatusText;
    public TextBlock SptServerStatusTextBlock => SptServerStatusText;
    public TextBlock HeadlessStatusTextBlock => HeadlessStatusText;
    public TextBlock SyncStatusTextBlock => SyncStatusText;
    public TextBlock CurrentSptVersionTextBlock => CurrentSptVersionText;
    public TextBlock StatusMessageTextBlock => StatusTextBlock;
    public Button KillHeadlessButtonRef => KillHeadlessButton;
    public Button RefreshButtonRef => RefreshButton;
    public Button CheckUpdatesButtonRef => CheckUpdatesButton;
    public Button LaunchOrUpdateButtonRef => LaunchOrUpdateButton;
    public Border AdminPanelRef => AdminPanel;
}
