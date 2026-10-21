using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Services;
using EEMC.WM.Core.Time;
using EEMC.WM.Data;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Services;
using Microsoft.Data.Sqlite;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace EEMC.WM.Tests;

public sealed class LegalTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eemc-wm-legal", Guid.NewGuid().ToString("N"));
    private readonly FixedClock _clock = new(new DateTime(2026, 10, 21, 9, 0, 0));
    private TestDbFactory _factory = null!;
    private LegalService _legal = null!;

    // Văn bản mẫu tự soạn để kiểm thử cách tách mục lục (không phải nội dung văn bản thật)
    private const string Sample = """
        CƠ QUAN BAN HÀNH MẪU
        Số: 12/2026/NĐ-MAU
        Hà Nội, ngày 5 tháng 3 năm 2026

        NGHỊ ĐỊNH
        Quy định mẫu để kiểm thử

        Căn cứ Luật mẫu;

        Chương I
        QUY ĐỊNH CHUNG

        Điều 1. Phạm vi điều chỉnh
        Nghị định này quy định nội dung mẫu.

        Điều 2. Đối tượng áp dụng
        1. Doanh nghiệp mẫu.
        a) Điểm a mẫu;
        b) Điểm b mẫu.
        2. Cơ quan mẫu.

        Chương II. TỔ CHỨC THỰC HIỆN
        Mục 1. HỒ SƠ
        Điều 3. Hồ sơ
        Nội dung Điều 3; xem Điều 5. Không phải tiêu đề vì nằm giữa dòng.
        Điều 3a. Điều bổ sung
        Nội dung điều bổ sung.

        Nơi nhận:
        - Lưu: VT.
        """;

    public async Task InitializeAsync()
    {
        var paths = new AppPaths(_dir);
        _factory = new TestDbFactory(paths.DatabasePath);
        await DbInitializer.InitializeAsync(_factory);
        _legal = new LegalService(_factory, paths, _clock);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------- Tách toàn văn

    [Fact]
    public void Parser_splits_chapters_sections_articles_and_keeps_lines()
    {
        var s = LegalTextParser.Parse(Sample);
        Assert.Equal(LegalSectionKind.Preamble, s[0].Kind);
        Assert.Contains("Căn cứ Luật mẫu;", s[0].Body);

        var ch1 = s.Single(x => x.Kind == LegalSectionKind.Chapter && x.Number == "I");
        Assert.Equal("QUY ĐỊNH CHUNG", ch1.Heading); // tiêu đề nằm ở dòng kế tiếp
        var ch2 = s.Single(x => x.Kind == LegalSectionKind.Chapter && x.Number == "II");
        Assert.Equal("TỔ CHỨC THỰC HIỆN", ch2.Heading);
        Assert.Equal("HỒ SƠ", s.Single(x => x.Kind == LegalSectionKind.Section).Heading);

        var articles = s.Where(x => x.Kind == LegalSectionKind.Article).ToList();
        Assert.Equal(new[] { "1", "2", "3", "3a" }, articles.Select(a => a.Number));
        Assert.Equal("Đối tượng áp dụng", articles[1].Heading);
        Assert.Equal("1. Doanh nghiệp mẫu.\na) Điểm a mẫu;\nb) Điểm b mẫu.\n2. Cơ quan mẫu.", articles[1].Body.Replace("\r", ""));
        Assert.DoesNotContain("Nơi nhận", articles[^1].Body);
        Assert.Equal("Nơi nhận, chữ ký", s[^1].Heading);
        Assert.Equal(Enumerable.Range(0, s.Count), s.Select(x => x.SortOrder));

        Assert.Equal(new[] { ("Chương I. QUY ĐỊNH CHUNG", 2), ("Chương II. TỔ CHỨC THỰC HIỆN", 2) }, LegalTextParser.ArticlesPerChapter(s));
        Assert.Equal(1, LegalTextParser.Indent("1. Khoản"));
        Assert.Equal(2, LegalTextParser.Indent("a) Điểm"));
        Assert.Equal("5", LegalTextParser.ArticleOf("Điều 5, khoản 1"));
        Assert.Null(LegalTextParser.ArticleOf("khoản 2"));
    }

    [Fact]
    public void Article_body_splits_into_clauses_and_points()
    {
        var body = LegalTextParser.SplitBody("Phần dẫn.\n1. Khoản một:\na) Điểm a;\ntiếp điểm a\nb) Điểm b.\n2. Khoản hai, 1.000 đồng.\n- gạch đầu dòng");
        Assert.Equal(new[] { "Phần dẫn." }, body.Intro);
        Assert.Equal(new[] { "1", "2" }, body.Clauses.Select(c => c.Number));
        Assert.Equal(new[] { "a", "b" }, body.Clauses[0].Points.Select(p => p.Letter));
        Assert.Equal(2, body.Clauses[0].Points[0].Lines.Count);
        Assert.Equal(2, body.Clauses[1].Lines.Count); // "1.000" không phải khoản mới
        var onlyPoints = LegalTextParser.SplitBody("a) Một;\nb) Hai.");
        Assert.Equal("", Assert.Single(onlyPoints.Clauses).Number);
    }

    [Fact]
    public void Citations_are_read_in_any_order_and_located_in_full_text()
    {
        Assert.Equal(new LegalCitation("5", "2", "a"), LegalTextParser.ParseCitation("Điều 5, khoản 2, điểm a"));
        Assert.Equal(new LegalCitation("5", "2", "a"), LegalTextParser.ParseCitation("điểm a khoản 2 Điều 5"));
        Assert.Equal(new LegalCitation("3a", null, null), LegalTextParser.ParseCitation("Điều 3a"));
        Assert.Equal("Điểm a khoản 2 Điều 5", LegalTextParser.Cite("5", "2", "a"));

        var s = LegalTextParser.Parse(Sample);
        var d2 = s.Single(x => x.Number == "2").SortOrder;
        Assert.Equal(($"sec-{d2}-k1-a", (string?)null), LegalTextParser.Locate(s, LegalTextParser.ParseCitation("Điều 2, khoản 1, điểm a")));
        Assert.Equal(($"sec-{d2}-k2", (string?)null), LegalTextParser.Locate(s, LegalTextParser.ParseCitation("khoản 2 Điều 2")));
        var (anchor, error) = LegalTextParser.Locate(s, LegalTextParser.ParseCitation("Điều 2, khoản 2, điểm c"));
        Assert.Equal($"sec-{d2}-k2", anchor); // mở khoản gần nhất
        Assert.NotNull(error);
        Assert.Equal("toàn văn không có Điều 9", LegalTextParser.Locate(s, LegalTextParser.ParseCitation("Điều 9")).Error);
        Assert.Equal((null, null), LegalTextParser.Locate(Array.Empty<LegalSection>(), LegalTextParser.ParseCitation("Điều 9"))); // chưa có toàn văn: không kiểm tra
        Assert.Equal(LegalDocType.Guidance, LegalTextParser.ReadHeader("HƯỚNG DẪN\nThực hiện quy chế mẫu").Type);
    }

    [Fact]
    public async Task Summary_citations_must_exist_in_full_text()
    {
        var d = await CreateAsync();
        var wrong = new[] { new LegalPoint { Kind = LegalPointKind.KeyPoint, Text = "Sai căn cứ", Citation = "Điều 2, khoản 5" } };
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _legal.SaveSummaryAsync(d.Id, "Nói về", null, wrong, "Người lập"));
        Assert.Contains("không có khoản 5", ex.Message);
        var right = new[] { new LegalPoint { Kind = LegalPointKind.KeyPoint, Text = "Đúng căn cứ", Citation = "điểm b khoản 1 Điều 2" } };
        await _legal.SaveSummaryAsync(d.Id, "Nói về", null, right, "Người lập");
    }

    [Fact]
    public void Header_reader_only_reads_what_is_written()
    {
        var (number, type, issued) = LegalTextParser.ReadHeader(Sample);
        Assert.Equal("12/2026/NĐ-MAU", number);
        Assert.Equal(LegalDocType.Decree, type);
        Assert.Equal(new DateTime(2026, 3, 5), issued);
        Assert.Equal((null, null, null), LegalTextParser.ReadHeader("Văn bản không có phần đầu"));
    }

    [Fact]
    public void Docx_text_is_extracted_paragraph_by_paragraph()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "mau.docx");
        using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(
                new W.Paragraph(new W.Run(new W.Text("Điều 1. Phạm vi"))),
                new W.Paragraph(new W.Run(new W.Text("Nội dung"), new W.TabChar(), new W.Text("mẫu")))));
        }
        var (text, warning) = LegalService.ExtractText(path);
        Assert.Null(warning);
        Assert.Equal("Điều 1. Phạm vi\nNội dung mẫu", text!.Replace("\r", ""));
    }

    // ---------------------------------------------------------------- Lưu, rút gọn, duyệt

    private Task<LegalDocument> CreateAsync(string title = "Nghị định mẫu", string? text = Sample) =>
        _legal.SaveAsync(new LegalDocument { Title = title, DocType = LegalDocType.Decree, FullText = text });

    [Fact]
    public async Task Saving_full_text_builds_table_of_contents_and_search()
    {
        var d = await CreateAsync();
        var loaded = (await _legal.GetAsync(d.Id))!;
        Assert.Equal(4, loaded.Sections.Count(s => s.Kind == LegalSectionKind.Article));
        Assert.Equal(LegalEffect.Unknown, loaded.Effect); // không tự khẳng định hiệu lực
        Assert.Single(await _legal.SearchAsync("dieu bo sung"));
        Assert.Empty(await _legal.SearchAsync("dieu bo sung xyzq"));

        loaded.FullText = "Điều 1. Một điều";
        await _legal.SaveAsync(loaded);
        Assert.Single((await _legal.GetAsync(d.Id))!.Sections);

        await Assert.ThrowsAsync<ValidationException>(() => _legal.SaveAsync(new LegalDocument
        {
            Title = "Sai ngày", IssuedDate = new DateTime(2026, 5, 1), EffectiveDate = new DateTime(2026, 4, 1)
        }));
    }

    [Fact]
    public async Task Summary_needs_citations_starts_unapproved_and_edit_resets_approval()
    {
        var d = await CreateAsync();
        var noCite = new[] { new LegalPoint { Kind = LegalPointKind.KeyPoint, Text = "Điểm không có căn cứ" } };
        await Assert.ThrowsAsync<ValidationException>(() => _legal.SaveSummaryAsync(d.Id, "Nói về", null, noCite, "Người lập"));

        var points = new[]
        {
            new LegalPoint { Kind = LegalPointKind.KeyPoint, Text = "Điểm chính", Citation = "Điều 2" },
            new LegalPoint { Kind = LegalPointKind.Duty, Text = "Việc cần làm", Citation = "Điều 3", Due = "Hằng năm" },
            new LegalPoint { Kind = LegalPointKind.Step, Text = "Bước 1" },
            new LegalPoint { Kind = LegalPointKind.KeyPoint, Text = "  " } // dòng trống bị bỏ
        };
        await _legal.SaveSummaryAsync(d.Id, "Nói về", "Áp dụng", points, "Người lập");
        var s = (await _legal.GetAsync(d.Id))!;
        Assert.Equal(SummaryState.Draft, s.SummaryState);
        Assert.Equal(3, s.Points.Count);

        await _legal.ApproveSummaryAsync(d.Id, "Người duyệt");
        s = (await _legal.GetAsync(d.Id))!;
        Assert.Equal(SummaryState.Approved, s.SummaryState);
        Assert.Equal("Người duyệt", s.SummaryApprovedBy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _legal.ApproveSummaryAsync(d.Id, "Người duyệt"));

        await _legal.SaveSummaryAsync(d.Id, "Nói về (sửa)", "Áp dụng", s.Points, "Người lập");
        s = (await _legal.GetAsync(d.Id))!;
        Assert.Equal(SummaryState.Draft, s.SummaryState);
        Assert.Null(s.SummaryApprovedBy);
    }

    [Fact]
    public async Task Relations_are_seen_from_both_sides_and_survive_target_deletion()
    {
        var law = await CreateAsync("Luật mẫu", null);
        var decree = await CreateAsync("Nghị định hướng dẫn mẫu", null);
        var amend = await CreateAsync("Nghị định sửa đổi mẫu", null);
        await _legal.AddRelationAsync(decree.Id, LegalRelationKind.Guides, law.Id, null);
        await _legal.AddRelationAsync(amend.Id, LegalRelationKind.Amends, decree.Id, null);
        await _legal.AddRelationAsync(decree.Id, LegalRelationKind.BasedOn, null, "Văn bản chưa có trong thư viện");
        await Assert.ThrowsAsync<ValidationException>(() => _legal.AddRelationAsync(decree.Id, LegalRelationKind.Guides, law.Id, null));
        await Assert.ThrowsAsync<ValidationException>(() => _legal.AddRelationAsync(decree.Id, LegalRelationKind.Amends, decree.Id, null));

        var views = await _legal.GetRelationsAsync(decree.Id);
        Assert.Equal(3, views.Count);
        Assert.Contains(views, v => v.Incoming && v.Relation.Kind == LegalRelationKind.Amends && v.OtherId == amend.Id);
        Assert.Contains(views, v => !v.Incoming && v.OtherId == null && v.OtherName == "Văn bản chưa có trong thư viện");

        await _legal.DeleteAsync(law.Id);
        var after = await _legal.GetRelationsAsync(decree.Id);
        var guides = after.Single(v => v.Relation.Kind == LegalRelationKind.Guides);
        Assert.Null(guides.OtherId);
        Assert.Equal("Luật mẫu", guides.OtherName);
    }
}
