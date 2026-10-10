using System.Diagnostics;
using System.Windows;
using Soundboard.Core;

namespace Soundboard.App;

public partial class AudioSetupWindow : Window
{
    private readonly AudioRouter _router;

    private readonly string _languageCode;
    private readonly bool _onboardingCompleted;
    private readonly double _currentVoiceVolume;
    private readonly double _currentSoundVolume;

    public AudioSetupWindow(AudioDeviceService devices, AudioRouter router, AppSettings current, string languageCode)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _router = router;
        _languageCode = UiText.NormalizeLanguage(languageCode);
        _onboardingCompleted = current.OnboardingCompleted;
        _currentVoiceVolume = current.VoiceVolume;
        _currentSoundVolume = current.SoundVolume;
        ApplyLanguage();

        var microphones = devices.GetMicrophones();
        var outputs = devices.GetOutputs();
        MicrophoneCombo.ItemsSource = microphones;
        CableCombo.ItemsSource = outputs;
        MonitorCombo.ItemsSource = outputs;

        MicrophoneCombo.SelectedItem = microphones.FirstOrDefault(device => device.Id == current.MicrophoneDeviceId)
            ?? devices.FindBestMicrophone()
            ?? microphones.FirstOrDefault();
        CableCombo.SelectedItem = outputs.FirstOrDefault(device => device.Id == current.CableDeviceId)
            ?? devices.FindCableInput();
        MonitorCombo.SelectedItem = outputs.FirstOrDefault(device => device.Id == current.MonitorDeviceId)
            ?? devices.FindBestMonitor();

        CableWarning.Visibility = devices.FindCableInput() is null ? Visibility.Visible : Visibility.Collapsed;
        ValidateSelections();
    }

    public AppSettings? Result { get; private set; }

    private void SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        ValidateSelections();

    private void ValidateSelections()
    {
        SaveButton.IsEnabled = MicrophoneCombo.SelectedItem is AudioDevice &&
                               CableCombo.SelectedItem is AudioDevice &&
                               MonitorCombo.SelectedItem is AudioDevice;
        TestButton.IsEnabled = MonitorCombo.SelectedItem is AudioDevice;
    }

    private void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (MonitorCombo.SelectedItem is not AudioDevice monitor)
        {
            return;
        }

        try
        {
            _router.PlayTestTone(monitor.Id);
            TestResult.Text = T("TestSent");
        }
        catch (Exception exception)
        {
            TestResult.Text = string.Format(T("TestFailed"), exception.Message);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (MicrophoneCombo.SelectedItem is not AudioDevice microphone ||
            CableCombo.SelectedItem is not AudioDevice cable ||
            MonitorCombo.SelectedItem is not AudioDevice monitor)
        {
            return;
        }

        Result = new AppSettings
        {
            LanguageCode = _languageCode,
            MicrophoneDeviceId = microphone.Id,
            CableDeviceId = cable.Id,
            MonitorDeviceId = monitor.Id,
            SetupCompleted = true,
            OnboardingCompleted = _onboardingCompleted,
            VoiceVolume = _currentVoiceVolume,
            SoundVolume = _currentSoundVolume
        };
        DialogResult = true;
    }

    private void OpenCableWebsite_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/") { UseShellExecute = true });

    private void ApplyLanguage()
    {
        Title = T("AudioWindowTitle");
        HeadingText.Text = T("ConfigureAudio");
        IntroText.Text = T("AudioIntro");
        MicrophoneStepText.Text = T("StepMicrophone");
        VoiceText.Text = T("YourVoice");
        MicrophoneHelpText.Text = T("MicHelp");
        CallsStepText.Text = T("StepCalls");
        CableHelpText.Text = T("CableHelp");
        DownloadCableButton.Content = T("Download");
        CableWarningText.Text = T("CableWarning");
        MonitorStepText.Text = T("StepMonitor");
        HeadphonesText.Text = T("YourHeadphones");
        TestButton.Content = T("TestSound");
        CancelButton.Content = T("Cancel");
        SaveButton.Content = T("SaveActivate");
    }

    private string T(string key) => UiText.Get(_languageCode, key);
}
