using ClosedXML.Excel;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Text;

namespace EEMC.WM.Data.Services;

/// <summary>Xuất danh sách công việc ra Excel (.xlsx).</summary>
public sealed class ExcelExportService
{
    public Task ExportTasksAsync(IReadOnlyList<WorkTask> tasks, string path, DateTime now, string title) => Task.Run(() =>
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Danh sách công việc");

        ws.Cell(1, 1).Value = "TỔNG CÔNG TY THIẾT BỊ ĐIỆN ĐÔNG ANH – CTCP";
        ws.Cell(2, 1).Value = title.ToUpperInvariant();
        ws.Cell(3, 1).Value = $"Thời điểm xuất: {Labels.DateTime(now)} — Tổng số: {tasks.Count} công việc";
        ws.Range(1, 1, 1, 6).Merge().Style.Font.SetBold();
        ws.Range(2, 1, 2, 14).Merge().Style.Font.SetBold().Font.SetFontSize(14).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Range(3, 1, 3, 14).Merge().Style.Font.SetItalic().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        string[] headers =
        {
            "STT", "Mã CV", "Nội dung công việc", "Ngày giờ nhận", "Hạn hoàn thành", "Thời gian còn lại", "Ưu tiên",
            "Trạng thái", "Tiến độ (%)", "Nhóm công việc", "Đề tài/Dự án", "Người giao", "Đơn vị phối hợp", "Ghi chú"
        };
        const int headerRow = 5;
        for (int i = 0; i < headers.Length; i++) ws.Cell(headerRow, i + 1).Value = headers[i];
        var header = ws.Range(headerRow, 1, headerRow, headers.Length);
        header.Style.Font.SetBold().Font.SetFontColor(XLColor.White)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#1F4E79"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
            .Alignment.SetWrapText();

        int r = headerRow + 1;
        int stt = 1;
        foreach (var t in tasks)
        {
            ws.Cell(r, 1).Value = stt++;
            ws.Cell(r, 2).Value = t.Code;
            ws.Cell(r, 3).Value = t.Title;
            ws.Cell(r, 4).Value = t.ReceivedAt;
            ws.Cell(r, 5).Value = t.DueAt;
            ws.Cell(r, 6).Value = StatusGroups.IsClosed(t.Status) ? "" : Labels.TimeRemaining(t.DueAt, now);
            ws.Cell(r, 7).Value = Labels.Of(t.Priority);
            ws.Cell(r, 8).Value = Labels.Of(t.Status);
            ws.Cell(r, 9).Value = t.Progress;
            ws.Cell(r, 10).Value = t.Category?.DisplayName ?? "";
            ws.Cell(r, 11).Value = t.ProjectName ?? "";
            ws.Cell(r, 12).Value = t.AssignedBy ?? "";
            ws.Cell(r, 13).Value = t.Coordinator ?? "";
            ws.Cell(r, 14).Value = t.Notes ?? "";
            if (!StatusGroups.IsClosed(t.Status) && t.DueAt < now)
                ws.Range(r, 1, r, headers.Length).Style.Font.SetFontColor(XLColor.FromHtml("#C0392B"));
            r++;
        }

        ws.Range(headerRow + 1, 4, Math.Max(headerRow + 1, r - 1), 5).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
        var table = ws.Range(headerRow, 1, Math.Max(headerRow, r - 1), headers.Length);
        table.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorder(XLBorderStyleValues.Thin);
        table.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
        ws.Column(1).Width = 6;
        ws.Column(2).Width = 16;
        ws.Column(3).Width = 60;
        ws.Column(3).Style.Alignment.SetWrapText();
        ws.Columns(4, 5).Width = 17;
        ws.Column(6).Width = 20;
        ws.Columns(7, 9).Width = 14;
        ws.Columns(10, 13).Width = 26;
        ws.Column(14).Width = 40;
        ws.SheetView.FreezeRows(headerRow);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
        wb.SaveAs(path);
    });
}
