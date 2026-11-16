using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SchoolDesk.Models;
using SchoolDesk.Services;

namespace SchoolDesk.Views;

public partial class AgendaWindow : UserControl
{
    private readonly AgendaService _service = new();
    private readonly SubjectService _subjects = new();
    private readonly ObservableCollection<AgendaItem> _visible = [];
    private readonly List<AgendaItem> _all;
    private string _viewMode = "Jour";
    public AgendaWindow()
    {
        InitializeComponent();
        _all = _service.Load();
        AgendaList.ItemsSource = _visible;
        SubjectBox.ItemsSource = _subjects.Load();
        SubjectBox.SelectedIndex = 0;
        DayPicker.SelectedDate = DateTime.Today;
        DueDatePicker.SelectedDate = DateTime.Today;
        RefreshDay();
    }
    private void DayPicker_Changed(object sender, SelectionChangedEventArgs e) => RefreshDay();
    private void View_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ViewBox?.SelectedItem is ComboBoxItem { Content: string mode })
        {
            _viewMode = mode;
            RefreshDay();
        }
    }

    private void RefreshDay()
    {
        if (DayPicker is null || DayPicker.SelectedDate is not DateTime day) return;
        var rangeStart = _viewMode switch
        {
            "Semaine" => day.Date.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
            "Mois" => new DateTime(day.Year, day.Month, 1),
            _ => day.Date
        };
        var rangeEnd = _viewMode switch
        {
            "Semaine" => rangeStart.AddDays(7),
            "Mois" => rangeStart.AddMonths(1),
            _ => rangeStart.AddDays(1)
        };
        _visible.Clear();
        foreach (var item in _all
            .Where(x => x.DueDate.Date >= rangeStart && x.DueDate.Date < rangeEnd)
            .OrderBy(x => x.DueDate.Date)
            .ThenBy(x => x.DueTime ?? TimeOnly.MinValue))
            _visible.Add(item);
        RangeText.Text = $"{_viewMode} · {_visible.Count(x => x.IsDone)} terminée(s) · {_visible.Count(x => !x.IsDone)} à faire · {rangeStart:dd/MM/yyyy}";
        EmptyState.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NewTask_Click(object sender, RoutedEventArgs e)
    {
        NewTaskPanel.Visibility = Visibility.Visible;
        TitleBox.Focus();
    }

    private void CancelNewTask_Click(object sender, RoutedEventArgs e)
    {
        NewTaskPanel.Visibility = Visibility.Collapsed;
        TitleBox.Clear();
        DescriptionBox.Clear();
        TimeBox.Clear();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text)) { MessageBox.Show("Saisis un intitulé."); return; }
        if (DueDatePicker.SelectedDate is not DateTime date) { MessageBox.Show("Choisis une date."); return; }
        TimeOnly? dueTime = null;
        if (!string.IsNullOrWhiteSpace(TimeBox.Text))
        {
            if (!TimeOnly.TryParseExact(TimeBox.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime))
            {
                MessageBox.Show("L'heure doit respecter le format HH:mm, ou rester vide.");
                return;
            }
            dueTime = parsedTime;
        }
        var now = DateTime.Now;
        var item = new AgendaItem
        {
            Title = TitleBox.Text.Trim(),
            Type = (TypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Devoir",
            Subject = SubjectBox.SelectedItem?.ToString() ?? "",
            DueDate = date.Date,
            DueTime = dueTime,
            Description = DescriptionBox.Text.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
        _all.Add(item);
        if (Save())
        {
            TitleBox.Clear();
            DescriptionBox.Clear();
            TimeBox.Clear();
            NewTaskPanel.Visibility = Visibility.Collapsed;
            DayPicker.SelectedDate = date.Date;
            RefreshDay();
        }
        else
        {
            _all.Remove(item);
        }
    }
    private void Done_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (sender is FrameworkElement { DataContext: AgendaItem item }) item.UpdatedAt = DateTime.Now;
        if (Save()) RefreshDay();
    }
    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AgendaItem item }) DeleteItem(item);
    }

    private void DeleteItem(AgendaItem item)
    {
        if (MessageBox.Show($"Supprimer « {item.Title} » ?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _all.Remove(item);
        if (!Save()) _all.Add(item);
        RefreshDay();
    }
    private bool Save()
    {
        try
        {
            _service.Save(_all);
            if (Window.GetWindow(this) is MainWindow main) main.RefreshAgendaCounters();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Enregistrement impossible : {ex.Message}", "SchoolDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow main) main.ShowSettings();
    }

    public void ReloadSubjects()
    {
        var selected = SubjectBox.SelectedItem?.ToString();
        var subjects = _subjects.Load();
        SubjectBox.ItemsSource = subjects;
        SubjectBox.SelectedItem = subjects.FirstOrDefault(subject => subject == selected);
        if (SubjectBox.SelectedIndex < 0) SubjectBox.SelectedIndex = 0;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow main) main.ShowDashboard();
    }
}
