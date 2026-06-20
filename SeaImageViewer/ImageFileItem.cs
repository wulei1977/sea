using System.IO;
using System.Windows.Media;

namespace SeaImageViewer;

public sealed class ImageFileItem
{
    public ImageFileItem(
        string filePath,
        ImageSource thumbnail,
        int pixelWidth,
        int pixelHeight,
        long fileSize)
    {
        FilePath = filePath;
        Thumbnail = thumbnail;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        FileSize = fileSize;
    }

    public string FilePath { get; }

    public string FileName => Path.GetFileName(FilePath);

    public ImageSource Thumbnail { get; }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public long FileSize { get; }

    public string DetailText => $"{PixelWidth} x {PixelHeight}  {ImageLoader.FormatFileSize(FileSize)}";
}
