using System.IO;
using System.Text.Json;
using SchoolDesk.Models;

namespace SchoolDesk.Services;

public sealed class ScheduleRepository
{
    private readonly List<ScheduleEntry> _entries;
    public bool HasEntries => _entries.Count > 0;
    public static string UserSchedulePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SchoolDesk", "schedule.json");

    public IReadOnlyList<string> Subjects => _entries
        .Where(x => x.Kind == ScheduleKind.Lesson)
        .Select(x => x.Title.Trim())
        .Where(x => x.Length > 0)
        .Distinct(StringComparer.CurrentCultureIgnoreCase)
        .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    public ScheduleRepository()
    {
        EnsureUserScheduleFile();
        var path = UserSchedulePath;

        var raw = JsonSerializer.Deserialize<List<ScheduleEntryDto>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                  ?? throw new InvalidDataException("L'emploi du temps est vide ou illisible.");
        if (raw.Any(x => x is null))
            throw new InvalidDataException("L'emploi du temps contient une entrée vide.");

        try
        {
            _entries = raw.Select(x => new ScheduleEntry
            {
                Day = Enum.Parse<DayOfWeek>(x.Day, true),
                Week = x.Week is "A" or "B" or "AB" ? x.Week : throw new InvalidDataException($"Semaine inconnue : {x.Week}."),
                Start = TimeOnly.Parse(x.Start),
                End = TimeOnly.Parse(x.End),
                Title = string.IsNullOrWhiteSpace(x.Title) ? throw new InvalidDataException("Un événement du planning n'a pas de titre.") : x.Title.Trim(),
                Room = x.Room,
                Kind = Enum.Parse<ScheduleKind>(x.Kind, true)
            }).ToList();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            throw new InvalidDataException("Le planning contient une journée, une heure, une catégorie ou une semaine invalide.", ex);
        }

        Validate();
    }

    public static void EnsureUserScheduleFile()
    {
        if (File.Exists(UserSchedulePath)) return;
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "schedule.json");
        if (!File.Exists(templatePath)) throw new FileNotFoundException("Le modèle d'emploi du temps est introuvable.", templatePath);
        Directory.CreateDirectory(Path.GetDirectoryName(UserSchedulePath)!);
        File.Copy(templatePath, UserSchedulePath);
    }

    public IReadOnlyList<ScheduleEntry> ForDay(DayOfWeek day, string week) =>
        _entries.Where(x => x.Day == day && x.AppliesTo(week))
            .OrderBy(x => x.Start).ToList();

    private void Validate()
    {
        foreach (var day in _entries.GroupBy(x => x.Day))
        {
            foreach (var week in new[] { "A", "B" })
            {
                var ordered = day.Where(x => x.AppliesTo(week)).OrderBy(x => x.Start).ToList();
                for (var i = 0; i < ordered.Count; i++)
                {
                    if (ordered[i].End <= ordered[i].Start)
                        throw new InvalidDataException($"Durée invalide : {ordered[i].Title}.");
                    if (i > 0 && ordered[i].Start < ordered[i - 1].End)
                        throw new InvalidDataException($"Chevauchement détecté le {day.Key} (semaine {week}).");
                }
            }
        }
    }

    private sealed class ScheduleEntryDto
    {
        public string Day { get; set; } = "Monday";
        public string Week { get; set; } = "AB";
        public string Start { get; set; } = "08:00";
        public string End { get; set; } = "09:00";
        public string Title { get; set; } = string.Empty;
        public string? Room { get; set; }
        public string Kind { get; set; } = "Lesson";
    }
}
