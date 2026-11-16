using System.IO;

namespace SchoolDesk.Services;

public sealed class QuickNotesService
{
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SchoolDesk");
    private string FilePath => Path.Combine(_directory, "quick-notes.txt");

    public string Load() => File.Exists(FilePath) ? File.ReadAllText(FilePath) : string.Empty;

    public void Save(string content)
    {
        Directory.CreateDirectory(_directory);
        var temporaryPath = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, content);
            if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", true);
            File.Move(temporaryPath, FilePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
