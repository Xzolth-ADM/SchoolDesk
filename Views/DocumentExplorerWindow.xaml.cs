using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualBasic.FileIO;
using SchoolDesk.Services;

namespace SchoolDesk.Views;

public sealed record DocumentEntry(string Path, string Name, string Modified, string Size);

public partial class DocumentExplorerWindow : UserControl
{
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");
    private readonly SubjectService _subjects = new();
    private readonly DocumentService _documents = new();
    private List<DocumentEntry> _allFiles = [];

    public DocumentExplorerWindow()
    {
        InitializeComponent();
        SubjectBox.ItemsSource = _subjects.Load();
        SubjectBox.SelectedIndex = 0;
        RefreshFiles();
    }

    private string Type => (TypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Cours";

    private void Location_Changed(object sender, SelectionChangedEventArgs e) => RefreshFiles();

    private void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void RefreshFiles()
    {
        if (FilesList is null || SubjectBox.SelectedItem is null || TypeBox.SelectedItem is null) return;
        var folder = _documents.GetSubjectFolder(SubjectBox.SelectedItem.ToString() ?? "", Type);
        FolderText.Text = folder;
        try
        {
            Directory.CreateDirectory(folder);
            _allFiles = Directory.GetFiles(folder, "*.docx")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTime)
                .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(file => new DocumentEntry(
                    file.FullName,
                    file.Name,
                    $"Modifié le {file.LastWriteTime.ToString("d MMM yyyy · HH:mm", FrenchCulture)}",
                    FormatSize(file.Length)))
                .ToList();
            ApplyFilter();
            StatusText.Text = "Les fichiers sont conservés sur cet ordinateur.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _allFiles = [];
            FilesList.ItemsSource = null;
            CountText.Text = "Dossier inaccessible";
            EmptyText.Text = "Impossible de lire ce dossier. Vérifie les autorisations ou réessaie.";
            EmptyState.Visibility = Visibility.Visible;
            StatusText.Text = ex.Message;
            UpdateActions();
        }
    }

    private void ApplyFilter()
    {
        if (FilesList is null) return;
        var query = SearchBox?.Text.Trim();
        var files = string.IsNullOrEmpty(query)
            ? _allFiles
            : _allFiles.Where(file => file.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        FilesList.ItemsSource = files;
        CountText.Text = query is { Length: > 0 }
            ? $"{files.Count} résultat(s) sur {_allFiles.Count}"
            : _allFiles.Count == 1 ? "1 document" : $"{_allFiles.Count} documents";
        EmptyText.Text = _allFiles.Count == 0
            ? "Aucun document dans cette catégorie. Les fichiers DOCX enregistrés apparaîtront ici."
            : "Aucun document ne correspond à cette recherche.";
        EmptyState.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateActions();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} o";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024d).ToString("0.#", FrenchCulture)} Ko";
        return $"{(bytes / (1024d * 1024d)).ToString("0.#", FrenchCulture)} Mo";
    }

    private void UpdateActions()
    {
        var hasSelection = FilesList?.SelectedItem is DocumentEntry;
        if (OpenButton is not null) OpenButton.IsEnabled = hasSelection;
        if (DeleteButton is not null) DeleteButton.IsEnabled = hasSelection;
    }

    private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateActions();
        if (FilesList.SelectedItem is DocumentEntry entry)
            StatusText.Text = $"{entry.Name} · {entry.Size} · {entry.Modified}";
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SubjectBox.SelectedItem is null) return;
        var folder = _documents.GetSubjectFolder(SubjectBox.SelectedItem.ToString() ?? "", Type);
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show($"Impossible d'ouvrir le dossier : {ex.Message}", "SchoolDesk", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void FilesList_DoubleClick(object sender, MouseButtonEventArgs e) => await OpenDocumentAsync();

    private async void OpenDocument_Click(object sender, RoutedEventArgs e) => await OpenDocumentAsync();

    private async Task OpenDocumentAsync()
    {
        if (FilesList.SelectedItem is not DocumentEntry entry) return;
        if (Window.GetWindow(this) is MainWindow main)
            await main.OpenDocumentInEditorAsync(entry.Path);
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is not DocumentEntry entry) return;
        var answer = MessageBox.Show(
            $"Envoyer « {entry.Name} » dans la Corbeille Windows ?\n\nTu pourras le restaurer depuis la Corbeille.",
            "Supprimer le document",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        if (Window.GetWindow(this) is MainWindow main &&
            !await main.PrepareDocumentForDeletionAsync(entry.Path))
            return;

        try
        {
            if (!File.Exists(entry.Path))
            {
                RefreshFiles();
                StatusText.Text = "Le fichier n’existe plus; la liste a été actualisée.";
                return;
            }

            FileSystem.DeleteFile(entry.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            RefreshFiles();
            StatusText.Text = $"« {entry.Name} » a été envoyé dans la Corbeille Windows.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            MessageBox.Show(
                $"Impossible de supprimer « {entry.Name} » : {ex.Message}\n\nLe fichier n’a pas été supprimé.",
                "Suppression impossible",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow main)
            main.ShowDashboard();
    }
}
