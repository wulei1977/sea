using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SeaImageViewer;

public partial class ImageViewerWindow : Window
{
    private const double MinZoom = 0.05;
    private const double MaxZoom = 20.0;

    private readonly IReadOnlyList<string> _imagePaths;
    private readonly AnimatedImagePlayer _imageAnimationPlayer;
    private CancellationTokenSource? _loadCts;
    private int _index;
    private double _zoom = 1.0;
    private ZoomMode _zoomMode = ZoomMode.Fit;
    private bool _isPanning;
    private Point _panStart;
    private Point _panOrigin;
    private bool _isFullScreen;
    private WindowStyle _windowedStyle;
    private WindowState _windowedState;
    private ResizeMode _windowedResizeMode;
    private bool _windowedTopmost;
    private Visibility _windowedToolbarVisibility;

    public ImageViewerWindow(IReadOnlyList<string> imagePaths, int selectedIndex)
    {
        InitializeComponent();

        _imageAnimationPlayer = new AnimatedImagePlayer(MainImage);
        _imagePaths = imagePaths;
        _index = Math.Clamp(selectedIndex, 0, Math.Max(0, imagePaths.Count - 1));
        UpdateZoomModeButtons();
        UpdateFullScreenMenuText();
    }

    protected override void OnClosed(EventArgs e)
    {
        _loadCts?.Cancel();
        _imageAnimationPlayer.Stop();
        base.OnClosed(e);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadCurrentImageAsync();
    }

    private async Task LoadCurrentImageAsync()
    {
        if (_imagePaths.Count == 0)
        {
            EmptyText.Text = "没有图片";
            return;
        }

        _loadCts?.Cancel();
        _imageAnimationPlayer.Stop();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        var path = _imagePaths[_index];
        FileNameText.Text = Path.GetFileName(path);
        ViewerStateText.Text = $"{_index + 1} / {_imagePaths.Count}";
        EmptyText.Visibility = Visibility.Visible;
        EmptyText.Text = "正在加载";

        try
        {
            var image = await Task.Run(() => ImageLoader.LoadDisplayImage(path), cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            MainImage.Source = image.Source;
            MainImage.Width = image.Width;
            MainImage.Height = image.Height;
            MainImage.Visibility = Visibility.Visible;
            EmptyText.Visibility = Visibility.Collapsed;

            if (image.Animation is not null)
            {
                _imageAnimationPlayer.Start(image.Animation);
            }

            Title = $"{Path.GetFileName(path)} - 查看图片";
            ApplyZoomMode();
            UpdateStateText();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ImageLoader.IsRecoverableImageError(ex))
        {
            _imageAnimationPlayer.Stop();
            MainImage.Source = null;
            MainImage.Visibility = Visibility.Collapsed;
            EmptyText.Text = $"无法打开图片：{ex.Message}";
            EmptyText.Visibility = Visibility.Visible;
            UpdateStateText();
        }
    }

    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        await ShowPreviousAsync();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        await ShowNextAsync();
    }

    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        SetZoomMode(ZoomMode.Fit);
    }

    private void ActualSize_Click(object sender, RoutedEventArgs e)
    {
        SetZoomMode(ZoomMode.ActualSize);
    }

    private void ToggleFullScreen_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullScreen();
    }

    private void ImageSurface_ContextMenuOpening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
    {
        UpdateFullScreenMenuText();
    }

    private void ToggleFullScreen()
    {
        if (_isFullScreen)
        {
            ExitFullScreen();
        }
        else
        {
            EnterFullScreen();
        }
    }

    private void EnterFullScreen()
    {
        if (_isFullScreen)
        {
            return;
        }

        _windowedStyle = WindowStyle;
        _windowedState = WindowState;
        _windowedResizeMode = ResizeMode;
        _windowedTopmost = Topmost;
        _windowedToolbarVisibility = ToolbarBorder.Visibility;

        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ToolbarBorder.Visibility = Visibility.Collapsed;
        Topmost = true;
        WindowState = WindowState.Maximized;
        _isFullScreen = true;

        UpdateFullScreenMenuText();
    }

    private void ExitFullScreen()
    {
        if (!_isFullScreen)
        {
            return;
        }

        WindowState = WindowState.Normal;
        WindowStyle = _windowedStyle;
        ResizeMode = _windowedResizeMode;
        Topmost = _windowedTopmost;
        ToolbarBorder.Visibility = _windowedToolbarVisibility;
        WindowState = _windowedState == WindowState.Minimized ? WindowState.Normal : _windowedState;
        _isFullScreen = false;

        UpdateFullScreenMenuText();
    }

    private void UpdateFullScreenMenuText()
    {
        FullScreenMenuItem.Header = _isFullScreen ? "切换窗口" : "切换全屏";
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _zoomMode = ZoomMode.Custom;
        UpdateZoomModeButtons();
        ZoomAt(1 / 1.15, new Point(ImageScroller.ViewportWidth / 2, ImageScroller.ViewportHeight / 2));
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _zoomMode = ZoomMode.Custom;
        UpdateZoomModeButtons();
        ZoomAt(1.15, new Point(ImageScroller.ViewportWidth / 2, ImageScroller.ViewportHeight / 2));
    }

    private async Task ShowPreviousAsync()
    {
        if (_imagePaths.Count == 0)
        {
            return;
        }

        _index = (_index - 1 + _imagePaths.Count) % _imagePaths.Count;
        await LoadCurrentImageAsync();
    }

    private async Task ShowNextAsync()
    {
        if (_imagePaths.Count == 0)
        {
            return;
        }

        _index = (_index + 1) % _imagePaths.Count;
        await LoadCurrentImageAsync();
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                await ShowPreviousAsync();
                e.Handled = true;
                break;
            case Key.Right:
                await ShowNextAsync();
                e.Handled = true;
                break;
            case Key.Return:
                await ShowNextAsync();
                e.Handled = true;
                break;
            case Key.Back:
                await ShowPreviousAsync();
                e.Handled = true;
                break;
            case Key.Escape:
                if (_isFullScreen)
                {
                    ExitFullScreen();
                }
                else
                {
                    Close();
                }

                e.Handled = true;
                break;
            case Key.F11:
                ToggleFullScreen();
                e.Handled = true;
                break;
            case Key.F:
                SetZoomMode(ZoomMode.Fit);
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                SetZoomMode(ZoomMode.ActualSize);
                e.Handled = true;
                break;
            case Key.Add:
            case Key.OemPlus:
                ZoomIn_Click(sender, e);
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                ZoomOut_Click(sender, e);
                e.Handled = true;
                break;
        }
    }

    private void ImageScroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_zoomMode == ZoomMode.Fit)
        {
            FitImageToViewport();
        }
    }

    private async void ImageScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            if (e.Delta > 0)
            {
                await ShowPreviousAsync();
            }
            else
            {
                await ShowNextAsync();
            }

            e.Handled = true;
            return;
        }

        if (MainImage.Source is null)
        {
            return;
        }

        _zoomMode = ZoomMode.Custom;
        UpdateZoomModeButtons();
        var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        ZoomAt(factor, e.GetPosition(ImageScroller));
        e.Handled = true;
    }

    private void ImageScroller_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (MainImage.Source is null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            StopPan();
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(ImageScroller);
        _panOrigin = new Point(ImageScroller.HorizontalOffset, ImageScroller.VerticalOffset);
        ImageScroller.CaptureMouse();
        ImageScroller.Cursor = Cursors.SizeAll;
    }

    private void ImageScroller_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        var current = e.GetPosition(ImageScroller);
        ImageScroller.ScrollToHorizontalOffset(_panOrigin.X - (current.X - _panStart.X));
        ImageScroller.ScrollToVerticalOffset(_panOrigin.Y - (current.Y - _panStart.Y));
    }

    private void ImageScroller_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        StopPan();
    }

    private void StopPan()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        ImageScroller.ReleaseMouseCapture();
        ImageScroller.Cursor = Cursors.Arrow;
    }

    private void FitImageToViewport()
    {
        if (MainImage.Source is not ImageSource image
            || ImageScroller.ViewportWidth <= 0
            || ImageScroller.ViewportHeight <= 0)
        {
            return;
        }

        var (imageWidth, imageHeight) = ImageLoader.GetImageSize(image);
        var horizontalScale = ImageScroller.ViewportWidth / imageWidth;
        var verticalScale = ImageScroller.ViewportHeight / imageHeight;
        SetZoom(Math.Clamp(Math.Min(horizontalScale, verticalScale), MinZoom, MaxZoom));
        CenterImage();
    }

    private void ZoomAt(double factor, Point cursorPosition)
    {
        if (MainImage.Source is null)
        {
            return;
        }

        var oldZoom = _zoom;
        var newZoom = Math.Clamp(oldZoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.0001)
        {
            return;
        }

        var nextHorizontalOffset = (ImageScroller.HorizontalOffset + cursorPosition.X) * (newZoom / oldZoom) - cursorPosition.X;
        var nextVerticalOffset = (ImageScroller.VerticalOffset + cursorPosition.Y) * (newZoom / oldZoom) - cursorPosition.Y;

        SetZoom(newZoom);

        Dispatcher.BeginInvoke(() =>
        {
            ImageScroller.ScrollToHorizontalOffset(nextHorizontalOffset);
            ImageScroller.ScrollToVerticalOffset(nextVerticalOffset);
        }, DispatcherPriority.Loaded);
    }

    private void SetZoom(double zoom)
    {
        _zoom = zoom;
        ImageScaleTransform.ScaleX = zoom;
        ImageScaleTransform.ScaleY = zoom;
        UpdateStateText();
    }

    private void SetZoomMode(ZoomMode mode)
    {
        _zoomMode = mode;
        UpdateZoomModeButtons();
        ApplyZoomMode();
    }

    private void ApplyZoomMode()
    {
        UpdateZoomModeButtons();

        if (MainImage.Source is null)
        {
            return;
        }

        switch (_zoomMode)
        {
            case ZoomMode.Fit:
                FitImageToViewport();
                break;
            case ZoomMode.ActualSize:
                SetZoom(1.0);
                CenterImage();
                break;
            case ZoomMode.Custom:
                SetZoom(_zoom);
                CenterImage();
                break;
        }
    }

    private void UpdateZoomModeButtons()
    {
        SetZoomModeButtonVisual(FitViewerButton, _zoomMode == ZoomMode.Fit);
        SetZoomModeButtonVisual(ActualSizeViewerButton, _zoomMode == ZoomMode.ActualSize);
    }

    private static void SetZoomModeButtonVisual(System.Windows.Controls.Button button, bool isActive)
    {
        button.Background = isActive ? CreateBrush(0x2F, 0x6F, 0xE8) : CreateBrush(0x1F, 0x29, 0x37);
        button.BorderBrush = isActive ? CreateBrush(0x8B, 0xB4, 0xFF) : CreateBrush(0x3A, 0x43, 0x50);
        button.Foreground = isActive ? CreateBrush(0xFF, 0xFF, 0xFF) : CreateBrush(0xF4, 0xF7, 0xFB);
        button.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private void CenterImage()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var horizontalOffset = Math.Max(0, (ImageScroller.ExtentWidth - ImageScroller.ViewportWidth) / 2);
            var verticalOffset = Math.Max(0, (ImageScroller.ExtentHeight - ImageScroller.ViewportHeight) / 2);
            ImageScroller.ScrollToHorizontalOffset(horizontalOffset);
            ImageScroller.ScrollToVerticalOffset(verticalOffset);
        }, DispatcherPriority.Loaded);
    }

    private void UpdateStateText()
    {
        ViewerStateText.Text = _imagePaths.Count == 0
            ? "-"
            : $"{_index + 1} / {_imagePaths.Count}    {_zoom:P0}";
    }
}
