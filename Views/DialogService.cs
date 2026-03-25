using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using EndFieldFightHelper.Models;
using EndFieldFightHelper.Services;

namespace EndFieldFightHelper.Views;

public class DialogService : IDialogService
{
    private static Window? GetMainWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<(int DelayMs, bool SuppressDuringSkill)?> ShowDodgeSettingsAsync(
        int currentDelay, bool currentSuppress)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return null;

        var dialog = new DodgeSettingsDialog();
        dialog.Initialize(currentDelay, currentSuppress);
        await dialog.ShowDialog(mainWindow);
        return dialog.Result;
    }

    public async Task<string?> ShowAutoSkillOrderAsync(string currentOrder)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return null;

        var dialog = new AutoSkillOrderDialog();
        dialog.Initialize(currentOrder);
        await dialog.ShowDialog(mainWindow);
        return dialog.Result;
    }

    public async Task<OverlayContentSettings?> ShowOverlayContentSettingsAsync(
        OverlayContentSettings current, IReadOnlyList<string> pipelineNames)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return null;

        var dialog = new OverlayContentSettingsDialog();
        dialog.Initialize(current, pipelineNames);
        await dialog.ShowDialog(mainWindow);
        return dialog.Result;
    }
}
