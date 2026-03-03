using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using EndFieldFightHelper.Models;
using EndFieldFightHelper.Services;
using EndFieldFightHelper.ViewModels;
using EndFieldFightHelper.Views;
using SukiUI.Controls;
using SukiUI.Toasts;

namespace EndFieldFightHelper;

public partial class MainWindow : SukiWindow
{
    private readonly MainWindowViewModel _viewModel;
    private TrayIconService? _trayIconService;
    private bool _isClosingConfirmed;
    private bool _suppressSideMenuSelection;
    public static ISukiToastManager ToastManager { get; } = new SukiToastManager();

    public MainWindow()
    {
        _viewModel = new MainWindowViewModel(ToastManager);
        DataContext = _viewModel;

        InitializeComponent();

        ToastHost.Manager = ToastManager;

        Loaded += OnLoaded;
        Closed += OnClosed;
        Closing += OnClosing;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        SideMenu.AddHandler(
            SelectingItemsControl.SelectionChangedEvent,
            OnSideMenuSelectionChanged,
            Avalonia.Interactivity.RoutingStrategies.Bubble,
            true);

        await _viewModel.SettingsViewModel.CheckResourcesOnStartupAsync();
        await _viewModel.SettingsViewModel.CheckAppUpdateOnStartupAsync();
    }

    private async void OnSideMenuSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (_suppressSideMenuSelection)
                return;

            var leavingTeamSetup = e.RemovedItems
                .OfType<SukiSideMenuItem>()
                .Any(item => item == TeamSetupMenuItem);

            if (!leavingTeamSetup || !_viewModel.TeamSetupViewModel.HasGaps)
                return;

            if (e.Source is not SelectingItemsControl selector)
                return;

            var targetItem = e.AddedItems.OfType<SukiSideMenuItem>().FirstOrDefault();

            _suppressSideMenuSelection = true;
            selector.SelectedItem = TeamSetupMenuItem;
            _suppressSideMenuSelection = false;

            var vm = _viewModel.TeamSetupViewModel;
            var slotNames = new List<string?>();
            for (var i = 0; i < vm.TeamSlots.Count; i++)
                slotNames.Add(vm.GetSlot(i + 1)?.Name);

            var dialog = new TeamCompactDialog();
            dialog.SetSlotNames(slotNames);
            await dialog.ShowDialog(this);

            if (dialog.IsConfirmed)
            {
                vm.CompactTeam();

                if (targetItem != null)
                {
                    _suppressSideMenuSelection = true;
                    selector.SelectedItem = targetItem;
                    _suppressSideMenuSelection = false;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SideMenu selection handler failed: {ex}");
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();

        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isClosingConfirmed)
        {
            _trayIconService?.Dispose();
            return;
        }

        var closeAction = _viewModel.SettingsViewModel.CloseAction;

        if (closeAction == CloseAction.MinimizeToTray)
        {
            e.Cancel = true;
            MinimizeToTray();
            return;
        }

        if (closeAction == CloseAction.Exit)
        {
            _isClosingConfirmed = true;
            return;
        }

        e.Cancel = true;
        await ShowClosingDialogAsync();
    }

    private async System.Threading.Tasks.Task ShowClosingDialogAsync()
    {
        var dialog = new ClosingDialog();
        await dialog.ShowDialog(this);

        var action = dialog.SelectedAction;
        var remember = dialog.RememberChoice;

        if (action == CloseAction.Ask)
        {
            return;
        }

        if (remember)
        {
            _viewModel.SettingsViewModel.CloseAction = action;
        }

        if (action == CloseAction.MinimizeToTray)
        {
            MinimizeToTray();
        }
        else if (action == CloseAction.Exit)
        {
            _isClosingConfirmed = true;
            Close();
        }
    }

    public void ConfirmAndClose()
    {
        _isClosingConfirmed = true;
        _trayIconService?.Dispose();
        Close();
    }

    private void MinimizeToTray()
    {
        _trayIconService ??= new TrayIconService(this);
        _trayIconService.MinimizeToTray();
    }
}
