using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using EndFieldFightHelper.ViewModels;

namespace EndFieldFightHelper.Views;

public partial class TeamSetupPage : UserControl
{
    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#FF9800"));
    private static readonly IBrush InactiveBrush = Brushes.Transparent;

    private Border[] _slotBorders = [];

    public TeamSetupPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _slotBorders =
        [
            this.FindControl<Border>("Slot1Border")!,
            this.FindControl<Border>("Slot2Border")!,
            this.FindControl<Border>("Slot3Border")!,
            this.FindControl<Border>("Slot4Border")!,
        ];
        UpdateSlotHighlight(1);
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is TeamSetupViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            UpdateSlotHighlight(vm.SelectedSlotIndex);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TeamSetupViewModel.SelectedSlotIndex) &&
            sender is TeamSetupViewModel vm)
        {
            UpdateSlotHighlight(vm.SelectedSlotIndex);
        }
    }

    private void UpdateSlotHighlight(int activeIndex)
    {
        if (_slotBorders.Length == 0) return;

        for (var i = 0; i < _slotBorders.Length; i++)
        {
            _slotBorders[i].BorderBrush = (i + 1 == activeIndex) ? ActiveBrush : InactiveBrush;
        }
    }
}
