using System.Text;
using DocumentFormat.OpenXml.Packaging;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Services;
using EEMC.WM.Core.Text;
using EEMC.WM.Core.Time;
using EEMC.WM.Data.Infrastructure;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace EEMC.WM.Data.Services;

/// <summary>Quan hệ nhìn từ một văn bản: chiều đi (văn bản này căn cứ/sửa đổi... văn bản khác) hoặc chiều đến.</summary>
public sealed record LegalRelationView(LegalRelation Relation, bool Incoming, int? OtherId, string OtherName, LegalEffect? OtherEffect, DateTime? OtherEffectiveDate);

/// <summary>Kết quả lưu tệp gốc: đường dẫn lưu, toàn văn trích được và cảnh báo (nếu có).</summary>
public sealed record LegalFileResult(string StoredFile, string FileName, long Size, string? Text, string? Warning);

/// <summary>Thư viện văn bản pháp luật: toàn văn, bản rút gọn (nhập thủ công, có duyệt), quan hệ và đồ họa hiệu lực.</summary>
public sealed class LegalService
{
    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".txt" };

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly AppPaths _paths;
    private readonly IClock _clock;

    public LegalService(IDbContextFactory<AppDbContext> factory, AppPaths paths, IClock clock)
    {
        _factory = factory;
        _paths = paths;
        _clock = clock;
    }

    public event EventHandler? Changed;

    private string FilesDir => Path.Combine(_paths.DataDir, "files");

    // ---------------------------------------------------------------- Danh sách, chi tiết

    /// <summary>Tìm theo số hiệu, tên, cơ quan, nhãn và nội dung toàn văn (không phân biệt dấu).</summary>
    public async Task<List<LegalDocument>> SearchAsync(string? text = null, LegalEffect? effect = null, LegalDocType? type = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var q = db.LegalDocuments.AsNoTracking().AsQueryable();
        if (effect.HasValue) q = q.Where(d => d.Effect == effect.Value);
        if (type.HasValue) q = q.Where(d => d.DocType == type.Value);
        foreach (var term in TextNormalizer.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            q = q.Where(d => d.SearchText.Contains(term));
        return await q.OrderByDescending(d => d.IssuedDate ?? d.CreatedAt).ThenByDescending(d => d.Id)
            .Select(d => new LegalDocument
            {
                Id = d.Id, Number = d.Number, Title = d.Title, DocType = d.DocType, Issuer = d.Issuer, IssuedDate = d.IssuedDate,
                EffectiveDate = d.EffectiveDate, ExpiryDate = d.ExpiryDate, Effect = d.Effect, Tags = d.Tags, SummaryState = d.SummaryState,
                CreatedAt = d.CreatedAt, UpdatedAt = d.UpdatedAt
            }).ToListAsync();
    }

    /// <summary>Số văn bản theo từng tình trạng hiệu lực.</summary>
    public async Task<Dictionary<LegalEffect, int>> CountByEffectAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.LegalDocuments.GroupBy(d => d.Effect).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
    }

    public async Task<LegalDocument?> GetAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var d = await db.LegalDocuments.AsNoTracking().Include(x => x.Sections).Include(x => x.Points).FirstOrDefaultAsync(x => x.Id == id);
        if (d == null) return null;
        d.Sections = d.Sections.OrderBy(s => s.SortOrder).ToList();
        d.Points = d.Points.OrderBy(p => p.Kind).ThenBy(p => p.SortOrder).ToList();
        return d;
    }

    // ---------------------------------------------------------------- Cây: Loại → Văn bản → Phần/Chương/Mục/Điều

    public async Task<Dictionary<LegalDocType, int>> CountByTypeAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.LegalDocuments.GroupBy(d => d.DocType).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
    }

    /// <summary>Văn bản của một loại, kèm số mục đã tách trong toàn văn (0 = chưa có toàn văn).</summary>
    public async Task<List<(LegalDocument Doc, int Sections)>> GetByTypeAsync(LegalDocType type)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var docs = await db.LegalDocuments.AsNoTracking().Where(d => d.DocType == type)
            .OrderByDescending(d => d.IssuedDate ?? d.CreatedAt).ThenByDescending(d => d.Id)
            .Select(d => new LegalDocument { Id = d.Id, Number = d.Number, Title = d.Title, DocType = d.DocType, Effect = d.Effect, EffectiveDate = d.EffectiveDate })
            .ToListAsync();
        var ids = docs.Select(d => d.Id).ToList();
        var counts = await db.LegalSections.Where(s => ids.Contains(s.DocumentId)).GroupBy(s => s.DocumentId)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        return docs.Select(d => (d, counts.GetValueOrDefault(d.Id))).ToList();
    }

    /// <summary>Mục lục (không kèm nội dung điều) theo thứ tự trong văn bản.</summary>
    public async Task<List<LegalSection>> GetOutlineAsync(int documentId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.LegalSections.AsNoTracking().Where(s => s.DocumentId == documentId).OrderBy(s => s.SortOrder)
            .Select(s => new LegalSection { Id = s.Id, DocumentId = s.DocumentId, Kind = s.Kind, Number = s.Number, Heading = s.Heading, SortOrder = s.SortOrder })
            .ToListAsync();
    }

    /// <summary>Điều có tên hoặc nội dung chứa mọi từ tìm kiếm (không phân biệt dấu), trong tối đa 20 văn bản phù hợp.</summary>
    public async Task<List<(int DocumentId, LegalDocType Type, int SortOrder)>> SearchArticlesAsync(string text, int take = 30)
    {
        var terms = TextNormalizer.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return new();
        await using var db = await _factory.CreateDbContextAsync();
        var q = db.LegalDocuments.AsQueryable();
        foreach (var term in terms) q = q.Where(d => d.SearchText.Contains(term));
        var docs = await q.Select(d => new { d.Id, d.DocType }).Take(20).ToListAsync();
        var result = new List<(int, LegalDocType, int)>();
        foreach (var d in docs)
        {
            var sections = await db.LegalSections.AsNoTracking().Where(s => s.DocumentId == d.Id && s.Kind == LegalSectionKind.Article)
                .OrderBy(s => s.SortOrder).ToListAsync();
            foreach (var s in sections)
            {
                var hay = TextNormalizer.Normalize(s.Number + " " + s.Heading + " " + s.Body);
                if (terms.All(hay.Contains)) result.Add((d.Id, d.DocType, s.SortOrder));
                if (result.Count >= take) return result;
            }
        }
        return result;
    }

    /// <summary>Văn bản sắp có hiệu lực hoặc sắp hết hiệu lực trong <paramref name="days"/> ngày tới.</summary>
    public async Task<List<LegalDocument>> GetUpcomingAsync(int days = 30)
    {
        var today = _clock.Now.Date;
        var until = today.AddDays(days);
        await using var db = await _factory.CreateDbContextAsync();
        return await db.LegalDocuments.AsNoTracking()
            .Where(d => (d.EffectiveDate >= today && d.EffectiveDate <= until) || (d.ExpiryDate >= today && d.ExpiryDate <= until))
            .OrderBy(d => d.EffectiveDate)
            .Select(d => new LegalDocument { Id = d.Id, Number = d.Number, Title = d.Title, DocType = d.DocType, EffectiveDate = d.EffectiveDate, ExpiryDate = d.ExpiryDate, Effect = d.Effect })
            .ToListAsync();
    }

    // ---------------------------------------------------------------- Thêm, sửa, xóa

    /// <summary>Lưu thông tin văn bản và toàn văn; mục lục được tách lại khi toàn văn thay đổi.</summary>
    public async Task<LegalDocument> SaveAsync(LegalDocument input)
    {
        Access.Require(UserRole.Editor);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(input.Title)) errors.Add("Vui lòng nhập tên (trích yếu) văn bản.");
        if (input.IssuedDate.HasValue && input.EffectiveDate.HasValue && input.EffectiveDate < input.IssuedDate)
            errors.Add("Ngày có hiệu lực không được trước ngày ban hành.");
        if (input.EffectiveDate.HasValue && input.ExpiryDate.HasValue && input.ExpiryDate < input.EffectiveDate)
            errors.Add("Ngày hết hiệu lực không được trước ngày có hiệu lực.");
        if (!string.IsNullOrWhiteSpace(input.SourceUrl) && !IsWebUrl(input.SourceUrl))
            errors.Add("Đường dẫn nguồn phải bắt đầu bằng http:// hoặc https://");
        if (errors.Count > 0) throw new ValidationException(errors);

        await using var db = await _factory.CreateDbContextAsync();
        var now = _clock.Now;
        LegalDocument d;
        string? oldFile = null;
        if (input.Id == 0)
        {
            d = new LegalDocument { CreatedAt = now };
            db.LegalDocuments.Add(d);
        }
        else
        {
            d = await db.LegalDocuments.FirstOrDefaultAsync(x => x.Id == input.Id) ?? throw new InvalidOperationException("Không tìm thấy văn bản.");
            if (d.StoredFile != null && d.StoredFile != input.StoredFile) oldFile = d.StoredFile;
        }

        bool textChanged = input.Id == 0 || Clean(d.FullText) != Clean(input.FullText);
        bool effectChanged = input.Id == 0 || d.Effect != input.Effect || d.EffectiveDate != input.EffectiveDate || d.ExpiryDate != input.ExpiryDate;

        d.Number = Clean(input.Number);
        d.Title = input.Title.Trim();
        d.DocType = input.DocType;
        d.Issuer = Clean(input.Issuer);
        d.IssuedDate = input.IssuedDate?.Date;
        d.EffectiveDate = input.EffectiveDate?.Date;
        d.ExpiryDate = input.ExpiryDate?.Date;
        d.Effect = input.Effect;
        if (effectChanged) d.EffectCheckedAt = now;
        d.Tags = Clean(input.Tags);
        d.SourceUrl = Clean(input.SourceUrl);
        d.StoredFile = input.StoredFile;
        d.FileName = input.StoredFile == null ? null : input.FileName;
        d.FileSize = input.StoredFile == null ? 0 : input.FileSize;
        d.FullText = Clean(input.FullText);
        d.UpdatedAt = now;
        d.SearchText = TextNormalizer.Normalize(string.Join(" ", d.Number, d.Title, d.Issuer, d.Tags, Labels.Of(d.DocType), d.FullText));
        await db.SaveChangesAsync();

        if (textChanged)
        {
            await db.LegalSections.Where(s => s.DocumentId == d.Id).ExecuteDeleteAsync();
            foreach (var s in LegalTextParser.Parse(d.FullText))
            {
                s.DocumentId = d.Id;
                db.LegalSections.Add(s);
            }
            await db.SaveChangesAsync();
        }
        if (oldFile != null) DeleteStored(oldFile);
        Changed?.Invoke(this, EventArgs.Empty);
        return d;
    }

    /// <summary>Đánh dấu đã kiểm tra tình trạng hiệu lực hôm nay (không đổi tình trạng).</summary>
    public async Task MarkEffectCheckedAsync(int id)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        await db.LegalDocuments.Where(d => d.Id == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.EffectCheckedAt, _clock.Now));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Xóa văn bản. Văn bản khác đang trỏ tới vẫn giữ tên văn bản này trong quan hệ.</summary>
    public async Task DeleteAsync(int id)
    {
        Access.Require(UserRole.Admin);
        await using var db = await _factory.CreateDbContextAsync();
        var d = await db.LegalDocuments.FirstOrDefaultAsync(x => x.Id == id);
        if (d == null) return;
        var name = d.ShortName;
        foreach (var r in await db.LegalRelations.Where(r => r.TargetId == id).ToListAsync())
        {
            r.TargetId = null;
            r.TargetText = name;
        }
        await db.LegalRelations.Where(r => r.DocumentId == id).ExecuteDeleteAsync();
        await db.LegalTaskLinks.Where(l => l.DocumentId == id).ExecuteDeleteAsync();
        await DocumentService.DetachOwnerAsync(db, new DocOwner(LegalDocumentId: id), d.Number ?? "Văn bản", _clock.Now);
        db.LegalDocuments.Remove(d);
        await db.SaveChangesAsync();
        DeleteStored(d.StoredFile);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- Tệp gốc và trích toàn văn

    /// <summary>Lưu tệp gốc (PDF có lớp chữ, DOCX, TXT) và trích toàn văn. Tệp gốc không bị sửa.</summary>
    public async Task<LegalFileResult> StoreFileAsync(Stream content, string fileName, long size)
    {
        Access.Require(UserRole.Editor);
        var safeName = Path.GetFileName(fileName ?? "").Trim();
        var ext = Path.GetExtension(safeName);
        var errors = new List<string>();
        if (safeName.Length == 0) errors.Add("Tên tệp không hợp lệ.");
        if (!AllowedExtensions.Contains(ext)) errors.Add("Chỉ nhận tệp PDF, DOCX hoặc TXT. Tệp .doc cũ hãy lưu lại thành .docx.");
        if (size > AttachmentService.MaxBytes) errors.Add($"Tệp vượt quá {AttachmentService.MaxBytes / 1024 / 1024} MB.");
        if (errors.Count > 0) throw new ValidationException(errors);

        var now = _clock.Now;
        var rel = Path.Combine("legal", now.ToString("yyyy"), $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}");
        var full = Path.Combine(FilesDir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        long written;
        await using (var fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write))
        {
            await content.CopyToAsync(fs);
            written = fs.Length;
        }
        if (written > AttachmentService.MaxBytes)
        {
            File.Delete(full);
            throw new ValidationException(new[] { $"Tệp vượt quá {AttachmentService.MaxBytes / 1024 / 1024} MB." });
        }

        string? text = null, warning = null;
        try { (text, warning) = ExtractText(full); }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Trích toàn văn " + safeName);
            warning = "Không đọc được nội dung tệp. Tệp gốc vẫn được lưu; hãy dán toàn văn vào ô bên dưới.";
        }
        return new LegalFileResult(rel, safeName, written, text, warning);
    }

    /// <summary>Bỏ tệp đã tải lên nhưng chưa lưu vào văn bản nào.</summary>
    public async Task DiscardFileAsync(string storedFile)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        if (await db.LegalDocuments.AnyAsync(d => d.StoredFile == storedFile)) return;
        DeleteStored(storedFile);
    }

    public static (string? Text, string? Warning) ExtractText(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var text = TextExtractor.Read(path);
        int letters = text.Count(char.IsLetter);
        if (ext == ".pdf" && letters < 200)
            return (null, "PDF không có lớp chữ (bản quét). Phần mềm không tự nhận dạng chữ; hãy dán toàn văn từ nguồn chính thức.");
        if (letters == 0) return (null, "Tệp không có nội dung chữ.");
        return (text, ext == ".pdf" ? "Toàn văn trích từ PDF có thể sai xuống dòng hoặc dấu cách. Hãy đối chiếu với bản gốc trước khi lưu." : null);
    }

    public string? ResolveStoredPath(string rel)
    {
        rel = rel.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(FilesDir) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(FilesDir, rel));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private void DeleteStored(string? rel)
    {
        if (string.IsNullOrEmpty(rel)) return;
        try
        {
            var full = ResolveStoredPath(rel);
            if (full != null && File.Exists(full)) File.Delete(full);
        }
        catch (Exception ex) { AppLog.Error(ex, "Xóa tệp văn bản pháp luật"); }
    }

    // ---------------------------------------------------------------- Bản rút gọn

    /// <summary>
    /// Lưu bản rút gọn do người lập nhập. Mỗi điểm chính và việc cần làm phải ghi điều khoản căn cứ.
    /// Lưu lại luôn đưa bản rút gọn về trạng thái "Chưa duyệt".
    /// </summary>
    public async Task SaveSummaryAsync(int documentId, string? about, string? applies, IEnumerable<LegalPoint> points, string author)
    {
        Access.Require(UserRole.Editor);
        var list = points.Where(p => !string.IsNullOrWhiteSpace(p.Text)).ToList();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(about) && string.IsNullOrWhiteSpace(applies) && list.Count == 0)
            errors.Add("Bản rút gọn đang trống.");
        foreach (var p in list.Where(p => p.Kind != LegalPointKind.Step && string.IsNullOrWhiteSpace(p.Citation)))
            errors.Add($"Thiếu điều khoản căn cứ cho: \"{Shorten(p.Text)}\".");

        await using var db = await _factory.CreateDbContextAsync();
        var d = await db.LegalDocuments.FirstOrDefaultAsync(x => x.Id == documentId) ?? throw new InvalidOperationException("Không tìm thấy văn bản.");
        // Dẫn nguồn chính xác: căn cứ phải trỏ tới điều/khoản/điểm có thật trong toàn văn (khi đã nhập toàn văn)
        var sections = await db.LegalSections.AsNoTracking().Where(s => s.DocumentId == documentId && s.Kind == LegalSectionKind.Article).ToListAsync();
        foreach (var p in list.Where(p => !string.IsNullOrWhiteSpace(p.Citation)))
        {
            var (_, error) = LegalTextParser.Locate(sections, LegalTextParser.ParseCitation(p.Citation));
            if (error != null) errors.Add($"Căn cứ \"{p.Citation!.Trim()}\" ({Shorten(p.Text)}): {error}.");
        }
        if (errors.Count > 0) throw new ValidationException(errors);
        var now = _clock.Now;
        d.SummaryAbout = Clean(about);
        d.SummaryApplies = Clean(applies);
        d.SummaryState = SummaryState.Draft;
        d.SummaryAuthor = Access.Current?.Name ?? author; // ưu tiên người đăng nhập thật
        d.SummaryUpdatedAt = now;
        d.SummaryApprovedBy = null;
        d.SummaryApprovedAt = null;
        d.UpdatedAt = now;
        await db.LegalPoints.Where(p => p.DocumentId == documentId).ExecuteDeleteAsync();
        foreach (var g in list.GroupBy(p => p.Kind))
        {
            int i = 0;
            foreach (var p in g)
                db.LegalPoints.Add(new LegalPoint
                {
                    DocumentId = documentId, Kind = p.Kind, Text = p.Text.Trim(), Citation = Clean(p.Citation), Due = Clean(p.Due),
                    TaskId = p.TaskId, SortOrder = i++
                });
        }
        await db.SaveChangesAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Duyệt bản rút gọn sau khi đã đối chiếu với toàn văn.</summary>
    public async Task ApproveSummaryAsync(int documentId, string approver)
    {
        Access.Require(UserRole.Admin);
        await using var db = await _factory.CreateDbContextAsync();
        var d = await db.LegalDocuments.FirstOrDefaultAsync(x => x.Id == documentId) ?? throw new InvalidOperationException("Không tìm thấy văn bản.");
        if (d.SummaryState != SummaryState.Draft) throw new InvalidOperationException("Chỉ duyệt được bản rút gọn đang ở trạng thái \"Chưa duyệt\".");
        d.SummaryState = SummaryState.Approved;
        d.SummaryApprovedBy = Access.Current?.Name ?? approver;
        d.SummaryApprovedAt = _clock.Now;
        await db.SaveChangesAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Hủy duyệt (ví dụ khi phát hiện bản rút gọn chưa đúng hoặc văn bản bị sửa đổi).</summary>
    public async Task RevokeApprovalAsync(int documentId)
    {
        Access.Require(UserRole.Admin);
        await using var db = await _factory.CreateDbContextAsync();
        await db.LegalDocuments.Where(d => d.Id == documentId && d.SummaryState == SummaryState.Approved)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.SummaryState, SummaryState.Draft)
                .SetProperty(d => d.SummaryApprovedBy, (string?)null).SetProperty(d => d.SummaryApprovedAt, (DateTime?)null));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- Quan hệ

    public async Task<List<LegalRelationView>> GetRelationsAsync(int documentId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var rels = await db.LegalRelations.AsNoTracking().Where(r => r.DocumentId == documentId || r.TargetId == documentId).ToListAsync();
        var ids = rels.Select(r => r.DocumentId == documentId ? r.TargetId : r.DocumentId).Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        var docs = await db.LegalDocuments.AsNoTracking().Where(d => ids.Contains(d.Id))
            .Select(d => new { d.Id, d.Number, d.Title, d.Effect, d.EffectiveDate }).ToDictionaryAsync(d => d.Id);
        return rels.Select(r =>
        {
            bool incoming = r.DocumentId != documentId;
            int? other = incoming ? r.DocumentId : r.TargetId;
            if (other.HasValue && docs.TryGetValue(other.Value, out var o))
                return new LegalRelationView(r, incoming, o.Id, string.IsNullOrWhiteSpace(o.Number) ? o.Title : $"{o.Number} – {o.Title}", o.Effect, o.EffectiveDate);
            return new LegalRelationView(r, incoming, null, r.TargetText ?? "(không rõ)", null, null);
        }).OrderBy(v => v.Relation.Kind).ThenBy(v => v.OtherName).ToList();
    }

    public async Task AddRelationAsync(int documentId, LegalRelationKind kind, int? targetId, string? targetText)
    {
        Access.Require(UserRole.Editor);
        if (targetId == null && string.IsNullOrWhiteSpace(targetText))
            throw new ValidationException(new[] { "Chọn văn bản trong thư viện hoặc nhập tên văn bản." });
        if (targetId == documentId) throw new ValidationException(new[] { "Văn bản không thể có quan hệ với chính nó." });
        await using var db = await _factory.CreateDbContextAsync();
        if (targetId.HasValue && !await db.LegalDocuments.AnyAsync(d => d.Id == targetId))
            throw new InvalidOperationException("Không tìm thấy văn bản đích.");
        var text = targetId.HasValue ? null : targetText!.Trim();
        if (await db.LegalRelations.AnyAsync(r => r.DocumentId == documentId && r.Kind == kind && r.TargetId == targetId && r.TargetText == text))
            throw new ValidationException(new[] { "Quan hệ này đã có." });
        db.LegalRelations.Add(new LegalRelation { DocumentId = documentId, Kind = kind, TargetId = targetId, TargetText = text });
        await db.SaveChangesAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteRelationAsync(int relationId)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        await db.LegalRelations.Where(r => r.Id == relationId).ExecuteDeleteAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- Công việc liên quan

    public async Task<List<WorkTask>> GetLinkedTasksAsync(int documentId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var ids = await db.LegalTaskLinks.Where(l => l.DocumentId == documentId).Select(l => l.TaskId).ToListAsync();
        return await db.Tasks.AsNoTracking().Where(t => ids.Contains(t.Id) && !t.IsDeleted).OrderBy(t => t.DueAt).ToListAsync();
    }

    public async Task LinkTaskAsync(int documentId, int taskId)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        if (!await db.Tasks.AnyAsync(t => t.Id == taskId && !t.IsDeleted)) throw new InvalidOperationException("Không tìm thấy công việc.");
        if (await db.LegalTaskLinks.AnyAsync(l => l.DocumentId == documentId && l.TaskId == taskId)) return;
        db.LegalTaskLinks.Add(new LegalTaskLink { DocumentId = documentId, TaskId = taskId });
        await db.SaveChangesAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task UnlinkTaskAsync(int documentId, int taskId)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        await db.LegalTaskLinks.Where(l => l.DocumentId == documentId && l.TaskId == taskId).ExecuteDeleteAsync();
        await db.LegalPoints.Where(p => p.DocumentId == documentId && p.TaskId == taskId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.TaskId, (int?)null));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ghi nhận công việc đã tạo từ một việc cần làm và gắn công việc với văn bản.</summary>
    public async Task SetPointTaskAsync(int pointId, int taskId)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        var p = await db.LegalPoints.FirstOrDefaultAsync(x => x.Id == pointId) ?? throw new InvalidOperationException("Không tìm thấy việc cần làm.");
        p.TaskId = taskId;
        if (!await db.LegalTaskLinks.AnyAsync(l => l.DocumentId == p.DocumentId && l.TaskId == taskId))
            db.LegalTaskLinks.Add(new LegalTaskLink { DocumentId = p.DocumentId, TaskId = taskId });
        await db.SaveChangesAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsWebUrl(string s) =>
        Uri.TryCreate(s.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Shorten(string s) => s.Length <= 60 ? s.Trim() : s[..57].Trim() + "...";
}
