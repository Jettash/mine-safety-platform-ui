using MySqlConnector;
namespace MineSafety.Web.Tests;

public sealed class Fixture : IAsyncDisposable
{
    public string Root
    {
        get;
    }
    public string ReaderConnection { get; private set; } = "";
    public string WriterConnection { get; private set; } = "";
    public DateTimeOffset Now { get; } = new(2026, 11, 9, 12, 0, 0, TimeSpan.FromHours(8));
    private readonly string database = "web_acceptance_" + Guid.NewGuid().ToString("N")[..12];
    private readonly string reader = "web_reader_" + Guid.NewGuid().ToString("N")[..10];
    private readonly string rootConnection;
    public Fixture(string root, string connection)
    {
        Root = root;
        Directory.CreateDirectory(root);
        rootConnection = connection;
    }
    public async Task Initialize()
    {
        await using var c = new MySqlConnection(rootConnection);
        await c.OpenAsync();
        await new MySqlCommand("CREATE DATABASE `" + database + "` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci", c).ExecuteNonQueryAsync();
        var writer = new MySqlConnectionStringBuilder(rootConnection) { Database = database, Pooling = true };
        WriterConnection = writer.ConnectionString;
        await using var w = new MySqlConnection(WriterConnection);
        await w.OpenAsync();
        await new MySqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "mysql-schema.sql")), w).ExecuteNonQueryAsync();
        var password = Guid.NewGuid().ToString("N");
        await new MySqlCommand($"CREATE USER '{reader}'@'127.0.0.1' IDENTIFIED BY '{password}'; GRANT SELECT ON `{database}`.* TO '{reader}'@'127.0.0.1'", c).ExecuteNonQueryAsync();
        ReaderConnection = new MySqlConnectionStringBuilder(writer.ConnectionString) { UserID = reader, Password = password }.ConnectionString;
        // History keeps original name even after the mutable vehicle directory was renamed.
        Execute("INSERT INTO vehicles(vehicle_id,name,updated_at_ms) VALUES ('Truck-A','改名后的一号车',0),('truck-a','小写编号车辆',0)");
        for (int i = 0; i < 520; i++)
            Insert("BULK-" + i.ToString("D4"), "Bulk-" + i, 2, at: Now.AddMinutes(-i - 30));
        Insert("VALID-ZERO", "Truck-A", 0, name: "一号车（历史名称）", at: Now);
        Insert("INVALID-MATCHED", "Truck-A", null, reason: "TEMPLATE_NOT_FOUND", at: Now.AddSeconds(-1));
        Insert("UNMATCHED", null, null, match: "NO_VALID_RTK", reason: "NO_VALID_RTK", at: Now.AddSeconds(-2));
        Insert("METRICS", "truck-a", 12.5, name: "小写编号车辆", metrics: true, at: Now.AddSeconds(-3));
        Insert("LOW-METRICS", "LOW", 3, metrics: true, at: Now.AddSeconds(-4));
        Execute("UPDATE measurements SET registration_fitness=0.01,cloud_coverage=0.02 WHERE event_id='LOW-METRICS'");
        Insert("ESCAPE", "literal%_!· space", 1, at: Now.AddSeconds(-5));
        Insert("XSS", "<img src=x onerror=alert(1)>", 1, name: "=SUM(1,2)", at: Now.AddSeconds(-6));
        Insert("SOURCE-TEST", "excluded", 99, source: "TEST");
        Insert("SOURCE-DEMO", "excluded", 99, source: "DEMO");
        Insert("SOURCE-LEGACY", "excluded", 99, source: "LEGACY_IMPORT");
        Insert("END-MINUTE", "boundary", 4, at: Now.Date.AddDays(1).AddMilliseconds(-1));
        Insert("NEXT-DAY", "boundary", 5, at: new DateTimeOffset(Now.Date.AddDays(1), Now.Offset));
        Insert("PREVIOUS-DAY", "boundary", 6, at: new DateTimeOffset(Now.Date.AddMilliseconds(-1), Now.Offset));
        Insert("LAST-MONTH", "boundary", 7, at: new DateTimeOffset(2026, 10, 31, 23, 59, 59, Now.Offset));
    }
    public void Insert(string id, string? vehicle, double? volume, string name = "", string source = "RTK_LIDAR",
        string match = "MATCHED", string reason = "OK", bool metrics = false, DateTimeOffset? at = null)
    {
        using var c = new MySqlConnection(WriterConnection);
        c.Open();
        using var cmd = new MySqlCommand("""
            INSERT INTO measurements(event_id,vehicle_id,vehicle_name,started_at_ms,ended_at_ms,match_status,
            volume_valid,volume_m3,speed_mps,reason,scan_file,result_file,source,template_id,template_name,
            registration_rmse_m,registration_fitness,cloud_coverage,algorithm_version,latitude,longitude,accuracy_m,gps_time_ms)
            VALUES(@id,@vehicle,@name,@time,@time+3000,@match,@valid,@volume,2.5,@reason,'scans.csv','result.json',@source,
            @template,@templateName,@rmse,0,0,'orthogonal-motion-cloud-grid-v2',39.1234567,111.1234567,0.02,@time)
            """, c);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@vehicle", (object?)vehicle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@time", (at ?? Now.AddSeconds(-10)).ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("@match", match);
        cmd.Parameters.AddWithValue("@valid", volume is not null);
        cmd.Parameters.AddWithValue("@volume", (object?)volume ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reason", reason);
        cmd.Parameters.AddWithValue("@source", source);
        cmd.Parameters.AddWithValue("@template", metrics ? "template-001" : DBNull.Value);
        cmd.Parameters.AddWithValue("@templateName", metrics ? "空车标定模板" : DBNull.Value);
        cmd.Parameters.AddWithValue("@rmse", metrics ? 0.012 : DBNull.Value);
        cmd.ExecuteNonQuery();
        if (metrics)
            Execute("UPDATE measurements SET registration_fitness=0.92,cloud_coverage=0.85 WHERE event_id=@id", ("@id", id));
    }
    public void Execute(string sql, params (string Name, object Value)[] args)
    {
        using var c = new MySqlConnection(WriterConnection);
        c.Open();
        using var cmd = new MySqlCommand(sql, c);
        foreach (var p in args)
            cmd.Parameters.AddWithValue(p.Name, p.Value);
        cmd.ExecuteNonQuery();
    }
    public async ValueTask DisposeAsync()
    {
        MySqlConnection.ClearAllPools();
        await using var c = new MySqlConnection(rootConnection);
        await c.OpenAsync();
        await new MySqlCommand($"DROP DATABASE IF EXISTS `{database}`; DROP USER IF EXISTS '{reader}'@'127.0.0.1'", c).ExecuteNonQueryAsync();
    }
}

