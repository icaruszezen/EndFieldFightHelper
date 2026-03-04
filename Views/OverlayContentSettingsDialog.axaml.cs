using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Views;

public partial class OverlayContentSettingsDialog : Window
{
    private readonly List<(string Name, CheckBox CheckBox)> _pipelineCheckBoxes = [];

    public OverlayContentSettings? Result { get; private set; }

    public OverlayContentSettingsDialog()
    {
        InitializeComponent();

        ConfirmButton.Click += OnConfirmClick;
        CancelButton.Click += OnCancelClick;
        ShowPipelineCheckBox.IsCheckedChanged += OnShowPipelineChanged;
        ShowBattleAxisCheckBox.IsCheckedChanged += OnShowBattleAxisChanged;
        BattleAxisFontSizeSlider.PropertyChanged += OnFontSizeSliderChanged;
    }

    public void Initialize(OverlayContentSettings current, IReadOnlyList<string> pipelineNames)
    {
        ShowLogCheckBox.IsChecked = current.ShowLog;
        ShowPipelineCheckBox.IsChecked = current.ShowPipelineStatus;
        ShowBattleAxisCheckBox.IsChecked = current.ShowBattleAxis;
        BattleAxisFontSizeSlider.Value = current.BattleAxisFontSize;
        FontSizeValueText.Text = current.BattleAxisFontSize.ToString("0");
        UpdateBattleAxisFontSizeVisibility();

        foreach (var name in pipelineNames)
        {
            var cb = new CheckBox
            {
                Content = name,
                IsChecked = current.VisiblePipelineNames.Count == 0 || current.VisiblePipelineNames.Contains(name),
                FontSize = 12,
            };
            _pipelineCheckBoxes.Add((name, cb));
            PipelineListPanel.Children.Add(cb);
        }

        UpdatePipelineListVisibility();
    }

    private void OnShowPipelineChanged(object? sender, RoutedEventArgs e)
    {
        UpdatePipelineListVisibility();
    }

    private void UpdatePipelineListVisibility()
    {
        PipelineListBorder.IsVisible = ShowPipelineCheckBox.IsChecked == true;
    }

    private void OnShowBattleAxisChanged(object? sender, RoutedEventArgs e)
    {
        UpdateBattleAxisFontSizeVisibility();
    }

    private void UpdateBattleAxisFontSizeVisibility()
    {
        BattleAxisFontSizeBorder.IsVisible = ShowBattleAxisCheckBox.IsChecked == true;
    }

    private void OnFontSizeSliderChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == nameof(Slider.Value))
            FontSizeValueText.Text = BattleAxisFontSizeSlider.Value.ToString("0");
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        var visiblePipelines = _pipelineCheckBoxes
            .Where(p => p.CheckBox.IsChecked == true)
            .Select(p => p.Name)
            .ToList();

        var allChecked = visiblePipelines.Count == _pipelineCheckBoxes.Count;

        Result = new OverlayContentSettings
        {
            ShowLog = ShowLogCheckBox.IsChecked == true,
            ShowPipelineStatus = ShowPipelineCheckBox.IsChecked == true,
            VisiblePipelineNames = allChecked ? [] : visiblePipelines,
            ShowBattleAxis = ShowBattleAxisCheckBox.IsChecked == true,
            BattleAxisFontSize = BattleAxisFontSizeSlider.Value,
        };
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }
}
