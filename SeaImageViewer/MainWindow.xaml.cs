using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace SeaImageViewer;

public partial class MainWindow : Window
{
    private const double MinZoom = 0.05;
    private const double MaxZoom = 20.0;
    private const int ThumbnailLoadConcurrency = 4;
    private const string FolderPlaceholder = "Loading";

    private readonly ObservableCollection<ImageFileItem> _images = [];
    private readonly AnimatedImagePlayer _previewAnimationPlayer;
    private ICollectionView? _imageView;
    private CancellationTokenSource? _folderLoadCts;
    private CancellationTokenSource? _previewLoadCts;
    private string? _currentFolder;
    private double _previewZoom = 1.0;
    private ZoomMode _previewZoomMode = ZoomMode.Fit;
    private bool _isPanning;
    private Point _panStart;
    private Point _panOrigin;
    private bool _isSyncingFolderTreeSelection;

    public MainWindow()
    {
        InitializeComponent();

        _previewAnimationPlayer = new AnimatedImagePlayer(PreviewImage);
        ThumbnailList.ItemsSource = _images;
        _imageView = CollectionViewSource.GetDefaultView(_images);
        _imageView.Filter = FilterImage;
        UpdatePreviewZoomButtons();
    }

    protected override void OnClosed(EventArgs e)
    {
        _folderLoadCts?.Cancel();
        _previewLoadCts?.Cancel();
        _previewAnimationPlayer.Stop();
        base.OnClosed(e);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LoadDriveTree();

        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (Directory.Exists(pictures))
        {
            LoadFolder(pictures, syncFolderTree: true);
        }
    }

    private void LoadDriveTree()
    {
        FolderTree.Items.Clear();

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
        {
            FolderTree.Items.Add(CreateFolderTreeItem(drive.Name));
        }
    }

    private static TreeViewItem CreateFolderTreeItem(string path)
    {
        var item = new TreeViewItem
        {
            Header = GetFolderHeader(path),
            Tag = path
        };

        if (HasSubdirectories(path))
        {
            item.Items.Add(FolderPlaceholder);
        }

        item.Expanded += FolderTreeItem_Expanded;
        return item;
    }

    private static void FolderTreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem item || item.Tag is not string path)
        {
            return;
        }

        LoadFolderTreeChildren(item);
    }

    private static void LoadFolderTreeChildren(TreeViewItem item)
    {
        if (item.Tag is not string path)
        {
            return;
        }

        if (item.Items.Count != 1 || item.Items[0] is not string placeholder || placeholder != FolderPlaceholder)
        {
            return;
        }

        item.Items.Clear();

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(path).OrderBy(GetFolderHeader, StringComparer.CurrentCultureIgnoreCase))
            {
                item.Items.Add(CreateFolderTreeItem(directory));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            item.Items.Add(new TreeViewItem
            {
                Header = "无法访问",
                IsEnabled = false
            });
        }
    }

    private static bool HasSubdirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static string GetFolderHeader(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.Equals(root, path, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private void SyncFolderTreeSelection(string folder)
    {
        var targetPath = NormalizeFolderPath(folder);

        foreach (var rootItem in FolderTree.Items.OfType<TreeViewItem>())
        {
            if (rootItem.Tag is not string rootPath)
            {
                continue;
            }

            if (IsSameOrAncestorFolder(NormalizeFolderPath(rootPath), targetPath)
                && TrySelectFolderTreeItem(rootItem, targetPath))
            {
                return;
            }
        }
    }

    private bool TrySelectFolderTreeItem(TreeViewItem item, string targetPath)
    {
        if (item.Tag is not string itemPath)
        {
            return false;
        }

        var normalizedItemPath = NormalizeFolderPath(itemPath);
        if (string.Equals(normalizedItemPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            _isSyncingFolderTreeSelection = true;
            try
            {
                item.IsSelected = true;
                item.BringIntoView();
            }
            finally
            {
                _isSyncingFolderTreeSelection = false;
            }

            return true;
        }

        if (!IsSameOrAncestorFolder(normalizedItemPath, targetPath))
        {
            return false;
        }

        item.IsExpanded = true;
        LoadFolderTreeChildren(item);

        foreach (var childItem in item.Items.OfType<TreeViewItem>())
        {
            if (childItem.Tag is string childPath
                && IsSameOrAncestorFolder(NormalizeFolderPath(childPath), targetPath)
                && TrySelectFolderTreeItem(childItem, targetPath))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeFolderPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);

        if (string.Equals(root, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsSameOrAncestorFolder(string ancestorPath, string targetPath)
    {
        if (string.Equals(ancestorPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ancestorWithSeparator = ancestorPath.EndsWith(Path.DirectorySeparatorChar)
            ? ancestorPath
            : ancestorPath + Path.DirectorySeparatorChar;

        return targetPath.StartsWith(ancestorWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_isSyncingFolderTreeSelection)
        {
            return;
        }

        if (e.NewValue is TreeViewItem { Tag: string path } && Directory.Exists(path))
        {
            LoadFolder(path);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择图片目录",
            InitialDirectory = Directory.Exists(_currentFolder)
                ? _currentFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            LoadFolder(dialog.SelectedPath, syncFolderTree: true);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_currentFolder))
        {
            LoadFolder(_currentFolder);
        }
    }

    private async void LoadFolder(string folder, bool syncFolderTree = false)
    {
        _folderLoadCts?.Cancel();
        _previewLoadCts?.Cancel();

        var cts = new CancellationTokenSource();
        _folderLoadCts = cts;
        var token = cts.Token;

        _currentFolder = folder;
        if (syncFolderTree)
        {
            SyncFolderTreeSelection(folder);
        }

        _images.Clear();
        SearchTextBox.Clear();
        _imageView?.Refresh();
        ClearPreview();

        CurrentFolderText.Text = folder;
        ImageCountText.Text = string.Empty;
        StatusText.Text = "正在扫描图片...";

        try
        {
            var files = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();

                return Directory
                    .EnumerateFiles(folder)
                    .Where(ImageLoader.IsSupportedImage)
                    .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }, token);

            if (files.Count == 0)
            {
                StatusText.Text = "当前目录没有可识别的图片";
                ImageCountText.Text = "0";
                return;
            }

            var failed = await LoadThumbnailsAsync(files, token);

            StatusText.Text = failed == 0
                ? $"完成：{_images.Count} 张图片"
                : $"完成：{_images.Count} 张图片，跳过 {failed} 个文件";
            UpdateImageCount();
        }
        catch (OperationCanceledException)
        {
            // A newer folder selection has taken over.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            StatusText.Text = $"无法读取目录：{ex.Message}";
            ImageCountText.Text = "0";
        }
    }

    private bool FilterImage(object item)
    {
        if (item is not ImageFileItem image)
        {
            return false;
        }

        var filter = SearchTextBox?.Text?.Trim();
        return string.IsNullOrEmpty(filter)
            || image.FileName.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
    }

    private async Task<int> LoadThumbnailsAsync(IReadOnlyList<string> files, CancellationToken token)
    {
        var failed = 0;
        var completed = 0;
        var nextIndex = 0;
        var nextDisplayIndex = 0;
        var pendingResults = new Dictionary<int, ThumbnailLoadResult>();
        var runningTasks = new List<Task<ThumbnailLoadResult>>(Math.Min(ThumbnailLoadConcurrency, files.Count));

        while (nextIndex < files.Count && runningTasks.Count < ThumbnailLoadConcurrency)
        {
            runningTasks.Add(StartThumbnailLoad(nextIndex, files[nextIndex], token));
            nextIndex++;
        }

        while (runningTasks.Count > 0)
        {
            token.ThrowIfCancellationRequested();

            var finishedTask = await Task.WhenAny(runningTasks);
            runningTasks.Remove(finishedTask);

            var result = await finishedTask;
            completed++;

            if (result.Item is null)
            {
                failed++;
            }

            pendingResults[result.Index] = result;
            FlushReadyThumbnails(pendingResults, ref nextDisplayIndex);

            if (nextIndex < files.Count)
            {
                runningTasks.Add(StartThumbnailLoad(nextIndex, files[nextIndex], token));
                nextIndex++;
            }

            if (completed % 10 == 0 || completed == files.Count)
            {
                UpdateImageCount();
                StatusText.Text = failed == 0
                    ? $"已加载 {completed} / {files.Count}"
                    : $"已加载 {completed} / {files.Count}，跳过 {failed} 个文件";
            }
        }

        return failed;
    }

    private static Task<ThumbnailLoadResult> StartThumbnailLoad(int index, string file, CancellationToken token)
    {
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();

            try
            {
                return new ThumbnailLoadResult(index, ImageLoader.CreateThumbnailItem(file));
            }
            catch (Exception ex) when (ImageLoader.IsRecoverableImageError(ex))
            {
                return new ThumbnailLoadResult(index, null);
            }
        }, token);
    }

    private void FlushReadyThumbnails(Dictionary<int, ThumbnailLoadResult> pendingResults, ref int nextDisplayIndex)
    {
        while (pendingResults.Remove(nextDisplayIndex, out var result))
        {
            if (result.Item is not null)
            {
                AddThumbnailItem(result.Item);
            }

            nextDisplayIndex++;
        }
    }

    private void AddThumbnailItem(ImageFileItem item)
    {
        _images.Add(item);

        if (ThumbnailList.SelectedItem is null && FilterImage(item))
        {
            ThumbnailList.SelectedItem = item;
            ThumbnailList.ScrollIntoView(item);
        }
    }

    private readonly record struct ThumbnailLoadResult(int Index, ImageFileItem? Item);

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _imageView?.Refresh();
        UpdateImageCount();

        if (ThumbnailList.SelectedItem is ImageFileItem selected && !FilterImage(selected))
        {
            ThumbnailList.SelectedItem = null;
        }
    }

    private void UpdateImageCount()
    {
        var visibleCount = _imageView?.Cast<object>().Count() ?? _images.Count;
        ImageCountText.Text = visibleCount == _images.Count
            ? _images.Count.ToString()
            : $"{visibleCount} / {_images.Count}";
    }

    private async void ThumbnailList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThumbnailList.SelectedItem is ImageFileItem item)
        {
            await LoadPreviewAsync(item);
        }
        else
        {
            ClearPreview();
        }
    }

    private async Task LoadPreviewAsync(ImageFileItem item)
    {
        _previewLoadCts?.Cancel();
        _previewAnimationPlayer.Stop();
        var cts = new CancellationTokenSource();
        _previewLoadCts = cts;

        try
        {
            SelectedImageText.Text = item.FileName;
            StatusText.Text = $"正在打开 {item.FileName}...";

            var image = await Task.Run(() => ImageLoader.LoadDisplayImage(item.FilePath), cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            PreviewImage.Source = image.Source;
            PreviewImage.Width = image.Width;
            PreviewImage.Height = image.Height;
            PreviewImage.Visibility = Visibility.Visible;
            EmptyPreviewText.Visibility = Visibility.Collapsed;

            if (image.Animation is not null)
            {
                _previewAnimationPlayer.Start(image.Animation);
            }

            ApplyPreviewZoomMode();
            StatusText.Text = $"{item.FileName}  {item.PixelWidth} x {item.PixelHeight}  {ImageLoader.FormatFileSize(item.FileSize)}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ImageLoader.IsRecoverableImageError(ex))
        {
            ClearPreview();
            SelectedImageText.Text = item.FileName;
            StatusText.Text = $"无法打开图片：{ex.Message}";
        }
    }

    private void ClearPreview()
    {
        _previewAnimationPlayer.Stop();
        PreviewImage.Source = null;
        PreviewImage.Width = double.NaN;
        PreviewImage.Height = double.NaN;
        PreviewImage.Visibility = Visibility.Collapsed;
        EmptyPreviewText.Visibility = Visibility.Visible;
        SelectedImageText.Text = "预览";
        PreviewScaleTransform.ScaleX = 1.0;
        PreviewScaleTransform.ScaleY = 1.0;
        ZoomText.Text = "-";
        UpdatePreviewZoomButtons();
    }

    private void OpenViewer_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedImageViewer();
    }

    private void ThumbnailList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedImageViewer();
    }

    private void OpenSelectedImageViewer()
    {
        if (ThumbnailList.SelectedItem is not ImageFileItem selected)
        {
            return;
        }

        var images = ThumbnailList.Items.Cast<ImageFileItem>().ToList();
        var selectedIndex = Math.Max(0, images.IndexOf(selected));
        var viewer = new ImageViewerWindow(images.Select(image => image.FilePath).ToList(), selectedIndex)
        {
            Owner = this
        };

        viewer.Show();
    }

    private void FitPreview_Click(object sender, RoutedEventArgs e)
    {
        SetPreviewZoomMode(ZoomMode.Fit);
    }

    private void ActualSizePreview_Click(object sender, RoutedEventArgs e)
    {
        SetPreviewZoomMode(ZoomMode.ActualSize);
    }

    private void PreviewScroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_previewZoomMode == ZoomMode.Fit)
        {
            FitPreviewToViewport();
        }
    }

    private void PreviewScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (PreviewImage.Source is null)
        {
            return;
        }

        _previewZoomMode = ZoomMode.Custom;
        UpdatePreviewZoomButtons();
        var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        ZoomPreviewAt(factor, e.GetPosition(PreviewScroller));
        e.Handled = true;
    }

    private void PreviewScroller_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (PreviewImage.Source is null)
        {
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(PreviewScroller);
        _panOrigin = new Point(PreviewScroller.HorizontalOffset, PreviewScroller.VerticalOffset);
        PreviewScroller.CaptureMouse();
        PreviewScroller.Cursor = Cursors.SizeAll;
    }

    private void PreviewScroller_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        var current = e.GetPosition(PreviewScroller);
        PreviewScroller.ScrollToHorizontalOffset(_panOrigin.X - (current.X - _panStart.X));
        PreviewScroller.ScrollToVerticalOffset(_panOrigin.Y - (current.Y - _panStart.Y));
    }

    private void PreviewScroller_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        StopPreviewPan();
    }

    private void StopPreviewPan()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        PreviewScroller.ReleaseMouseCapture();
        PreviewScroller.Cursor = Cursors.Arrow;
    }

    private void FitPreviewToViewport()
    {
        if (PreviewImage.Source is not ImageSource image
            || PreviewScroller.ViewportWidth <= 0
            || PreviewScroller.ViewportHeight <= 0)
        {
            return;
        }

        var (imageWidth, imageHeight) = ImageLoader.GetImageSize(image);
        var horizontalScale = PreviewScroller.ViewportWidth / imageWidth;
        var verticalScale = PreviewScroller.ViewportHeight / imageHeight;
        var scale = Math.Min(horizontalScale, verticalScale);
        SetPreviewZoom(Math.Clamp(scale, MinZoom, MaxZoom));
        CenterPreview();
    }

    private void ZoomPreviewAt(double factor, Point cursorPosition)
    {
        var oldZoom = _previewZoom;
        var newZoom = Math.Clamp(oldZoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.0001)
        {
            return;
        }

        var nextHorizontalOffset = (PreviewScroller.HorizontalOffset + cursorPosition.X) * (newZoom / oldZoom) - cursorPosition.X;
        var nextVerticalOffset = (PreviewScroller.VerticalOffset + cursorPosition.Y) * (newZoom / oldZoom) - cursorPosition.Y;

        SetPreviewZoom(newZoom);

        Dispatcher.BeginInvoke(() =>
        {
            PreviewScroller.ScrollToHorizontalOffset(nextHorizontalOffset);
            PreviewScroller.ScrollToVerticalOffset(nextVerticalOffset);
        }, DispatcherPriority.Loaded);
    }

    private void SetPreviewZoom(double zoom)
    {
        _previewZoom = zoom;
        PreviewScaleTransform.ScaleX = zoom;
        PreviewScaleTransform.ScaleY = zoom;
        ZoomText.Text = $"{zoom:P0}";
    }

    private void SetPreviewZoomMode(ZoomMode mode)
    {
        _previewZoomMode = mode;
        UpdatePreviewZoomButtons();
        ApplyPreviewZoomMode();
    }

    private void ApplyPreviewZoomMode()
    {
        UpdatePreviewZoomButtons();

        if (PreviewImage.Source is null)
        {
            return;
        }

        switch (_previewZoomMode)
        {
            case ZoomMode.Fit:
                FitPreviewToViewport();
                break;
            case ZoomMode.ActualSize:
                SetPreviewZoom(1.0);
                CenterPreview();
                break;
            case ZoomMode.Custom:
                SetPreviewZoom(_previewZoom);
                CenterPreview();
                break;
        }
    }

    private void UpdatePreviewZoomButtons()
    {
        SetZoomModeButtonVisual(FitPreviewButton, _previewZoomMode == ZoomMode.Fit);
        SetZoomModeButtonVisual(ActualSizePreviewButton, _previewZoomMode == ZoomMode.ActualSize);
    }

    private static void SetZoomModeButtonVisual(Button button, bool isActive)
    {
        button.Background = isActive ? CreateBrush(0xEA, 0xF2, 0xFF) : CreateBrush(0xF8, 0xFA, 0xFC);
        button.BorderBrush = isActive ? CreateBrush(0x25, 0x63, 0xEB) : CreateBrush(0xC9, 0xD2, 0xDE);
        button.Foreground = isActive ? CreateBrush(0x1D, 0x4E, 0xD8) : CreateBrush(0x1F, 0x29, 0x37);
        button.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private void CenterPreview()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var horizontalOffset = Math.Max(0, (PreviewScroller.ExtentWidth - PreviewScroller.ViewportWidth) / 2);
            var verticalOffset = Math.Max(0, (PreviewScroller.ExtentHeight - PreviewScroller.ViewportHeight) / 2);
            PreviewScroller.ScrollToHorizontalOffset(horizontalOffset);
            PreviewScroller.ScrollToVerticalOffset(verticalOffset);
        }, DispatcherPriority.Loaded);
    }
}
