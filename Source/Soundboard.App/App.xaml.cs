using System.Windows;
using System.Globalization;

namespace Soundboard.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var french = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";
            var message = args.ExceptionObject is Exception exception
                ? exception.Message
                : french ? "Erreur inconnue" : "Unknown error";
            MessageBox.Show(message, french ? "NemBoard — erreur" : "NemBoard — error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        base.OnStartup(e);
    }
}
