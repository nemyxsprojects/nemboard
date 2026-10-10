using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Soundboard.Core;

namespace Soundboard.App;

public partial class MainWindow : Window
{
    private readonly string _appDataDirectory;
    private readonly SoundLibrary _library = new();
    private readonly AudioDeviceService _deviceService = new();
    private readonly AudioRouter _audioRouter;
    private readonly Dictionary<SoundTileViewModel, PlaybackSession> _activeSessions = [];
    private readonly SettingsStore _settingsStore;
    private readonly DispatcherTimer _mixerSaveTimer;
    private AppSettings _settings = new();
    private CoreWebView2Environment? _webEnvironment;
    private bool _sortAscending = true;
    private string _browserUrl = "https://www.myinstants.com/";
    private bool _settingsLoaded;
    private int _playbackCount;

    public MainWindow()
    {
        _appDataDirectory = ResolveAppDataDirectory();
        InitializeComponent();
        DarkTitleBar.Apply(this);
        LanguageCombo.ItemsSource = new[]
        {
            new LanguageOption("en", "EN", "English"),
            new LanguageOption("fr", "FR", "Français"),
            new LanguageOption("es", "ES", "Español"),
            new LanguageOption("de", "DE", "Deutsch"),
            new LanguageOption("it", "IT", "Italiano"),
            new LanguageOption("pt", "PT", "Português")
        };
        LanguageCombo.SelectedValue = "en";
        ApplyLanguage("en");
        DataContext = this;
        _audioRouter = new AudioRouter(_deviceService);
        _audioRouter.ActivePlaybackCountChanged += AudioRouter_ActivePlaybackCountChanged;
        _settingsStore = new SettingsStore(Path.Combine(_appDataDirectory, "settings.json"));
        _mixerSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _mixerSaveTimer.Tick += async (_, _) =>
        {
            _mixerSaveTimer.Stop();
            await _settingsStore.SaveAsync(_settings);
        };
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    public ObservableCollection<SoundTileViewModel> VisibleSounds { get; } = [];

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var bootstrapLanguage = ReadSetupLanguage();
        _settings = await _settingsStore.LoadAsync();
        MigrateImportedPaths();
        if (bootstrapLanguage is not null)
        {
            _settings.LanguageCode = bootstrapLanguage;
        }

        _settings.LanguageCode = UiText.NormalizeLanguage(_settings.LanguageCode);
        _settings.VoiceVolume = Math.Clamp(_settings.VoiceVolume, 0.0, 2.0);
        _settings.SoundVolume = Math.Clamp(_settings.SoundVolume, 0.0, 1.0);
        LanguageCombo.SelectedValue = _settings.LanguageCode;
        VoiceMixSlider.Value = _settings.VoiceVolume * 100.0;
        SoundMixSlider.Value = _settings.SoundVolume * 100.0;
        UpdateMixerLabels();
        ApplyMixerLevels();
        ApplyLanguage(_settings.LanguageCode);
        _settingsLoaded = true;
        await _settingsStore.SaveAsync(_settings);

        LoadSavedLibrary();
        UpdateVisibleSounds();

        var configured = await EnsureAutomaticAudioConfigurationAsync();
        if (!_settings.OnboardingCompleted)
        {
            configured = await OpenOnboardingAsync();
        }

        if (configured)
        {
            ActivateRouting();
        }
        else
        {
            _ = Dispatcher.BeginInvoke(() => OpenAudioSetup());
        }
    }

    private void LoadSavedLibrary()
    {
        var clips = new List<SoundClip>();
        foreach (var filePath in _settings.ImportedFiles.Where(File.Exists))
        {
            try
            {
                clips.Add(CreateClip(filePath, clips.Count));
            }
            catch
            {
                // A corrupt or partially copied audio file is skipped without blocking startup.
            }
        }

        _library.AddRange(clips);
    }

    private static SoundClip CreateClip(string filePath, int index)
    {
        var hotkey = index < 12 ? $"F{index + 1}" : string.Empty;
        return new SoundClip(
            Guid.NewGuid().ToString("N"),
            Path.GetFileNameWithoutExtension(filePath).Replace('-', ' ').Replace('_', ' '),
            filePath,
            AudioRouter.ReadDuration(filePath),
            hotkey);
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = T("ImportDialogTitle"),
            Filter = T("AudioFilesFilter"),
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await ImportFilesAsync(dialog.FileNames);
    }

    private async Task ImportFilesAsync(IEnumerable<string> sourcePaths)
    {
        var candidates = sourcePaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (candidates.Length == 0)
        {
            return;
        }

        var libraryDirectory = Path.Combine(_appDataDirectory, "Library");
        Directory.CreateDirectory(libraryDirectory);
        var imported = new List<SoundClip>();
        var skipped = 0;

        foreach (var sourcePath in candidates)
        {
            if (!SoundLibrary.IsSupported(sourcePath))
            {
                skipped++;
                continue;
            }

            try
            {
                var destinationPath = GetDestinationPath(libraryDirectory, sourcePath);
                if (!File.Exists(destinationPath))
                {
                    File.Copy(sourcePath, destinationPath);
                }

                if (_settings.ImportedFiles.Contains(destinationPath, StringComparer.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                imported.Add(CreateClip(destinationPath, _library.Items.Count + imported.Count));
                _settings.ImportedFiles.Add(destinationPath);
            }
            catch
            {
                skipped++;
            }
        }

        _library.AddRange(imported);
        await _settingsStore.SaveAsync(_settings);
        UpdateVisibleSounds();
        var plural = imported.Count > 1 ? "s" : string.Empty;
        AudioStatusText.Text = imported.Count > 0
            ? string.Format(T("ImportedCount"), imported.Count, plural)
            : T("NoNewSound");

        if (skipped > 0)
        {
            RouteSummaryText.Text = string.Format(T("SkippedCount"), skipped, skipped > 1 ? "s" : string.Empty);
        }
    }

    private void LibraryView_DragOver(object sender, DragEventArgs e)
    {
        var supported = e.Data.GetDataPresent(DataFormats.FileDrop) &&
                        e.Data.GetData(DataFormats.FileDrop) is string[] files &&
                        files.Any(SoundLibrary.IsSupported);
        e.Effects = supported ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void LibraryView_DragLeave(object sender, DragEventArgs e) =>
        DropOverlay.Visibility = Visibility.Collapsed;

    private async void LibraryView_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            await ImportFilesAsync(files);
        }

        e.Handled = true;
    }

    private static string GetDestinationPath(string directory, string sourcePath)
    {
        var candidate = Path.Combine(directory, Path.GetFileName(sourcePath));
        if (!File.Exists(candidate) || new FileInfo(candidate).Length == new FileInfo(sourcePath).Length)
        {
            return candidate;
        }

        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);
        for (var suffix = 2; ; suffix++)
        {
            candidate = Path.Combine(directory, $"{name} ({suffix}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (SearchHint is not null)
        {
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateVisibleSounds();
    }

    private void SortButton_Click(object sender, RoutedEventArgs e)
    {
        _sortAscending = !_sortAscending;
        ((System.Windows.Controls.Button)sender).Content = T(_sortAscending ? "SortAz" : "SortZa");
        UpdateVisibleSounds();
    }

    private void UpdateVisibleSounds()
    {
        if (SearchBox is null)
        {
            return;
        }

        var existing = VisibleSounds.ToDictionary(tile => tile.Clip.Id);
        var clips = _library.Search(SearchBox.Text);
        clips = (_sortAscending
                ? clips.OrderBy(clip => clip.Name, StringComparer.CurrentCultureIgnoreCase)
                : clips.OrderByDescending(clip => clip.Name, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();

        VisibleSounds.Clear();
        foreach (var clip in clips)
        {
            VisibleSounds.Add(existing.TryGetValue(clip.Id, out var tile) ? tile : new SoundTileViewModel(clip));
        }

        EmptyState.Visibility = VisibleSounds.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SoundItems.Visibility = VisibleSounds.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyTitle.Text = T(_library.Items.Count == 0 ? "NoImportedSounds" : "NoSoundsFound");
        EmptyCopy.Text = _library.Items.Count == 0
            ? T("ImportEmptyCopy")
            : T("SearchEmptyCopy");
    }

    private void SoundButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not SoundTileViewModel tile)
        {
            return;
        }

        ToggleSound(tile);
    }

    private void ToggleSound(SoundTileViewModel tile)
    {
        if (!_settings.SetupCompleted)
        {
            OpenAudioSetup();
            return;
        }

        if (_activeSessions.Remove(tile, out var active))
        {
            active.Stop();
            tile.IsPlaying = false;
            return;
        }

        try
        {
            tile.IsPlaying = true;
            var session = _audioRouter.Play(tile.FilePath, _settings.CableDeviceId, _settings.MonitorDeviceId, () =>
            {
                Dispatcher.Invoke(() =>
                {
                    _activeSessions.Remove(tile);
                    tile.IsPlaying = false;
                });
            });
            _activeSessions[tile] = session;
        }
        catch (Exception exception)
        {
            tile.IsPlaying = false;
            AudioStatusText.Text = T("PlaybackFailed");
            RouteSummaryText.Text = exception.Message == AudioRouter.NoOutputSelectedMessage
                ? T("SelectOutput")
                : exception.Message;
        }
    }

    private void StopAllButton_Click(object sender, RoutedEventArgs e)
    {
        _audioRouter.StopAll();
        foreach (var tile in _activeSessions.Keys.ToArray())
        {
            tile.IsPlaying = false;
        }

        _activeSessions.Clear();
    }

    private void AudioRouter_ActivePlaybackCountChanged(object? sender, int count) =>
        Dispatcher.Invoke(() =>
        {
            _playbackCount = count;
            PlaybackCountText.Text = string.Format(T("PlaybackCount"), count);
        });

    private async void OpenAudioSetup_Click(object sender, RoutedEventArgs e) =>
        await OpenAudioSetupAsync();

    private void OpenAudioSetup() => _ = OpenAudioSetupAsync();

    private async Task OpenAudioSetupAsync()
    {
        var window = new AudioSetupWindow(_deviceService, _audioRouter, _settings, _settings.LanguageCode) { Owner = this };
        if (window.ShowDialog() != true || window.Result is null)
        {
            return;
        }

        window.Result.ImportedFiles = _settings.ImportedFiles;
        window.Result.LanguageCode = _settings.LanguageCode;
        window.Result.VoiceVolume = _settings.VoiceVolume;
        window.Result.SoundVolume = _settings.SoundVolume;
        _settings = window.Result;
        await _settingsStore.SaveAsync(_settings);
        ActivateRouting();
    }

    private async Task<bool> OpenOnboardingAsync()
    {
        var window = new OnboardingWindow(_deviceService, _audioRouter, _settings, _settings.LanguageCode) { Owner = this };
        if (window.ShowDialog() != true || window.Result is null)
        {
            return _settings.SetupCompleted;
        }

        _settings = window.Result;
        await _settingsStore.SaveAsync(_settings);
        return true;
    }

    private async Task<bool> EnsureAutomaticAudioConfigurationAsync()
    {
        var microphones = _deviceService.GetMicrophones();
        var outputs = _deviceService.GetOutputs();
        var defaultMicrophone = _deviceService.FindBestMicrophone();
        var defaultMonitor = _deviceService.FindBestMonitor();
        var cable = _deviceService.FindCableInput();
        var changed = false;

        var savedMicrophone = microphones.FirstOrDefault(device => device.Id == _settings.MicrophoneDeviceId);
        if (savedMicrophone is null ||
            (AudioDeviceService.IsStreamingDevice(savedMicrophone) && defaultMicrophone is not null && savedMicrophone.Id != defaultMicrophone.Id))
        {
            _settings.MicrophoneDeviceId = defaultMicrophone?.Id;
            changed = true;
        }

        var savedMonitor = outputs.FirstOrDefault(device => device.Id == _settings.MonitorDeviceId);
        if (savedMonitor is null ||
            (AudioDeviceService.IsStreamingDevice(savedMonitor) && defaultMonitor is not null && savedMonitor.Id != defaultMonitor.Id))
        {
            _settings.MonitorDeviceId = defaultMonitor?.Id;
            changed = true;
        }

        if (cable is not null && _settings.CableDeviceId != cable.Id)
        {
            _settings.CableDeviceId = cable.Id;
            changed = true;
        }

        var completed = !string.IsNullOrWhiteSpace(_settings.MicrophoneDeviceId) &&
                        !string.IsNullOrWhiteSpace(_settings.CableDeviceId) &&
                        !string.IsNullOrWhiteSpace(_settings.MonitorDeviceId);
        if (_settings.SetupCompleted != completed)
        {
            _settings.SetupCompleted = completed;
            changed = true;
        }

        if (changed)
        {
            await _settingsStore.SaveAsync(_settings);
        }

        return completed;
    }

    private void ActivateRouting()
    {
        try
        {
            ApplyMixerLevels();
            if (string.IsNullOrWhiteSpace(_settings.MicrophoneDeviceId) ||
                string.IsNullOrWhiteSpace(_settings.CableDeviceId))
            {
                throw new InvalidOperationException(T("DevicesUnavailable"));
            }

            _audioRouter.StartRouting(_settings.MicrophoneDeviceId, _settings.CableDeviceId);
            AudioStatusDot.Fill = new SolidColorBrush(Color.FromRgb(3, 252, 86));
            AudioStatusDot.Stroke = new SolidColorBrush(Color.FromRgb(3, 252, 86));
            AudioStatusText.Text = T("AudioReady");
            RouteSummaryText.Text = T("RouteSummary");
            _ = EnsureBrowserRoutingAsync();
        }
        catch (Exception exception)
        {
            AudioStatusDot.Fill = Brushes.Transparent;
            AudioStatusDot.Stroke = Brushes.White;
            AudioStatusText.Text = T("AudioCheck");
            RouteSummaryText.Text = exception.Message;
        }
    }

    private async void PageTab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not System.Windows.Controls.RadioButton tab || tab.Tag is not string page)
        {
            return;
        }

        if (page == "library")
        {
            LibraryView.Visibility = Visibility.Visible;
            BrowserView.Visibility = Visibility.Collapsed;
            return;
        }

        _browserUrl = page == "youtube" ? "https://music.youtube.com/" : "https://www.myinstants.com/";
        LibraryView.Visibility = Visibility.Collapsed;
        BrowserView.Visibility = Visibility.Visible;
        AddressBox.Text = _browserUrl;
        await NavigateBrowserAsync(_browserUrl);
    }

    private async Task NavigateBrowserAsync(string url)
    {
        BrowserRecoveryButton.Visibility = Visibility.Collapsed;
        BrowserMessage.Visibility = Visibility.Visible;
        BrowserMessageTitle.Text = T("Loading");
        BrowserMessageCopy.Text = T("BrowserProfile");

        try
        {
            _webEnvironment ??= await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: Path.Combine(_appDataDirectory, "WebView2"));
            await Browser.EnsureCoreWebView2Async(_webEnvironment);
            await EnsureBrowserRoutingAsync();
            Browser.CoreWebView2.Navigate(url);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            BrowserMessageTitle.Text = T("WebViewRequired");
            BrowserMessageCopy.Text = T("WebViewCopy");
            BrowserRecoveryButton.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            BrowserMessageTitle.Text = T("BrowserFailed");
            BrowserMessageCopy.Text = exception.Message;
        }
    }

    private async Task EnsureBrowserRoutingAsync()
    {
        if (Browser.CoreWebView2 is null || string.IsNullOrWhiteSpace(_settings.CableDeviceId))
        {
            return;
        }

        try
        {
            await _audioRouter.StartBrowserRoutingAsync(
                checked((uint)Browser.CoreWebView2.BrowserProcessId),
                _settings.CableDeviceId);
        }
        catch (Exception exception)
        {
            AudioStatusDot.Fill = Brushes.Transparent;
            AudioStatusDot.Stroke = Brushes.White;
            AudioStatusText.Text = T("AudioCheck");
            RouteSummaryText.Text = exception.Message;
        }
    }

    private void Browser_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
    {
        AddressBox.Text = e.Uri;
        BrowserMessage.Visibility = Visibility.Visible;
        BrowserMessageTitle.Text = T("Loading");
        BrowserMessageCopy.Text = new Uri(e.Uri).Host;
    }

    private async void Browser_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            BrowserMessage.Visibility = Visibility.Collapsed;
            await ApplyBrowserVolumeAsync();
        }
        else
        {
            BrowserMessage.Visibility = Visibility.Visible;
            BrowserMessageTitle.Text = T("PageUnavailable");
            BrowserMessageCopy.Text = T("CheckConnection");
        }
    }

    private void BrowserBack_Click(object sender, RoutedEventArgs e) => LibraryTab.IsChecked = true;

    private void BrowserRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (Browser.CoreWebView2 is not null)
        {
            Browser.Reload();
        }
        else
        {
            _ = NavigateBrowserAsync(_browserUrl);
        }
    }

    private void BrowserRecoveryButton_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });

    private async void LanguageCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LanguageCombo.SelectedValue is not string languageCode)
        {
            return;
        }

        languageCode = UiText.NormalizeLanguage(languageCode);
        _settings.LanguageCode = languageCode;
        ApplyLanguage(languageCode);

        if (_settingsLoaded)
        {
            await _settingsStore.SaveAsync(_settings);
        }
    }

    private void ApplyLanguage(string languageCode)
    {
        UiText.ApplyCulture(languageCode);
        LanguageCombo.ToolTip = T("Language", languageCode);
        LibraryTab.Content = T("Library", languageCode);
        AudioSetupButton.ToolTip = T("AudioSettings", languageCode);
        ImportButtonText.Text = T("ImportMp3", languageCode);
        SortButton.Content = T(_sortAscending ? "SortAz" : "SortZa", languageCode);
        SearchBox.ToolTip = T("Search", languageCode);
        SearchHint.Text = T("Search", languageCode);
        EmptyImportButton.Content = T("ImportSounds", languageCode);
        BrowserBackButton.ToolTip = T("BackLibrary", languageCode);
        BrowserRefreshButton.ToolTip = T("Refresh", languageCode);
        BrowserRecoveryButton.Content = T("InstallWebView2", languageCode);
        DropSoundsText.Text = T("DropSounds", languageCode);
        StopAllText.Text = T("StopAll", languageCode);
        PlaybackCountText.Text = string.Format(T("PlaybackCount", languageCode), _playbackCount);
        VoiceMixLabel.Text = T("VoiceLevel", languageCode);
        SoundMixLabel.Text = T("SoundsLevel", languageCode);
        VoiceMixSlider.ToolTip = T("VoiceLevelHelp", languageCode);
        SoundMixSlider.ToolTip = T("SoundsLevelHelp", languageCode);
        VoiceMixGroup.ToolTip = T("VoiceLevelHelp", languageCode);
        SoundMixGroup.ToolTip = T("SoundsLevelHelp", languageCode);

        if (_settings.SetupCompleted)
        {
            AudioStatusText.Text = T("AudioReady", languageCode);
            RouteSummaryText.Text = T("RouteSummary", languageCode);
        }
        else
        {
            AudioStatusText.Text = T("ConfigurationRequired", languageCode);
            RouteSummaryText.Text = T("RouteSummary", languageCode);
        }

        if (IsLoaded)
        {
            UpdateVisibleSounds();
        }
    }

    private void MixerSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_settingsLoaded || VoiceMixSlider is null || SoundMixSlider is null)
        {
            return;
        }

        _settings.VoiceVolume = VoiceMixSlider.Value / 100.0;
        _settings.SoundVolume = SoundMixSlider.Value / 100.0;
        UpdateMixerLabels();
        ApplyMixerLevels();
        _ = ApplyBrowserVolumeAsync();
        _mixerSaveTimer.Stop();
        _mixerSaveTimer.Start();
    }

    private void UpdateMixerLabels()
    {
        VoiceMixValue.Text = $"{Math.Round(VoiceMixSlider.Value):0}%";
        SoundMixValue.Text = $"{Math.Round(SoundMixSlider.Value):0}%";
    }

    private void ApplyMixerLevels()
    {
        _audioRouter.VoiceVolume = (float)_settings.VoiceVolume;
        _audioRouter.SoundVolume = (float)_settings.SoundVolume;
    }

    private async Task ApplyBrowserVolumeAsync()
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        var level = _settings.SoundVolume.ToString("0.####", CultureInfo.InvariantCulture);
        var script = $$"""
            (() => {
              window.__nemBoardSoundVolume = {{level}};
              const attach = (media) => {
                if (!media || !('volume' in media)) return;
                const target = window.__nemBoardSoundVolume;
                if (Math.abs(media.volume - target) > 0.001) media.volume = target;
                if (media.dataset.nemBoardVolumeBound === '1') return;
                media.dataset.nemBoardVolumeBound = '1';
                media.addEventListener('volumechange', () => {
                  const wanted = window.__nemBoardSoundVolume;
                  if (Math.abs(media.volume - wanted) > 0.001) media.volume = wanted;
                });
              };
              const apply = (root) => root.querySelectorAll?.('audio, video').forEach(attach);
              apply(document);
              if (!window.__nemBoardVolumeObserver && document.documentElement) {
                window.__nemBoardVolumeObserver = new MutationObserver(records => {
                  for (const record of records) {
                    for (const node of record.addedNodes) {
                      if (node.matches?.('audio, video')) attach(node);
                      apply(node);
                    }
                  }
                });
                window.__nemBoardVolumeObserver.observe(document.documentElement, { childList: true, subtree: true });
              }
            })();
            """;

        try
        {
            await Browser.ExecuteScriptAsync(script);
        }
        catch (InvalidOperationException)
        {
            // Navigation can replace the document while the slider is moving.
        }
        catch (COMException)
        {
            // WebView2 can be unavailable for a moment during navigation.
        }
    }

    private string? ReadSetupLanguage()
    {
        var path = Path.Combine(_appDataDirectory, "setup-language.txt");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var languageCode = UiText.NormalizeLanguage(File.ReadAllText(path).Trim());
            File.Delete(path);
            return languageCode;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ResolveAppDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var newDirectory = Path.Combine(localAppData, "NemBoard");
        var legacyDirectory = Path.Combine(localAppData, "Soundboard");

        if (!Directory.Exists(newDirectory) && Directory.Exists(legacyDirectory))
        {
            try
            {
                Directory.Move(legacyDirectory, newDirectory);
            }
            catch (IOException)
            {
                return legacyDirectory;
            }
            catch (UnauthorizedAccessException)
            {
                return legacyDirectory;
            }
        }

        Directory.CreateDirectory(newDirectory);
        return newDirectory;
    }

    private void MigrateImportedPaths()
    {
        var legacyRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Soundboard");
        for (var index = 0; index < _settings.ImportedFiles.Count; index++)
        {
            var path = _settings.ImportedFiles[index];
            if (!path.StartsWith(legacyRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var migrated = Path.Combine(_appDataDirectory, Path.GetRelativePath(legacyRoot, path));
            if (File.Exists(migrated))
            {
                _settings.ImportedFiles[index] = migrated;
            }
        }
    }

    private string T(string key, string? languageCode = null) =>
        UiText.Get(languageCode ?? _settings.LanguageCode, key);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is < Key.F1 or > Key.F12)
        {
            return;
        }

        var index = (int)e.Key - (int)Key.F1;
        if (index >= 0 && index < _library.Items.Count)
        {
            var id = _library.Items[index].Id;
            var tile = VisibleSounds.FirstOrDefault(item => item.Clip.Id == id) ?? new SoundTileViewModel(_library.Items[index]);
            ToggleSound(tile);
            e.Handled = true;
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _audioRouter.Dispose();
        Browser.Dispose();
    }
}
