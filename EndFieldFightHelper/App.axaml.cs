using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using EndFieldFightHelper.ViewModels;
using EndFieldFightHelper.Views;

namespace EndFieldFightHelper;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();

            services.AddSingleton<HomePageViewModel>();
            services.AddSingleton<SettingsPageViewModel>();
            services.AddSingleton<MainViewModel>();

            var provider = services.BuildServiceProvider();

            var viewLocator = new ViewLocator();
            DataTemplates.Add(viewLocator);

            var mainVm = provider.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainVm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
