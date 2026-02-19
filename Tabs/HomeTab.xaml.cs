using System.Windows.Controls;

namespace SPTCoffeeModManager.Tabs;

/// <summary>
/// Interaction logic for HomeTab.xaml
/// </summary>
public partial class HomeTab : UserControl
{
    public HomeTab()
    {
        InitializeComponent();
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
}


