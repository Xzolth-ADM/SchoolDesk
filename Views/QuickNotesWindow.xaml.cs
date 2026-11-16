using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SchoolDesk.Services;

namespace SchoolDesk.Views;

public partial class QuickNotesWindow : UserControl
{
    private readonly QuickNotesService _service = new();
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private bool _loading;

    public QuickNotesWindow()
    {
        InitializeComponent();
        _loading = true;
        try
        {
            NotesBox.Text = _service.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Impossible de lire les notes : {ex.Message}", "SchoolDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Lecture impossible · vérifie le stockage local";
            return;
        }
        finally
        {
            _loading = false;
        }

        _autosave.Tick += (_, _) => SaveNotes();
        StatusText.Text = "Sauvegarde automatique locale activée";
    }

    private void NotesBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        _autosave.Stop();
        _autosave.Start();
        StatusText.Text = "Modifications en attente…";
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveNotes();

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        PrepareToLeave();
        if (Window.GetWindow(this) is MainWindow main) main.ShowDashboard();
    }

    public void PrepareToLeave()
    {
        if (!_autosave.IsEnabled) return;
        _autosave.Stop();
        SaveNotes();
    }

    private void SaveNotes()
    {
        _autosave.Stop();
        try
        {
            _service.Save(NotesBox.Text);
            StatusText.Text = $"Enregistré · {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Erreur de sauvegarde : " + ex.Message;
        }
    }
}
