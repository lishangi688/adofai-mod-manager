using System.Windows;
using Wpf.Ui.Controls;

namespace AdofaiModManager.Views.Dialogs;

public partial class TextInputDialog : FluentWindow
{
    public TextInputDialog(string title, string prompt, string initialText = "")
    {
        InitializeComponent();

        Title = title;
        TitleBarControl.Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initialText;

        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    public string InputText => InputBox.Text;

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
