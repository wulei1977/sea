using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace SeaImageViewer;

public sealed class LocalizationManager : INotifyPropertyChanged
{
    private const string LanguageFolderName = "Languages";

    public static LocalizationManager Instance { get; } = new();

    private readonly Dictionary<string, LanguagePack> _languagePacks = new(StringComparer.OrdinalIgnoreCase);
    private LanguagePack? _currentPack;

    private LocalizationManager()
    {
        LoadLanguagePacks();
        SetLanguage(SelectInitialCulture(), saveSelection: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public IReadOnlyList<LanguageInfo> Languages { get; private set; } = [];

    public LanguageInfo? CurrentLanguage => _currentPack?.Info;

    public string this[string key] => Translate(key);

    public void SetLanguage(string cultureName)
    {
        SetLanguage(cultureName, saveSelection: true);
    }

    public string Translate(string key)
    {
        if (_currentPack?.Strings.TryGetValue(key, out var text) == true)
        {
            return text;
        }

        if (_languagePacks.TryGetValue("zh-CN", out var fallback)
            && fallback.Strings.TryGetValue(key, out var fallbackText))
        {
            return fallbackText;
        }

        return key;
    }

    public string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentUICulture, Translate(key), args);
    }

    private void SetLanguage(string cultureName, bool saveSelection)
    {
        var pack = FindLanguagePack(cultureName);
        if (pack is null || string.Equals(pack.Info.CultureName, _currentPack?.Info.CultureName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentPack = pack;

        var culture = CultureInfo.GetCultureInfo(pack.Info.CultureName);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        if (saveSelection)
        {
            SaveSelectedCulture(pack.Info.CultureName);
        }

        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged("Item[]");
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private LanguagePack? FindLanguagePack(string cultureName)
    {
        if (_languagePacks.TryGetValue(cultureName, out var exactPack))
        {
            return exactPack;
        }

        var neutralName = cultureName.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(neutralName))
        {
            return _languagePacks.Values.FirstOrDefault(pack =>
                pack.Info.CultureName.StartsWith(neutralName + "-", StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private string SelectInitialCulture()
    {
        var configuredCulture = AppConfiguration.Instance.Language;
        if (!string.IsNullOrWhiteSpace(configuredCulture) && FindLanguagePack(configuredCulture) is not null)
        {
            return configuredCulture;
        }

        if (_languagePacks.ContainsKey("en-US"))
        {
            return "en-US";
        }

        return _languagePacks.Keys.FirstOrDefault() ?? CultureInfo.CurrentUICulture.Name;
    }

    private void LoadLanguagePacks()
    {
        var languageDirectory = FindLanguageDirectory();
        if (languageDirectory is null)
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(languageDirectory, "*.json"))
        {
            try
            {
                using var stream = File.OpenRead(file);
                var languageFile = JsonSerializer.Deserialize<LanguagePackFile>(stream, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (languageFile is null
                    || string.IsNullOrWhiteSpace(languageFile.Culture)
                    || string.IsNullOrWhiteSpace(languageFile.DisplayName)
                    || languageFile.Strings is null)
                {
                    continue;
                }

                var info = new LanguageInfo(languageFile.Culture, languageFile.DisplayName, languageFile.Order);
                _languagePacks[info.CultureName] = new LanguagePack(info, languageFile.Strings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Ignore broken language packs so one bad file does not prevent the app from starting.
            }
        }

        Languages = _languagePacks.Values
            .Select(pack => pack.Info)
            .OrderBy(info => info.Order)
            .ThenBy(info => info.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string? FindLanguageDirectory()
    {
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, LanguageFolderName);
        if (Directory.Exists(outputDirectory))
        {
            return outputDirectory;
        }

        var projectDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", LanguageFolderName));
        return Directory.Exists(projectDirectory) ? projectDirectory : null;
    }

    private static void SaveSelectedCulture(string cultureName)
    {
        AppConfiguration.Instance.Language = cultureName;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed record LanguagePack(LanguageInfo Info, IReadOnlyDictionary<string, string> Strings);

    private sealed class LanguagePackFile
    {
        public string Culture { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public int Order { get; init; } = 100;
        public Dictionary<string, string>? Strings { get; init; }
    }
}

public sealed record LanguageInfo(string CultureName, string DisplayName, int Order);
