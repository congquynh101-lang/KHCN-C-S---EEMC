namespace EEMC.WM.Data.Reports;

public enum ReportKind { Daily, Weekly, Monthly }

public sealed class ReportTable
{
    public required string[] Columns { get; init; }
    /// <summary>Tỷ lệ độ rộng tương đối của từng cột.</summary>
    public double[]? Widths { get; init; }
    public List<string[]> Rows { get; } = new();
    /// <summary>Dòng tổng cộng (in đậm) nếu có.</summary>
    public string[]? Footer { get; set; }
}

public sealed class ReportSection
{
    public required string Heading { get; init; }
    public List<string> Paragraphs { get; } = new();
    public ReportTable? Table { get; set; }
    /// <summary>Hiển thị khi bảng trống.</summary>
    public string? EmptyText { get; set; }
    /// <summary>Nội dung do người dùng tự nhập (không phải số liệu hệ thống).</summary>
    public bool IsUserInput { get; init; }
}

/// <summary>Báo cáo trung lập về định dạng: dùng chung cho hiển thị web và xuất Word/Excel/PDF.</summary>
public sealed class ReportDocument
{
    public ReportKind Kind { get; init; }
    public string Organization { get; init; } = "TỔNG CÔNG TY THIẾT BỊ ĐIỆN ĐÔNG ANH – CTCP";
    public string Unit { get; init; } = "";
    public required string Title { get; init; }
    public required string Period { get; init; }
    public string PlaceDate { get; init; } = "";
    public string Author { get; init; } = "";
    public DateTime GeneratedAt { get; init; }
    public List<ReportSection> Sections { get; } = new();

    public string FileBaseName { get; init; } = "BaoCao";
}

/// <summary>Nội dung người dùng tự nhập kèm báo cáo (hệ thống không tự suy diễn).</summary>
public sealed class ReportNotes
{
    public string? Difficulties { get; set; }
    public string? Proposals { get; set; }
}
