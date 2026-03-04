using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace EndFieldFightHelper.Views;

public partial class DodgeSettingsDialog : Window
{
    public (int DelayMs, bool SuppressDuringSkill)? Result { get; private set; }

    public DodgeSettingsDialog()
    {
        InitializeComponent();

        ConfirmButton.Click += OnConfirmClick;
        CancelButton.Click += OnCancelClick;
        DelaySlider.PropertyChanged += OnDelaySliderChanged;
    }

    public void Initialize(int delayMs, bool suppressDuringSkill)
    {
        DelaySlider.Value = delayMs;
        DelayValueText.Text = delayMs.ToString();
        SuppressDuringSkillCheckBox.IsChecked = suppressDuringSkill;
    }

    private void OnDelaySliderChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == RangeBase.ValueProperty)
            DelayValueText.Text = ((int)DelaySlider.Value).ToString();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Result = ((int)DelaySlider.Value, SuppressDuringSkillCheckBox.IsChecked == true);
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }
}
