namespace EEMC.WM.Core.Domain;

/// <summary>Thực thể được đồng bộ giữa các máy.</summary>
public interface ISyncEntity
{
    /// <summary>Định danh toàn cục (32 ký tự hex), giống nhau trên mọi máy.</summary>
    string SyncId { get; set; }
    /// <summary>Thời điểm thay đổi gần nhất (UTC ticks) để quyết định bản nào mới hơn.</summary>
    long SyncStamp { get; set; }
    /// <summary>Có thay đổi trên máy này chưa gửi đi.</summary>
    bool SyncDirty { get; set; }
}

public static class SyncIds
{
    public static string New() => Guid.NewGuid().ToString("N");

    /// <summary>Định danh cố định suy ra từ nội dung (dùng cho danh mục mặc định để các máy trùng nhau).</summary>
    public static string Deterministic(string key)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public class WorkTask : ISyncEntity
{
    public int Id { get; set; }

    /// <summary>Mã công việc duy nhất, tự sinh: CV-yyyy-nnnnn.</summary>
    public string Code { get; set; } = "";

    // ---- 4 trường bắt buộc ----
    public DateTime ReceivedAt { get; set; }
    public DateTime DueAt { get; set; }
    public string Title { get; set; } = "";
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    public WorkTaskStatus Status { get; set; } = WorkTaskStatus.NotStarted;

    // ---- Thông tin mở rộng (không bắt buộc) ----
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public string? ProjectName { get; set; }
    public string? AssignedBy { get; set; }
    public string? Assignee { get; set; }
    public string? Coordinator { get; set; }
    /// <summary>Thời lượng dự kiến (giờ làm việc). Null = chưa ước tính.</summary>
    public double? EstimatedHours { get; set; }
    public DateTime? PlannedStart { get; set; }
    /// <summary>Công việc có thời điểm thực hiện cố định (họp, làm việc với đoàn...).</summary>
    public bool IsFixedTime { get; set; }
    public int Progress { get; set; }
    public string? Deliverable { get; set; }
    public DocumentStatus DocumentStatus { get; set; } = DocumentStatus.None;
    public string? DocumentNumber { get; set; }
    public string? LeaderOpinionNeeded { get; set; }
    public string? Notes { get; set; }
    public string? FolderPath { get; set; }
    /// <summary>Ngày dự kiến nhận phản hồi (với công việc chờ phản hồi/phối hợp/phê duyệt).</summary>
    public DateTime? ExpectedResponseAt { get; set; }

    public int? ParentId { get; set; }
    public WorkTask? Parent { get; set; }
    /// <summary>Trọng số khi tính tiến độ của công việc cha (mặc định 1).</summary>
    public double Weight { get; set; } = 1;
    /// <summary>Dự án KH&amp;CN liên quan (Giai đoạn 2). ProjectName vẫn giữ để tương thích dữ liệu cũ.</summary>
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }

    // ---- Hệ thống ----
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    /// <summary>Thời điểm đóng công việc (hoàn thành hoặc hủy).</summary>
    public DateTime? ClosedAt { get; set; }
    /// <summary>Chuỗi chuẩn hóa (không dấu, chữ thường) phục vụ tìm kiếm.</summary>
    public string SearchText { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    /// <summary>Tạm hoãn nhắc việc đến thời điểm này (chỉ trên máy này, không đồng bộ).</summary>
    public DateTime? SnoozeUntil { get; set; }

    // ---- Đồng bộ ----
    public string SyncId { get; set; } = SyncIds.New();
    public long SyncStamp { get; set; }
    public bool SyncDirty { get; set; }
    /// <summary>Máy đã tạo công việc (dùng khi xử lý trùng mã).</summary>
    public string? OriginDevice { get; set; }

    // ---- Thông tin tính toán (không lưu CSDL) ----
    /// <summary>Id các công việc phải hoàn thành trước công việc này.</summary>
    public List<int> DependsOnIds { get; set; } = new();
    /// <summary>Mã các công việc phụ thuộc CHƯA hoàn thành (công việc này đang bị chặn).</summary>
    public List<string> BlockedBy { get; set; } = new();
    public bool IsBlocked => BlockedBy.Count > 0;

    public void RefreshSearchText()
    {
        SearchText = Text.TextNormalizer.Normalize(string.Join(" ",
            Code, Title, ProjectName, AssignedBy, Assignee, Coordinator, Deliverable, DocumentNumber, Notes, LeaderOpinionNeeded));
    }

    public WorkTask CloneForCopy()
    {
        return new WorkTask
        {
            Title = Title,
            Priority = Priority,
            DueAt = DueAt,
            ReceivedAt = ReceivedAt,
            CategoryId = CategoryId,
            ProjectName = ProjectName,
            AssignedBy = AssignedBy,
            Assignee = Assignee,
            Coordinator = Coordinator,
            EstimatedHours = EstimatedHours,
            IsFixedTime = IsFixedTime,
            Deliverable = Deliverable,
            LeaderOpinionNeeded = LeaderOpinionNeeded,
            Notes = Notes,
            FolderPath = FolderPath,
            ProjectId = ProjectId,
            ParentId = ParentId,
            Weight = Weight,
        };
    }
}

/// <summary>Danh mục nghiệp vụ hai cấp: Nhóm (ParentId = null) và Mục con.</summary>
public class Category : ISyncEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string SyncId { get; set; } = SyncIds.New();
    public long SyncStamp { get; set; }
    public bool SyncDirty { get; set; }

    public string DisplayName => Parent == null ? Name : $"{Parent.Name} › {Name}";
}

/// <summary>Lịch sử thay đổi và lịch sử trao đổi, xử lý của công việc.</summary>
public class TaskHistory : ISyncEntity
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public DateTime At { get; set; }
    /// <summary>Tạo mới, Cập nhật, Đổi trạng thái, Trao đổi, Xóa, Khôi phục...</summary>
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool IsComment { get; set; }
    public string SyncId { get; set; } = SyncIds.New();
    public long SyncStamp { get; set; }
    public bool SyncDirty { get; set; }
}

public class AppNotification
{
    public int Id { get; set; }
    public int? TaskId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public NotificationSeverity Severity { get; set; }
    public bool IsRead { get; set; }
}

/// <summary>Ghi nhận các mốc nhắc việc đã phát để không gửi trùng.</summary>
public class ReminderLog
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public string ReminderKey { get; set; } = "";
    public DateTime FiredAt { get; set; }
}

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>Bộ đếm sinh mã theo năm.</summary>
public class CodeCounter
{
    public int Year { get; set; }
    public int LastNumber { get; set; }
}

// ============================ GIAI ĐOẠN 2 ============================

public enum ProjectStatus
{
    Proposed = 1,     // Đề xuất
    Preparing = 2,    // Đang chuẩn bị hồ sơ
    InProgress = 3,   // Đang thực hiện
    Acceptance = 4,   // Nghiệm thu
    Settlement = 5,   // Thanh quyết toán
    Completed = 6,    // Hoàn thành
    Suspended = 7     // Tạm dừng
}

/// <summary>Đề tài/dự án KH&amp;CN.</summary>
public class Project
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Cấp quản lý: Nhà nước, Bộ, Tập đoàn, Tổng công ty, Cơ sở...</summary>
    public string? ManagementLevel { get; set; }
    public string? LeadUnit { get; set; }
    public string? Leader { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Proposed;
    public string? Description { get; set; }
    /// <summary>Các vấn đề tồn đọng.</summary>
    public string? Issues { get; set; }
    /// <summary>Những quyết định cần xin ý kiến.</summary>
    public string? DecisionsNeeded { get; set; }
    public string? FolderPath { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<Milestone> Milestones { get; set; } = new();
}

/// <summary>Mốc quan trọng của dự án.</summary>
public class Milestone
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = "";
    public DateTime? DueDate { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
}

public enum AttachmentKind
{
    Other = 0, Word = 1, Excel = 2, Pdf = 3, Folder = 4, Link = 5,
    Minutes = 6,      // Biên bản họp
    Decision = 7,     // Quyết định
    Proposal = 8,     // Tờ trình
    Report = 9        // Báo cáo
}

/// <summary>
/// Hồ sơ, tài liệu: tệp tải lên máy chủ hoặc đường dẫn/liên kết. Một tài liệu có thể nằm ở nhiều nơi
/// (thư mục, công việc, dự án, quyết định) qua <see cref="AttachmentLink"/> mà không nhân bản tệp.
/// </summary>
public class Attachment
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public AttachmentKind Kind { get; set; }
    /// <summary>Đường dẫn mạng nội bộ, thư mục hoặc URL (khi không tải tệp lên).</summary>
    public string? Link { get; set; }
    /// <summary>Tệp của phiên bản hiện hành lưu trên máy chủ (khi tải tệp lên).</summary>
    public string? StoredFile { get; set; }
    public long SizeBytes { get; set; }
    /// <summary>Mã SHA-256 của phiên bản hiện hành, dùng phát hiện tệp trùng.</summary>
    public string? Sha256 { get; set; }
    public int CurrentVersion { get; set; } = 1;
    public string? DocumentNumber { get; set; }
    public DocumentStatus DocumentStatus { get; set; }
    /// <summary>Nội dung chữ trích từ PDF/DOCX/TXT (null = chưa đọc, rỗng = không có chữ).</summary>
    public string? ExtractedText { get; set; }
    /// <summary>Chuỗi chuẩn hóa (không dấu) gồm tên, số văn bản và nội dung, phục vụ tìm kiếm.</summary>
    public string SearchText { get; set; } = "";
    public string? UploadedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Vị trí của một tài liệu: đúng một trong FolderId / TaskId / ProjectId / LegalDocumentId.
/// Nhiều vị trí cùng trỏ tới một tài liệu (không nhân bản tệp).
/// </summary>
public class AttachmentLink
{
    public int Id { get; set; }
    public int AttachmentId { get; set; }
    public int? FolderId { get; set; }
    public int? TaskId { get; set; }
    public int? ProjectId { get; set; }
    public int? LegalDocumentId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Một phiên bản tệp; bản cũ được giữ lại để xem, tải và khôi phục.</summary>
public class AttachmentVersion
{
    public int Id { get; set; }
    public int AttachmentId { get; set; }
    public int VersionNo { get; set; }
    public string StoredFile { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public string? Note { get; set; }
    public string? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }
}

/// <summary>
/// Thư mục hồ sơ. Thư mục gốc thuộc về một dự án, công việc, quyết định (văn bản loại Quyết định) hoặc là thư mục chung;
/// thư mục con mang cùng chủ sở hữu với thư mục gốc.
/// </summary>
public class DocFolder
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ParentId { get; set; }
    public int? ProjectId { get; set; }
    public int? TaskId { get; set; }
    public int? LegalDocumentId { get; set; }
    public string? CreatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Công việc <see cref="TaskId"/> chỉ bắt đầu được sau khi <see cref="DependsOnTaskId"/> hoàn thành.</summary>
public class TaskDependency
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public int DependsOnTaskId { get; set; }
}

/// <summary>Vai trò người dùng bản web; giá trị lớn hơn bao gồm quyền của giá trị nhỏ hơn.</summary>
public enum UserRole
{
    Viewer = 1,   // Chỉ xem
    Editor = 2,   // Biên tập: thêm, sửa nội dung
    Admin = 3     // Quản trị: người dùng, cài đặt, duyệt, xóa vĩnh viễn
}

/// <summary>Tài khoản đăng nhập bản web.</summary>
public class AppUser
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Viewer;
    /// <summary>Tài khoản bị khóa bởi quản trị (không đăng nhập được).</summary>
    public bool IsDisabled { get; set; }
    public bool IsAdmin => Role == UserRole.Admin;
    public int FailedLogins { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>Dấu vết xóa vĩnh viễn để các máy khác cũng xóa.</summary>
public class SyncTombstone
{
    public int Id { get; set; }
    /// <summary>"task" hoặc "category".</summary>
    public string Kind { get; set; } = "";
    public string SyncId { get; set; } = "";
    public long SyncStamp { get; set; }
    public bool SyncDirty { get; set; }
}

// ============================ VĂN BẢN PHÁP LUẬT ============================

public enum LegalDocType
{
    Law = 1,             // Luật, Bộ luật
    Resolution = 2,      // Nghị quyết
    Decree = 3,          // Nghị định
    Decision = 4,        // Quyết định
    Circular = 5,        // Thông tư
    Internal = 6,        // Quy chế, quy định nội bộ
    Standard = 7,        // Tiêu chuẩn, quy chuẩn
    Guidance = 8,        // Văn bản hướng dẫn (công văn hướng dẫn, hướng dẫn nghiệp vụ...)
    Other = 9
}

/// <summary>Tình trạng hiệu lực do người nhập ghi theo nguồn chính thức; phần mềm không tự khẳng định.</summary>
public enum LegalEffect
{
    Unknown = 0,          // Chưa xác định
    InEffect = 1,         // Còn hiệu lực
    PartlyExpired = 2,    // Hết hiệu lực một phần
    Expired = 3,          // Hết hiệu lực
    NotYetEffective = 4   // Chưa có hiệu lực
}

public enum SummaryState
{
    None = 0,      // Chưa lập
    Draft = 1,     // Chưa duyệt
    Approved = 2   // Đã duyệt
}

/// <summary>Văn bản pháp luật hoặc quy chế nội bộ: toàn văn, bản rút gọn có duyệt, quan hệ với văn bản khác.</summary>
public class LegalDocument
{
    public int Id { get; set; }
    /// <summary>Số hiệu văn bản.</summary>
    public string? Number { get; set; }
    /// <summary>Tên/trích yếu.</summary>
    public string Title { get; set; } = "";
    public LegalDocType DocType { get; set; } = LegalDocType.Other;
    public string? Issuer { get; set; }
    public DateTime? IssuedDate { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public LegalEffect Effect { get; set; }
    /// <summary>Ngày người dùng kiểm tra tình trạng hiệu lực gần nhất.</summary>
    public DateTime? EffectCheckedAt { get; set; }
    /// <summary>Lĩnh vực/nhãn, phân cách bằng dấu phẩy.</summary>
    public string? Tags { get; set; }
    public string? SourceUrl { get; set; }
    /// <summary>Tệp gốc lưu trong thư mục dữ liệu (files\legal\...).</summary>
    public string? StoredFile { get; set; }
    public string? FileName { get; set; }
    public long FileSize { get; set; }
    /// <summary>Toàn văn (văn bản thuần) dùng để tách mục lục và tìm kiếm.</summary>
    public string? FullText { get; set; }

    // ---- Bản rút gọn ----
    /// <summary>Văn bản nói về gì.</summary>
    public string? SummaryAbout { get; set; }
    /// <summary>Áp dụng với EEMC như thế nào.</summary>
    public string? SummaryApplies { get; set; }
    public SummaryState SummaryState { get; set; }
    public string? SummaryAuthor { get; set; }
    public DateTime? SummaryUpdatedAt { get; set; }
    public string? SummaryApprovedBy { get; set; }
    public DateTime? SummaryApprovedAt { get; set; }

    public string SearchText { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<LegalSection> Sections { get; set; } = new();
    public List<LegalPoint> Points { get; set; } = new();

    public string ShortName => string.IsNullOrWhiteSpace(Number) ? Title : $"{Number} – {Title}";
}

public enum LegalSectionKind
{
    Preamble = 0,   // Phần mở đầu, căn cứ ban hành
    Part = 1,       // Phần
    Chapter = 2,    // Chương
    Section = 3,    // Mục
    Article = 4     // Điều
}

/// <summary>Một mục trong mục lục toàn văn (tách tự động từ toàn văn).</summary>
public class LegalSection
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public LegalSectionKind Kind { get; set; }
    /// <summary>Số thứ tự như trong văn bản: "I", "2", "5a".</summary>
    public string Number { get; set; } = "";
    public string Heading { get; set; } = "";
    public string Body { get; set; } = "";
    public int SortOrder { get; set; }
}

public enum LegalPointKind
{
    KeyPoint = 1,   // Điểm chính cần biết
    Duty = 2,       // Việc EEMC cần làm
    Step = 3        // Bước trong trình tự thủ tục
}

/// <summary>Một ý của bản rút gọn, luôn kèm điều khoản căn cứ.</summary>
public class LegalPoint
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public LegalPointKind Kind { get; set; }
    public string Text { get; set; } = "";
    /// <summary>Căn cứ: "Điều 5, khoản 1".</summary>
    public string? Citation { get; set; }
    /// <summary>Thời hạn (văn bản tự do, theo quy định của văn bản).</summary>
    public string? Due { get; set; }
    /// <summary>Công việc đã tạo từ việc cần làm này.</summary>
    public int? TaskId { get; set; }
    public int SortOrder { get; set; }
}

public enum LegalRelationKind
{
    BasedOn = 1,    // Căn cứ
    Guides = 2,     // Hướng dẫn thi hành
    Amends = 3,     // Sửa đổi, bổ sung
    Replaces = 4,   // Thay thế
    Repeals = 5     // Bãi bỏ
}

/// <summary>Văn bản <see cref="DocumentId"/> [Kind] văn bản đích (trong thư viện hoặc chỉ ghi tên).</summary>
public class LegalRelation
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public LegalRelationKind Kind { get; set; }
    public int? TargetId { get; set; }
    /// <summary>Tên văn bản đích khi chưa có trong thư viện.</summary>
    public string? TargetText { get; set; }
}

/// <summary>Công việc liên quan đến văn bản.</summary>
public class LegalTaskLink
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int TaskId { get; set; }
}

// ============================ TRỢ LÝ AI ============================

/// <summary>Một cuộc hội thoại với trợ lý AI, chỉ người tạo xem được.</summary>
public class ChatConversation
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ChatMessage
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    /// <summary>"user" hoặc "assistant".</summary>
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    /// <summary>Danh sách nguồn trích dẫn (JSON) của câu trả lời.</summary>
    public string? SourcesJson { get; set; }
    public string? Model { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    /// <summary>Thông báo lỗi khi không lấy được câu trả lời.</summary>
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Tệp thay đổi cuối cùng đã nhận từ từng máy khác.</summary>
public class SyncSourceState
{
    public string DeviceId { get; set; } = "";
    public string LastFile { get; set; } = "";
    public string? DeviceName { get; set; }
    public DateTime LastImportUtc { get; set; }
}