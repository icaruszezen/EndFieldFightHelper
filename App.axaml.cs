using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EndFieldFightHelper.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EndFieldFightHelper;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IScreenshotService, ScreenshotService>();
        services.AddSingleton<YoloDetectionService>();
        services.AddSingleton<IInputService, InputService>();
        services.AddSingleton<OverlayService>();
        services.AddSingleton<ResourceService>();
        services.AddSingleton<AppUpdateService>();

        return services.BuildServiceProvider();
    }

    public static Window? GetMainWindow()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}
