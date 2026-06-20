using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SeaImageViewer;

public sealed record AnimatedImage(
    IReadOnlyList<BitmapSource> Frames,
    IReadOnlyList<TimeSpan> Delays,
    int Width,
    int Height);

public sealed record LoadedImage(
    ImageSource Source,
    int Width,
    int Height,
    AnimatedImage? Animation);
