using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Compunet.YoloSharp;
using Compunet.YoloSharp.Plotting;
using Microsoft.ML.OnnxRuntime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.PixelFormats;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public class YoloDetectionService : IDisposable
{
    private YoloPredictor? _predictor;
    private string? _currentModelPath;
    private static bool? _gpuAvailableCache;
    private static readonly object GpuDllPinLock = new();
    private static readonly Dictionary<string, IntPtr> PinnedGpuDllHandles = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    private static string ResolveProviderDirectory()
    {
        var runtimesDir = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native");
        var providerPath = Path.Combine(runtimesDir, "onnxruntime_providers_cuda.dll");
        return File.Exists(providerPath) ? runtimesDir : AppContext.BaseDirectory;
    }

    private static void EnsureGpuDependencyChainPinned()
    {
        lock (GpuDllPinLock)
        {
            var providerDir = ResolveProviderDirectory();
            var preloadOrder = new[]
            {
                "cublasLt64_12.dll",
                "cublas64_12.dll",
                "cufft64_11.dll",
                "cudart64_12.dll",
                "cudnn_graph64_9.dll",
                "cudnn_ops64_9.dll",
                "cudnn_cnn64_9.dll",
                "cudnn_adv64_9.dll",
                "cudnn_heuristic64_9.dll",
                "cudnn_engines_runtime_compiled64_9.dll",
                "cudnn_engines_precompiled64_9.dll",
                "cudnn64_9.dll",
                "nvJitLink64_12.dll",
                "nvrtc64_120_0.dll",
                "nvrtc-builtins64_120.dll"
            };

            foreach (var fileName in preloadOrder)
            {
                if (PinnedGpuDllHandles.TryGetValue(fileName, out var pinned) && pinned != IntPtr.Zero)
                {
                    continue;
                }

                var fullPath = Path.Combine(providerDir, fileName);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                var handle = LoadLibrary(fullPath);
                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                PinnedGpuDllHandles[fileName] = handle;
            }
        }
    }

    public bool IsModelLoaded => _predictor != null;
    public string? ModelPath => _currentModelPath;

    public static bool CudaDllsPresent => CudaDependencyService.CheckInstalled();

    public static bool IsGpuAvailable()
    {
        if (_gpuAvailableCache.HasValue)
        {
            return _gpuAvailableCache.Value;
        }

        if (!CudaDllsPresent)
        {
            _gpuAvailableCache = false;
            return false;
        }

        EnsureGpuDependencyChainPinned();

        try
        {
            var sessionOptions = SessionOptions.MakeSessionOptionWithCudaProvider(0);
            sessionOptions.Dispose();
            _gpuAvailableCache = true;
        }
        catch
        {
            _gpuAvailableCache = false;
        }

        return _gpuAvailableCache.Value;
    }

    public static void ResetGpuCache()
    {
        _gpuAvailableCache = null;
    }

    public bool IsUsingGpu { get; private set; }
    public string? GpuFallbackReason { get; private set; }

    public void LoadModel(string modelPath, bool useGpu = false,
        float confidence = 0.3f, float iou = 0.45f)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("模型文件不存在", modelPath);

        var configuration = new YoloConfiguration
        {
            Confidence = confidence,
            IoU = iou,
            ApplyAutoOrient = false,
            SuppressParallelInference = false,
        };

        GpuFallbackReason = null;

        if (useGpu)
        {
            EnsureGpuDependencyChainPinned();

            try
            {
                var gpuOptions = new YoloPredictorOptions
                {
                    UseCuda = true,
                    CudaDeviceId = 0,
                    Configuration = configuration,
                };
                var gpuPredictor = new YoloPredictor(modelPath, gpuOptions);

                _predictor?.Dispose();
                _predictor = gpuPredictor;
                _currentModelPath = modelPath;
                IsUsingGpu = true;
                return;
            }
            catch (Exception ex)
            {
                GpuFallbackReason = ex.Message;
                _gpuAvailableCache = false;
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        var sessionOptions = new SessionOptions
        {
            ExecutionMode = ExecutionMode.ORT_PARALLEL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            EnableMemoryPattern = true,
        };
        var cpuOptions = new YoloPredictorOptions
        {
            UseCuda = false,
            SessionOptions = sessionOptions,
            Configuration = configuration,
        };
        var cpuPredictor = new YoloPredictor(modelPath, cpuOptions);

        _predictor?.Dispose();
        _predictor = cpuPredictor;
        _currentModelPath = modelPath;
        IsUsingGpu = false;
    }

    public void UnloadModel()
    {
        _predictor?.Dispose();
        _predictor = null;
        _currentModelPath = null;
    }

    public async Task<(List<DetectionResult> Results, byte[]? PlottedImageBytes)> DetectAsync(
        System.Drawing.Bitmap bitmap, bool skipPlot = false)
    {
        if (_predictor == null)
            throw new InvalidOperationException("模型未加载");

        using var imageSharpImage = ConvertToImageSharp(bitmap);
        return await DetectCoreAsync(imageSharpImage, skipPlot);
    }

    public async Task<(List<DetectionResult> Results, byte[]? PlottedImageBytes)> DetectAsync(
        string imagePath, bool skipPlot = false)
    {
        if (_predictor == null)
            throw new InvalidOperationException("模型未加载");

        using var image = Image.Load(imagePath);
        return await DetectCoreAsync(image, skipPlot);
    }

    private async Task<(List<DetectionResult> Results, byte[]? PlottedImageBytes)> DetectCoreAsync(
        Image image, bool skipPlot = false)
    {
        var detectionResult = await _predictor!.DetectAsync(image);

        var results = new List<DetectionResult>();
        foreach (var box in detectionResult)
        {
            results.Add(new DetectionResult
            {
                Name = box.Name.ToString(),
                Confidence = box.Confidence,
                X = box.Bounds.X,
                Y = box.Bounds.Y,
                Width = box.Bounds.Width,
                Height = box.Bounds.Height
            });
        }

        byte[]? plottedBytes = null;
        if (!skipPlot)
        {
            using var plotted = await detectionResult.PlotImageAsync(image);
            using var ms = new MemoryStream();
            await plotted.SaveAsync(ms, new BmpEncoder { BitsPerPixel = BmpBitsPerPixel.Pixel32 });
            plottedBytes = ms.ToArray();
        }

        return (results, plottedBytes);
    }

    private static unsafe Image ConvertToImageSharp(System.Drawing.Bitmap bitmap)
    {
        var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var image = new Image<Bgra32>(bitmap.Width, bitmap.Height);
            var srcPtr = (byte*)bmpData.Scan0;
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    new ReadOnlySpan<Bgra32>(srcPtr + y * bmpData.Stride, accessor.Width)
                        .CopyTo(row);
                }
            });
            return image;
        }
        finally
        {
            bitmap.UnlockBits(bmpData);
        }
    }

    public void Dispose()
    {
        _predictor?.Dispose();
        _predictor = null;
    }
}
