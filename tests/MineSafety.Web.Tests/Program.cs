using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using MineSafety.Web;
using MineSafety.Web.Tests;
using MySqlConnector;

var output = Path.GetFullPath(Value("--output") ?? Path.Combine(Environment.CurrentDirectory, ".verification", "tests"));
Directory.CreateDirectory(output);
var checks = new List<string>();
await using var sandbox = await MySqlSandbox.Start(output);
await using var fixture = new Fixture(Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N")[..8]), sandbox.ConnectionString);
await fixture.Initialize();
var clock = new TestClock(fixture.Now);
await using var app = AppHost.Build(["--urls","http://127.0.0.1:0","--Web:MySqlConnectionString",fixture.ReaderConnection,
    "--Web:StateDirectory",Path.Combine(fixture.Root,"accounts"),"--Logging:LogLevel:Default","Warning"], clock);
await app.StartAsync();
var address = app.Urls.Single();
using var client = Client();
try
{
    Check((await client.GetAsync("/api/home")).StatusCode == HttpStatusCode.Unauthorized, "anonymous data denied");
    Check((await client.PostAsJsonAsync("/api/queries", new
    {
    })).StatusCode == HttpStatusCode.Unauthorized, "anonymous queries denied");
    Check((await client.GetAsync("/private/accounts.json")).StatusCode == HttpStatusCode.NotFound, "private files not served");
    var info = await Get("/api/setup-info");
    using (var setup = new HttpRequestMessage(HttpMethod.Post, "/api/setup"))
    {
        setup.Headers.Add("X-Setup-Token", info.GetProperty("setupToken").GetString());
        setup.Content = JsonContent.Create(new
        {
            adminPassword = "test-admin-2026",
            viewerPassword = "test-viewer-2026"
        });
        Check((await client.SendAsync(setup)).IsSuccessStatusCode, "local account setup");
    }
    var accountText = await File.ReadAllTextAsync(Path.Combine(fixture.Root, "accounts", "accounts.json"));
    Check(!accountText.Contains("test-admin-2026") && !accountText.Contains("test-viewer-2026"), "passwords stored as hashes");
    Check((await client.PostAsJsonAsync("/api/login", new
    {
        username = "admin",
        password = "wrong"
    })).StatusCode == HttpStatusCode.Unauthorized, "bad password denied");
    Check((await client.PostAsJsonAsync("/api/login", new
    {
        username = "admin",
        password = "test-admin-2026"
    })).IsSuccessStatusCode, "admin login");

    var home = await Get("/api/home");
    var initial = home.GetProperty("summary").GetProperty("total").GetInt32();
    Check(initial == 528, "home counts all 528 rows, beyond C++ desktop 500 limit");
    Check(home.GetProperty("items").GetArrayLength() == 5, "latest five only");
    Check(home.GetProperty("summary").GetProperty("valid").GetInt32() == 526, "valid zero included; invalid excluded");
    Check(home.GetProperty("summary").GetProperty("volume").GetDecimal() == 1061.5m, "whole-day volume total");
    var query = await Query();
    var id = query.GetProperty("snapshotId").GetString()!;
    Check(query.GetProperty("totalCount").GetInt32() == 528 && query.GetProperty("items").GetArrayLength() == 20, "full snapshot with paged rows");
    var summary = query.GetProperty("summary").GetRawText();
    var second = await Get($"/api/queries/{id}?page=2&pageSize=10");
    Check(second.GetProperty("summary").GetRawText() == summary && second.GetProperty("page").GetInt32() == 2, "paging keeps whole-range summary");
    var zero = await Get($"/api/queries/{id}/records/VALID-ZERO");
    Check(zero.GetProperty("volumeValid").GetBoolean() && zero.GetProperty("volume").GetDecimal() == 0, "valid zero rendered as numeric zero");
    Check(zero.GetProperty("vehicleName").GetString() == "一号车（历史名称）", "historical vehicle name retained after rename");
    var invalid = await Get($"/api/queries/{id}/records/INVALID-MATCHED");
    Check(!invalid.GetProperty("volumeValid").GetBoolean() && invalid.GetProperty("volume").ValueKind == JsonValueKind.Null
        && invalid.GetProperty("matchStatus").GetString() == "MATCHED", "RTK match is not volume validity");
    Check(invalid.GetProperty("registrationFitness").ValueKind == JsonValueKind.Null, "uncomputed metrics hidden despite stored default zeros");
    var metrics = await Get($"/api/queries/{id}/records/METRICS");
    Check(metrics.GetProperty("registrationRmseM").GetDouble() == 0.012 && metrics.GetProperty("cloudCoverage").GetDouble() == 0.85, "template registration metrics mapped");
    Check(metrics.GetProperty("latitude").GetDouble() == 39.1234567 && metrics.GetProperty("gpsTime").ValueKind == JsonValueKind.String, "history RTK snapshot mapped");
    var low = await Get($"/api/queries/{id}/records/LOW-METRICS");
    Check(low.GetProperty("volumeValid").GetBoolean(), "web does not independently reject low metrics");
    Check((await Query(vehicle: "Truck-A")).GetProperty("totalCount").GetInt32() == 2, "vehicle identifiers remain case sensitive");
    Check((await Query(vehicle: "truck-a")).GetProperty("totalCount").GetInt32() == 1, "lowercase ID distinct");
    var alias = await Query(vehicle: "改名后");
    Check(alias.GetProperty("totalCount").GetInt32() == 2 && alias.GetProperty("items")[0].GetProperty("vehicleName").GetString() == "一号车（历史名称）", "search current alias preserves historical display");
    Check((await Query(vehicle: "%_!· space")).GetProperty("totalCount").GetInt32() == 1, "LIKE wildcards and literal dot/space preserved");
    Check((await Query(vehicle: "' OR 1=1 --")).GetProperty("totalCount").GetInt32() == 0, "parameterized injection is literal");
    Check((await Query("2026-11-09T23:59", "2026-11-09T23:59")).GetProperty("totalCount").GetInt32() == 1, "end minute includes final millisecond");
    Check((await Query("2026-11-10T00:00", "2026-11-10T00:00")).GetProperty("totalCount").GetInt32() == 1, "next midnight in next day");
    Check(Reports.Period("2026-11-08 23:59:59", "week") == "2026-11-02 至 2026-11-08", "week starts Monday across Sunday");
    Check(Reports.Period("2026-10-31 23:59:59", "month") == "2026-10", "month boundary");
    Check((await Query(vehicle: "no-such-vehicle")).GetProperty("summary").GetProperty("volume").GetDecimal() == 0, "empty results are zero");
    Check((await client.PostAsJsonAsync("/api/queries", new
    {
        start = "bad",
        end = "bad",
        vehicle = ""
    })).StatusCode == HttpStatusCode.BadRequest, "invalid time rejected");
    Check((await client.GetAsync($"/api/queries/{id}?pageSize=100")).StatusCode == HttpStatusCode.BadRequest, "unsupported page size rejected");
    fixture.Insert("AFTER-SNAPSHOT", "NEW-VEHICLE", 8);
    fixture.Execute("UPDATE measurements SET volume_m3=50 WHERE event_id='METRICS'");
    fixture.Execute("DELETE FROM vehicles WHERE vehicle_id='Truck-A'");
    Check((await Get($"/api/queries/{id}")).GetProperty("summary").GetRawText() == summary, "snapshot freezes inserts updates and directory deletion");
    Check((await Get($"/api/queries/{id}/records/METRICS")).GetProperty("volume").GetDecimal() == 12.5m, "details frozen");
    Check((await Get("/api/home")).GetProperty("summary").GetProperty("total").GetInt32() == 529, "home follows new committed records");
    var report = await Get($"/api/queries/{id}/report?grain=week");
    Check(report.GetProperty("rows")[0].GetProperty("total").GetInt32() == 528, "weekly report covers full frozen result");
    var export = await client.GetByteArrayAsync($"/api/queries/{id}/export.xlsx?grain=day&includeRecords=true");
    await File.WriteAllBytesAsync(Path.Combine(output, "acceptance.xlsx"), export);
    using (var wb = new XLWorkbook(new MemoryStream(export)))
    {
        var d = wb.Worksheet("车辆明细");
        Check(d.LastRowUsed()!.RowNumber() == 529, "Excel contains every frozen row");
        var cells = d.RowsUsed().Skip(1).ToArray();
        var z = cells.Single(r => r.Cell(21).GetString() == "VALID-ZERO");
        Check(z.Cell(6).DataType == XLDataType.Number && z.Cell(6).GetDouble() == 0, "Excel valid zero is numeric");
        Check(cells.Single(r => r.Cell(21).GetString() == "INVALID-MATCHED").Cell(6).IsEmpty(), "Excel invalid volume blank");
        Check(cells.Single(r => r.Cell(21).GetString() == "METRICS").Cell(6).GetDouble() == 12.5, "Excel snapshot matches details");
        Check(cells.Single(r => r.Cell(21).GetString() == "XSS").Cell(4).GetString() == "=SUM(1,2)" && !cells.Single(r => r.Cell(21).GetString() == "XSS").Cell(4).HasFormula, "Excel user text not formula");
        Check(wb.Worksheet("统计报表").Cell(7, 2).GetDouble() == 528 && wb.Worksheet("统计报表").Cell(7, 5).GetDouble() == 1061.5, "Excel totals match snapshot");
    }
    using (var other = Client())
    {
        await other.PostAsJsonAsync("/api/login", new
        {
            username = "viewer",
            password = "test-viewer-2026"
        });
        Check((await other.GetAsync($"/api/queries/{id}")).StatusCode == HttpStatusCode.Gone, "snapshot tied to login session");
        Check((await other.GetAsync("/api/home")).IsSuccessStatusCode, "viewer can query");
    }
    using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/logout"))
    {
        request.Headers.Add("Origin", "http://elsewhere.invalid");
        Check((await client.SendAsync(request)).StatusCode == HttpStatusCode.Forbidden, "foreign origin POST rejected");
    }
    var options = new WebOptions { MySqlConnectionString = fixture.ReaderConnection, StateDirectory = Path.Combine(fixture.Root, "small"), MaxQueryRecords = 10 };
    options.Normalize(AppContext.BaseDirectory);
    var repo = new RecordRepository(options);
    await Expect("QUERY_TOO_LARGE", () => repo.Read(QueryScope.Today(fixture.Now)), "query limit fails explicitly, no silent truncation");
    Check((await repo.Home(QueryScope.Today(fixture.Now))).Summary.Total == 529, "home aggregate independent of snapshot cap");
    var smallClock = new TestClock(fixture.Now);
    var cacheOptions = new WebOptions { MaxQueryRecords = 1, MaxCachedRecords = 1, SnapshotMinutes = 1 };
    var cache = new QuerySnapshots(cacheOptions, smallClock);
    var scope = QueryScope.Today(fixture.Now);
    var saved = cache.Add("owner", scope, Array.Empty<RecordRow>());
    try
    {
        cache.Add("owner", scope, Array.Empty<RecordRow>());
        throw new Exception("No cache limit");
    }
    catch (ApiError e) when (e.Code == "QUERY_CACHE_FULL") { Check(true, "empty snapshots bounded by cache budget"); }
    smallClock.Advance(TimeSpan.FromMinutes(2));
    try
    {
        cache.Get(saved.Id, "owner");
        throw new Exception("Not expired");
    }
    catch (ApiError e) when (e.Code == "QUERY_EXPIRED") { Check(true, "cache expires after TTL"); }
    await using (var readOnly = new MySqlConnection(fixture.ReaderConnection))
    {
        await readOnly.OpenAsync();
        try
        {
            await new MySqlCommand("DELETE FROM measurements WHERE event_id='never'", readOnly).ExecuteNonQueryAsync();
            throw new Exception("Read account can write");
        }
        catch (MySqlException e) when (e.Number == 1142) { Check(true, "database SELECT-only account cannot write"); }
    }
    var writes = Task.Run(() => { for (int i = 0; i < 15; i++) fixture.Insert("CONCURRENT-" + i, "C", 1); });
    for (int i = 0; i < 5; i++)
    {
        var current = await repo.Home(scope);
        Check(current.Summary.Valid + current.Summary.Invalid == current.Summary.Total, "concurrent committed read consistent " + i);
    }
    await writes;
    fixture.Insert("TRUE-WITH-NULL","DATA-CONTRADICTION",0);
    fixture.Execute("UPDATE measurements SET volume_m3=NULL WHERE event_id='TRUE-WITH-NULL'");
    fixture.Insert("FALSE-WITH-VALUE","DATA-CONTRADICTION",9);
    fixture.Execute("UPDATE measurements SET volume_valid=0 WHERE event_id='FALSE-WITH-VALUE'");
    var contradictions=await new RecordRepository(new WebOptions{MySqlConnectionString=fixture.ReaderConnection}).Read(QueryScope.Parse(new("2026-11-09T00:00","2026-11-09T23:59","DATA-CONTRADICTION")));
    Check(contradictions.Count==2 && contradictions.All(r=>!r.VolumeValid && r.Volume is null),"contradictory stored validity never displayed as valid volume");
    var noConfig=new RecordRepository(new WebOptions{MySqlConnectionFile=Path.Combine(fixture.Root,"not-created.dpapi")});
    await Expect("DATABASE_CONFIG_REQUIRED",()=>noConfig.Read(scope),"missing configuration fails explicitly without creating database");
    fixture.Execute("RENAME TABLE measurements TO measurements_temporarily_unavailable");
    try
    {
        Check((await client.GetAsync("/api/home")).StatusCode == HttpStatusCode.ServiceUnavailable, "schema mismatch returns explicit database failure");
    }
    finally { fixture.Execute("RENAME TABLE measurements_temporarily_unavailable TO measurements"); }

    clock.Advance(TimeSpan.FromMinutes(16));
    Check((await client.GetAsync($"/api/queries/{id}")).StatusCode == HttpStatusCode.Gone, "HTTP snapshot expiration");
    clock.Set(fixture.Now);
    if (args.Contains("--browser"))
        await BrowserQa.Run(address, output, fixture);
    Check((await client.PostAsync("/api/logout", null)).IsSuccessStatusCode && (await client.GetAsync("/api/home")).StatusCode == HttpStatusCode.Unauthorized, "logout invalidates data access");
    await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
    {
        passed = true,
        checks,
        source = "isolated MySQL 8.4",
        productionDataWritten = false
    }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"PASS: {checks.Count} acceptance checks; output: {output}");
}
finally { await app.StopAsync(); }
string? Value(string key) { var n = Array.IndexOf(args, key); return n >= 0 && n + 1 < args.Length ? args[n + 1] : null; }
HttpClient Client() => new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false, UseProxy = false }) { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(60) };
void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("FAILED: " + label); checks.Add(label); }
async Task<JsonElement> Get(string url) { using var r = await client.GetAsync(url); r.EnsureSuccessStatusCode(); return (await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync())).RootElement.Clone(); }
async Task<JsonElement> Query(string start = "2026-11-09T00:00", string end = "2026-11-09T23:59", string vehicle = "")
{
    using var r = await client.PostAsJsonAsync("/api/queries", new
    {
        start,
        end,
        vehicle
    });
    r.EnsureSuccessStatusCode();
    return (await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync())).RootElement.Clone();
}
async Task Expect(string code, Func<Task<IReadOnlyList<RecordRow>>> action, string label)
{
    try
    {
        await action();
        throw new Exception("No expected error");
    }
    catch (ApiError e) when (e.Code == code) { Check(true, label); }
}
public sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset value = now; public override DateTimeOffset GetUtcNow() => value; public void Advance(TimeSpan delta) => value += delta; public void Set(DateTimeOffset time) => value = time;
}


