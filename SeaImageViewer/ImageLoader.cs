using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;
using Drawing = System.Drawing;
using DrawingImaging = System.Drawing.Imaging;

namespace SeaImageViewer;

public static class ImageLoader
{
    private const int GifFrameDelayPropertyId = 0x5100;
    private static readonly TimeSpan DefaultGifFrameDelay = TimeSpan.FromMilliseconds(100);

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".gif",
        ".tif",
        ".tiff",
        ".webp",
        ".ico",
        ".cur",
        ".jfif",
        ".svg",
        ".svgz"
    };

    public static bool IsSupportedImage(string path)
    {
        return SupportedExtensions.Contains(Path.GetExtension(path));
    }

    public static ImageFileItem CreateThumbnailItem(string path, int decodePixelWidth = 220)
    {
        if (IsSvgFile(path))
        {
            var image = LoadSvgImage(path);
            var (svgWidth, svgHeight) = GetImageSize(image);
            var svgFileSize = new FileInfo(path).Length;

            return new ImageFileItem(path, image, svgWidth, svgHeight, svgFileSize);
        }

        var (width, height) = ReadImageSize(path);
        var thumbnail = LoadBitmap(path, decodePixelWidth);
        var fileSize = new FileInfo(path).Length;

        return new ImageFileItem(path, thumbnail, width, height, fileSize);
    }

    public static ImageSource LoadBitmap(string path, int? decodePixelWidth = null)
    {
        return IsSvgFile(path)
            ? LoadSvgImage(path)
            : IsIconFile(path)
            ? LoadIconBitmap(path, decodePixelWidth)
            : LoadBitmapImage(path, decodePixelWidth);
    }

    public static LoadedImage LoadDisplayImage(string path)
    {
        var animation = LoadAnimatedGif(path);
        if (animation is not null)
        {
            return new LoadedImage(animation.Frames[0], animation.Width, animation.Height, animation);
        }

        var image = LoadBitmap(path);
        var (width, height) = GetImageSize(image);
        return new LoadedImage(image, width, height, null);
    }

    public static (int Width, int Height) GetImageSize(ImageSource image)
    {
        if (image is BitmapSource bitmap)
        {
            return (bitmap.PixelWidth, bitmap.PixelHeight);
        }

        var width = Math.Max(1, (int)Math.Ceiling(image.Width));
        var height = Math.Max(1, (int)Math.Ceiling(image.Height));

        return (width, height);
    }

    public static bool IsRecoverableImageError(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or FileFormatException
            or InvalidOperationException
            or ArgumentException
            or ExternalException
            or System.Xml.XmlException;
    }

    private static BitmapSource LoadBitmapImage(string path, int? decodePixelWidth)
    {
        using var stream = File.OpenRead(path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.None;
        bitmap.StreamSource = stream;

        if (decodePixelWidth is > 0)
        {
            bitmap.DecodePixelWidth = decodePixelWidth.Value;
        }

        bitmap.EndInit();
        bitmap.Freeze();

        return NormalizeBitmapForDisplay(bitmap);
    }

    private static BitmapSource LoadIconBitmap(string path, int? decodePixelWidth)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);

        BitmapSource source = SelectBestFrame(decoder, decodePixelWidth);

        if (decodePixelWidth is > 0 && source.PixelWidth > decodePixelWidth.Value)
        {
            var scale = decodePixelWidth.Value / (double)source.PixelWidth;
            source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        }

        return NormalizeBitmapForDisplay(source);
    }

    private static BitmapSource NormalizeBitmapForDisplay(BitmapSource source)
    {
        if (IsDisplayFriendlyFormat(source.Format))
        {
            if (source.CanFreeze && !source.IsFrozen)
            {
                source.Freeze();
            }

            return source;
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static bool IsDisplayFriendlyFormat(PixelFormat format)
    {
        return format == PixelFormats.Bgr24
            || format == PixelFormats.Rgb24
            || format == PixelFormats.Bgr32
            || format == PixelFormats.Bgra32
            || format == PixelFormats.Pbgra32
            || format == PixelFormats.Gray8;
    }

    private static AnimatedImage? LoadAnimatedGif(string path)
    {
        if (!IsGifFile(path))
        {
            return null;
        }

        using var image = Drawing.Image.FromFile(path);
        var frameDimension = new DrawingImaging.FrameDimension(image.FrameDimensionsList[0]);
        var frameCount = image.GetFrameCount(frameDimension);
        if (frameCount <= 1)
        {
            return null;
        }

        var delays = ReadGifFrameDelays(image, frameCount);
        var frames = new List<BitmapSource>(frameCount);

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            image.SelectActiveFrame(frameDimension, frameIndex);
            frames.Add(CreateGifFrameBitmap(image));
        }

        return new AnimatedImage(frames, delays, image.Width, image.Height);
    }

    private static IReadOnlyList<TimeSpan> ReadGifFrameDelays(Drawing.Image image, int frameCount)
    {
        var delays = Enumerable.Repeat(DefaultGifFrameDelay, frameCount).ToArray();
        if (!image.PropertyIdList.Contains(GifFrameDelayPropertyId))
        {
            return delays;
        }

        try
        {
            if (image.GetPropertyItem(GifFrameDelayPropertyId)?.Value is not byte[] delayValues)
            {
                return delays;
            }

            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var offset = frameIndex * sizeof(int);
                if (offset + sizeof(int) > delayValues.Length)
                {
                    break;
                }

                var hundredths = BitConverter.ToInt32(delayValues, offset);
                delays[frameIndex] = NormalizeGifFrameDelay(hundredths);
            }
        }
        catch (ArgumentException)
        {
            return delays;
        }

        return delays;
    }

    private static TimeSpan NormalizeGifFrameDelay(int hundredths)
    {
        if (hundredths <= 0)
        {
            return DefaultGifFrameDelay;
        }

        return TimeSpan.FromMilliseconds(Math.Max(20, hundredths * 10));
    }

    private static BitmapSource CreateGifFrameBitmap(Drawing.Image image)
    {
        using var bitmap = new Drawing.Bitmap(image.Width, image.Height, DrawingImaging.PixelFormat.Format32bppPArgb);
        bitmap.SetResolution(96, 96);

        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(Drawing.Color.Transparent);
            graphics.DrawImage(image, new Drawing.Rectangle(0, 0, image.Width, image.Height));
        }

        return ConvertDrawingBitmapToBitmapSource(bitmap);
    }

    private static BitmapSource ConvertDrawingBitmapToBitmapSource(Drawing.Bitmap bitmap)
    {
        var rect = new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, DrawingImaging.ImageLockMode.ReadOnly, DrawingImaging.PixelFormat.Format32bppPArgb);

        try
        {
            var stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * bitmap.Height];

            for (var row = 0; row < bitmap.Height; row++)
            {
                var source = IntPtr.Add(data.Scan0, row * data.Stride);
                Marshal.Copy(source, pixels, row * stride, stride);
            }

            var sourceBitmap = BitmapSource.Create(
                bitmap.Width,
                bitmap.Height,
                96,
                96,
                PixelFormats.Pbgra32,
                null,
                pixels,
                stride);
            sourceBitmap.Freeze();
            return sourceBitmap;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static DrawingImage LoadSvgImage(string path)
    {
        var settings = new WpfDrawingSettings
        {
            IncludeRuntime = false,
            TextAsGeometry = true,
            OptimizePath = true
        };

        using var reader = new FileSvgReader(settings, false);
        var drawing = reader.Read(path);

        if (drawing is null || drawing.Bounds.IsEmpty)
        {
            throw new FileFormatException("The SVG contains no drawable content.");
        }

        if (drawing.CanFreeze)
        {
            drawing.Freeze();
        }

        var image = new DrawingImage(drawing);
        image.Freeze();

        return image;
    }

    public static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }

    private static (int Width, int Height) ReadImageSize(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.None);

        var frame = SelectBestFrame(decoder, null);
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private static BitmapSource SelectBestFrame(BitmapDecoder decoder, int? preferredWidth)
    {
        if (decoder.Frames.Count == 0)
        {
            throw new FileFormatException("The image contains no frames.");
        }

        if (preferredWidth is > 0)
        {
            return decoder.Frames
                .OrderBy(frame => Math.Abs(frame.PixelWidth - preferredWidth.Value))
                .ThenByDescending(frame => (long)frame.PixelWidth * frame.PixelHeight)
                .First();
        }

        return decoder.Frames
            .OrderByDescending(frame => (long)frame.PixelWidth * frame.PixelHeight)
            .First();
    }

    private static bool IsIconFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".ico", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cur", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGifFile(string path)
    {
        return Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSvgFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".svg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".svgz", StringComparison.OrdinalIgnoreCase);
    }
}
