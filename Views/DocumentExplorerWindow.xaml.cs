using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SchoolDesk.Services;

namespace SchoolDesk.Views;

public partial class DocumentExplorerWindow : UserControl
{
    private readonly SubjectService _subjects = new();
    private readonly DocumentService _documents = new();

    public DocumentExplorerWindow()
    {
        InitializeComponent();
        SubjectBox.ItemsSource = _subjects.Load();
        SubjectBox.SelectedIndex = 0;
        RefreshFiles();
    }

    private string Type => (TypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Cours";

    private void Location_Changed(object sender, SelectionChangedEventArgs e) => RefreshFiles();

    private void RefreshFiles()
    {
        if (FilesList is null || SubjectBox.SelectedItem is null || TypeBox.SelectedItem is null) return;
        var folder = _documents.GetSubjectFolder(SubjectBox.SelectedItem.ToString() ?? "", Type);
        FolderText.Text = folder;
        try
        {
            Directory.CreateDirectory(folder);
            var files = Directory.GetFiles(folder, "*.docx")
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            FilesList.ItemsSource = files;
            EmptyText.Text = files.Count == 0 ? "Aucun document dans cette catégorie." : $"{files.Count} document(s)";
            StatusText.Text = "Les fichiers sont stockés sur cet ordinateur.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FilesList.ItemsSource = null;
            EmptyText.Text = "Impossible de lire ce dossier.";
            StatusText.Text = ex.Message;
        }
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
        if (FilesList.SelectedItem is not string path) return;
        if (Window.GetWindow(this) is MainWindow main)
            await main.OpenDocumentInEditorAsync(path);
    }
}
