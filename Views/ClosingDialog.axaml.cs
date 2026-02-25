using Avalonia.Controls;
using Avalonia.Interactivity;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Views;

public partial class ClosingDialog : Window
{
    public CloseAction SelectedAction { get; private set; } = CloseAction.Ask;
    public bool RememberChoice { get; private set; }

    private readonly Button _minimizeButton;
    private readonly Button _exitButton;
    private readonly Button _cancelButton;
    private readonly CheckBox _rememberCheckBox;

    public ClosingDialog()
    {
        InitializeComponent();
        
        _minimizeButton = this.FindControl<Button>("MinimizeButton")!;
        _exitButton = this.FindControl<Button>("ExitButton")!;
        _cancelButton = this.FindControl<Button>("CancelButton")!;
        _rememberCheckBox = this.FindControl<CheckBox>("RememberCheckBox")!;
        
        _minimizeButton.Click += OnMinimizeClick;
        _exitButton.Click += OnExitClick;
        _cancelButton.Click += OnCancelClick;
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        SelectedAction = CloseAction.MinimizeToTray;
        RememberChoice = _rememberCheckBox.IsChecked ?? false;
        Close();
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        SelectedAction = CloseAction.Exit;
        RememberChoice = _rememberCheckBox.IsChecked ?? false;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        SelectedAction = CloseAction.Ask;
        RememberChoice = false;
        Close();
    }
}
