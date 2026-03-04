using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EndFieldFightHelper.Views;

public partial class AutoSkillOrderDialog : Window
{
    public string? Result { get; private set; }

    public AutoSkillOrderDialog()
    {
        InitializeComponent();

        ConfirmButton.Click += OnConfirmClick;
        CancelButton.Click += OnCancelClick;
        ResetButton.Click += OnResetClick;
        OrderTextBox.TextChanged += OnOrderTextChanged;

        Preset1234.Click += (_, _) => OrderTextBox.Text = "1234";
        Preset1324.Click += (_, _) => OrderTextBox.Text = "1324";
        Preset1.Click += (_, _) => OrderTextBox.Text = "1";
        Preset12.Click += (_, _) => OrderTextBox.Text = "12";
        Preset23.Click += (_, _) => OrderTextBox.Text = "23";
    }

    public void Initialize(string currentOrder)
    {
        OrderTextBox.Text = currentOrder;
        UpdatePreview();
    }

    private void OnOrderTextChanged(object? sender, TextChangedEventArgs e)
    {
        Validate();
        UpdatePreview();
    }

    private bool Validate()
    {
        var text = OrderTextBox.Text ?? "";

        if (string.IsNullOrEmpty(text))
        {
            ValidationText.IsVisible = false;
            return true;
        }

        foreach (var ch in text)
        {
            if (ch < '1' || ch > '4')
            {
                ValidationText.Text = "只允许输入数字 1-4";
                ValidationText.IsVisible = true;
                return false;
            }
        }

        ValidationText.IsVisible = false;
        return true;
    }

    private void UpdatePreview()
    {
        var text = (OrderTextBox.Text ?? "").Trim();

        if (string.IsNullOrEmpty(text))
        {
            PreviewText.Text = "按配队顺序循环: 1 → 2 → 3 → 4 → 1 → ...";
            return;
        }

        var valid = text.All(ch => ch >= '1' && ch <= '4');
        if (!valid)
        {
            PreviewText.Text = "—";
            return;
        }

        var sb = new StringBuilder();
        var repeatCount = text.Length == 1 ? 5 : (text.Length <= 2 ? 6 : 8);
        for (var i = 0; i < repeatCount; i++)
        {
            if (i > 0) sb.Append(" → ");
            sb.Append(text[i % text.Length]);
        }
        sb.Append(" → ...");
        PreviewText.Text = sb.ToString();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (!Validate()) return;

        var text = (OrderTextBox.Text ?? "").Trim();
        Result = text;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        OrderTextBox.Text = "";
    }
}
