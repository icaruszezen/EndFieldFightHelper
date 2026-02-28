using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
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

    public bool IsModelLoaded => _predictor != null;
    public string? ModelPath => _currentModelPath;
    public GpuDeviceInfo ActiveDevice { get; private set; } = GpuDeviceInfo.CpuDevice;
    public string? GpuFallbackReason { get; private set; }

    public void LoadModel(string modelPath, GpuDeviceInfo? device = null,
        float confidence = 0.5f, float iou = 0.45f)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("模型文件不存在", modelPath);

        device ??= GpuDeviceInfo.CpuDevice;

        var configuration = new YoloConfiguration
        {
            Confidence = confidence,
            IoU = iou,
            ApplyAutoOrient = false,
            SuppressParallelInference = false,
        };

        GpuFallbackReason = null;
        YoloPredictor? newPredictor = null;
        var actualDevice = GpuDeviceInfo.CpuDevice;

        if (!device.IsCpu)
        {
            try
            {
                var so = new SessionOptions();
                so.AppendExecutionProvider_DML(device.DeviceId);
                so.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

                newPredictor = new YoloPredictor(modelPath, new YoloPredictorOptions
                {
                    UseCuda = false,
                    SessionOptions = so,
                    Configuration = configuration,
                });
                actualDevice = device;
            }
            catch (Exception ex)
            {
                GpuFallbackReason = ex.Message;
            }
        }

        if (newPredictor == null)
        {
            var cpuSession = new SessionOptions
            {
                ExecutionMode = ExecutionMode.ORT_PARALLEL,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                EnableMemoryPattern = true,
            };
            newPredictor = new YoloPredictor(modelPath, new YoloPredictorOptions
            {
                UseCuda = false,
                SessionOptions = cpuSession,
                Configuration = configuration,
            });
            actualDevice = GpuDeviceInfo.CpuDevice;
        }

        _predictor?.Dispose();
        _predictor = newPredictor;
        _currentModelPath = modelPath;
        ActiveDevice = actualDevice;
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
