using System;
using System.Diagnostics;
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
                catch (Exception ex) { Debug.WriteLine($"Failed to clean up {dir}: {ex.Message}"); }
            }
        }
        catch { /* ignore */ }
    }

    /// <summary>
    /// Loads managed assemblies from the <c>libs</c> subdirectory via ALC fallback resolver,
    /// and prepends <c>libs</c> to PATH so native DLLs (ONNX Runtime, DirectML, etc.) are
    /// found by third-party P/Invoke calls.
    /// Security: <c>libs</c> shares the same trust boundary as the EXE — an attacker who can
    /// write to <c>libs</c> can equally replace the EXE itself, so this adds no extra attack surface.
    /// PATH is modified per-process only; <c>NativeLibrary.SetDllImportResolver</c> is not used
    /// because it only intercepts P/Invoke from one assembly and would not cover native loads
    /// initiated by third-party managed wrappers.
    /// </summary>
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
