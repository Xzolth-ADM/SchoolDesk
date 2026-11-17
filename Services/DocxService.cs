using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

namespace SchoolDesk.Services;

/// <summary>Import/export DOCX local pour le texte, les images, les titres et les tableaux simples.</summary>
public sealed class DocxService
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private static readonly XNamespace Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const double DipsPerPoint = 96d / 72d;
    private const double EmusPerDip = 9525d;
    private const double MaximumImageWidth = 560;

    private static double PointsToDips(double points) => points * DipsPerPoint;

    public async Task<DocxImportResult> ImportAsync(string path)
    {
        var package = await Task.Run(() => ReadPackage(path));
        return new DocxImportResult(
            Import(package),
            package.ContainsUnsupportedImages,
            package.ContainsUnsupportedBlocks);
    }

    public sealed record DocxImportResult(
        FlowDocument Document,
        bool ContainsUnsupportedImages,
        bool ContainsUnsupportedBlocks);

    private sealed record DocxPackage(
        XDocument Document,
        IReadOnlyDictionary<string, byte[]> ImagesByRelationship,
        bool ContainsUnsupportedImages,
        bool ContainsUnsupportedBlocks);

    private static DocxPackage ReadPackage(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var documentEntry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("Le document DOCX ne contient pas word/document.xml.");
        if (documentEntry.Length > 32 * 1024 * 1024)
            throw new InvalidDataException("Le contenu DOCX est trop volumineux pour être ouvert de façon fiable.");
        using var documentStream = documentEntry.Open();
        var document = XDocument.Load(documentStream);

        var imageTargets = new Dictionary<string, string>(StringComparer.Ordinal);
        var relationshipsEntry = zip.GetEntry("word/_rels/document.xml.rels");
        if (relationshipsEntry is not null)
        {
            using var relationshipsStream = relationshipsEntry.Open();
            var relationships = XDocument.Load(relationshipsStream);
            foreach (var relationship in relationships.Root?.Elements(PackageRelationships + "Relationship") ?? [])
            {
                var id = (string?)relationship.Attribute("Id");
                var type = (string?)relationship.Attribute("Type");
                var target = (string?)relationship.Attribute("Target");
                var mode = (string?)relationship.Attribute("TargetMode");
                if (id is null || target is null || mode == "External" ||
                    type?.EndsWith("/image", StringComparison.Ordinal) != true)
                    continue;

                imageTargets[id] = NormalizePackageTarget(target);
            }
        }

        var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long totalImageBytes = 0;
        foreach (var (relationshipId, target) in imageTargets)
        {
            var entry = zip.GetEntry(target);
            if (entry is null) continue;
            if (entry.Length > 32 * 1024 * 1024 || totalImageBytes + entry.Length > 64 * 1024 * 1024)
                throw new InvalidDataException("Les images intégrées au DOCX dépassent la taille maximale prise en charge.");
            using var stream = entry.Open();
            using var imageStream = new MemoryStream((int)entry.Length);
            stream.CopyTo(imageStream);
            var bytes = imageStream.ToArray();
            totalImageBytes += bytes.Length;
            images[relationshipId] = bytes;
        }

        var body = document.Root?.Element(W + "body")
            ?? throw new InvalidDataException("Structure DOCX invalide.");
        var containsUnsupportedImages = document.Descendants(W + "pict").Any() ||
            document.Descendants(W + "drawing").Any(drawing =>
            {
                var blip = drawing.Descendants(A + "blip").FirstOrDefault();
                if (blip is null) return true;
                var relationshipId = (string?)blip.Attribute(R + "embed");
                if (relationshipId is null || !images.ContainsKey(relationshipId)) return true;
                return drawing.Descendants(A + "srcRect").Any(rectangle =>
                    rectangle.Attributes().Any(attribute => attribute.Value != "0"));
            });
        var containsUnsupportedBlocks = body.Elements()
                .Any(element => element.Name != W + "p" && element.Name != W + "tbl" && element.Name != W + "sectPr") ||
            document.Descendants(W + "object").Any() ||
            document.Descendants(W + "altChunk").Any() ||
            document.Descendants(W + "fldChar").Any() ||
            document.Descendants(W + "instrText").Any() ||
            document.Descendants(W + "footnoteReference").Any() ||
            document.Descendants(W + "endnoteReference").Any() ||
            document.Descendants(W + "commentRangeStart").Any() ||
            document.Descendants(W + "commentRangeEnd").Any() ||
            document.Descendants(Wps + "wsp").Any() ||
            document.Descendants(Wps + "txbx").Any();

        return new DocxPackage(document, images, containsUnsupportedImages, containsUnsupportedBlocks);
    }

    private static string NormalizePackageTarget(string target)
    {
        var uri = new Uri(new Uri("http://schooldesk.local/word/"), target);
        var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')).Replace('\\', '/');
        if (path.Length == 0 || path.Split('/').Any(part => part is "." or ".."))
            throw new InvalidDataException("Une ressource du DOCX possède un chemin invalide.");
        return path;
    }

    private static FlowDocument Import(DocxPackage package)
    {
        var body = package.Document.Root?.Element(W + "body")
            ?? throw new InvalidDataException("Structure DOCX invalide.");
        var document = new FlowDocument
        {
            PageWidth = 793.7,
            PageHeight = 1122.5,
            PagePadding = new Thickness(96),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = PointsToDips(12)
        };
        foreach (var node in body.Elements())
        {
            if (node.Name == W + "p")
                document.Blocks.Add(ReadParagraph(node, package));
            else if (node.Name == W + "tbl")
                document.Blocks.Add(ReadTable(node, package));
        }
        if (document.Blocks.Count == 0)
            document.Blocks.Add(new Paragraph());
        return document;
    }

    private static Paragraph ReadParagraph(XElement element, DocxPackage package)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
        var paragraphProperties = element.Element(W + "pPr");
        var style = ((string?)paragraphProperties?.Element(W + "pStyle")?.Attribute(W + "val") ?? "")
            .Replace(" ", "", StringComparison.Ordinal)
            .ToLowerInvariant();
        if (style is "title" or "titre")
        {
            paragraph.Tag = "Title";
            paragraph.FontSize = PointsToDips(30);
            paragraph.FontWeight = FontWeights.Bold;
            paragraph.Foreground = new SolidColorBrush(Color.FromRgb(49, 90, 155));
            paragraph.Margin = new Thickness(0, 0, 0, 16);
        }
        else if (style.Contains("heading1", StringComparison.Ordinal) || style is "titre1" or "titre1char")
        {
            paragraph.Tag = "Heading1";
            paragraph.FontSize = PointsToDips(24);
            paragraph.FontWeight = FontWeights.Bold;
            paragraph.Margin = new Thickness(0, 14, 0, 8);
        }
        else if (style.Contains("heading2", StringComparison.Ordinal) || style is "titre2" or "titre2char")
        {
            paragraph.Tag = "Heading2";
            paragraph.FontSize = PointsToDips(20);
            paragraph.FontWeight = FontWeights.Bold;
            paragraph.Margin = new Thickness(0, 12, 0, 7);
        }
        else if (style.Contains("heading3", StringComparison.Ordinal) || style is "titre3" or "titre3char")
        {
            paragraph.Tag = "Heading3";
            paragraph.FontSize = PointsToDips(16);
            paragraph.FontWeight = FontWeights.SemiBold;
            paragraph.Margin = new Thickness(0, 10, 0, 6);
        }
        else
        {
            paragraph.Tag = "Normal";
        }

        var alignment = (string?)paragraphProperties?.Element(W + "jc")?.Attribute(W + "val");
        paragraph.TextAlignment = alignment switch
        {
            "center" => TextAlignment.Center,
            "right" or "end" => TextAlignment.Right,
            "both" or "distribute" => TextAlignment.Justify,
            _ => TextAlignment.Left
        };

        var indentation = (string?)paragraphProperties?.Element(W + "ind")?.Attribute(W + "left");
        if (int.TryParse(indentation, out var twips))
            paragraph.Margin = new Thickness(Math.Max(0, twips / 15d), paragraph.Margin.Top, 0, paragraph.Margin.Bottom);

        var spacing = paragraphProperties?.Element(W + "spacing");
        var hasBefore = double.TryParse((string?)spacing?.Attribute(W + "before"), out var before);
        var hasAfter = double.TryParse((string?)spacing?.Attribute(W + "after"), out var after);
        if (hasBefore || hasAfter)
        {
            var margin = paragraph.Margin;
            paragraph.Margin = new Thickness(
                margin.Left,
                hasBefore ? Math.Max(0, before / 15d) : margin.Top,
                margin.Right,
                hasAfter ? Math.Max(0, after / 15d) : margin.Bottom);
        }

        foreach (var child in element.Elements())
        {
            if (child.Name == W + "r")
                AddRun(paragraph, child, package);
            else if (child.Name == W + "hyperlink")
                foreach (var run in child.Elements(W + "r"))
                    AddRun(paragraph, run, package, true);
        }
        return paragraph;
    }

    private static void AddRun(Paragraph paragraph, XElement run, DocxPackage package, bool hyperlink = false)
    {
        var runProperties = run.Element(W + "rPr");
        var pendingText = new StringBuilder();

        void FlushText()
        {
            if (pendingText.Length == 0) return;
            paragraph.Inlines.Add(CreateFormattedRun(pendingText.ToString(), runProperties, hyperlink));
            pendingText.Clear();
        }

        foreach (var child in run.Elements())
        {
            if (child.Name == W + "t" || child.Name == W + "delText")
                pendingText.Append(child.Value);
            else if (child.Name == W + "tab")
                pendingText.Append('\t');
            else if (child.Name == W + "br" || child.Name == W + "cr")
                pendingText.Append('\n');
            else if (child.Name == W + "drawing")
            {
                FlushText();
                foreach (var drawing in child.Descendants(A + "blip"))
                    AddImportedImage(paragraph, drawing, child, package);
            }
        }
        FlushText();
    }

    private static Inline CreateFormattedRun(string text, XElement? properties, bool hyperlink)
    {
        var inline = new Run(text);
        if (IsOn(properties?.Element(W + "b")))
            inline.FontWeight = FontWeights.Bold;
        if (IsOn(properties?.Element(W + "i")))
            inline.FontStyle = FontStyles.Italic;
        if (IsOn(properties?.Element(W + "u")))
            inline.TextDecorations = TextDecorations.Underline;
        if (IsOn(properties?.Element(W + "strike")) || IsOn(properties?.Element(W + "dstrike")))
            inline.TextDecorations = TextDecorations.Strikethrough;

        var color = (string?)properties?.Element(W + "color")?.Attribute(W + "val");
        if (TryReadColor(color, out var foreground))
            inline.Foreground = foreground;
        var highlight = (string?)properties?.Element(W + "highlight")?.Attribute(W + "val");
        if (TryReadHighlight(highlight, out var background))
            inline.Background = background;
        else
        {
            var shading = (string?)properties?.Element(W + "shd")?.Attribute(W + "fill");
            if (TryReadColor(shading, out background))
                inline.Background = background;
        }
        var size = (string?)properties?.Element(W + "sz")?.Attribute(W + "val");
        if (double.TryParse(size, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var halfPoints))
            inline.FontSize = PointsToDips(halfPoints / 2);
        var fonts = properties?.Element(W + "rFonts");
        var font = (string?)fonts?.Attribute(W + "ascii") ?? (string?)fonts?.Attribute(W + "hAnsi");
        if (!string.IsNullOrWhiteSpace(font))
            inline.FontFamily = new FontFamily(font);

        if (hyperlink)
        {
            inline.Foreground = new SolidColorBrush(Color.FromRgb(49, 90, 155));
            inline.TextDecorations = TextDecorations.Underline;
        }
        return inline;
    }

    private static bool IsOn(XElement? element)
    {
        var value = (string?)element?.Attribute(W + "val");
        return element is not null && value is not "0" and not "false" and not "off";
    }

    private static void AddImportedImage(Paragraph paragraph, XElement blip, XElement drawing, DocxPackage package)
    {
        var relationshipId = (string?)blip.Attribute(R + "embed");
        if (relationshipId is null || !package.ImagesByRelationship.TryGetValue(relationshipId, out var bytes))
            return;

        using var stream = new MemoryStream(bytes, writable: false);
        var source = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        source.Freeze();
        var extent = drawing.Descendants(Wp + "extent").FirstOrDefault();
        var width = double.TryParse((string?)extent?.Attribute("cx"), out var cx) ? cx / EmusPerDip : source.PixelWidth;
        var height = double.TryParse((string?)extent?.Attribute("cy"), out var cy) ? cy / EmusPerDip : source.PixelHeight;
        var scale = Math.Min(1, Math.Min(MaximumImageWidth / Math.Max(1, width), 720 / Math.Max(1, height)));
        var properties = drawing.Descendants(Wp + "docPr").FirstOrDefault();
        var description = (string?)properties?.Attribute("descr") ??
            (string?)properties?.Attribute("name") ?? "Image intégrée";
        var image = new Image
        {
            Source = source,
            Width = width * scale,
            Height = height * scale,
            Stretch = Stretch.Uniform,
            ToolTip = description,
            SnapsToDevicePixels = true
        };
        AutomationProperties.SetName(image, description);
        paragraph.Inlines.Add(new InlineUIContainer(image));
    }

    private static Table ReadTable(XElement element, DocxPackage package)
    {
        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 8, 0, 8)
        };
        var rows = element.Elements(W + "tr").ToList();
        if (rows.Count == 0) return table;
        var columnCount = rows.Max(row => row.Elements(W + "tc").Count());
        for (var index = 0; index < columnCount; index++)
            table.Columns.Add(new TableColumn());

        var group = new TableRowGroup();
        foreach (var row in rows)
        {
            var tableRow = new TableRow();
            foreach (var cellElement in row.Elements(W + "tc"))
            {
                var cellProperties = cellElement.Element(W + "tcPr");
                var cell = new TableCell
                {
                    BorderBrush = ReadTableBorderColor(cellProperties) ?? Brushes.Gray,
                    BorderThickness = new Thickness(.5),
                    Padding = ReadCellPadding(cellProperties),
                    Background = ReadCellBackground(cellProperties) ?? Brushes.White
                };
                foreach (var paragraph in cellElement.Elements(W + "p"))
                    cell.Blocks.Add(ReadParagraph(paragraph, package));
                if (cell.Blocks.Count == 0)
                    cell.Blocks.Add(new Paragraph());
                tableRow.Cells.Add(cell);
            }
            group.Rows.Add(tableRow);
        }
        table.RowGroups.Add(group);
        return table;
    }

    private static Brush? ReadTableBorderColor(XElement? cellProperties)
    {
        var color = (string?)cellProperties?.Element(W + "tcBorders")?.Element(W + "top")?.Attribute(W + "color");
        return TryReadColor(color, out var brush) ? brush : null;
    }

    private static Brush? ReadCellBackground(XElement? cellProperties)
    {
        var color = (string?)cellProperties?.Element(W + "shd")?.Attribute(W + "fill");
        return TryReadColor(color, out var brush) ? brush : null;
    }

    private static Thickness ReadCellPadding(XElement? cellProperties)
    {
        var margins = cellProperties?.Element(W + "tcMar");
        double ReadSide(string side, double fallback) =>
            double.TryParse((string?)margins?.Element(W + side)?.Attribute(W + "w"), out var twips)
                ? Math.Max(0, twips / 15d)
                : fallback;
        return new Thickness(ReadSide("left", 6), ReadSide("top", 5), ReadSide("right", 6), ReadSide("bottom", 5));
    }

    private static bool TryReadColor(string? value, out SolidColorBrush brush)
    {
        brush = Brushes.Transparent as SolidColorBrush ?? new SolidColorBrush(Colors.Transparent);
        if (value is not { Length: 6 } || !value.All(Uri.IsHexDigit) || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return false;
        var color = Color.FromRgb(
            Convert.ToByte(value[..2], 16),
            Convert.ToByte(value[2..4], 16),
            Convert.ToByte(value[4..6], 16));
        brush = new SolidColorBrush(color);
        brush.Freeze();
        return true;
    }

    private static bool TryReadHighlight(string? value, out SolidColorBrush brush)
    {
        var color = value?.ToLowerInvariant() switch
        {
            "yellow" => Colors.Yellow,
            "green" => Colors.LightGreen,
            "cyan" => Colors.LightBlue,
            "magenta" => Colors.LightPink,
            "red" => Colors.LightCoral,
            "blue" => Colors.LightBlue,
            "darkblue" => Colors.Navy,
            "darkcyan" => Colors.DarkCyan,
            "darkgreen" => Colors.DarkGreen,
            "darkmagenta" => Colors.DarkMagenta,
            "darkred" => Colors.DarkRed,
            "darkyellow" => Colors.Olive,
            "gray" or "lightgray" => Colors.LightGray,
            "black" => Colors.Black,
            _ => Colors.Transparent
        };
        if (color == Colors.Transparent)
        {
            brush = new SolidColorBrush(Colors.Transparent);
            return false;
        }
        brush = new SolidColorBrush(color);
        brush.Freeze();
        return true;
    }

    public async Task ExportAsync(FlowDocument document, string path)
    {
        var export = new ExportContext();
        var contentTypes = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ContentTypes + "Types",
                new XElement(ContentTypes + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ContentTypes + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(ContentTypes + "Default",
                    new XAttribute("Extension", "png"),
                    new XAttribute("ContentType", "image/png")),
                new XElement(ContentTypes + "Override",
                    new XAttribute("PartName", "/word/document.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
                new XElement(ContentTypes + "Override",
                    new XAttribute("PartName", "/word/styles.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"))));

        var documentXml = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "document",
                new XAttribute(XNamespace.Xmlns + "w", W),
                new XAttribute(XNamespace.Xmlns + "r", R),
                new XAttribute(XNamespace.Xmlns + "wp", Wp),
                new XAttribute(XNamespace.Xmlns + "a", A),
                new XAttribute(XNamespace.Xmlns + "pic", Pic),
                new XElement(W + "body")));
        var body = documentXml.Root!.Element(W + "body")!;
        var blockCount = 0;
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph paragraph)
                body.Add(await WriteParagraphAsync(paragraph, export));
            else if (block is Table table)
                body.Add(await WriteTableAsync(table, export));
            else if (block is List list)
            {
                var number = 1;
                foreach (var item in list.ListItems)
                    foreach (var itemBlock in item.Blocks.OfType<Paragraph>())
                    {
                        var prefix = list.MarkerStyle == TextMarkerStyle.Decimal ? $"{number++}. " : "• ";
                        body.Add(await WriteParagraphAsync(itemBlock, export, prefix));
                    }
            }

            if (++blockCount % 8 == 0)
                await Dispatcher.Yield(DispatcherPriority.Background);
        }

        body.Add(new XElement(W + "sectPr",
            new XElement(W + "pgSz", new XAttribute(W + "w", "11906"), new XAttribute(W + "h", "16838")),
            new XElement(W + "pgMar",
                new XAttribute(W + "top", "1440"),
                new XAttribute(W + "right", "1440"),
                new XAttribute(W + "bottom", "1440"),
                new XAttribute(W + "left", "1440"))));

        var packageRelationships = new XDocument(
            new XElement(PackageRelationships + "Relationships",
                new XElement(PackageRelationships + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "word/document.xml"))));
        var documentRelationships = new XDocument(
            new XElement(PackageRelationships + "Relationships",
                new XElement(PackageRelationships + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                    new XAttribute("Target", "styles.xml"))));

        await Dispatcher.Yield(DispatcherPriority.Background);
        await Task.Run(() => WritePackage(
            path,
            contentTypes,
            packageRelationships,
            documentRelationships,
            documentXml,
            CreateStylesXml(),
            export.Images));
    }

    private static XDocument CreateStylesXml() =>
        new(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "styles",
                new XAttribute(XNamespace.Xmlns + "w", W),
                new XElement(W + "docDefaults",
                    new XElement(W + "rPrDefault",
                        new XElement(W + "rPr",
                            new XElement(W + "rFonts",
                                new XAttribute(W + "ascii", "Segoe UI"),
                                new XAttribute(W + "hAnsi", "Segoe UI")),
                            new XElement(W + "sz", new XAttribute(W + "val", "24")))),
                    new XElement(W + "pPrDefault",
                        new XElement(W + "pPr",
                            new XElement(W + "spacing", new XAttribute(W + "after", "120"))))),
                new XElement(W + "style",
                    new XAttribute(W + "type", "paragraph"), new XAttribute(W + "default", "1"), new XAttribute(W + "styleId", "Normal"),
                    new XElement(W + "name", new XAttribute(W + "val", "Normal"))),
                CreateParagraphStyle("Title", "Title", 60, null, bold: true, color: "315A9B"),
                CreateParagraphStyle("Heading1", "heading 1", 48, 0, bold: true),
                CreateParagraphStyle("Heading2", "heading 2", 40, 1, bold: true),
                CreateParagraphStyle("Heading3", "heading 3", 32, 2, bold: true)));

    private static XElement CreateParagraphStyle(string id, string name, int halfPoints, int? outlineLevel, bool bold, string? color = null)
    {
        var properties = new XElement(W + "rPr");
        if (bold)
            properties.Add(new XElement(W + "b"));
        properties.Add(new XElement(W + "sz", new XAttribute(W + "val", halfPoints)));
        if (color is not null)
            properties.Add(new XElement(W + "color", new XAttribute(W + "val", color)));
        var paragraphProperties = new XElement(W + "pPr");
        if (outlineLevel is not null)
            paragraphProperties.Add(new XElement(W + "outlineLvl", new XAttribute(W + "val", outlineLevel.Value)));
        return new XElement(W + "style",
            new XAttribute(W + "type", "paragraph"),
            new XAttribute(W + "styleId", id),
            new XElement(W + "name", new XAttribute(W + "val", name)),
            new XElement(W + "basedOn", new XAttribute(W + "val", "Normal")),
            new XElement(W + "next", new XAttribute(W + "val", "Normal")),
            new XElement(W + "qFormat"),
            paragraphProperties,
            properties);
    }

    private static async Task<XElement> WriteParagraphAsync(Paragraph paragraph, ExportContext export, string prefix = "")
    {
        var properties = new XElement(W + "pPr");
        var style = ResolveParagraphStyle(paragraph);
        if (style is not null)
            properties.Add(new XElement(W + "pStyle", new XAttribute(W + "val", style)));
        var alignment = paragraph.TextAlignment switch
        {
            TextAlignment.Center => "center",
            TextAlignment.Right => "right",
            TextAlignment.Justify => "both",
            _ => "left"
        };
        if (alignment != "left")
            properties.Add(new XElement(W + "jc", new XAttribute(W + "val", alignment)));
        if (paragraph.Margin.Left > 0)
            properties.Add(new XElement(W + "ind", new XAttribute(W + "left", Math.Round(paragraph.Margin.Left * 15))));
        if (paragraph.Margin.Top > 0 || paragraph.Margin.Bottom > 0)
            properties.Add(new XElement(W + "spacing",
                new XAttribute(W + "before", Math.Round(paragraph.Margin.Top * 15)),
                new XAttribute(W + "after", Math.Round(paragraph.Margin.Bottom * 15))));

        var result = new XElement(W + "p", properties);
        if (prefix.Length > 0)
            result.Add(MakeRun(prefix, paragraph));
        var inlineCount = 0;
        foreach (var inline in paragraph.Inlines)
        {
            WriteInline(result, inline, export);
            if (++inlineCount % 64 == 0)
                await Dispatcher.Yield(DispatcherPriority.Background);
        }
        return result;
    }

    private static string? ResolveParagraphStyle(Paragraph paragraph)
    {
        var explicitStyle = paragraph.Tag?.ToString();
        if (explicitStyle is "Title" or "Heading1" or "Heading2" or "Heading3")
            return explicitStyle;
        if (explicitStyle == "Normal")
            return null;
        if (paragraph.FontSize >= PointsToDips(28))
            return "Title";
        if (paragraph.FontSize >= PointsToDips(22))
            return "Heading1";
        if (paragraph.FontSize >= PointsToDips(18))
            return "Heading2";
        if (paragraph.FontSize >= PointsToDips(15))
            return "Heading3";
        return null;
    }

    private static void WriteInline(XElement paragraph, Inline inline, ExportContext export)
    {
        if (inline is LineBreak)
        {
            paragraph.Add(new XElement(W + "r", new XElement(W + "br")));
            return;
        }
        if (inline is InlineUIContainer { Child: Image { Source: BitmapSource source } image })
        {
            paragraph.Add(new XElement(W + "r", export.CreateDrawing(source, image.Width, image.Height, image.ToolTip?.ToString())));
            return;
        }
        if (inline is Span span)
        {
            foreach (var child in span.Inlines)
                WriteInline(paragraph, child, export);
            return;
        }
        if (inline is Run run)
            paragraph.Add(MakeRun(run.Text, run));
    }

    private static XElement MakeRun(string? text, TextElement element)
    {
        var properties = new XElement(W + "rPr");
        if (element.FontWeight == FontWeights.Bold || element.FontWeight == FontWeights.SemiBold)
            properties.Add(new XElement(W + "b"));
        if (element.FontStyle == FontStyles.Italic)
            properties.Add(new XElement(W + "i"));
        if (element is Inline inline &&
            inline.TextDecorations?.Any(decoration => decoration.Location == TextDecorationLocation.Underline) == true)
            properties.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
        if (element is Inline strikeInline &&
            strikeInline.TextDecorations?.Any(decoration => decoration.Location == TextDecorationLocation.Strikethrough) == true)
            properties.Add(new XElement(W + "strike"));
        if (element.Foreground is SolidColorBrush foreground)
            properties.Add(new XElement(W + "color",
                new XAttribute(W + "val", $"{foreground.Color.R:X2}{foreground.Color.G:X2}{foreground.Color.B:X2}")));
        if (element.Background is SolidColorBrush background && background.Color != Colors.Transparent)
        {
            var highlight = MapHighlight(background.Color);
            if (highlight is not null)
                properties.Add(new XElement(W + "highlight", new XAttribute(W + "val", highlight)));
            else
                properties.Add(new XElement(W + "shd",
                    new XAttribute(W + "val", "clear"),
                    new XAttribute(W + "color", "auto"),
                    new XAttribute(W + "fill", $"{background.Color.R:X2}{background.Color.G:X2}{background.Color.B:X2}")));
        }
        if (element.FontSize > 0)
            properties.Add(new XElement(W + "sz", new XAttribute(W + "val",
                Math.Round(element.FontSize / DipsPerPoint * 2, MidpointRounding.AwayFromZero))));
        if (element.FontFamily is not null)
            properties.Add(new XElement(W + "rFonts",
                new XAttribute(W + "ascii", element.FontFamily.Source),
                new XAttribute(W + "hAnsi", element.FontFamily.Source)));

        var result = new XElement(W + "r");
        if (properties.HasElements)
            result.Add(properties);
        result.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text ?? ""));
        return result;
    }

    private static string? MapHighlight(Color color) => color switch
    {
        var value when value == Colors.Yellow => "yellow",
        var value when value == Colors.LightGreen => "green",
        var value when value == Colors.LightBlue => "cyan",
        var value when value == Colors.LightPink => "magenta",
        var value when value == Colors.LightCoral => "red",
        _ => null
    };

    private static async Task<XElement> WriteTableAsync(Table table, ExportContext export)
    {
        var borders = new XElement(W + "tblBorders",
            CreateBorder("top"), CreateBorder("left"), CreateBorder("bottom"), CreateBorder("right"),
            CreateBorder("insideH"), CreateBorder("insideV"));
        var result = new XElement(W + "tbl",
            new XElement(W + "tblPr",
                new XElement(W + "tblW", new XAttribute(W + "w", "0"), new XAttribute(W + "type", "auto")),
                borders));
        var rowCount = 0;
        foreach (var group in table.RowGroups)
            foreach (var row in group.Rows)
            {
                var tableRow = new XElement(W + "tr");
                foreach (var cell in row.Cells)
                {
                    var cellProperties = new XElement(W + "tcPr",
                        new XElement(W + "tcMar",
                            CreateCellMargin("top", cell.Padding.Top),
                            CreateCellMargin("left", cell.Padding.Left),
                            CreateCellMargin("bottom", cell.Padding.Bottom),
                            CreateCellMargin("right", cell.Padding.Right)));
                    if (cell.Background is SolidColorBrush background && background.Color != Colors.White)
                        cellProperties.Add(new XElement(W + "shd",
                            new XAttribute(W + "val", "clear"),
                            new XAttribute(W + "color", "auto"),
                            new XAttribute(W + "fill", $"{background.Color.R:X2}{background.Color.G:X2}{background.Color.B:X2}")));

                    var tableCell = new XElement(W + "tc", cellProperties);
                    foreach (var block in cell.Blocks)
                    {
                        if (block is Paragraph paragraph)
                            tableCell.Add(await WriteParagraphAsync(paragraph, export));
                        else if (block is Table nestedTable)
                            tableCell.Add(await WriteTableAsync(nestedTable, export));
                    }
                    if (!tableCell.Elements(W + "p").Any() && !tableCell.Elements(W + "tbl").Any())
                        tableCell.Add(new XElement(W + "p"));
                    tableRow.Add(tableCell);
                }
                result.Add(tableRow);
                if (++rowCount % 8 == 0)
                    await Dispatcher.Yield(DispatcherPriority.Background);
            }
        return result;
    }

    private static XElement CreateBorder(string side) =>
        new(W + side,
            new XAttribute(W + "val", "single"),
            new XAttribute(W + "sz", "4"),
            new XAttribute(W + "space", "0"),
            new XAttribute(W + "color", "D8DDE5"));

    private static XElement CreateCellMargin(string side, double dips) =>
        new(W + side,
            new XAttribute(W + "w", Math.Max(0, Math.Round(dips * 15))),
            new XAttribute(W + "type", "dxa"));

    private sealed class ExportContext
    {
        private int _relationshipNumber = 1;
        private int _imageNumber;
        public List<(string Path, byte[] Bytes)> Images { get; } = [];

        public XElement CreateDrawing(BitmapSource source, double width, double height, string? description)
        {
            if (!source.IsFrozen && source.Dispatcher != Dispatcher.CurrentDispatcher)
                throw new InvalidOperationException("L'image doit être figée avant son export.");

            var encoded = EncodePng(source);
            var imageNumber = ++_imageNumber;
            var relationshipId = $"rId{++_relationshipNumber}";
            var imagePath = $"media/image{imageNumber}.png";
            Images.Add((imagePath, encoded));
            var cx = ToEmu(double.IsFinite(width) && width > 0 ? width : source.PixelWidth);
            var cy = ToEmu(double.IsFinite(height) && height > 0 ? height : source.PixelHeight);
            var alt = string.IsNullOrWhiteSpace(description) ? $"Image {imageNumber}" : description;

            return new XElement(W + "drawing",
                new XElement(Wp + "inline",
                    new XAttribute("distT", "0"),
                    new XAttribute("distB", "0"),
                    new XAttribute("distL", "0"),
                    new XAttribute("distR", "0"),
                    new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                    new XElement(Wp + "docPr",
                        new XAttribute("id", imageNumber),
                        new XAttribute("name", alt),
                        new XAttribute("descr", alt)),
                    new XElement(Wp + "cNvGraphicFramePr",
                        new XElement(A + "graphicFrameLocks", new XAttribute("noChangeAspect", "1"))),
                    new XElement(A + "graphic",
                        new XElement(A + "graphicData",
                            new XAttribute("uri", Pic.NamespaceName),
                            new XElement(Pic + "pic",
                                new XElement(Pic + "nvPicPr",
                                    new XElement(Pic + "cNvPr", new XAttribute("id", imageNumber), new XAttribute("name", alt)),
                                    new XElement(Pic + "cNvPicPr")),
                                new XElement(Pic + "blipFill",
                                    new XElement(A + "blip", new XAttribute(R + "embed", relationshipId)),
                                    new XElement(A + "stretch", new XElement(A + "fillRect"))),
                                new XElement(Pic + "spPr",
                                    new XElement(A + "xfrm",
                                        new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                        new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                                    new XElement(A + "prstGeom",
                                        new XAttribute("prst", "rect"),
                                        new XElement(A + "avLst"))))))));
        }

        private static byte[] EncodePng(BitmapSource source)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        private static long ToEmu(double dips) =>
            (long)Math.Round(Math.Max(1, dips) * EmusPerDip, MidpointRounding.AwayFromZero);
    }

    private static void WritePackage(
        string path,
        XDocument contentTypes,
        XDocument packageRelationships,
        XDocument documentRelationships,
        XDocument document,
        XDocument styles,
        IReadOnlyList<(string Path, byte[] Bytes)> images)
    {
        var documentRelationshipsRoot = documentRelationships.Root!;
        foreach (var relationship in images.Select((image, index) =>
                     new XElement(PackageRelationships + "Relationship",
                         new XAttribute("Id", $"rId{index + 2}"),
                         new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                         new XAttribute("Target", image.Path))))
            documentRelationshipsRoot.Add(relationship);

        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                WriteXml(zip, "[Content_Types].xml", contentTypes);
                WriteXml(zip, "_rels/.rels", packageRelationships);
                WriteXml(zip, "word/_rels/document.xml.rels", documentRelationships);
                WriteXml(zip, "word/document.xml", document);
                WriteXml(zip, "word/styles.xml", styles);
                foreach (var image in images)
                    WriteBytes(zip, $"word/{image.Path}", image.Bytes);
            }

            if (File.Exists(path) && !File.Exists(path + ".bak"))
                File.Copy(path, path + ".bak");
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static void WriteXml(ZipArchive zip, string name, XDocument xml)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        xml.Save(stream);
    }

    private static void WriteBytes(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes);
    }
}
