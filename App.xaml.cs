using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SchoolDesk;

public partial class App : Application
{
    private string ErrorLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SchoolDesk", "errors.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            LogException("Démarrage", ex);
            MessageBox.Show(
                $"SchoolDesk n'a pas pu démarrer. Les détails sont enregistrés dans :\n{ErrorLogPath}\n\n{ex.Message}",
                "Erreur de démarrage",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("Interface", e.Exception);
        MessageBox.Show(
            $"Une erreur inattendue s'est produite. Les détails ont été enregistrés dans :\n{ErrorLogPath}\n\n{e.Exception.Message}",
            "Erreur SchoolDesk",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("Tâche en arrière-plan", e.Exception);
        e.SetObserved();
    }

    private void LogException(string source, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ErrorLogPath)!);
            File.AppendAllText(ErrorLogPath, $"[{DateTime.Now:O}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException)
        {
        }
    }
}
