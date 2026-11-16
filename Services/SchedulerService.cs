using SchoolDesk.Models;

namespace SchoolDesk.Services;

public sealed class SchedulerService(ScheduleRepository repository, WeekResolver weekResolver)
{
    private static readonly TimeSpan TransitionThreshold = TimeSpan.FromMinutes(10);

    public SchedulerResult Evaluate(DateTime now, WeekConfiguration configuration)
    {
        var date = DateOnly.FromDateTime(now);
        var week = weekResolver.Resolve(date, configuration);
        if (week is null)
            return new(ScheduleState.Free, null, null, null, null, null, "?", "Configure la semaine de référence A/B.");
        if (!repository.HasEntries)
            return new(ScheduleState.Free, null, null, null, null, null, week, "Configure ton emploi du temps local.");

        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            var weekendLesson = FindNextLesson(date, TimeOnly.MinValue, configuration);
            return new(ScheduleState.Finished, null, null, weekendLesson.Entry, weekendLesson.DateTime, null, week, "Pas de cours aujourd'hui.");
        }

        var entries = repository.ForDay(now.DayOfWeek, week);
        var time = TimeOnly.FromDateTime(now);
        var current = entries.FirstOrDefault(x => time >= x.Start && time < x.End);
        var next = entries.FirstOrDefault(x => x.Start > time);
        var nextLesson = FindNextLesson(date, time, configuration);

        if (current is not null)
        {
            var remaining = current.End.ToTimeSpan() - time.ToTimeSpan();
            var message = current.Kind switch
            {
                ScheduleKind.Break => "Récréation",
                ScheduleKind.Lunch => "Pause déjeuner",
                ScheduleKind.Free => "Temps libre",
                ScheduleKind.HomeMeal => "Repas à la maison",
                _ => "En cours"
            };
            return new(ScheduleState.InProgress, current, next, nextLesson.Entry, nextLesson.DateTime, remaining, week, message);
        }

        var previous = entries.LastOrDefault(x => x.End <= time);
        if (next is null)
            return new(ScheduleState.Finished, previous, null, nextLesson.Entry, nextLesson.DateTime, null, week, "Journée terminée.");

        if (previous is null)
            return new(ScheduleState.BeforeFirstEvent, null, next, nextLesson.Entry, nextLesson.DateTime, next.Start.ToTimeSpan() - time.ToTimeSpan(), week, "Avant le premier cours");

        var gap = next.Start.ToTimeSpan() - time.ToTimeSpan();
        var state = gap <= TransitionThreshold ? ScheduleState.Transition : ScheduleState.Free;
        return new(state, null, next, nextLesson.Entry, nextLesson.DateTime, gap, week, state == ScheduleState.Transition ? "Transition" : "Temps libre");
    }

    private (ScheduleEntry? Entry, DateTime? DateTime) FindNextLesson(
        DateOnly fromDate, TimeOnly afterTime, WeekConfiguration configuration)
    {
        // Recherche sur les 14 prochains jours pour couvrir week-ends et alternance A/B.
        for (var offset = 0; offset < 14; offset++)
        {
            var date = fromDate.AddDays(offset);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;

            var week = weekResolver.Resolve(date, configuration);
            if (week is null) return (null, null);

            var threshold = offset == 0 ? afterTime : TimeOnly.MinValue;
            var lesson = repository.ForDay(date.DayOfWeek, week)
                .FirstOrDefault(x => x.Kind == ScheduleKind.Lesson && x.Start > threshold);

            if (lesson is not null)
                return (lesson, date.ToDateTime(lesson.Start));
        }

        return (null, null);
    }
}
