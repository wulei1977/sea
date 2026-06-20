# Sea Image Viewer

Sea Image Viewer is a local image viewer built with C# and WPF. It provides a lightweight ACDSee-style browsing workflow with folder navigation, thumbnail loading, image preview, zooming, panning, and a dedicated viewer window.

## Features

- Browse folders and list image files in the selected directory
- Display images as thumbnails
- Load thumbnails with limited concurrency, currently 4 images at a time
- Support common image formats: JPG, JPEG, PNG, BMP, GIF, TIF, TIFF, WEBP, ICO, CUR, and JFIF
- Preview images in the main window
- Open a dedicated viewer window by double-clicking a thumbnail or clicking the viewer button
- Fit-to-window, 1:1 display, zoom, and drag-to-pan support
- Fullscreen mode in the viewer window
- Right-click context menu in the viewer window
- Application and window icon support

## Viewer Controls

| Action | Result |
| --- | --- |
| Mouse wheel up | Previous image |
| Mouse wheel down | Next image |
| Ctrl + mouse wheel | Zoom image |
| Enter | Next image |
| Backspace | Previous image |
| Left arrow | Previous image |
| Right arrow | Next image |
| F | Fit to window |
| 1 | 1:1 display |
| + / - | Zoom in / out |
| F11 | Toggle fullscreen |
| Esc | Exit fullscreen, or close the viewer window when windowed |
| Left mouse drag | Pan image |
| Right click | Open context menu |

## Tech Stack

- C#
- WPF
- .NET 10 Windows Desktop

Target framework:

```xml
net10.0-windows
```

## Requirements

Install a .NET SDK that supports Windows Desktop development. You can check your installed SDKs with:

```powershell
dotnet --info
```

## Build

Run from the repository root:

```powershell
dotnet build SeaImageViewer.slnx
```

## Run

```powershell
dotnet run --project SeaImageViewer
```

You can also run the built executable directly:

```powershell
SeaImageViewer\bin\Debug\net10.0-windows\SeaImageViewer.exe
```

## Project Structure

```text
SeaImageViewer.slnx
SeaImageViewer/
  App.xaml
  MainWindow.xaml              Main window: folder tree, thumbnail list, preview area
  MainWindow.xaml.cs
  ImageViewerWindow.xaml       Dedicated image viewer window
  ImageViewerWindow.xaml.cs
  ImageLoader.cs               Image format detection, decoding, thumbnail loading
  ImageFileItem.cs             Image list data model
  ZoomMode.cs                  Zoom mode enum
  app_icon.png                 Window icon and toolbar icon resource
  app_icon.ico                 Application icon
```

## Icon Resources

The project contains two icon resources:

- `SeaImageViewer/app_icon.ico`: used as the executable application icon in Explorer, the taskbar, and similar places.
- `SeaImageViewer/app_icon.png`: packaged as a WPF resource and used for the main window icon, viewer window icon, and the small toolbar icon in the main window.

## Thumbnail Loading

Thumbnail loading uses limited concurrency instead of creating work for every image at once. The default concurrency is defined in `MainWindow.xaml.cs`:

```csharp
private const int ThumbnailLoadConcurrency = 4;
```

You can tune this value based on machine performance and image directory size.

## License

MIT
