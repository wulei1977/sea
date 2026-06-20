using System.Globalization;
using System.IO;
using System.Text;

namespace SeaImageViewer;

public sealed class AppConfiguration
{
    private const string EnvFileName = ".env";

    private static readonly string[] KnownKeys =
    [
        "LANGUAGE",
        "DEFAULT_OPEN_FOLDER",
        "MAIN_LAYOUT_FOLDER_WIDTH",
        "MAIN_LAYOUT_THUMBNAILS_WIDTH",
        "MAIN_LAYOUT_PREVIEW_WIDTH",
        "PREVIEW_ZOOM_MODE",
        "VIEWER_ZOOM_MODE"
    ];

    public static AppConfiguration Instance { get; } = new();

    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _envPath;

    private AppConfiguration()
    {
        _envPath = FindEnvPath();
        Load();
        EnsureDefaults();
        Save();
    }

    public string EnvPath => _envPath;

    public string Language
    {
        get => GetString("LANGUAGE", "en-US");
        set => SetString("LANGUAGE", string.IsNullOrWhiteSpace(value) ? "en-US" : value);
    }

    public string DefaultOpenFolder
    {
        get => GetString("DEFAULT_OPEN_FOLDER", string.Empty);
        set => SetString("DEFAULT_OPEN_FOLDER", value.Trim());
    }

    public double? MainLayoutFolderWidth
    {
        get => GetDouble("MAIN_LAYOUT_FOLDER_WIDTH");
        set => SetDouble("MAIN_LAYOUT_FOLDER_WIDTH", value);
    }

    public double? MainLayoutThumbnailsWidth
    {
        get => GetDouble("MAIN_LAYOUT_THUMBNAILS_WIDTH");
        set => SetDouble("MAIN_LAYOUT_THUMBNAILS_WIDTH", value);
    }

    public double? MainLayoutPreviewWidth
    {
        get => GetDouble("MAIN_LAYOUT_PREVIEW_WIDTH");
        set => SetDouble("MAIN_LAYOUT_PREVIEW_WIDTH", value);
    }

    public ZoomMode PreviewZoomMode
    {
        get => GetZoomMode("PREVIEW_ZOOM_MODE", ZoomMode.Fit);
        set => SetString("PREVIEW_ZOOM_MODE", value.ToString());
    }

    public ZoomMode ViewerZoomMode
    {
        get => GetZoomMode("VIEWER_ZOOM_MODE", ZoomMode.Fit);
        set => SetString("VIEWER_ZOOM_MODE", value.ToString());
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_envPath)!);

        using var writer = new StreamWriter(_envPath, false, new UTF8Encoding(false));
        writer.WriteLine("# Sea Image Viewer configuration");
        writer.WriteLine("# This file is updated by the application.");

        foreach (var key in KnownKeys)
        {
            writer.WriteLine($"{key}={GetString(key, string.Empty)}");
        }

        foreach (var entry in _values
                     .Where(entry => !KnownKeys.Contains(entry.Key, StringComparer.OrdinalIgnoreCase))
                     .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteLine($"{entry.Key}={entry.Value}");
        }
    }

    private void Load()
    {
        if (!File.Exists(_envPath))
        {
            return;
        }

        foreach (var rawLine in File.ReadAllLines(_envPath, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            _values[key] = value;
        }
    }

    private void EnsureDefaults()
    {
        EnsureDefault("LANGUAGE", "en-US");
        EnsureDefault("DEFAULT_OPEN_FOLDER", string.Empty);
        EnsureDefault("MAIN_LAYOUT_FOLDER_WIDTH", "240");
        EnsureDefault("MAIN_LAYOUT_THUMBNAILS_WIDTH", string.Empty);
        EnsureDefault("MAIN_LAYOUT_PREVIEW_WIDTH", "400");
        EnsureDefault("PREVIEW_ZOOM_MODE", ZoomMode.Fit.ToString());
        EnsureDefault("VIEWER_ZOOM_MODE", ZoomMode.Fit.ToString());
    }

    private void EnsureDefault(string key, string value)
    {
        if (!_values.ContainsKey(key))
        {
            _values[key] = value;
        }
    }

    private string GetString(string key, string fallback)
    {
        return _values.TryGetValue(key, out var value) ? value : fallback;
    }

    private void SetString(string key, string value)
    {
        _values[key] = value;
        Save();
    }

    private double? GetDouble(string key)
    {
        var value = GetString(key, string.Empty);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : null;
    }

    private void SetDouble(string key, double? value)
    {
        _values[key] = value is > 0
            ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
            : string.Empty;
        Save();
    }

    private ZoomMode GetZoomMode(string key, ZoomMode fallback)
    {
        return Enum.TryParse(GetString(key, string.Empty), ignoreCase: true, out ZoomMode mode)
            ? mode
            : fallback;
    }

    private static string FindEnvPath()
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, EnvFileName);
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        var projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", EnvFileName));
        if (File.Exists(projectPath))
        {
            return projectPath;
        }

        return outputPath;
    }
}
