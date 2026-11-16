namespace SchoolDesk.Models;

public sealed class AgendaItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Type { get; set; } = "Devoir";
    public DateTime DueDate { get; set; } = DateTime.Today;
    public TimeOnly? DueTime { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public DateTime? CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; } = DateTime.Now;
    public string DisplayDate => DueTime?.ToString("HH:mm") ?? "Heure non définie";
    public string DisplaySubtitle => string.IsNullOrWhiteSpace(Subject) ? Type : $"{Type} · {Subject}";
}
