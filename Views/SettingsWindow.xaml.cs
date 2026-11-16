using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SchoolDesk.Services;

namespace SchoolDesk.Views;

public partial class SettingsWindow : UserControl
{
    private readonly SubjectService _service = new();
    private readonly StartupService _startup = new();
    private readonly List<string> _subjects;

    public SettingsWindow()
    {
        InitializeComponent();
        _subjects = _service.Load();
        SubjectsList.ItemsSource = _subjects;
        StartupCheck.IsChecked = _startup.IsEnabled();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = NewSubjectBox.Text.Trim();
        if (name.Length == 0 || _subjects.Contains(name, StringComparer.CurrentCultureIgnoreCase)) return;
        _subjects.Add(name);
        SubjectsList.Items.Refresh();
        NewSubjectBox.Clear();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SubjectsList.SelectedItem is not string name) return;

        bool isInUse;
        try
        {
            var agendaService = new AgendaService();
            var documentService = new DocumentService();
            isInUse = agendaService.IsSubjectInUse(name) || documentService.HasSubjectContent(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show($"Impossible de vérifier l'utilisation de cette matière : {ex.Message}", "SchoolDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (isInUse)
        {
            MessageBox.Show(
                $"La matière « {name} » est encore utilisée dans l'agenda ou dans les documents. Elle ne peut pas être supprimée sans risque de perdre des données associées.",
                "Supprimer une matière",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"Retirer « {name} » de la liste des matières ? Les fichiers déjà enregistrés ne seront pas supprimés.", "Supprimer une matière", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            _subjects.Remove(name);
            SubjectsList.Items.Refresh();
        }
    }

    private void OpenSchedule_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ScheduleRepository.EnsureUserScheduleFile();
            Process.Start(new ProcessStartInfo(ScheduleRepository.UserSchedulePath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            MessageBox.Show($"Impossible d'ouvrir l'emploi du temps : {ex.Message}", "SchoolDesk", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _service.Save(_subjects);
            _startup.SetEnabled(StartupCheck.IsChecked == true);
            if (Window.GetWindow(this) is MainWindow main) main.NavigateBack();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
        {
            MessageBox.Show($"Impossible d'enregistrer les paramètres : {ex.Message}", "SchoolDesk", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow main) main.NavigateBack();
    }
}
