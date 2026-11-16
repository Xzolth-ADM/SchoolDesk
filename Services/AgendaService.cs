using System.IO;
using System.Text.Json;
using SchoolDesk.Models;

namespace SchoolDesk.Services;

public sealed class AgendaService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SchoolDesk");
    private string FilePath => Path.Combine(_directory, "agenda.json");

    public List<AgendaItem> Load()
    {
        if (!File.Exists(FilePath)) return [];
        List<AgendaItem> items;
        try
        {
            items = JsonSerializer.Deserialize<List<AgendaItem>>(File.ReadAllText(FilePath))
                ?? throw new JsonException("Le fichier d'agenda contient une valeur vide.");
            if (items.Any(item => item is null))
                throw new JsonException("Le fichier d'agenda contient une tâche vide.");
        }
        catch (JsonException ex)
        {
            var backupPath = FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(FilePath, backupPath, false);
            throw new InvalidDataException($"L'agenda est illisible. Une copie a été conservée ici : {backupPath}", ex);
        }

        foreach (var item in items)
        {
            if (item.DueTime is null && item.DueDate.TimeOfDay != TimeSpan.Zero)
                item.DueTime = TimeOnly.FromDateTime(item.DueDate);
            item.DueDate = item.DueDate.Date;
        }
        return items;
    }

    public bool IsSubjectInUse(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return false;

        return Load().Any(item => string.Equals(item.Subject, subject, StringComparison.OrdinalIgnoreCase));
    }

    public void Save(IEnumerable<AgendaItem> items)
    {
        Directory.CreateDirectory(_directory);
        var temporaryPath = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(items, JsonOptions));
            if (File.Exists(FilePath) && !File.Exists(FilePath + ".bak"))
                File.Copy(FilePath, FilePath + ".bak");
            File.Move(temporaryPath, FilePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
