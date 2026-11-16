namespace SchoolDesk.Models;

public sealed class WeekConfiguration
{
    public DateOnly? ReferenceDate { get; set; }
    public string? ReferenceWeek { get; set; }
    public string? TimeZoneId { get; set; } = "Romance Standard Time";
}
