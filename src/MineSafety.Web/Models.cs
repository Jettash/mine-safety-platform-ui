using System.Globalization;
namespace MineSafety.Web;

public sealed class WebOptions
{
    public string StateDirectory { get; set; } = "private";
    public string MySqlConnectionFile { get; set; } = "mysql.connection.dpapi";
    // Only for process-level secrets and isolated automated tests; not appsettings.json.
    public string MySqlConnectionString { get; set; } = "";
    public int SnapshotMinutes { get; set; } = 15;
    public int MaxQueryRecords { get; set; } = 100000;
    public int MaxCachedRecords { get; set; } = 200000;
    public int DatabaseTimeoutSeconds { get; set; } = 5;
    public void Normalize(string baseDirectory)
    {
        StateDirectory = Path.GetFullPath(StateDirectory, baseDirectory);
        MySqlConnectionFile = Path.GetFullPath(MySqlConnectionFile, StateDirectory);
        var root = Path.Combine(baseDirectory, "wwwroot");
        static bool Within(string p, string r) => p.Equals(r, StringComparison.OrdinalIgnoreCase) || p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (Within(StateDirectory, root) || Within(MySqlConnectionFile, root))
            throw new InvalidOperationException("私有配置不能放在 wwwroot 内。");
        if (SnapshotMinutes is < 1 or > 1440 || MaxQueryRecords is < 1 or > 100000 || MaxCachedRecords < MaxQueryRecords || MaxCachedRecords > 200000 || DatabaseTimeoutSeconds is < 1 or > 60)
            throw new InvalidOperationException("网页服务配置数值无效。");
    }
}
public sealed class ApiError(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status; public string Code { get; } = code;
}
public sealed record QueryRequest(string? Start, string? End, string? Vehicle);
public sealed record LoginRequest(string? Username, string? Password);
public sealed record SetupRequest(string? AdminPassword, string? ViewerPassword);
public sealed record QueryScope(string Start, string End, string Vehicle, long FromMs, long UntilExclusiveMs)
{
    public static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);
    public static QueryScope Parse(QueryRequest request)
    {
        const string format = "yyyy-MM-dd'T'HH:mm";
        if (!DateTime.TryParseExact(request.Start, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
           !DateTime.TryParseExact(request.End, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            throw new ApiError(400, "INVALID_TIME", "请选择有效的开始时间和结束时间。");
        if (start > end)
            throw new ApiError(400, "INVALID_TIME", "开始时间不能晚于结束时间。");
        var vehicle = (request.Vehicle ?? "").Trim();
        if (vehicle.Length > 128)
            throw new ApiError(400, "INVALID_VEHICLE", "车辆查询内容过长。");
        try
        {
            return new(request.Start!, request.End!, vehicle, new DateTimeOffset(start, ChinaOffset).ToUnixTimeMilliseconds(),
            new DateTimeOffset(end.AddMinutes(1), ChinaOffset).ToUnixTimeMilliseconds());
        }
        catch (ArgumentOutOfRangeException) { throw new ApiError(400, "INVALID_TIME", "查询时间超出支持范围。"); }
    }
    public static QueryScope Today(DateTimeOffset now)
    {
        var day = now.ToOffset(ChinaOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Parse(new(day + "T00:00", day + "T23:59", ""));
    }
}
public sealed record RecordRow(string EventId, string Time, string EndTime, string? VehicleId, string VehicleName,
    string MatchStatus, bool VolumeValid, decimal? Volume, string Reason, double? SpeedMps, string CalibrationId,
    string? TemplateId, string? TemplateName, double? RegistrationRmseM, double? RegistrationFitness, double? CloudCoverage,
    string? AlgorithmVersion, double? Latitude, double? Longitude, double? AccuracyM, string? GpsTime, string Source)
{
    public string Status => VolumeValid ? "valid" : "invalid";
    public string MatchLabel => MatchStatus switch
    {
        "MATCHED" => "已匹配",
        "NO_VALID_RTK" => "无有效 RTK",
        "MULTIPLE_VEHICLES" => "多车候选",
        "LEGACY_IMPORT" => "历史导入",
        _ => MatchStatus
    };
}
public sealed record RecordSummary(long Total, long Valid, long Invalid, decimal Volume)
{
    public static RecordSummary From(IReadOnlyCollection<RecordRow> rows) => new(rows.Count, rows.LongCount(r => r.VolumeValid),
        rows.LongCount(r => !r.VolumeValid), rows.Sum(r => r.Volume ?? 0));
}
public sealed record HomeData(RecordSummary Summary, IReadOnlyList<RecordRow> Items);
public sealed record ReportRow(string Period, long Total, long Valid, long Invalid, decimal Volume);

