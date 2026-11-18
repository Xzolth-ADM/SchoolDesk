using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SchoolDesk.Models;
using SchoolDesk.Services;
using SchoolDesk.Views;

namespace SchoolDesk;

public partial class MainWindow : Window
{
    private SchedulerService _scheduler;
    private ScheduleRepository _scheduleRepository;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, UserControl> _pages = new(StringComparer.Ordinal);
    private readonly object _dashboardPage;
    private string _currentPage = "dashboard";
    private string _previousPage = "dashboard";
    // Référence fixe demandée : le mercredi 11 novembre 2026 est une semaine A.
    private readonly WeekConfiguration _configuration = new()
    {
        ReferenceDate = new DateOnly(2026, 11, 11),
        ReferenceWeek = "A"
    };
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");

    public MainWindow()
    {
        InitializeComponent();
        _dashboardPage = PageHost.Content;
        _scheduleRepository = new ScheduleRepository();
        _scheduler = new SchedulerService(_scheduleRepository, new WeekResolver());
        _timer.Tick += (_, _) => RefreshDashboard();
        Loaded += (_, _) => { RefreshTaskCounters(); RefreshDashboard(); _timer.Start(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Normal) RefreshDashboard(); };
    }

    private void RefreshDashboard()
    {
        var now = DateTime.Now;
        DateText.Text = now.ToString("dddd d MMMM yyyy", FrenchCulture);
        ClockText.Text = now.ToString("HH:mm:ss", FrenchCulture);

        var result = _scheduler.Evaluate(now, _configuration);
        DashboardPrimaryButton.Content = _scheduleRepository.HasEntries
            ? "Ouvrir l’agenda"
            : "Configurer mon emploi du temps";
        WeekText.Text = result.Week == "?" ? "Semaine indisponible" : $"Semaine {result.Week} · alternance automatique";
        StateText.Text = result.Message.ToUpper(FrenchCulture);

        if (result.State == ScheduleState.InProgress && result.Current is not null)
        {
            CurrentTitle.Text = result.Current.Title;
            CurrentDetails.Text = FormatDetails(result.Current);
            RemainingText.Text = FormatDuration(result.Remaining);
        }
        else
        {
            CurrentTitle.Text = result.State switch
            {
                ScheduleState.Transition => "Changement de salle",
                ScheduleState.Free => "Temps libre",
                ScheduleState.BeforeFirstEvent => "En attente du premier cours",
                ScheduleState.Finished => "C'est terminé pour aujourd'hui",
                _ => "Configure ton emploi du temps"
            };
            CurrentDetails.Text = result.Next is null ? result.Message : $"Prochain : {result.Next.Title} à {result.Next.Start:HH\\:mm}";
            RemainingText.Text = FormatDuration(result.Remaining);
        }

        if (result.NextLesson is not null && result.NextLessonDateTime is not null)
        {
            var lesson = result.NextLesson;
            var lessonDate = result.NextLessonDateTime.Value;
            NextTitle.Text = lesson.Title;
            var dayLabel = lessonDate.Date == now.Date
                ? "Aujourd'hui"
                : lessonDate.ToString("dddd d MMMM", FrenchCulture);
            NextDetails.Text = $"{dayLabel} · {lesson.Start:HH\\:mm} – {lesson.End:HH\\:mm}" +
                (string.IsNullOrWhiteSpace(lesson.Room) ? "" : $"\nSalle : {lesson.Room}") +
                (string.IsNullOrWhiteSpace(lesson.Teacher) ? "" : $"\nProfesseur : {lesson.Teacher}");
            var untilLesson = lessonDate - now;
            NextCountdown.Text = untilLesson <= TimeSpan.Zero
                ? "Le cours commence maintenant"
                : $"Dans {FormatDuration(untilLesson)}";
        }
        else
        {
            NextTitle.Text = "Aucun cours trouvé";
            NextDetails.Text = "Vérifie la configuration de ton emploi du temps.";
            NextCountdown.Text = "";
        }
    }

    private static string FormatDetails(ScheduleEntry entry) =>
        $"{entry.Start:HH\\:mm} – {entry.End:HH\\:mm}" +
        (string.IsNullOrWhiteSpace(entry.Room) ? "" : $"  •  {entry.Room}") +
        (string.IsNullOrWhiteSpace(entry.Teacher) ? "" : $"  •  {entry.Teacher}");

    private static string FormatDuration(TimeSpan? duration) => duration is null
        ? "--:--"
        : $"{Math.Max(0, (int)duration.Value.TotalHours):00}:{Math.Max(0, duration.Value.Minutes):00}";

    public void RefreshTaskCounters()
    {
        try
        {
            var items = new AgendaService().Load();
            TodoCountText.Text = items.Count(x => !x.IsDone).ToString(CultureInfo.InvariantCulture);
            DoneCountText.Text = items.Count(x => x.IsDone).ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TodoCountText.Text = "!";
            DoneCountText.Text = "!";
            TodoCountText.ToolTip = DoneCountText.ToolTip = $"Impossible de lire l'agenda : {ex.Message}";
        }
    }

    private async void OpenAgenda_Click(object sender, RoutedEventArgs e)
    {
        await NavigateToPageAsync("agenda");
    }

    private async void DashboardPrimary_Click(object sender, RoutedEventArgs e)
    {
        await NavigateToPageAsync(_scheduleRepository.HasEntries ? "agenda" : "settings");
    }

    private async void OpenEditor_Click(object sender, RoutedEventArgs e)
    {
        await NavigateToPageAsync("editor");
    }

    private async void OpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        await NavigateToPageAsync("explorer");
    }

    private async void OpenNotes_Click(object sender, RoutedEventArgs e)
    {
        await NavigateToPageAsync("notes");
    }

    private async void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        await NavigateToPageAsync("settings");
    }

    private async void Dashboard_Click(object sender, RoutedEventArgs e) => await NavigateToPageAsync("dashboard");

    public async void ShowSettings() => await NavigateToPageAsync("settings");

    public async void ShowDashboard() => await NavigateToPageAsync("dashboard");

    public async void NavigateBack() => await NavigateToPageAsync(_previousPage);

    public async Task OpenDocumentInEditorAsync(string path)
    {
        var editor = GetEditorPage();
        if (!await editor.OpenDocumentAsync(path)) return;
        PageHost.Content = editor;
        _currentPage = "editor";
        SetNavigationSelection(_currentPage);
    }

    public async Task<bool> PrepareDocumentForDeletionAsync(string path)
    {
        if (_pages.TryGetValue("editor", out var page) && page is EditorWindow editor)
            return await editor.PrepareDocumentDeletionAsync(path);
        return true;
    }

    private async Task NavigateToPageAsync(string page)
    {
        if (_currentPage == page) return;
        if (_currentPage == "editor" && _pages.TryGetValue("editor", out var current) &&
            current is EditorWindow editor && !await editor.PrepareToLeaveAsync())
            return;
        if (_currentPage == "notes" && _pages.TryGetValue("notes", out var notesPage) &&
            notesPage is QuickNotesWindow notes)
            notes.PrepareToLeave();

        if (page == "settings") _previousPage = _currentPage;
        if (page == "dashboard")
        {
            PageHost.Content = _dashboardPage;
        }
        else
        {
            PageHost.Content = page switch
            {
                "agenda" => GetPage("agenda", () => new AgendaWindow()),
                "editor" => GetPage("editor", () => new EditorWindow()),
                "explorer" => GetPage("explorer", () => new DocumentExplorerWindow()),
                "notes" => GetPage("notes", () => new QuickNotesWindow()),
                "settings" => GetPage("settings", () => new SettingsWindow()),
                _ => throw new ArgumentOutOfRangeException(nameof(page), page, "Page inconnue.")
            };
        }
        _currentPage = page;
        SetNavigationSelection(page);
        if (page == "agenda" && _pages.TryGetValue("agenda", out var agendaPage) && agendaPage is AgendaWindow agenda)
        {
            agenda.ReloadSubjects();
            RefreshTaskCounters();
        }
    }

    private T GetPage<T>(string key, Func<T> create) where T : UserControl
    {
        if (_pages.TryGetValue(key, out var existing)) return (T)existing;
        var page = create();
        _pages.Add(key, page);
        return page;
    }

    private EditorWindow GetEditorPage() => GetPage("editor", () => new EditorWindow());

    public void RefreshAgendaCounters() => RefreshTaskCounters();

    public void RefreshSchedule()
    {
        _scheduleRepository = new ScheduleRepository();
        _scheduler = new SchedulerService(_scheduleRepository, new WeekResolver());
        RefreshDashboard();
    }

    private void SetNavigationSelection(string page)
    {
        var selectedBackground = (Brush)FindResource("AccentSubtleBrush");
        var selectedForeground = (Brush)FindResource("AccentBrush");
        var inactiveForeground = (Brush)FindResource("MutedBrush");
        var navigation = new (string Page, Button Button)[]
        {
            ("dashboard", NavDashboard),
            ("agenda", NavAgenda),
            ("editor", NavDocuments),
            ("explorer", NavExplorer),
            ("notes", NavNotes),
            ("settings", NavSettings)
        };

        foreach (var item in navigation)
        {
            var isSelected = item.Page == page;
            item.Button.Background = isSelected ? selectedBackground : Brushes.Transparent;
            item.Button.Foreground = isSelected ? selectedForeground : inactiveForeground;
        }
    }

    private void ReduceButton_Click(object sender, RoutedEventArgs e) => AskToMinimize();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        AskToMinimize();
    }

    private void AskToMinimize()
    {
        var answer = MessageBox.Show(
            "Voulez-vous réduire SchoolDesk dans la barre des tâches ?",
            "Réduire SchoolDesk", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            WindowState = WindowState.Minimized;
    }
}
