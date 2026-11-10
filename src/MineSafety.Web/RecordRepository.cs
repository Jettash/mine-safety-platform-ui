using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MySqlConnector;
namespace MineSafety.Web;

// All queries run in READ ONLY transactions. Deployed account also only has SELECT.
// This service never creates databases/tables or runs C++ migrations/recovery.
public sealed class RecordRepository(WebOptions options)
{
    private const string Columns = """
        m.event_id,m.started_at_ms,m.ended_at_ms,m.vehicle_id,m.vehicle_name,m.match_status,
        m.volume_valid,m.volume_m3,m.reason,m.speed_mps,m.calibration_id,m.template_id,m.template_name,
        m.registration_rmse_m,m.registration_fitness,m.cloud_coverage,m.algorithm_version,
        m.latitude,m.longitude,m.accuracy_m,m.gps_time_ms,m.source
        """;
    private const string Where = "m.source='RTK_LIDAR' AND m.started_at_ms>=@from AND m.started_at_ms<@until";
    private string ConnectionString()
    {
        string value;
        if (!string.IsNullOrWhiteSpace(options.MySqlConnectionString))
            value = options.MySqlConnectionString;
        else
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException();
                var bytes = ProtectedData.Unprotect(File.ReadAllBytes(options.MySqlConnectionFile),
                    Encoding.UTF8.GetBytes("MineSafety.Web.MySql.v1"), DataProtectionScope.CurrentUser);
                try
                {
                    value = Encoding.UTF8.GetString(bytes);
                }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or PlatformNotSupportedException)
            {
                throw new ApiError(503, "DATABASE_CONFIG_REQUIRED", "MySQL 连接配置缺失或无法解密，请在服务器运行数据库配置脚本。");
            }
        }
        try
        {
            var settings = new MySqlConnectionStringBuilder(value)
            {
                ConnectionTimeout = (uint)options.DatabaseTimeoutSeconds,
                DefaultCommandTimeout = (uint)options.DatabaseTimeoutSeconds,
                PersistSecurityInfo = false,
                AllowLoadLocalInfile = false,
                MaximumPoolSize = 10
            };
            if (string.IsNullOrWhiteSpace(settings.Database))
                throw new ArgumentException();
            return settings.ConnectionString;
        }
        catch (ArgumentException) { throw new ApiError(503, "DATABASE_CONFIG_INVALID", "MySQL 连接配置无效，请重新配置。"); }
    }
    private static void Bounds(MySqlCommand command, QueryScope scope)
    {
        command.Parameters.AddWithValue("@from", scope.FromMs);
        command.Parameters.AddWithValue("@until", scope.UntilExclusiveMs);
    }
    public async Task<IReadOnlyList<RecordRow>> Read(QueryScope scope, CancellationToken cancel = default)
    {
        await using var connection = new MySqlConnection(ConnectionString());
        await connection.OpenAsync(cancel);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, true, cancel);
        var filter = scope.Vehicle.Length == 0 ? "" : """
            AND (m.vehicle_id LIKE @vehicle ESCAPE '!' OR m.vehicle_name LIKE @vehicle ESCAPE '!'
            OR EXISTS (SELECT 1 FROM vehicles v WHERE v.vehicle_id=m.vehicle_id AND v.name LIKE @vehicle ESCAPE '!'))
            """;
        await using var command = new MySqlCommand($"SELECT {Columns} FROM measurements m WHERE {Where} {filter} ORDER BY m.started_at_ms DESC,m.event_id DESC LIMIT @limit", connection, tx);
        Bounds(command, scope);
        command.Parameters.AddWithValue("@limit", options.MaxQueryRecords + 1);
        if (filter.Length > 0)
            command.Parameters.AddWithValue("@vehicle", "%" + scope.Vehicle.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_") + "%");
        var rows = await Rows(command, cancel);
        if (rows.Count > options.MaxQueryRecords)
            throw new ApiError(400, "QUERY_TOO_LARGE", $"本次查询超过 {options.MaxQueryRecords:N0} 条，请缩小时间或车辆范围。");
        await tx.CommitAsync(cancel);
        return rows.AsReadOnly();
    }
    public async Task<HomeData> Home(QueryScope scope, CancellationToken cancel = default)
    {
        await using var connection = new MySqlConnection(ConnectionString());
        await connection.OpenAsync(cancel);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, true, cancel);
        const string valid = "m.volume_valid=1 AND m.volume_m3 IS NOT NULL AND m.volume_m3>=0";
        await using var summaryCommand = new MySqlCommand($"SELECT COUNT(*),COALESCE(SUM({valid}),0),COALESCE(SUM(CASE WHEN {valid} THEN m.volume_m3 ELSE 0 END),0) FROM measurements m WHERE {Where}", connection, tx);
        Bounds(summaryCommand, scope);
        RecordSummary summary;
        await using (var r = await summaryCommand.ExecuteReaderAsync(cancel))
        {
            await r.ReadAsync(cancel);
            var total = r.GetInt64(0);
            var count = r.GetInt64(1);
            summary = new(total, count, total - count, CheckedVolume(r.GetDouble(2)));
        }
        await using var recent = new MySqlCommand($"SELECT {Columns} FROM measurements m WHERE {Where} ORDER BY m.started_at_ms DESC,m.event_id DESC LIMIT 5", connection, tx);
        Bounds(recent, scope);
        var rows = await Rows(recent, cancel);
        await tx.CommitAsync(cancel);
        return new(summary, rows.AsReadOnly());
    }
    private static decimal CheckedVolume(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value >= (double)decimal.MaxValue)
            throw new ApiError(503, "DATABASE_VALUE_INVALID", "数据库中的体积数值异常，请联系测量端维护人员。");
        return (decimal)value;
    }
    private static string Time(long ms)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToOffset(QueryScope.ChinaOffset).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException) { throw new ApiError(503, "DATABASE_VALUE_INVALID", "数据库中的时间数值异常，请联系测量端维护人员。"); }
    }
    private static async Task<List<RecordRow>> Rows(MySqlCommand command, CancellationToken cancel)
    {
        var rows = new List<RecordRow>();
        await using var r = await command.ExecuteReaderAsync(cancel);
        while (await r.ReadAsync(cancel))
        {
            string? Text(int n) => r.IsDBNull(n) ? null : r.GetString(n);
            double? Number(int n) => r.IsDBNull(n) ? null : r.GetDouble(n);
            var raw = Number(7);
            var valid = r.GetBoolean(6) && raw is >= 0;
            var volume = valid ? CheckedVolume(raw!.Value) : (decimal?)null;
            var rmse = Number(13);
            var registered = rmse is >= 0;
            rows.Add(new(r.GetString(0), Time(r.GetInt64(1)), Time(r.GetInt64(2)), Text(3), r.GetString(4),
                r.GetString(5), valid, volume, r.GetString(8), Number(9), r.GetString(10), Text(11), Text(12),
                registered ? rmse : null, registered ? Number(14) : null, registered ? Number(15) : null, Text(16),
                Number(17), Number(18), Number(19), r.IsDBNull(20) ? null : Time(r.GetInt64(20)), r.GetString(21)));
        }
        return rows;
    }
}

