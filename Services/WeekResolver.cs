using SchoolDesk.Models;

namespace SchoolDesk.Services;

public sealed class WeekResolver
{
    public string? Resolve(DateOnly date, WeekConfiguration configuration)
    {
        if (configuration.ReferenceDate is null || string.IsNullOrWhiteSpace(configuration.ReferenceWeek))
            return null;

        if (configuration.ReferenceWeek is not ("A" or "B"))
            return null;

        var weekIndex = GetWeekIndex(date, configuration.ReferenceDate.Value, configuration.ReferenceWeek);
        return weekIndex % 2 == 0 ? "A" : "B";
    }

    public int GetWeekIndex(DateOnly date, DateOnly referenceDate, string referenceWeek)
    {
        if (referenceWeek is not ("A" or "B"))
            throw new ArgumentOutOfRangeException(nameof(referenceWeek), "La semaine de référence doit être A ou B.");

        var referenceMonday = StartOfWeek(referenceDate);
        var currentMonday = StartOfWeek(date);
        var deltaWeeks = (currentMonday.DayNumber - referenceMonday.DayNumber) / 7;
        var parityOffset = referenceWeek == "A" ? 0 : 1;
        return (deltaWeeks + parityOffset) % 2;
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7; // lundi = 0
        return date.AddDays(-offset);
    }
}
