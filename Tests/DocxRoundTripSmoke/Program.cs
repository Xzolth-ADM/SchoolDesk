using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using SchoolDesk.Controls;
using SchoolDesk.Models;
using SchoolDesk.Services;

namespace SchoolDesk.DocxRoundTripSmoke;

internal static class Program
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [STAThread]
    private static int Main()
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Startup += async (_, _) =>
        {
            try
            {
                await VerifyRoundTripAsync();
                VerifySchedulePersistence();
                Console.WriteLine("DOCX and timetable smoke checks passed.");
                Environment.ExitCode = 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                Environment.ExitCode = 1;
            }
            finally
            {
                application.Shutdown();
            }
        };
        return application.Run();
    }

    private static void VerifySchedulePersistence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SchoolDesk-schedule-{Guid.NewGuid():N}.json");
        var monday = new ScheduleEntry
        {
            Day = DayOfWeek.Monday,
            Week = "AB",
            Start = new TimeOnly(8, 10),
            End = new TimeOnly(9, 5),
            Title = "Chinois LV2",
            Room = "s.104",
            Teacher = "Tornier.Y",
            Kind = ScheduleKind.Lesson
        };
        var transitionLesson = new ScheduleEntry
        {
            Day = DayOfWeek.Monday,
            Week = "AB",
            Start = new TimeOnly(9, 10),
            End = new TimeOnly(10, 5),
            Title = "Technologie",
            Room = "s.T5 · Vilpois C",
            Kind = ScheduleKind.Lesson
        };
        try
        {
            ScheduleRepository.SaveEntries(path, [monday, transitionLesson]);
            var repository = new ScheduleRepository(path);
            Assert(repository.HasEntries && repository.ForDay(DayOfWeek.Monday, "A").Count == 2,
                "The schedule was not saved and reloaded.");
            Assert(repository.ForDay(DayOfWeek.Monday, "B").Count == 2,
                "An A/B shared lesson was not returned for both weeks.");
            var scheduler = new SchedulerService(repository, new WeekResolver());
            var result = scheduler.Evaluate(
                new DateTime(2026, 11, 16, 9, 7, 0),
                new WeekConfiguration { ReferenceDate = new DateOnly(2026, 11, 11), ReferenceWeek = "A" });
            Assert(result.State == ScheduleState.Transition && result.Remaining == TimeSpan.FromMinutes(3),
                "The five-minute between-class transition was not calculated.");

            var overlap = new ScheduleEntry
            {
                Day = DayOfWeek.Monday,
                Week = "A",
                Start = new TimeOnly(9, 0),
                End = new TimeOnly(9, 30),
                Title = "Chevauchement",
                Kind = ScheduleKind.Lesson
            };
            try
            {
                ScheduleRepository.SaveEntries(path, [monday, transitionLesson, overlap]);
                throw new InvalidDataException("A colliding timetable slot was accepted.");
            }
            catch (InvalidDataException exception) when (exception.Message.Contains("Chevauchement", StringComparison.Ordinal))
            {
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }
    }

    private static async Task VerifyRoundTripAsync()
    {
        foreach (var name in new[] { "image", "table", "shapes" })
            _ = new SvgIcon { Icon = name };

        var firstPath = Path.Combine(Path.GetTempPath(), $"SchoolDesk-docx-{Guid.NewGuid():N}.docx");
        var secondPath = Path.Combine(Path.GetTempPath(), $"SchoolDesk-docx-roundtrip-{Guid.NewGuid():N}.docx");
        try
        {
            var document = new FlowDocument { PageWidth = 793.7, PageHeight = 1122.5, PagePadding = new Thickness(96) };
            document.Blocks.Add(new Paragraph(new Run("Document de test"))
            {
                Tag = "Title",
                FontSize = 40,
                FontWeight = FontWeights.Bold
            });
            document.Blocks.Add(new Paragraph(new Run("Titre de section"))
            {
                Tag = "Heading1",
                FontSize = 32,
                FontWeight = FontWeights.Bold
            });
            document.Blocks.Add(new Paragraph(new Run("Sous-section"))
            {
                Tag = "Heading2",
                FontSize = 26.67,
                FontWeight = FontWeights.Bold
            });
            document.Blocks.Add(new Paragraph(new Run("Petit titre"))
            {
                Tag = "Heading3",
                FontSize = 21.33,
                FontWeight = FontWeights.SemiBold
            });
            document.Blocks.Add(new Paragraph(new Run("Surlignage orange")
            {
                Background = new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                FontSize = 16
            }));
            var imageParagraph = new Paragraph();
            imageParagraph.Inlines.Add(new InlineUIContainer(new Image
            {
                Source = CreateTestImage(),
                Width = 48,
                Height = 32,
                ToolTip = "Image de test"
            }));
            document.Blocks.Add(imageParagraph);
            document.Blocks.Add(CreateTestTable());

            var service = new DocxService();
            await service.ExportAsync(document, firstPath);
            VerifyPackage(firstPath, expectedImages: 1, expectedHeadings: ["Title", "Heading1", "Heading2", "Heading3"]);

            var imported = await service.ImportAsync(firstPath);
            Assert(!imported.ContainsUnsupportedImages, "An exported image was unexpectedly marked unsupported.");
            Assert(!imported.ContainsUnsupportedBlocks, "A supported document block was unexpectedly marked unsupported.");
            Assert(imported.Document.Blocks.OfType<Paragraph>().Any(paragraph => paragraph.Tag?.ToString() == "Title"),
                "The Title style was not retained.");
            Assert(imported.Document.Blocks.OfType<Paragraph>().Any(paragraph => paragraph.Tag?.ToString() == "Heading1"),
                "The Heading 1 style was not retained.");
            Assert(imported.Document.Blocks.OfType<Paragraph>().Any(paragraph => paragraph.Tag?.ToString() == "Heading2"),
                "The Heading 2 style was not retained.");
            Assert(imported.Document.Blocks.OfType<Paragraph>().Any(paragraph => paragraph.Tag?.ToString() == "Heading3"),
                "The Heading 3 style was not retained.");
            var highlighted = imported.Document.Blocks.OfType<Paragraph>()
                .SelectMany(paragraph => paragraph.Inlines)
                .OfType<Run>()
                .Single(run => run.Text == "Surlignage orange");
            Assert(highlighted.Background is SolidColorBrush { Color: var highlightColor } &&
                   highlightColor == Color.FromRgb(255, 165, 0),
                "The custom text highlight color was not retained.");
            Assert(imported.Document.Blocks.OfType<Paragraph>()
                .SelectMany(paragraph => paragraph.Inlines)
                .OfType<InlineUIContainer>()
                .Any(container => container.Child is Image { Source: BitmapSource }),
                "The embedded image was not imported.");
            var table = imported.Document.Blocks.OfType<Table>().SingleOrDefault();
            Assert(table is not null && table.RowGroups[0].Rows.Count == 2 && table.RowGroups[0].Rows[0].Cells.Count == 2,
                "The simple table was not retained.");

            await service.ExportAsync(imported.Document, secondPath);
            VerifyPackage(secondPath, expectedImages: 1, expectedHeadings: ["Title", "Heading1", "Heading2", "Heading3"]);
        }
        finally
        {
            if (File.Exists(firstPath)) File.Delete(firstPath);
            if (File.Exists(secondPath)) File.Delete(secondPath);
        }
    }

    private static BitmapSource CreateTestImage()
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawRectangle(Brushes.CornflowerBlue, null, new Rect(0, 0, 48, 32));
        var bitmap = new RenderTargetBitmap(48, 32, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Table CreateTestTable()
    {
        var table = new Table { CellSpacing = 0 };
        table.Columns.Add(new TableColumn());
        table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        for (var index = 0; index < 2; index++)
        {
            var row = new TableRow();
            for (var column = 0; column < 2; column++)
            {
                var cell = new TableCell
                {
                    Background = index == 0 ? new SolidColorBrush(Color.FromRgb(231, 237, 247)) : Brushes.White
                };
                cell.Blocks.Add(new Paragraph(new Run($"Cell {index}-{column}")));
                row.Cells.Add(cell);
            }
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        return table;
    }

    private static void VerifyPackage(string path, int expectedImages, IReadOnlyCollection<string> expectedHeadings)
    {
        using var zip = ZipFile.OpenRead(path);
        var documentEntry = zip.GetEntry("word/document.xml");
        var stylesEntry = zip.GetEntry("word/styles.xml");
        var relationshipsEntry = zip.GetEntry("word/_rels/document.xml.rels");
        Assert(documentEntry is not null && stylesEntry is not null && relationshipsEntry is not null,
            "The DOCX package is missing its document, styles, or relationship part.");

        using var documentStream = documentEntry!.Open();
        var document = XDocument.Load(documentStream);
        var styles = XDocument.Load(stylesEntry!.Open());
        var relationships = XDocument.Load(relationshipsEntry!.Open());
        var imageParts = zip.Entries.Count(entry => entry.FullName.StartsWith("word/media/", StringComparison.Ordinal));
        Assert(imageParts == expectedImages, $"Expected {expectedImages} embedded image(s), found {imageParts}.");
        Assert(document.Descendants(W + "tbl").Any(), "The DOCX has no table.");
        Assert(document.Descendants(W + "pgSz").Any(size =>
            (string?)size.Attribute(W + "w") == "11906" && (string?)size.Attribute(W + "h") == "16838"),
            "The document is not using the expected A4 page size.");
        foreach (var heading in expectedHeadings)
        {
            Assert(styles.Descendants(W + "style").Any(style => (string?)style.Attribute(W + "styleId") == heading),
                $"The Word style {heading} is missing.");
            Assert(document.Descendants(W + "pStyle").Any(style => (string?)style.Attribute(W + "val") == heading),
                $"The document does not use the {heading} style.");
        }
        Assert(relationships.Descendants().Count(relationship =>
            ((string?)relationship.Attribute("Type"))?.EndsWith("/image", StringComparison.Ordinal) == true) == expectedImages,
            "Image relationships do not match the embedded media.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }
}
