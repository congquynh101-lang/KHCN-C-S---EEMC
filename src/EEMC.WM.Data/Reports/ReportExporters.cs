using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using W = DocumentFormat.OpenXml.Wordprocessing;
using M = MigraDoc.DocumentObjectModel;

namespace EEMC.WM.Data.Reports;

/// <summary>Xuất <see cref="ReportDocument"/> ra Excel, Word (.docx) và PDF theo thể thức văn bản hành chính.</summary>
public static class ReportExporters
{
    public const string Font = "Times New Roman";

    // ======================================================================== Excel

    public static byte[] ToExcel(ReportDocument doc)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Báo cáo");
        int maxCols = Math.Max(3, doc.Sections.Where(s => s.Table != null).Select(s => s.Table!.Columns.Length).DefaultIfEmpty(3).Max());
        int r = 1;
        void Line(string text, bool bold = false, double size = 12, bool center = false, bool italic = false)
        {
            var range = ws.Range(r, 1, r, maxCols).Merge();
            range.Value = text;
            range.Style.Font.SetBold(bold).Font.SetItalic(italic).Font.SetFontSize(size).Font.SetFontName(Font);
            range.Style.Alignment.SetWrapText();
            if (center) range.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            r++;
        }
        Line(doc.Organization, bold: true);
        Line(doc.Unit, bold: true);
        r++;
        Line(doc.Title, bold: true, size: 14, center: true);
        Line(doc.Period, center: true, italic: true);
        Line(doc.PlaceDate, center: true, italic: true);
        r++;
        foreach (var s in doc.Sections)
        {
            Line(s.Heading, bold: true);
            foreach (var p in s.Paragraphs) Line(p);
            if (s.Table != null && s.Table.Rows.Count > 0)
            {
                var t = s.Table;
                for (int c = 0; c < t.Columns.Length; c++) ws.Cell(r, c + 1).Value = t.Columns[c];
                var head = ws.Range(r, 1, r, t.Columns.Length);
                head.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#DCE6F1")).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetWrapText();
                int start = r;
                r++;
                foreach (var row in t.Rows)
                {
                    for (int c = 0; c < row.Length; c++) ws.Cell(r, c + 1).Value = row[c];
                    r++;
                }
                if (t.Footer != null)
                {
                    for (int c = 0; c < t.Footer.Length; c++) ws.Cell(r, c + 1).Value = t.Footer[c];
                    ws.Range(r, 1, r, t.Columns.Length).Style.Font.SetBold();
                    r++;
                }
                var body = ws.Range(start, 1, r - 1, t.Columns.Length);
                body.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorder(XLBorderStyleValues.Thin)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Top).Alignment.SetWrapText().Font.SetFontName(Font);
            }
            else if (s.Table != null && !string.IsNullOrEmpty(s.EmptyText)) Line(s.EmptyText, italic: true);
            r++;
        }
        if (!string.IsNullOrWhiteSpace(doc.Author)) { Line("NGƯỜI LẬP", bold: true); r += 2; Line(doc.Author, bold: true); }
        ws.Column(1).Width = 6;
        ws.Column(2).Width = 16;
        ws.Column(3).Width = 60;
        for (int c = 4; c <= maxCols; c++) ws.Column(c).Width = 18;
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ======================================================================== Word

    public static byte[] ToWord(ReportDocument doc)
    {
        using var ms = new MemoryStream();
        using (var w = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = w.AddMainDocumentPart();
            main.Document = new W.Document(new Body());
            var body = main.Document.Body!;

            // Khối tiêu đề: đơn vị (trái) – quốc hiệu (phải)
            var header = new W.Table(new TableProperties(
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableBorders(new TopBorder { Val = BorderValues.None }, new BottomBorder { Val = BorderValues.None },
                    new LeftBorder { Val = BorderValues.None }, new RightBorder { Val = BorderValues.None },
                    new InsideHorizontalBorder { Val = BorderValues.None }, new InsideVerticalBorder { Val = BorderValues.None })));
            var hr = new W.TableRow();
            hr.Append(Cell(new[] { (doc.Organization, true, 12), (doc.Unit, true, 12) }, JustificationValues.Center, 2200));
            hr.Append(Cell(new[] { ("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM", true, 12), ("Độc lập – Tự do – Hạnh phúc", true, 13) }, JustificationValues.Center, 2800));
            header.Append(hr);
            body.Append(header);
            body.Append(Para(doc.PlaceDate, italic: true, align: JustificationValues.Right, spaceBefore: 120));
            body.Append(Para(doc.Title, bold: true, size: 14, align: JustificationValues.Center, spaceBefore: 240));
            body.Append(Para(doc.Period, italic: true, align: JustificationValues.Center, spaceAfter: 240));

            foreach (var s in doc.Sections)
            {
                body.Append(Para(s.Heading, bold: true, spaceBefore: 160));
                foreach (var p in s.Paragraphs) body.Append(Para(p, firstLineIndent: 567));
                if (s.Table != null && s.Table.Rows.Count > 0) body.Append(WordTable(s.Table));
                else if (s.Table != null && !string.IsNullOrEmpty(s.EmptyText)) body.Append(Para(s.EmptyText, italic: true, firstLineIndent: 567));
            }
            if (!string.IsNullOrWhiteSpace(doc.Author))
            {
                body.Append(Para("NGƯỜI LẬP", bold: true, align: JustificationValues.Right, spaceBefore: 360));
                body.Append(Para("", spaceBefore: 600));
                body.Append(Para(doc.Author, bold: true, align: JustificationValues.Right));
            }

            // Khổ A4 ngang, lề theo thể thức (trên/dưới 2 cm, trái 3 cm, phải 1.5 cm)
            body.Append(new SectionProperties(
                new W.PageSize { Width = 16838, Height = 11906, Orient = PageOrientationValues.Landscape },
                new PageMargin { Top = 1134, Bottom = 1134, Left = 1701, Right = 850, Header = 708, Footer = 708 }));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static RunProperties RunProps(bool bold, bool italic, int size)
    {
        var rp = new RunProperties(new RunFonts { Ascii = Font, HighAnsi = Font, ComplexScript = Font, EastAsia = Font });
        if (bold) rp.Append(new W.Bold());
        if (italic) rp.Append(new W.Italic());
        rp.Append(new FontSize { Val = (size * 2).ToString() });
        rp.Append(new FontSizeComplexScript { Val = (size * 2).ToString() });
        return rp;
    }

    private static W.Paragraph Para(string text, bool bold = false, bool italic = false, int size = 13,
        JustificationValues? align = null, int spaceBefore = 0, int spaceAfter = 60, int firstLineIndent = 0)
    {
        var pp = new ParagraphProperties(new SpacingBetweenLines { Before = spaceBefore.ToString(), After = spaceAfter.ToString(), Line = "300", LineRule = LineSpacingRuleValues.Auto });
        if (align.HasValue) pp.Append(new Justification { Val = align.Value });
        else pp.Append(new Justification { Val = JustificationValues.Both });
        if (firstLineIndent > 0) pp.Append(new Indentation { FirstLine = firstLineIndent.ToString() });
        var rp = RunProps(bold, italic, size);
        RemoveNulls(rp);
        return new W.Paragraph(pp, new W.Run(rp, new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static void RemoveNulls(OpenXmlElement e)
    {
        foreach (var c in e.ChildElements.Where(c => c == null).ToList()) c.Remove();
    }

    private static W.TableCell Cell(IEnumerable<(string Text, bool Bold, int Size)> lines, JustificationValues align, int widthPct50)
    {
        var cell = new W.TableCell(new TableCellProperties(new TableCellWidth { Width = widthPct50.ToString(), Type = TableWidthUnitValues.Pct }));
        foreach (var (text, bold, size) in lines)
        {
            var rp = RunProps(bold, false, size);
            RemoveNulls(rp);
            cell.Append(new W.Paragraph(new ParagraphProperties(new Justification { Val = align }, new SpacingBetweenLines { After = "0" }),
                new W.Run(rp, new W.Text(text) { Space = SpaceProcessingModeValues.Preserve })));
        }
        return cell;
    }

    private static W.Table WordTable(ReportTable t)
    {
        var border = BorderValues.Single;
        var table = new W.Table(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(new TopBorder { Val = border, Size = 4 }, new BottomBorder { Val = border, Size = 4 },
                new LeftBorder { Val = border, Size = 4 }, new RightBorder { Val = border, Size = 4 },
                new InsideHorizontalBorder { Val = border, Size = 4 }, new InsideVerticalBorder { Val = border, Size = 4 }),
            new TableLayout { Type = TableLayoutValues.Fixed }));
        var widths = Widths(t);
        var grid = new TableGrid();
        foreach (var wv in widths) grid.Append(new GridColumn { Width = ((int)(wv * 14000)).ToString() });
        table.Append(grid);

        W.TableRow Row(string[] cells, bool head)
        {
            var row = new W.TableRow();
            if (head) row.Append(new TableRowProperties(new TableHeader()));
            for (int i = 0; i < t.Columns.Length; i++)
            {
                var text = i < cells.Length ? cells[i] : "";
                var tcp = new TableCellProperties(new TableCellWidth { Width = ((int)(widths[i] * 5000)).ToString(), Type = TableWidthUnitValues.Pct });
                if (head) tcp.Append(new W.Shading { Val = ShadingPatternValues.Clear, Fill = "DCE6F1" });
                var rp = RunProps(head, false, 12);
                RemoveNulls(rp);
                row.Append(new W.TableCell(tcp, new W.Paragraph(
                    new ParagraphProperties(new Justification { Val = head || i == 0 ? JustificationValues.Center : JustificationValues.Left }, new SpacingBetweenLines { After = "0" }),
                    new W.Run(rp, new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }))));
            }
            return row;
        }
        table.Append(Row(t.Columns, true));
        foreach (var r in t.Rows) table.Append(Row(r, false));
        if (t.Footer != null) table.Append(Row(t.Footer, true));
        return table;
    }

    private static double[] Widths(ReportTable t)
    {
        var w = t.Widths != null && t.Widths.Length == t.Columns.Length ? t.Widths : Enumerable.Repeat(1.0, t.Columns.Length).ToArray();
        var sum = w.Sum();
        return w.Select(x => x / sum).ToArray();
    }

    // ======================================================================== PDF

    private static bool _fontsConfigured;

    public static byte[] ToPdf(ReportDocument doc)
    {
        if (!_fontsConfigured)
        {
            // Dùng phông chữ Windows (Times New Roman) để hiển thị đúng tiếng Việt
            PdfSharp.Fonts.GlobalFontSettings.FontResolver ??= new ReportFontResolver();
            _fontsConfigured = true;
        }
        var d = new M.Document();
        d.Info.Title = doc.Title;
        var normal = d.Styles[StyleNames.Normal]!;
        normal.Font.Name = Font;
        normal.Font.Size = 12;
        var sec = d.AddSection();
        sec.PageSetup = d.DefaultPageSetup.Clone();
        sec.PageSetup.PageFormat = PageFormat.A4;
        sec.PageSetup.Orientation = M.Orientation.Landscape;
        sec.PageSetup.TopMargin = Unit.FromCentimeter(2);
        sec.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        sec.PageSetup.LeftMargin = Unit.FromCentimeter(3);
        sec.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
        var footer = sec.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Right;
        footer.Format.Font.Size = 9;
        footer.AddText("Trang ");
        footer.AddPageField();
        footer.AddText("/");
        footer.AddNumPagesField();

        var contentWidth = Unit.FromCentimeter(29.7 - 3 - 1.5);
        var head = sec.AddTable();
        head.Borders.Visible = false;
        head.AddColumn(Unit.FromCentimeter(contentWidth.Centimeter * 0.45));
        head.AddColumn(Unit.FromCentimeter(contentWidth.Centimeter * 0.55));
        var hrow = head.AddRow();
        foreach (var (cell, lines) in new[] { (hrow.Cells[0], new[] { doc.Organization, doc.Unit }), (hrow.Cells[1], new[] { "CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM", "Độc lập – Tự do – Hạnh phúc" }) })
            foreach (var l in lines)
            {
                var p = cell.AddParagraph(l);
                p.Format.Alignment = ParagraphAlignment.Center;
                p.Format.Font.Bold = true;
            }

        void P(string text, bool bold = false, bool italic = false, double size = 12, ParagraphAlignment align = ParagraphAlignment.Justify, double before = 0, double indent = 0)
        {
            var p = sec.AddParagraph(text);
            p.Format.Font.Bold = bold;
            p.Format.Font.Italic = italic;
            p.Format.Font.Size = size;
            p.Format.Alignment = align;
            p.Format.SpaceBefore = Unit.FromPoint(before);
            p.Format.SpaceAfter = Unit.FromPoint(3);
            if (indent > 0) p.Format.FirstLineIndent = Unit.FromCentimeter(indent);
        }

        P(doc.PlaceDate, italic: true, align: ParagraphAlignment.Right, before: 6);
        P(doc.Title, bold: true, size: 14, align: ParagraphAlignment.Center, before: 12);
        P(doc.Period, italic: true, align: ParagraphAlignment.Center);
        foreach (var s in doc.Sections)
        {
            P(s.Heading, bold: true, before: 10);
            foreach (var para in s.Paragraphs) P(para, indent: 1);
            if (s.Table != null && s.Table.Rows.Count > 0)
            {
                var t = s.Table;
                var widths = Widths(t);
                var tbl = sec.AddTable();
                tbl.Borders.Width = 0.5;
                tbl.Format.Font.Size = 10.5;
                foreach (var w in widths) tbl.AddColumn(Unit.FromCentimeter(contentWidth.Centimeter * w));
                var hr = tbl.AddRow();
                hr.HeadingFormat = true;
                hr.Shading.Color = new M.Color(0xDC, 0xE6, 0xF1);
                hr.Format.Font.Bold = true;
                for (int i = 0; i < t.Columns.Length; i++)
                {
                    hr.Cells[i].AddParagraph(t.Columns[i]).Format.Alignment = ParagraphAlignment.Center;
                }
                foreach (var r in t.Rows.Concat(t.Footer != null ? new[] { t.Footer } : Array.Empty<string[]>()))
                {
                    var row = tbl.AddRow();
                    for (int i = 0; i < t.Columns.Length; i++) row.Cells[i].AddParagraph(i < r.Length ? r[i] : "");
                }
            }
            else if (s.Table != null && !string.IsNullOrEmpty(s.EmptyText)) P(s.EmptyText, italic: true, indent: 1);
        }
        if (!string.IsNullOrWhiteSpace(doc.Author))
        {
            P("NGƯỜI LẬP", bold: true, align: ParagraphAlignment.Right, before: 18);
            P(doc.Author, bold: true, align: ParagraphAlignment.Right, before: 40);
        }

        var renderer = new PdfDocumentRenderer { Document = d };
        renderer.RenderDocument();
        using var ms = new MemoryStream();
        renderer.PdfDocument.Save(ms, false);
        return ms.ToArray();
    }
}
