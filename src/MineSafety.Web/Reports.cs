using System.Globalization;
using ClosedXML.Excel;
namespace MineSafety.Web;

public static class Reports
{
    private static readonly SemaphoreSlim ExportGate = new(1, 1);
    public static IReadOnlyList<ReportRow> Group(SavedQuery q, string grain)
    {
        if (grain is not ("day" or "week" or "month"))
            throw new ApiError(400, "INVALID_GRAIN", "统计粒度无效。");
        return q.Records.GroupBy(r => Period(r.Time, grain)).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => { var s = RecordSummary.From(g.ToArray()); return new ReportRow(g.Key, s.Total, s.Valid, s.Invalid, s.Volume); }).ToArray();
    }
    public static string Period(string time, string grain)
    {
        var d = DateOnly.ParseExact(time[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var monday = d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        return grain switch
        {
            "day" => d.ToString("yyyy-MM-dd"),
            "month" => d.ToString("yyyy-MM"),
            "week" => monday.ToString("yyyy-MM-dd") + " 至 " + monday.AddDays(6).ToString("yyyy-MM-dd"),
            _ => throw new ApiError(400, "INVALID_GRAIN", "统计粒度无效。")
        };
    }
    public static async Task<byte[]> Excel(SavedQuery q, string grain, bool includeRecords, CancellationToken cancel)
    {
        var groups = Group(q, grain);
        if (!await ExportGate.WaitAsync(0, cancel))
            throw new ApiError(429, "EXPORT_BUSY", "已有报表正在生成，请稍后重试。");
        try
        {
            using var wb = new XLWorkbook();
            var sheet = wb.AddWorksheet("统计报表");
            sheet.Cell(1, 1).Value = "魏家峁矿车辆载量查询报表";
            sheet.Cell(2, 1).Value = $"查询范围：{q.Scope.Start.Replace('T', ' ')} 至 {q.Scope.End.Replace('T', ' ')}（北京时间，包含结束分钟）";
            sheet.Cell(3, 1).Value = "车辆条件：" + (q.Scope.Vehicle.Length == 0 ? "全部车辆" : q.Scope.Vehicle);
            Header(sheet, 5, ["统计时间", "总车次", "有效测量", "无效测量", "有效体积（m³）"]);
            int row = 6;
            foreach (var g in groups)
            {
                sheet.Cell(row, 1).Value = g.Period;
                Summary(sheet, row, g.Total, g.Valid, g.Invalid, g.Volume);
                row++;
            }
            sheet.Cell(row, 1).Value = "合计";
            var s = q.Summary;
            Summary(sheet, row, s.Total, s.Valid, s.Invalid, s.Volume);
            sheet.Column(1).Width = 29;
            sheet.Columns(2, 4).Width = 16;
            sheet.Column(5).Width = 24;
            sheet.Column(5).Style.NumberFormat.Format = "0.00";
            sheet.SheetView.FreezeRows(5);
            if (includeRecords)
            {
                var d = wb.AddWorksheet("车辆明细");
                Header(d, 1, ["开始时间（北京时间）", "结束时间（北京时间）", "车辆编号", "车辆名称（记录时）", "测量结果", "有效体积（m³）", "RTK匹配", "原始原因", "速度（m/s）", "模板编号", "模板名称", "配准RMSE（m）", "配准匹配率", "点云覆盖率", "算法版本", "纬度", "经度", "RTK精度（m）", "RTK时间（北京时间）", "标定编号", "事件编号", "数据来源"]);
                row = 2;
                foreach (var r in q.Records)
                {
                    cancel.ThrowIfCancellationRequested();
                    string[] texts = [r.Time, r.EndTime, r.VehicleId ?? "", r.VehicleName, r.VolumeValid ? "有效" : "无效", "", r.MatchLabel, r.Reason, "", r.TemplateId ?? "", r.TemplateName ?? "", "", "", "", r.AlgorithmVersion ?? "", "", "", "", r.GpsTime ?? "", r.CalibrationId, r.EventId, r.Source];
                    for (int c = 0; c < texts.Length; c++)
                        d.Cell(row, c + 1).Value = texts[c];
                    if (r.Volume is not null)
                        d.Cell(row, 6).Value = (double)r.Volume.Value;
                    Number(d, row, 9, r.SpeedMps);
                    Number(d, row, 12, r.RegistrationRmseM);
                    Number(d, row, 13, r.RegistrationFitness);
                    Number(d, row, 14, r.CloudCoverage);
                    Number(d, row, 16, r.Latitude);
                    Number(d, row, 17, r.Longitude);
                    Number(d, row, 18, r.AccuracyM);
                    row++;
                }
                d.Columns().Width = 22;
                d.Column(8).Width = 52;
                d.Column(21).Width = 38;
                d.Columns(1, 2).Width = 28;
                d.Column(19).Width = 28;
                d.Column(6).Style.NumberFormat.Format = "0.00";
                d.Columns(13, 14).Style.NumberFormat.Format = "0.00%";
                d.SheetView.FreezeRows(1);
            }
            using var buffer = new MemoryStream();
            wb.SaveAs(buffer);
            return buffer.ToArray();
        }
        finally { ExportGate.Release(); }
    }
    private static void Number(IXLWorksheet s, int row, int col, double? v)
    {
        if (v is not null)
            s.Cell(row, col).Value = v.Value;
    }
    private static void Summary(IXLWorksheet s, int row, long total, long valid, long invalid, decimal volume)
    {
        s.Cell(row, 2).Value = total;
        s.Cell(row, 3).Value = valid;
        s.Cell(row, 4).Value = invalid;
        s.Cell(row, 5).Value = (double)volume;
    }
    private static void Header(IXLWorksheet s, int row, IReadOnlyList<string> headers)
    {
        for (int c = 0; c < headers.Count; c++)
            s.Cell(row, c + 1).Value = headers[c];
        var range = s.Range(row, 1, row, headers.Count);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#E7F2F3");
    }
}

