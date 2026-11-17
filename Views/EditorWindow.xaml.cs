using System.Globalization;
using System.IO;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SchoolDesk.Services;

namespace SchoolDesk.Views;

public partial class EditorWindow : UserControl
{
    private readonly SubjectService _subjects = new();
    private readonly DocumentService _documents = new();
    private readonly DocxService _docx = new();
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private string? _currentPath;
    private bool _loading;
    private bool _dirty;
    private bool _suppressFileSelection;
    private int _editVersion;
    private const double MaxImageWidth = 560;
    private const double MaxImageHeight = 720;
    private static readonly (string Name, Brush Brush)[] TextPalette =
    [
        ("Noir", Brushes.Black),
        ("Rouge foncé", Brushes.DarkRed),
        ("Bleu foncé", Brushes.Navy),
        ("Vert foncé", Brushes.DarkGreen),
        ("Brun", Brushes.SaddleBrown),
        ("Rose", Brushes.MediumVioletRed)
    ];
    private static readonly (string Name, Brush Brush)[] HighlightPalette =
    [
        ("Jaune", Brushes.Yellow),
        ("Vert clair", Brushes.LightGreen),
        ("Bleu clair", Brushes.LightBlue),
        ("Rose clair", Brushes.LightPink),
        ("Orange", Brushes.Orange)
    ];

    public EditorWindow()
    {
        InitializeComponent();
        SubjectBox.ItemsSource = _subjects.Load();
        SubjectBox.SelectedIndex = 0;
        EditorBox.Document = CreateDocument();
        _autosave.Tick += async (_, _) =>
        {
            _autosave.Stop();
            if (_dirty) await SaveCurrentAsync(false);
        };
        RefreshFiles();
        UpdateDocumentTitle();
        StatusText.Text = "Prêt · vos modifications sont enregistrées automatiquement";
    }

    private string Subject => SubjectBox.SelectedItem?.ToString() ?? "Sans matière";
    private string Type => (TypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Cours";

    private bool HasDocumentContent() =>
        !string.IsNullOrWhiteSpace(new TextRange(EditorBox.Document.ContentStart, EditorBox.Document.ContentEnd).Text);

    private static FlowDocument CreateDocument() => new()
    {
        PageWidth = 793.7,
        PageHeight = 1122.5,
        PagePadding = new Thickness(96),
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 16,
        Foreground = Brushes.Black
    };

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmLeaveCurrentAsync()) return;
        _loading = true;
        _currentPath = null;
        FileNameBox.Text = "Nouveau document";
        EditorBox.Document = CreateDocument();
        UpdatePageWidth();
        _loading = false;
        _dirty = false;
        _autosave.Stop();
        UpdateDocumentTitle();
        StatusText.Text = "Nouveau document · sauvegarde automatique activée";
        RefreshFiles();
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Documents Word (*.docx)|*.docx",
            Title = "Ouvrir un document Word",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true || !await ConfirmLeaveCurrentAsync()) return;
        await LoadFileAsync(dialog.FileName);
    }

    public async Task<bool> OpenDocumentAsync(string path)
    {
        if (!await ConfirmLeaveCurrentAsync()) return false;
        return await LoadFileAsync(path);
    }

    private async Task<bool> LoadFileAsync(string path)
    {
        try
        {
            StatusText.Text = "Ouverture du document…";
            EditorHintText.Text = "Lecture du DOCX…";
            var imported = await _docx.ImportAsync(path);
            if (imported.ContainsUnsupportedImages || imported.ContainsUnsupportedBlocks)
            {
                var content = imported.ContainsUnsupportedImages
                    ? "Ce document contient des images ou des formes Word qui ne peuvent pas être conservées par l'éditeur."
                    : "Ce document contient des blocs avancés qui ne peuvent pas être conservés par l'éditeur.";
                var answer = MessageBox.Show(
                    $"{content}\n\nSi vous le modifiez et l'enregistrez, le document original restera disponible dans le fichier .bak. Continuer quand même ?",
                    "Compatibilité DOCX limitée",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes)
                {
                    EditorHintText.Text = "DOCX · sauvegarde automatique";
                    StatusText.Text = "Ouverture annulée ; le document original n'a pas été modifié.";
                    return false;
                }
            }
            _loading = true;
            EditorBox.Document = imported.Document;
            UpdatePageWidth();
            _currentPath = path;
            FileNameBox.Text = Path.GetFileNameWithoutExtension(path);
            _loading = false;
            _dirty = false;
            _autosave.Stop();
            UpdateDocumentTitle();
            StatusText.Text = "Document ouvert · " + Path.GetFileName(path);
            RefreshFiles();
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException or UnauthorizedAccessException or ArgumentException or FormatException or NotSupportedException)
        {
            _loading = false;
            EditorHintText.Text = "DOCX · sauvegarde automatique";
            StatusText.Text = "Ouverture impossible";
            MessageBox.Show($"Impossible d'ouvrir ce DOCX : {ex.Message}", "SchoolDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveCurrentAsync(true);

    private async Task<bool> SaveCurrentAsync(bool showStatus)
    {
        if (_loading) return false;
        await _saveLock.WaitAsync();
        try
        {
            var path = EnsureDocumentPath();
            var version = _editVersion;
            var snapshot = EditorBox.Document;
            SaveButton.IsEnabled = false;
            EditorToolbar.IsEnabled = false;
            EditorSurface.IsEnabled = false;
            HeaderActions.IsEnabled = false;
            EditorHintText.Text = "Enregistrement…";
            await _docx.ExportAsync(snapshot, path);
            if (version == _editVersion) _dirty = false;
            EditorHintText.Text = _dirty ? "Nouvelles modifications…" : "DOCX · enregistré";
            StatusText.Text = showStatus
                ? "Document enregistré · " + Path.GetFileName(path)
                : $"Enregistré automatiquement · {DateTime.Now:HH:mm:ss}";
            RefreshFiles();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Xml.XmlException)
        {
            EditorHintText.Text = "Échec de l'enregistrement";
            StatusText.Text = "Erreur de sauvegarde : " + ex.Message;
            return false;
        }
        finally
        {
            SaveButton.IsEnabled = true;
            EditorToolbar.IsEnabled = true;
            EditorSurface.IsEnabled = true;
            HeaderActions.IsEnabled = true;
            _saveLock.Release();
            if (_dirty && IsLoaded && !_autosave.IsEnabled) _autosave.Start();
        }
    }

    private string EnsureDocumentPath()
    {
        if (!string.IsNullOrWhiteSpace(_currentPath)) return _currentPath;
        var raw = string.IsNullOrWhiteSpace(FileNameBox.Text) ? "Document" : FileNameBox.Text.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(raw.Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(name)) name = "Document";
        var folder = _documents.SubjectFolder(Subject, Type);
        var path = Path.Combine(folder, name + ".docx");
        if (File.Exists(path)) path = Path.Combine(folder, name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".docx");
        _currentPath = path;
        UpdateDocumentTitle();
        return path;
    }

    private async Task<bool> ConfirmLeaveCurrentAsync()
    {
        _autosave.Stop();
        if (!_dirty) return true;
        var answer = MessageBox.Show(
            "Enregistrer les modifications avant de continuer ?",
            "Modifications non enregistrées",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel)
        {
            if (_dirty && IsLoaded) _autosave.Start();
            return false;
        }
        if (answer == MessageBoxResult.No)
        {
            _dirty = false;
            return true;
        }
        return await SaveCurrentAsync(true) && !_dirty;
    }

    public Task<bool> PrepareToLeaveAsync() => ConfirmLeaveCurrentAsync();

    public async Task<bool> PrepareDocumentDeletionAsync(string path)
    {
        if (!string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!await ConfirmLeaveCurrentAsync())
            return false;

        _loading = true;
        _currentPath = null;
        FileNameBox.Text = "Nouveau document";
        EditorBox.Document = CreateDocument();
        UpdatePageWidth();
        _loading = false;
        _dirty = false;
        _autosave.Stop();
        UpdateDocumentTitle();
        EditorHintText.Text = "DOCX · sauvegarde automatique";
        StatusText.Text = "Document fermé avant sa suppression.";
        RefreshFiles();
        return true;
    }

    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmLeaveCurrentAsync() && Window.GetWindow(this) is MainWindow main)
            main.ShowDashboard();
    }

    private void EditorBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || !IsLoaded) return;
        _editVersion++;
        _dirty = true;
        EditorHintText.Text = "Modifications en attente…";
        StatusText.Text = "Sauvegarde automatique dans quelques secondes";
        _autosave.Stop();
        _autosave.Start();
    }

    private void FileName_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || !IsLoaded || _currentPath is not null) return;
        _editVersion++;
        _dirty = true;
        UpdateDocumentTitle();
        _autosave.Stop();
        _autosave.Start();
    }

    private void Metadata_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || SubjectBox.SelectedItem is null || TypeBox.SelectedItem is null) return;
        RefreshFiles();
    }

    private void UpdateDocumentTitle()
    {
        if (DocumentTitleText is not null)
            DocumentTitleText.Text = string.IsNullOrWhiteSpace(FileNameBox.Text) ? "Nouveau document" : FileNameBox.Text;
    }

    private void RefreshFiles()
    {
        if (FilesList is null || SubjectBox.SelectedItem is null || TypeBox.SelectedItem is null) return;
        try
        {
            var folder = _documents.GetSubjectFolder(Subject, Type);
            Directory.CreateDirectory(folder);
            _suppressFileSelection = true;
            FilesList.ItemsSource = Directory.GetFiles(folder, "*.docx")
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _suppressFileSelection = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _suppressFileSelection = false;
            StatusText.Text = "Impossible d'afficher les documents : " + ex.Message;
        }
    }

    private async void FilesList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFileSelection || FilesList.SelectedItem is not string name) return;
        var path = Path.Combine(_documents.GetSubjectFolder(Subject, Type), name);
        if (File.Exists(path) && !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase)
            && await ConfirmLeaveCurrentAsync())
            await LoadFileAsync(path);
    }

    private void Bold_Click(object sender, RoutedEventArgs e) => ToggleFormat(TextElement.FontWeightProperty, FontWeights.Bold, FontWeights.Normal);
    private void Italic_Click(object sender, RoutedEventArgs e) => ToggleFormat(TextElement.FontStyleProperty, FontStyles.Italic, FontStyles.Normal);

    private void Underline_Click(object sender, RoutedEventArgs e)
    {
        var value = EditorBox.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        var hasUnderline = value is TextDecorationCollection decorations && decorations.Any(item => item.Location == TextDecorationLocation.Underline);
        EditorBox.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, hasUnderline ? new TextDecorationCollection() : TextDecorations.Underline);
        EditorBox.Focus();
    }

    private void Strike_Click(object sender, RoutedEventArgs e)
    {
        var value = EditorBox.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
        var hasStrike = value is TextDecorationCollection decorations && decorations.Any(item => item.Location == TextDecorationLocation.Strikethrough);
        EditorBox.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, hasStrike ? new TextDecorationCollection() : TextDecorations.Strikethrough);
        EditorBox.Focus();
    }

    private void ToggleFormat(DependencyProperty property, object enabled, object disabled)
    {
        var current = EditorBox.Selection.GetPropertyValue(property);
        EditorBox.Selection.ApplyPropertyValue(property, Equals(current, enabled) ? disabled : enabled);
        EditorBox.Focus();
    }

    private void Color_Click(object sender, RoutedEventArgs e) =>
        ShowColorPalette(sender, TextPalette, TextElement.ForegroundProperty);

    private void Highlight_Click(object sender, RoutedEventArgs e) =>
        ShowColorPalette(sender, HighlightPalette, TextElement.BackgroundProperty);

    private void ShowColorPalette(object sender, (string Name, Brush Brush)[] colors, DependencyProperty property)
    {
        if (sender is not Button button) return;
        var palette = new ContextMenu
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom
        };
        foreach (var color in colors)
        {
            var item = new MenuItem
            {
                Header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new Border
                        {
                            Width = 16,
                            Height = 16,
                            Background = color.Brush,
                            BorderBrush = (Brush)FindResource("BorderBrush"),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(2),
                            Margin = new Thickness(0, 0, 8, 0)
                        },
                        new TextBlock { Text = color.Name, VerticalAlignment = VerticalAlignment.Center }
                    }
                }
            };
            item.Click += (_, _) =>
            {
                EditorBox.Selection.ApplyPropertyValue(property, color.Brush);
                EditorBox.Focus();
                MarkFormatted();
            };
            palette.Items.Add(item);
        }
        palette.IsOpen = true;
    }

    private void Style_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || StyleBox.SelectedItem is not ComboBoxItem item || EditorBox.CaretPosition.Paragraph is not Paragraph paragraph) return;
        switch (item.Content?.ToString())
        {
            case "Titre":
                paragraph.Tag = "Title";
                paragraph.FontSize = 30 * 96d / 72d;
                paragraph.FontWeight = FontWeights.Bold;
                paragraph.Foreground = (Brush)FindResource("AccentBrush");
                paragraph.Margin = new Thickness(0, 0, 0, 16);
                break;
            case "Titre 1":
                paragraph.Tag = "Heading1";
                paragraph.FontSize = 24 * 96d / 72d;
                paragraph.FontWeight = FontWeights.Bold;
                paragraph.Foreground = Brushes.Black;
                paragraph.Margin = new Thickness(0, 18, 0, 9);
                break;
            case "Titre 2":
                paragraph.Tag = "Heading2";
                paragraph.FontSize = 20 * 96d / 72d;
                paragraph.FontWeight = FontWeights.Bold;
                paragraph.Foreground = Brushes.Black;
                paragraph.Margin = new Thickness(0, 15, 0, 8);
                break;
            case "Titre 3":
                paragraph.Tag = "Heading3";
                paragraph.FontSize = 16 * 96d / 72d;
                paragraph.FontWeight = FontWeights.SemiBold;
                paragraph.Foreground = Brushes.Black;
                paragraph.Margin = new Thickness(0, 12, 0, 6);
                break;
            default:
                paragraph.Tag = "Normal";
                paragraph.FontSize = 12 * 96d / 72d;
                paragraph.FontWeight = FontWeights.Normal;
                paragraph.Foreground = Brushes.Black;
                paragraph.Margin = new Thickness(0, 0, 0, 8);
                break;
        }
        MarkFormatted();
    }

    private void Font_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || FontBox.SelectedItem is not ComboBoxItem item) return;
        EditorBox.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(item.Content?.ToString() ?? "Segoe UI"));
        MarkFormatted();
    }

    private void Size_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || SizeBox.SelectedItem is not ComboBoxItem item ||
            !double.TryParse(item.Content?.ToString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var points)) return;
        EditorBox.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, points * 96d / 72d);
        MarkFormatted();
    }

    private void MarkFormatted()
    {
        EditorBox.Focus();
        if (_loading) return;
        _editVersion++;
        _dirty = true;
        EditorHintText.Text = "Modifications en attente…";
        _autosave.Stop();
        _autosave.Start();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => EditorBox.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => EditorBox.Redo();
    private void Left_Click(object sender, RoutedEventArgs e) => EditingCommands.AlignLeft.Execute(null, EditorBox);
    private void Center_Click(object sender, RoutedEventArgs e) => EditingCommands.AlignCenter.Execute(null, EditorBox);
    private void Right_Click(object sender, RoutedEventArgs e) => EditingCommands.AlignRight.Execute(null, EditorBox);
    private void Justify_Click(object sender, RoutedEventArgs e) => EditingCommands.AlignJustify.Execute(null, EditorBox);
    private void Bullet_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleBullets.Execute(null, EditorBox);
    private void Number_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleNumbering.Execute(null, EditorBox);

    private void InsertTableMenu_Click(object sender, RoutedEventArgs e) => OpenInsertMenu(sender);

    private void InsertShapeMenu_Click(object sender, RoutedEventArgs e) => OpenInsertMenu(sender);

    private static void OpenInsertMenu(object sender)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void InsertTable_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string dimensions }) return;
        var parts = dimensions.Split(',');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var rows) ||
            !int.TryParse(parts[1], out var columns) || rows is < 1 or > 10 || columns is < 1 or > 10)
            return;

        if (!EditorBox.Selection.IsEmpty) EditorBox.Selection.Text = string.Empty;
        var paragraph = EditorBox.CaretPosition.Paragraph;
        if (paragraph is null) return;

        var border = (Brush)FindResource("BorderBrush");
        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 8, 0, 8)
        };
        for (var column = 0; column < columns; column++)
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

        var group = new TableRowGroup();
        for (var rowIndex = 0; rowIndex < rows; rowIndex++)
        {
            var row = new TableRow();
            for (var column = 0; column < columns; column++)
            {
                var cell = new TableCell
                {
                    BorderBrush = border,
                    BorderThickness = new Thickness(.5),
                    Padding = new Thickness(8),
                    Background = rowIndex == 0 ? (Brush)FindResource("AccentSubtleBrush") : Brushes.White
                };
                cell.Blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 2) });
                row.Cells.Add(cell);
            }
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        paragraph.SiblingBlocks.InsertAfter(paragraph, table);
        EditorBox.CaretPosition = table.RowGroups[0].Rows[0].Cells[0].ContentStart;
        EditorBox.Focus();
        MarkFormatted();
    }

    private async void InsertImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff",
            Title = "Insérer une image"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var image = await Task.Run(() => DecodeImage(dialog.FileName));
            InsertInlineImage(image, Path.GetFileName(dialog.FileName));
            StatusText.Text = "Image insérée · elle sera intégrée au DOCX à l'enregistrement";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or FileFormatException)
        {
            MessageBox.Show($"Impossible d'insérer cette image : {ex.Message}", "Image non prise en charge", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static BitmapSource DecodeImage(string path)
    {
        var file = new FileInfo(path);
        if (file.Length > 25 * 1024 * 1024)
            throw new InvalidDataException("La taille maximale d'une image est de 25 Mo.");

        var decoder = BitmapDecoder.Create(
            new Uri(path),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.FirstOrDefault()
            ?? throw new InvalidDataException("Le fichier ne contient pas d'image décodable.");
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 ||
            (long)frame.PixelWidth * frame.PixelHeight > 80_000_000)
            throw new InvalidDataException("Les dimensions de cette image sont trop grandes.");
        frame.Freeze();
        return frame;
    }

    private void InsertShape_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string shape }) return;
        var source = DrawShape(shape);
        var name = shape switch
        {
            "rectangle" => "Rectangle",
            "ellipse" => "Ellipse",
            "arrow" => "Flèche",
            "line" => "Ligne",
            _ => "Forme"
        };
        InsertInlineImage(source, name);
        StatusText.Text = $"{name} insérée · forme intégrée au DOCX comme image";
    }

    private static BitmapSource DrawShape(string shape)
    {
        const int width = 480;
        const int height = 180;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var stroke = new Pen(new SolidColorBrush(Color.FromRgb(49, 90, 155)), 5);
            stroke.Freeze();
            var fill = new SolidColorBrush(Color.FromRgb(231, 237, 247));
            fill.Freeze();
            switch (shape)
            {
                case "rectangle":
                    context.DrawRoundedRectangle(fill, stroke, new Rect(24, 24, width - 48, height - 48), 8, 8);
                    break;
                case "ellipse":
                    context.DrawEllipse(fill, stroke, new Point(width / 2d, height / 2d), width / 2d - 24, height / 2d - 24);
                    break;
                case "arrow":
                    var arrowLine = new Pen(new SolidColorBrush(Color.FromRgb(49, 90, 155)), 7)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round
                    };
                    arrowLine.Freeze();
                    context.DrawLine(arrowLine, new Point(32, height / 2d), new Point(width - 62, height / 2d));
                    var arrowHead = new StreamGeometry();
                    using (var geometry = arrowHead.Open())
                    {
                        geometry.BeginFigure(new Point(width - 62, 24), true, true);
                        geometry.LineTo(new Point(width - 24, height / 2d), true, false);
                        geometry.LineTo(new Point(width - 62, height - 24), true, false);
                    }
                    arrowHead.Freeze();
                    context.DrawGeometry(new SolidColorBrush(Color.FromRgb(49, 90, 155)), null, arrowHead);
                    break;
                case "line":
                    var line = new Pen(new SolidColorBrush(Color.FromRgb(49, 90, 155)), 5)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round
                    };
                    line.Freeze();
                    context.DrawLine(line, new Point(24, height / 2d), new Point(width - 24, height / 2d));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape), shape, "Forme inconnue.");
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private void InsertInlineImage(BitmapSource source, string description)
    {
        if (!EditorBox.Selection.IsEmpty) EditorBox.Selection.Text = string.Empty;
        var scale = Math.Min(1, Math.Min(MaxImageWidth / source.PixelWidth, MaxImageHeight / source.PixelHeight));
        var image = new System.Windows.Controls.Image
        {
            Source = source,
            Width = source.PixelWidth * scale,
            Height = source.PixelHeight * scale,
            Stretch = Stretch.Uniform,
            ToolTip = description,
            SnapsToDevicePixels = true
        };
        AutomationProperties.SetName(image, description);
        var container = new InlineUIContainer(image, EditorBox.CaretPosition)
        {
            BaselineAlignment = BaselineAlignment.Center
        };
        EditorBox.CaretPosition = container.ElementEnd;
        EditorBox.Focus();
        MarkFormatted();
    }

    private void EditorBox_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePageWidth();

    private void UpdatePageWidth()
    {
        if (EditorBox?.Document is null || EditorBox.ActualWidth <= 0) return;
        EditorBox.Document.PageWidth = Math.Min(793.7, Math.Max(240, EditorBox.ActualWidth - 2));
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.S)
        {
            e.Handled = true;
            await SaveCurrentAsync(true);
        }
        else if (e.Key == Key.N)
        {
            e.Handled = true;
            New_Click(sender, e);
        }
        else if (e.Key == Key.O)
        {
            e.Handled = true;
            Open_Click(sender, e);
        }
    }
}
