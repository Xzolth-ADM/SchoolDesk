using System.IO;

namespace SchoolDesk.Services;

public sealed class DocumentService
{
    public string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SchoolDesk");

    public string SubjectFolder(string subject, string type)
    {
        var path = Path.Combine(Root, Clean(subject), Clean(type));
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetSubjectFolder(string subject, string type) =>
        Path.Combine(Root, Clean(subject), Clean(type));

    public bool HasSubjectContent(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return false;

        var subjectFolder = Path.Combine(Root, Clean(subject));
        if (!Directory.Exists(subjectFolder))
            return false;

        return Directory.EnumerateFileSystemEntries(subjectFolder, "*", SearchOption.AllDirectories).Any();
    }

    private static string Clean(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((value ?? string.Empty).Trim().Select(c => invalid.Contains(c) ? '-' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) || cleaned is "." or ".." ? "Sans matière" : cleaned;
    }
}
