using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using SchoolDesk.Models;
using SchoolDesk.Services;

namespace SchoolDesk.Views;

public partial class SettingsWindow : UserControl
{
    private readonly SubjectService _service = new();
    private readonly StartupService _startup = new();
    private readonly List<string> _subjects;
    private List<ScheduleEntry> _scheduleEntries = [];
    private bool _loadingSchedule;

    public SettingsWindow()
    {
        InitializeComponent();
        _scheduleEntries = new ScheduleRepository().Entries.ToList();
        _subjects = _service.Load();
        var subjectsChanged = false;
        foreach (var subject in _scheduleEntries
                     .Where(entry => entry.Kind == ScheduleKind.Lesson)
                     .Select(entry => entry.Title)
                     .Distinct(StringComparer.CurrentCultureIgnoreCase))
        {
            if (_subjects.Contains(subject, StringComparer.CurrentCultureIgnoreCase)) continue;
            _subjects.Add(subject);
            subjectsChanged = true;
        }
        if (subjectsChanged)
        {
            _subjects.Sort(StringComparer.CurrentCultureIgnoreCase);
            _service.Save(_subjects);
        }
        SubjectsList.ItemsSource = _subjects;
        StartupCheck.IsChecked = _startup.IsEnabled();
        RefreshScheduleEntries();
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

    private void ScheduleFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ScheduleEntriesList is null || _loadingSchedule) return;
        RefreshScheduleEntries();
        NewScheduleSlot_Click(sender, e);
    }

    private void RefreshScheduleEntries()
    {
        if (ScheduleEntriesList is null || ScheduleDayBox.SelectedItem is not ComboBoxItem dayItem ||
            ScheduleFilterWeekBox.SelectedItem is not ComboBoxItem weekItem)
            return;

        var day = Enum.Parse<DayOfWeek>((string)dayItem.Tag);
        var week = (string)weekItem.Tag;
        _loadingSchedule = true;
        ScheduleEntriesList.ItemsSource = _scheduleEntries
            .Where(entry => entry.Day == day && (week == "AB" || entry.AppliesTo(week)))
            .OrderBy(entry => entry.Start)
            .ToList();
        _loadingSchedule = false;
        ScheduleEntriesList.SelectedItem = null;
    }

    private void ScheduleEntry_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSchedule || ScheduleEntriesList.SelectedItem is not ScheduleEntry entry)
            return;

        _loadingSchedule = true;
        ScheduleDayBox.SelectedItem = ScheduleDayBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals((string)item.Tag, entry.Day.ToString(), StringComparison.Ordinal));
        ScheduleWeekBox.SelectedItem = ScheduleWeekBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals((string)item.Tag, entry.Week, StringComparison.Ordinal));
        ScheduleStartBox.Text = entry.Start.ToString("HH:mm", CultureInfo.InvariantCulture);
        ScheduleEndBox.Text = entry.End.ToString("HH:mm", CultureInfo.InvariantCulture);
        ScheduleTitleBox.Text = entry.Title;
        ScheduleRoomBox.Text = entry.Room ?? "";
        ScheduleTeacherBox.Text = entry.Teacher ?? "";
        ScheduleKindBox.SelectedItem = ScheduleKindBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals((string)item.Tag, entry.Kind.ToString(), StringComparison.Ordinal));
        SaveScheduleSlotButton.Content = "Enregistrer ce créneau";
        _loadingSchedule = false;
    }

    private void NewScheduleSlot_Click(object sender, RoutedEventArgs e)
    {
        ScheduleEntriesList.SelectedItem = null;
        ScheduleTitleBox.Clear();
        ScheduleRoomBox.Clear();
        ScheduleTeacherBox.Clear();
        ScheduleKindBox.SelectedIndex = 0;
        if (ScheduleDayBox.SelectedItem is ComboBoxItem dayItem &&
            ScheduleFilterWeekBox.SelectedItem is ComboBoxItem weekItem)
        {
            var day = Enum.Parse<DayOfWeek>((string)dayItem.Tag);
            var week = (string)weekItem.Tag;
            var lastEnd = _scheduleEntries
                .Where(entry => entry.Day == day && (week == "AB" || entry.AppliesTo(week)))
                .Select(entry => (TimeOnly?)entry.End)
                .Max();
            var start = lastEnd ?? new TimeOnly(8, 10);
            ScheduleStartBox.Text = start.ToString("HH:mm", CultureInfo.InvariantCulture);
            ScheduleEndBox.Text = start.AddMinutes(55).ToString("HH:mm", CultureInfo.InvariantCulture);
            ScheduleWeekBox.SelectedItem = ScheduleWeekBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals((string)item.Tag, week == "AB" ? "A" : week, StringComparison.Ordinal));
        }
        SaveScheduleSlotButton.Content = "Ajouter ce créneau";
        ScheduleTitleBox.Focus();
    }

    private void SaveScheduleSlot_Click(object sender, RoutedEventArgs e)
    {
        if (ScheduleDayBox.SelectedItem is not ComboBoxItem dayItem ||
            ScheduleWeekBox.SelectedItem is not ComboBoxItem weekItem ||
            ScheduleKindBox.SelectedItem is not ComboBoxItem kindItem)
            return;

        if (!TimeOnly.TryParseExact(ScheduleStartBox.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !TimeOnly.TryParseExact(ScheduleEndBox.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        {
            MessageBox.Show("Saisis des heures valides au format HH:mm, par exemple 09:05.", "Horaires invalides",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(ScheduleTitleBox.Text))
        {
            MessageBox.Show("Saisis une matière ou le nom de l’activité.", "Créneau incomplet",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ScheduleTitleBox.Focus();
            return;
        }

        var entry = new ScheduleEntry
        {
            Day = Enum.Parse<DayOfWeek>((string)dayItem.Tag),
            Week = (string)weekItem.Tag,
            Start = start,
            End = end,
            Title = ScheduleTitleBox.Text.Trim(),
            Room = string.IsNullOrWhiteSpace(ScheduleRoomBox.Text) ? null : ScheduleRoomBox.Text.Trim(),
            Teacher = string.IsNullOrWhiteSpace(ScheduleTeacherBox.Text) ? null : ScheduleTeacherBox.Text.Trim(),
            Kind = Enum.Parse<ScheduleKind>((string)kindItem.Tag)
        };
        var candidate = _scheduleEntries.ToList();
        if (ScheduleEntriesList.SelectedItem is ScheduleEntry selected)
        {
            var index = candidate.FindIndex(item => ReferenceEquals(item, selected));
            if (index >= 0) candidate[index] = entry;
        }
        else
        {
            candidate.Add(entry);
        }

        try
        {
            ScheduleRepository.SaveEntries(candidate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            MessageBox.Show($"Impossible d'enregistrer ce créneau : {ex.Message}", "Planning non enregistré",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _scheduleEntries = candidate;
        RefreshScheduleEntries();
        if (entry.Kind == ScheduleKind.Lesson &&
            !_subjects.Contains(entry.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            _subjects.Add(entry.Title);
            _subjects.Sort(StringComparer.CurrentCultureIgnoreCase);
            SubjectsList.Items.Refresh();
            try
            {
                _service.Save(_subjects);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                MessageBox.Show(
                    $"Le créneau a bien été enregistré, mais la liste des matières n'a pas pu être mise à jour : {ex.Message}",
                    "Mise à jour des matières incomplète",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        if (Window.GetWindow(this) is MainWindow main)
            main.RefreshSchedule();
        StatusText.Text = $"Créneau enregistré · {ScheduleEntry.DayLabel(entry.Day)} {entry.Start:HH\\:mm}";
        NewScheduleSlot_Click(sender, e);
    }

    private void DeleteScheduleSlot_Click(object sender, RoutedEventArgs e)
    {
        if (ScheduleEntriesList.SelectedItem is not ScheduleEntry selected)
        {
            MessageBox.Show("Sélectionne d’abord un créneau dans le planning.", "Aucun créneau sélectionné",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show($"Supprimer « {selected.Title} » le {selected.Day} de {selected.Start:HH\\:mm} à {selected.End:HH\\:mm} ?",
                "Supprimer un créneau", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var candidate = _scheduleEntries.Where(item => !ReferenceEquals(item, selected)).ToList();
        try
        {
            ScheduleRepository.SaveEntries(candidate);
            _scheduleEntries = candidate;
            RefreshScheduleEntries();
            if (Window.GetWindow(this) is MainWindow main)
                main.RefreshSchedule();
            StatusText.Text = "Créneau supprimé.";
            NewScheduleSlot_Click(sender, e);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            MessageBox.Show($"Impossible de modifier l'emploi du temps : {ex.Message}", "SchoolDesk",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _service.Save(_subjects);
            _startup.SetEnabled(StartupCheck.IsChecked == true);
            if (Window.GetWindow(this) is MainWindow main)
            {
                main.RefreshSchedule();
                main.NavigateBack();
            }
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
