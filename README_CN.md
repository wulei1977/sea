# Sea Image Viewer

Sea Image Viewer 是一个使用 C# 和 WPF 开发的本地看图软件，功能参考 ACDSee 的基础浏览体验：目录浏览、缩略图列表、图片查看窗口、缩放和平移。

## 功能

- 浏览磁盘目录并列出当前目录中的图片文件
- 以缩略图方式展示图片，缩略图加载使用限制并发，默认同时加载 4 张
- 支持常见图片格式：JPG、JPEG、PNG、BMP、GIF、TIF、TIFF、WEBP、ICO、CUR、JFIF
- 右侧预览区支持适应窗口、1:1 显示、鼠标滚轮缩放和拖拽平移
- 双击缩略图或点击“查看”可打开独立图片查看窗口
- 查看窗口支持上一张、下一张、全屏、适应、1:1、缩放和平移
- 查看窗口支持右键菜单
- 支持应用图标和窗体图标

## 查看窗口操作

| 操作 | 功能 |
| --- | --- |
| 鼠标滚轮向上 | 上一张 |
| 鼠标滚轮向下 | 下一张 |
| Ctrl + 鼠标滚轮 | 缩放图片 |
| Enter | 下一张 |
| Backspace | 上一张 |
| 左方向键 | 上一张 |
| 右方向键 | 下一张 |
| F | 适应窗口 |
| 1 | 1:1 显示 |
| + / - | 放大 / 缩小 |
| F11 | 切换全屏 |
| Esc | 全屏时退出全屏，窗口模式下关闭查看窗口 |
| 鼠标左键拖拽 | 平移图片 |
| 鼠标右键 | 打开上下文菜单 |

## 技术栈

- C#
- WPF
- .NET 10 Windows Desktop

项目目标框架：

```xml
net10.0-windows
```

## 运行环境

需要安装支持 Windows Desktop 的 .NET SDK。可通过以下命令确认：

```powershell
dotnet --info
```

## 构建

在项目根目录执行：

```powershell
dotnet build SeaImageViewer.slnx
```

## 运行

```powershell
dotnet run --project SeaImageViewer
```

也可以直接运行构建后的程序：

```powershell
SeaImageViewer\bin\Debug\net10.0-windows\SeaImageViewer.exe
```

## 项目结构

```text
SeaImageViewer.slnx
SeaImageViewer/
  App.xaml
  MainWindow.xaml              主窗口：目录树、缩略图列表、预览区
  MainWindow.xaml.cs
  ImageViewerWindow.xaml       独立查看窗口
  ImageViewerWindow.xaml.cs
  ImageLoader.cs               图片格式识别、解码、缩略图加载
  ImageFileItem.cs             图片列表数据模型
  ZoomMode.cs                  缩放模式枚举
  app_icon.png                 窗体图标和工具栏图标资源
  app_icon.ico                 应用程序图标
```

## 资源说明

项目包含两类图标资源：

- `SeaImageViewer/app_icon.ico`：用于生成 exe 应用程序图标，显示在资源管理器、任务栏等位置。
- `SeaImageViewer/app_icon.png`：作为 WPF 资源打包，用于主窗口图标、查看窗口图标，以及主窗口顶部工具栏左侧的小图标。

## 说明

缩略图加载采用限制并发策略，不会一次性为所有图片创建任务。默认并发数位于 `MainWindow.xaml.cs`：

```csharp
private const int ThumbnailLoadConcurrency = 4;
```

如果目录中图片很多，可以根据机器性能适当调大或调小该值。

## License

MIT
