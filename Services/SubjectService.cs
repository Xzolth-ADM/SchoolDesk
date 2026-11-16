using System.IO;
using System.Text.Json;

namespace SchoolDesk.Services;

public sealed class SubjectService
{
    private readonly string _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SchoolDesk");
    private string FilePath => Path.Combine(_folder, "subjects.json");
    public List<string> Load()
    {
        if (!File.Exists(FilePath)) return Defaults();
        try
        {
            var subjects = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath))
                ?? throw new JsonException("La liste des matières est vide.");
            return subjects.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (JsonException ex)
        {
            var backupPath = FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(FilePath, backupPath, false);
            throw new InvalidDataException($"La liste des matières est illisible. Une copie a été conservée ici : {backupPath}", ex);
        }
    }

    public void Save(IEnumerable<string> subjects)
    {
        Directory.CreateDirectory(_folder);
        var temp = FilePath + ".tmp";
        try
        {
            var normalized = subjects.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase);
            File.WriteAllText(temp, JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", true);
            File.Move(temp, FilePath, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public static List<string> Defaults() => new ScheduleRepository().Subjects.ToList();
}
