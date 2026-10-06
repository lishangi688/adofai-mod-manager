using System.Windows;
using Wpf.Ui.Controls;

namespace AdofaiModManager.Views.Dialogs;

public enum KernelSourceChoice
{
    Cancel,
    FetchFromSite,
    UseBundled,
    ImportZip,
}

public partial class KernelSourceDialog : FluentWindow
{
    public KernelSourceDialog(string? latestFromSite = null)
    {
        InitializeComponent();

        if (!string.IsNullOrWhiteSpace(latestFromSite))
        {
            HeadlineText.Text = $"选择内核（UnityModManager）来源　·　ADOFAITools 最新版：{latestFromSite}";
        }
    }

    public KernelSourceChoice Choice { get; private set; } = KernelSourceChoice.Cancel;

    private void Fetch_Click(object sender, RoutedEventArgs e)
    {
        Choice = KernelSourceChoice.FetchFromSite;
        DialogResult = true;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        Choice = KernelSourceChoice.ImportZip;
        DialogResult = true;
    }

    private void Bundled_Click(object sender, RoutedEventArgs e)
    {
        Choice = KernelSourceChoice.UseBundled;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
