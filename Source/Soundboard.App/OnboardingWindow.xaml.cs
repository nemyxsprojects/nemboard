using System.Windows;
using System.Windows.Media;
using Soundboard.Core;

namespace Soundboard.App;

public partial class OnboardingWindow : Window
{
    private readonly AudioDeviceService _devices;
    private readonly AudioRouter _router;
    private readonly AppSettings _current;
    private readonly string _languageCode;
    private readonly AudioDevice? _cable;
    private int _step;

    public OnboardingWindow(AudioDeviceService devices, AudioRouter router, AppSettings current, string languageCode)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _devices = devices;
        _router = router;
        _current = current;
        _languageCode = UiText.NormalizeLanguage(languageCode);
        _cable = devices.FindCableInput();

        var microphones = devices.GetMicrophones();
        var outputs = devices.GetOutputs();
        MicrophoneCombo.ItemsSource = microphones;
        MonitorCombo.ItemsSource = outputs;
        MicrophoneCombo.SelectedItem = microphones.FirstOrDefault(device => device.Id == current.MicrophoneDeviceId)
            ?? devices.FindBestMicrophone() ?? microphones.FirstOrDefault();
        MonitorCombo.SelectedItem = outputs.FirstOrDefault(device => device.Id == current.MonitorDeviceId)
            ?? devices.FindBestMonitor() ?? outputs.FirstOrDefault();

        ApplyLanguage();
        ShowStep();
    }

    public AppSettings? Result { get; private set; }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_step < 3)
        {
            _step++;
            ShowStep();
            return;
        }

        if (MicrophoneCombo.SelectedItem is not AudioDevice microphone ||
            MonitorCombo.SelectedItem is not AudioDevice monitor || _cable is null)
        {
            return;
        }

        Result = new AppSettings
        {
            LanguageCode = _languageCode,
            MicrophoneDeviceId = microphone.Id,
            CableDeviceId = _cable.Id,
            MonitorDeviceId = monitor.Id,
            SetupCompleted = true,
            OnboardingCompleted = true,
            VoiceVolume = _current.VoiceVolume,
            SoundVolume = _current.SoundVolume,
            ImportedFiles = _current.ImportedFiles
        };
        DialogResult = true;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 0) return;
        _step--;
        ShowStep();
    }

    private void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (MonitorCombo.SelectedItem is AudioDevice monitor)
        {
            _router.PlayTestTone(monitor.Id);
        }
    }

    private void DeviceSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateNextState();

    private void ShowStep()
    {
        WelcomePage.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        MicrophonePage.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        MonitorPage.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        AppsPage.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = _step == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = T(_step == 0 ? "OnboardingStart" : _step == 3 ? "OnboardingFinish" : "OnboardingNext");
        StepText.Text = $"{_step + 1} / 4";
        var dots = new[] { Dot1, Dot2, Dot3, Dot4 };
        for (var index = 0; index < dots.Length; index++) dots[index].Fill = index == _step ? Brushes.White : new SolidColorBrush(Color.FromRgb(85, 85, 85));
        UpdateNextState();
    }

    private void UpdateNextState()
    {
        if (NextButton is null) return;
        NextButton.IsEnabled = _step switch
        {
            1 => MicrophoneCombo.SelectedItem is AudioDevice,
            2 => MonitorCombo.SelectedItem is AudioDevice && _cable is not null,
            3 => _cable is not null,
            _ => true
        };
    }

    private void ApplyLanguage()
    {
        Title = T("OnboardingTitle");
        WelcomeTitle.Text = T("OnboardingWelcomeTitle");
        WelcomeCopy.Text = T("OnboardingWelcomeCopy");
        WelcomeRoute.Text = T("OnboardingRoute");
        MicrophoneTitle.Text = T("OnboardingMicrophoneTitle");
        MicrophoneCopy.Text = T("OnboardingMicrophoneCopy");
        MicrophoneLabel.Text = T("StepMicrophone");
        MonitorTitle.Text = T("OnboardingMonitorTitle");
        MonitorCopy.Text = T("OnboardingMonitorCopy");
        MonitorLabel.Text = T("OnboardingMonitorLabel");
        TestButton.Content = T("OnboardingTest");
        CableStatus.Text = T(_cable is null ? "OnboardingCableMissing" : "OnboardingCableReady");
        CableDot.Fill = _cable is null ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(3, 252, 86));
        AppsTitle.Text = T("OnboardingAppsTitle");
        AppsCopy.Text = T("OnboardingAppsCopy");
        AppsHint.Text = T("OnboardingAppsHint");
        AppsDone.Text = T("OnboardingDone");
        BackButton.Content = T("OnboardingBack");
    }

    private string T(string key) => UiText.Get(_languageCode, key);
}
