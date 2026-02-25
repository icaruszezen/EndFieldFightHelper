using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Compunet.YoloSharp;
using Compunet.YoloSharp.Plotting;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public class YoloDetectionService : IDisposable
{
    private YoloPredictor? _predictor;
    private string? _currentModelPath;

    public bool IsModelLoaded => _predictor != null;
    public string? ModelPath => _currentModelPath;

    public void LoadModel(string modelPath)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("模型文件不存在", modelPath);

        _predictor?.Dispose();
        _predictor = new YoloPredictor(modelPath);
        _currentModelPath = modelPath;
    }

    public void UnloadModel()
    {
        _predictor?.Dispose();
        _predictor = null;
        _currentModelPath = null;
    }

    public async Task<(List<DetectionResult> Results, byte[] PlottedImageBytes)> DetectAsync(System.Drawing.Bitmap bitmap)
    {
        if (_predictor == null)
            throw new InvalidOperationException("模型未加载");

        using var imageSharpImage = ConvertToImageSharp(bitmap);
        return await DetectCoreAsync(imageSharpImage);
    }

    public async Task<(List<DetectionResult> Results, byte[] PlottedImageBytes)> DetectAsync(string imagePath)
    {
        if (_predictor == null)
            throw new InvalidOperationException("模型未加载");

        using var image = Image.Load(imagePath);
        return await DetectCoreAsync(image);
    }

    private async Task<(List<DetectionResult> Results, byte[] PlottedImageBytes)> DetectCoreAsync(Image image)
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

        using var plotted = await detectionResult.PlotImageAsync(image);
        using var ms = new MemoryStream();
        await plotted.SaveAsync(ms, new BmpEncoder { BitsPerPixel = BmpBitsPerPixel.Pixel32 });
        var plottedBytes = ms.ToArray();

        return (results, plottedBytes);
    }

    private static Image ConvertToImageSharp(System.Drawing.Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;
        return Image.Load(ms);
    }

    public void Dispose()
    {
        _predictor?.Dispose();
        _predictor = null;
    }
}
