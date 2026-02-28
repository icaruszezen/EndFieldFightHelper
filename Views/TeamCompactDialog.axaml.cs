using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EndFieldFightHelper.Views;

public partial class TeamCompactDialog : Window
{
    public bool IsConfirmed { get; private set; }

    private readonly Button _confirmButton;
    private readonly Button _cancelButton;
    private readonly TextBlock _exampleText;

    public TeamCompactDialog()
    {
        InitializeComponent();

        _confirmButton = this.FindControl<Button>("ConfirmButton")!;
        _cancelButton = this.FindControl<Button>("CancelButton")!;
        _exampleText = this.FindControl<TextBlock>("ExampleText")!;

        _confirmButton.Click += OnConfirmClick;
        _cancelButton.Click += OnCancelClick;
    }

    public void SetSlotNames(IReadOnlyList<string?> slotNames)
    {
        var before = new List<string>();
        var after = new List<string>();

        foreach (var name in slotNames)
            before.Add(name ?? "空");

        foreach (var name in slotNames)
        {
            if (name != null)
                after.Add(name);
        }
        while (after.Count < slotNames.Count)
            after.Add("空");

        _exampleText.Text = $"补位前：{string.Join(" - ", before)}\n补位后：{string.Join(" - ", after)}";
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        IsConfirmed = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }
}
