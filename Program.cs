using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Avalonia;

namespace EndFieldFightHelper;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        CleanupOldUpdateTempDirs();
        SetupLibsResolver();
        StartApp(args);
    }

    private static void CleanupOldUpdateTempDirs()
    {
        try
        {
            var tempRoot = Path.GetTempPath();
            foreach (var dir in Directory.EnumerateDirectories(tempRoot, "effh-update-*"))
            {
                try { Directory.Delete(dir, true); }
                catch { /* best effort */ }
            }
        }
        catch { /* ignore */ }
    }

    private static void SetupLibsResolver()
    {
        var libsPath = Path.Combine(AppContext.BaseDirectory, "libs");
        if (!Directory.Exists(libsPath)) return;

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (name.Name is null) return null;
            var dllPath = Path.Combine(libsPath, name.Name + ".dll");
            if (File.Exists(dllPath))
                return context.LoadFromAssemblyPath(dllPath);
            return null;
        };

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        Environment.SetEnvironmentVariable("PATH", libsPath + ";" + path);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartApp(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
