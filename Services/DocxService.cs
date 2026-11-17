using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;

namespace SchoolDesk.Services;

/// <summary>Import/export DOCX local pour les éléments de mise en forme courants.</summary>
public sealed class DocxService
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const double DipsPerPoint = 96d / 72d;

    private static double PointsToDips(double points) => points * DipsPerPoint;

    public async Task<DocxImportResult> ImportAsync(string path)
    {
        var xml = await Task.Run(() => ReadDocumentXml(path));
        var containsImages = xml.Descendants().Any(element => element.Name == W + "drawing" || element.Name == W + "pict");
        var containsUnsupportedBlocks = xml.Root?.Element(W + "body")?.Elements()
            .Any(element => element.Name != W + "p" && element.Name != W + "tbl" && element.Name != W + "sectPr") == true;
        return new DocxImportResult(Import(xml), containsImages, containsUnsupportedBlocks);
    }

    public sealed record DocxImportResult(FlowDocument Document, bool ContainsImages, bool ContainsUnsupportedBlocks);

    private static XDocument ReadDocumentXml(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("Le document DOCX ne contient pas word/document.xml.");
        if (entry.Length > 32 * 1024 * 1024)
            throw new InvalidDataException("Le contenu DOCX est trop volumineux pour être ouvert de façon fiable.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static FlowDocument Import(XDocument xml)
    {
        var body = xml.Root?.Element(W + "body") ?? throw new InvalidDataException("Structure DOCX invalide.");
        var document = new FlowDocument { PagePadding = new Thickness(96), FontFamily = new FontFamily("Segoe UI"), FontSize = PointsToDips(12) };
        foreach (var node in body.Elements())
        {
            if (node.Name == W + "p") document.Blocks.Add(ReadParagraph(node));
            else if (node.Name == W + "tbl") document.Blocks.Add(ReadTable(node));
        }
        if (document.Blocks.Count == 0) document.Blocks.Add(new Paragraph());
        return document;
    }

    private static Paragraph ReadParagraph(XElement element)
    {
        var p = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
        var pPr = element.Element(W + "pPr");
        var style = (string?)pPr?.Element(W + "pStyle")?.Attribute(W + "val") ?? "";
        if (style.Contains("Heading1", StringComparison.OrdinalIgnoreCase)) { p.FontSize = PointsToDips(24); p.FontWeight = FontWeights.Bold; p.Margin = new Thickness(0, 14, 0, 8); }
        else if (style.Contains("Heading2", StringComparison.OrdinalIgnoreCase)) { p.FontSize = PointsToDips(19); p.FontWeight = FontWeights.Bold; p.Margin = new Thickness(0, 12, 0, 7); }
        else if (style.Contains("Heading3", StringComparison.OrdinalIgnoreCase)) { p.FontSize = PointsToDips(16); p.FontWeight = FontWeights.Bold; }
        var jc = (string?)pPr?.Element(W + "jc")?.Attribute(W + "val");
        p.TextAlignment = jc switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, "both" => TextAlignment.Justify, _ => TextAlignment.Left };
        var indent = (string?)pPr?.Element(W + "ind")?.Attribute(W + "left");
        if (int.TryParse(indent, out var twips)) p.Margin = new Thickness(Math.Max(0, twips / 15d), p.Margin.Top, 0, p.Margin.Bottom);
        foreach (var child in element.Elements())
        {
            if (child.Name == W + "r") AddRun(p, child);
            else if (child.Name == W + "hyperlink") foreach (var run in child.Elements(W + "r")) AddRun(p, run, true);
        }
        return p;
    }

    private static void AddRun(Paragraph p, XElement run, bool hyperlink = false)
    {
        var text = string.Concat(run.Descendants(W + "t").Select(x => x.Value));
        if (run.Descendants(W + "tab").Any()) text += "\t";
        if (run.Descendants(W + "br").Any()) text += "\n";
        var rPr = run.Element(W + "rPr");
        Inline inline = new Run(text);
        if (rPr?.Element(W + "b") is not null) inline = new Bold(inline);
        if (rPr?.Element(W + "i") is not null) inline = new Italic(inline);
        if (rPr?.Element(W + "u") is not null) inline = new Underline(inline);
        if (rPr?.Element(W + "strike") is not null) inline = new Span(inline) { TextDecorations = TextDecorations.Strikethrough };
        var color = (string?)rPr?.Element(W + "color")?.Attribute(W + "val");
        if (color is { Length: 6 } && color.All(Uri.IsHexDigit))
        {
            inline.Foreground = new SolidColorBrush(Color.FromRgb(
                Convert.ToByte(color[..2], 16),
                Convert.ToByte(color[2..4], 16),
                Convert.ToByte(color[4..6], 16)));
        }
        var highlight = (string?)rPr?.Element(W + "highlight")?.Attribute(W + "val");
        if (!string.IsNullOrWhiteSpace(highlight))
        {
            var brush = highlight switch { "yellow" => Brushes.Yellow, "green" => Brushes.LightGreen, "cyan" => Brushes.Cyan, "magenta" => Brushes.Magenta, "red" => Brushes.LightCoral, _ => Brushes.LightYellow };
            inline.Background = brush;
        }
        var size = (string?)rPr?.Element(W + "sz")?.Attribute(W + "val");
        if (double.TryParse(size, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var halfPoints))
            inline.FontSize = PointsToDips(halfPoints / 2);
        var font = (string?)rPr?.Element(W + "rFonts")?.Attribute(W + "ascii");
        if (!string.IsNullOrWhiteSpace(font)) inline.FontFamily = new FontFamily(font);
        if (hyperlink) { inline.Foreground = Brushes.DeepSkyBlue; inline.TextDecorations = TextDecorations.Underline; }
        p.Inlines.Add(inline);
    }

    private static Table ReadTable(XElement element)
    {
        var table = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
        var rows = element.Elements(W + "tr").ToList();
        if (rows.Count == 0) return table;
        var columns = rows.Max(r => r.Elements(W + "tc").Count());
        for (var i = 0; i < columns; i++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        foreach (var row in rows)
        {
            var tr = new TableRow();
            foreach (var cell in row.Elements(W + "tc"))
            {
                var tc = new TableCell { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(.5), Padding = new Thickness(5) };
                foreach (var p in cell.Elements(W + "p")) tc.Blocks.Add(ReadParagraph(p));
                if (tc.Blocks.Count == 0) tc.Blocks.Add(new Paragraph());
                tr.Cells.Add(tc);
            }
            group.Rows.Add(tr);
        }
        table.RowGroups.Add(group);
        return table;
    }

    public async Task ExportAsync(FlowDocument document, string path)
    {
        var root = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ContentTypes + "Types",
                new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(ContentTypes + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"))));
        var documentXml = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W), new XElement(W + "body")));
        var body = documentXml.Root!.Element(W + "body")!;
        var blockCount = 0;
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph paragraph) body.Add(await WriteParagraphAsync(paragraph));
            else if (block is Table table) body.Add(await WriteTableAsync(table));
            else if (block is List list)
            {
                var number = 1;
                foreach (var item in list.ListItems)
                    foreach (var itemBlock in item.Blocks)
                        if (itemBlock is Paragraph listParagraph)
                        {
                            var prefix = list.MarkerStyle == TextMarkerStyle.Decimal ? $"{number++}. " : "• ";
                            body.Add(await WriteParagraphAsync(listParagraph, prefix));
                        }
            }

            if (++blockCount % 8 == 0)
                await Dispatcher.Yield(DispatcherPriority.Background);
        }
        body.Add(new XElement(W + "sectPr", new XElement(W + "pgSz", new XAttribute(W + "w", "12240"), new XAttribute(W + "h", "15840")), new XElement(W + "pgMar", new XAttribute(W + "top", "1440"), new XAttribute(W + "right", "1440"), new XAttribute(W + "bottom", "1440"), new XAttribute(W + "left", "1440"))));
        var relationships = new XDocument(new XElement(PackageRelationships + "Relationships",
            new XElement(PackageRelationships + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                new XAttribute("Target", "word/document.xml"))));
        await Task.Run(() => WritePackage(path, root, relationships, documentXml));
    }

    private static async Task<XElement> WriteParagraphAsync(Paragraph paragraph, string prefix = "")
    {
        var props = new XElement(W + "pPr");
        if (paragraph.FontSize >= PointsToDips(22)) props.Add(new XElement(W + "pStyle", new XAttribute(W + "val", "Heading1")));
        else if (paragraph.FontSize >= PointsToDips(18)) props.Add(new XElement(W + "pStyle", new XAttribute(W + "val", "Heading2")));
        else if (paragraph.FontSize >= PointsToDips(15)) props.Add(new XElement(W + "pStyle", new XAttribute(W + "val", "Heading3")));
        var align = paragraph.TextAlignment switch { TextAlignment.Center => "center", TextAlignment.Right => "right", TextAlignment.Justify => "both", _ => "left" };
        if (align != "left") props.Add(new XElement(W + "jc", new XAttribute(W + "val", align)));
        var result = new XElement(W + "p", props);
        if (prefix.Length > 0) result.Add(new XElement(W + "r", new XElement(W + "t", prefix)));

        var inlineCount = 0;
        foreach (var inline in paragraph.Inlines)
        {
            WriteInline(result, inline);
            if (++inlineCount % 64 == 0)
                await Dispatcher.Yield(DispatcherPriority.Background);
        }
        return result;
    }

    private static async Task<XElement> WriteTableAsync(Table table)
    {
        var tbl = new XElement(W + "tbl");
        var rowCount = 0;
        foreach (var group in table.RowGroups)
            foreach (var row in group.Rows)
            {
                var tr = new XElement(W + "tr");
                foreach (var cell in row.Cells)
                {
                    var tc = new XElement(W + "tc");
                    foreach (var block in cell.Blocks)
                        if (block is Paragraph paragraph)
                            tc.Add(await WriteParagraphAsync(paragraph));
                    if (!tc.Elements(W + "p").Any()) tc.Add(new XElement(W + "p"));
                    tr.Add(tc);
                }
                tbl.Add(tr);
                if (++rowCount % 8 == 0)
                    await Dispatcher.Yield(DispatcherPriority.Background);
            }
        return tbl;
    }

    private static void WritePackage(string path, XDocument contentTypes, XDocument relationships, XDocument documentXml)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                WriteXml(zip, "[Content_Types].xml", contentTypes);
                WriteXml(zip, "_rels/.rels", relationships);
                WriteXml(zip, "word/document.xml", documentXml);
            }

            if (File.Exists(path) && !File.Exists(path + ".bak"))
                File.Copy(path, path + ".bak");
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static XElement WriteParagraph(Paragraph p, string prefix = "")
    {
        var props = new XElement(W + "pPr");
        if (p.FontSize >= PointsToDips(22)) props.Add(new XElement(W + "pStyle", new XAttribute(W + "val", "Heading1")));
        else if (p.FontSize >= PointsToDips(18)) props.Add(new XElement(W + "pStyle", new XAttribute(W + "val", "Heading2")));
        else if (p.FontSize >= PointsToDips(15)) props.Add(new XElement(W + "pStyle", new XAttribute(W + "val", "Heading3")));
        var align = p.TextAlignment switch { TextAlignment.Center => "center", TextAlignment.Right => "right", TextAlignment.Justify => "both", _ => "left" };
        if (align != "left") props.Add(new XElement(W + "jc", new XAttribute(W + "val", align)));
        var result = new XElement(W + "p", props);
        if (prefix.Length > 0) result.Add(new XElement(W + "r", new XElement(W + "t", prefix)));
        foreach (var inline in p.Inlines) WriteInline(result, inline);
        return result;
    }

    private static void WriteInline(XElement paragraph, Inline inline)
    {
        if (inline is LineBreak) { paragraph.Add(new XElement(W + "r", new XElement(W + "br"))); return; }
        if (inline is Span span)
        {
            foreach (var child in span.Inlines) WriteInlineWithInherited(paragraph, child, span);
            return;
        }
        if (inline is Run run) paragraph.Add(MakeRun(run.Text, inline));
    }

    private static void WriteInlineWithInherited(XElement paragraph, Inline child, Span parent)
    {
        if (child is Run run) paragraph.Add(MakeRun(run.Text, child));
        else if (child is Span nested) foreach (var nestedChild in nested.Inlines) WriteInlineWithInherited(paragraph, nestedChild, nested);
    }

    private static XElement MakeRun(string? text, Inline inline)
    {
        var rPr = new XElement(W + "rPr");
        if (inline.FontWeight == FontWeights.Bold) rPr.Add(new XElement(W + "b"));
        if (inline.FontStyle == FontStyles.Italic) rPr.Add(new XElement(W + "i"));
        var decorations = inline.TextDecorations;
        if (decorations?.Any(d => d.Location == TextDecorationLocation.Underline) == true) rPr.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
        if (decorations?.Any(d => d.Location == TextDecorationLocation.Strikethrough) == true) rPr.Add(new XElement(W + "strike"));
        if (inline.Foreground is SolidColorBrush brush) rPr.Add(new XElement(W + "color", new XAttribute(W + "val", $"{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}")));
        if (inline.Background is SolidColorBrush bg)
        {
            var color = bg.Color == Colors.Yellow ? "yellow" : bg.Color == Colors.LightGreen ? "green" : bg.Color == Colors.LightBlue ? "cyan" : "magenta";
            rPr.Add(new XElement(W + "highlight", new XAttribute(W + "val", color)));
        }
        if (inline.FontSize > 0)
            rPr.Add(new XElement(W + "sz", new XAttribute(W + "val",
                Math.Round(inline.FontSize / DipsPerPoint * 2, MidpointRounding.AwayFromZero))));
        if (inline.FontFamily is not null) rPr.Add(new XElement(W + "rFonts", new XAttribute(W + "ascii", inline.FontFamily.Source), new XAttribute(W + "hAnsi", inline.FontFamily.Source)));
        var r = new XElement(W + "r"); if (rPr.HasElements) r.Add(rPr);
        r.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text ?? ""));
        return r;
    }

    private static XElement WriteTable(Table table)
    {
        var tbl = new XElement(W + "tbl");
        foreach (var group in table.RowGroups) foreach (var row in group.Rows)
        {
            var tr = new XElement(W + "tr");
            foreach (var cell in row.Cells)
            {
                var tc = new XElement(W + "tc");
                foreach (var block in cell.Blocks) if (block is Paragraph p) tc.Add(WriteParagraph(p));
                if (!tc.Elements(W + "p").Any()) tc.Add(new XElement(W + "p"));
                tr.Add(tc);
            }
            tbl.Add(tr);
        }
        return tbl;
    }

    private static void WriteXml(ZipArchive zip, string name, XDocument xml)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open(); xml.Save(stream);
    }
}
