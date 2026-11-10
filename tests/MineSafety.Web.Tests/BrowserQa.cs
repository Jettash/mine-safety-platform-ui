using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

namespace MineSafety.Web.Tests;

public static class BrowserQa
{
    public static async Task Run(string address, string output, Fixture fixture)
    {
        var edge = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe");
        if (!File.Exists(edge))
            throw new InvalidOperationException("Microsoft Edge not found.");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var folder = Path.Combine(output, "browser");
        Directory.CreateDirectory(folder);
        var downloads = Path.Combine(folder, "downloads-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(downloads);
        var start = new ProcessStartInfo(edge) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true };
        foreach (var a in new[]{"--headless=new","--disable-gpu","--no-first-run","--no-default-browser-check","--disable-extensions",
            "--disable-background-networking","--remote-debugging-address=127.0.0.1","--remote-debugging-port="+port,
            "--user-data-dir="+Path.Combine(folder,"profile-"+Guid.NewGuid().ToString("N")[..8]),"about:blank"})
            start.ArgumentList.Add(a);
        using var process = Process.Start(start)!;
        var log = process.StandardError.ReadToEndAsync();
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
        try
        {
            string? websocket = null;
            for (int i = 0; i < 100; i++)
            {
                try
                {
                    using var list = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json/list"));
                    websocket = list.RootElement.EnumerateArray().First(x => x.GetProperty("type").GetString() == "page").GetProperty("webSocketDebuggerUrl").GetString();
                    if (websocket is not null)
                        break;
                }
                catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException) { await Task.Delay(100); }
            }
            if (websocket is null)
                throw new Exception("Edge debugging not ready");
            await using var b = new Cdp();
            await b.Connect(websocket);
            await b.Call("Page.enable");
            await b.Call("Runtime.enable");
            await b.Call("Emulation.setDeviceMetricsOverride", new
            {
                width = 1366,
                height = 768,
                deviceScaleFactor = 1,
                mobile = false
            });
            await b.Call("Browser.setDownloadBehavior", new
            {
                behavior = "allow",
                downloadPath = downloads
            });
            // An independent account state verifies initial setup without touching the installed users.
            await using var setupApp = AppHost.Build(["--urls","http://127.0.0.1:0","--Web:MySqlConnectionString",fixture.ReaderConnection,
                "--Web:StateDirectory",Path.Combine(fixture.Root,"browser-auth-"+Guid.NewGuid().ToString("N")),"--Logging:LogLevel:Default","Warning"]);
            await setupApp.StartAsync();
            await b.Navigate(setupApp.Urls.Single() + "/");
            await b.Wait("!!document.querySelector('#admin-password')");
            await b.Screenshot(Path.Combine(folder, "setup.png"));
            await b.Eval("document.querySelector('#admin-password').value='test-admin-2026';document.querySelector('#admin-confirm').value='test-admin-2026';document.querySelector('#viewer-password').value='test-viewer-2026';document.querySelector('#viewer-confirm').value='test-viewer-2026';document.querySelector('#auth-form').requestSubmit();true");
            await b.Wait("location.pathname==='/login' && !!document.querySelector('#username')");
            await b.Screenshot(Path.Combine(folder, "login.png"));
            await setupApp.StopAsync();
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "reference", "design-v4.html")))
                root = root.Parent;
            if (root is null)
                throw new Exception("Original reference missing");
            await b.Navigate(new Uri(Path.Combine(root.FullName, "reference", "design-v4.html")).AbsoluteUri);
            await b.Wait("!!document.querySelector('.home-table')");
            const string geometry = "JSON.stringify(['.topbar','aside','main','.cards','.home-table'].map(s=>{const r=document.querySelector(s).getBoundingClientRect();return {s,x:r.x,y:r.y,width:r.width,height:r.height}}))";
            var reference = (await b.Eval(geometry)).GetString()!;
            await b.Screenshot(Path.Combine(folder, "before-reference-home.png"));
            await b.Navigate(address + "/login");
            await b.Wait("!!document.querySelector('#username')");
            await b.Eval("document.querySelector('#username').value='admin';document.querySelector('#password').value='test-admin-2026';document.querySelector('#auth-form').requestSubmit();true");
            await b.Wait("location.pathname==='/' && document.querySelector('#home-total')?.textContent !== '—'");
            await b.Screenshot(Path.Combine(folder, "home-1366.png"));
            var actual = (await b.Eval(geometry)).GetString()!;
            // Five real rows replace five design rows; chrome/sidebar/main/card geometry must remain exact.
            using (var r = JsonDocument.Parse(reference))
            using (var a = JsonDocument.Parse(actual))
                for (int i = 0; i < 4; i++)
                    Check(r.RootElement[i].GetRawText() == a.RootElement[i].GetRawText(), "Original page frame geometry changed");
            Check((await b.Eval("document.documentElement.scrollWidth <= innerWidth")).GetBoolean(), "Home horizontal overflow at 1366");
            var initial = (await b.Eval("Number(document.querySelector('#home-total').textContent)")).GetInt32();
            fixture.Insert("BROWSERNEW", "浏览器新增车辆", 1, at: fixture.Now.AddSeconds(5));
            await b.Wait($"Number(document.querySelector('#home-total').textContent)==={initial + 1}");
            await b.Eval("document.querySelector('[data-page=query]').click();document.querySelector('#merged-query-form').requestSubmit();true");
            await b.Wait("!document.querySelector('#query-results').hidden && !document.querySelector('#export-open').disabled");
            var count = (await b.Eval("Number(document.querySelector('#stat-total').textContent)")).GetInt32();
            Check(count == initial + 1, "Browser query missed committed record");
            Check((await b.Eval("document.querySelectorAll('#query-records img').length===0")).GetBoolean(), "User text injected markup");
            await b.Eval("document.querySelector('#next-page').click();true");
            await b.Wait("document.querySelector('#page-index').textContent.startsWith('2 /')");
            Check((await b.Eval("Number(document.querySelector('#stat-total').textContent)")).GetInt32() == count, "Paging changed totals");
            await b.Eval("document.querySelector('#page-size').value='50';document.querySelector('#page-size').dispatchEvent(new Event('change'));true");
            await b.Wait("document.querySelector('#query-records').children.length===50 && document.querySelector('#page-index').textContent.startsWith('1 /')");
            await b.Screenshot(Path.Combine(folder, "query.png"));
            await b.Eval("[...document.querySelectorAll('#query-records tr')].find(r=>r.textContent.includes('小写编号车辆')).querySelector('button').click();true");
            await b.Wait("document.querySelector('#vehicle-detail').open && document.querySelector('#detail-metadata').textContent.includes('0.012')");
            Check((await b.Eval("document.querySelector('#detail-metadata').textContent.includes('92.00%')")).GetBoolean(), "Registration percentage incorrect");
            await b.Screenshot(Path.Combine(folder, "detail-metrics.png"));
            await b.Eval("document.querySelector('#detail-close').click();[...document.querySelectorAll('#query-records tr')].find(r=>r.textContent.includes('无效')).querySelector('button').click();true");
            await b.Wait("document.querySelector('#vehicle-detail').open && document.querySelector('#detail-volume').textContent==='—'");
            Check((await b.Eval("document.querySelector('#detail-front-box').classList.contains('missing') && !document.querySelector('#detail-front').getAttribute('src')")).GetBoolean(), "Missing photo replaced by sample");
            await b.Screenshot(Path.Combine(folder, "detail-invalid.png"));
            await b.Eval("document.querySelector('#detail-close').click();document.querySelector('#query-vehicle').value='Truck';document.querySelector('#query-vehicle').dispatchEvent(new Event('input'));true");
            Check((await b.Eval("document.querySelector('#export-open').disabled")).GetBoolean(), "Dirty query still exportable");
            await b.Eval("document.querySelector('#query-vehicle').value='';document.querySelector('#query-vehicle').dispatchEvent(new Event('input'));document.querySelector('#merged-query-form').requestSubmit();true");
            await b.Wait("!document.querySelector('#export-open').disabled");
            await b.Eval("document.querySelector('#export-open').click();document.querySelector('#export-grain').value='week';document.querySelector('#export-grain').dispatchEvent(new Event('change'));true");
            await b.Wait("document.querySelector('#export-preview').textContent.includes(' 至 ')");
            await b.Screenshot(Path.Combine(folder, "export.png"));
            await b.Eval("document.querySelector('#include-records').checked=false;document.querySelector('#export-confirm').click();true");
            string? download = null;
            for (int i = 0; i < 150; i++)
            {
                download = Directory.GetFiles(downloads, "*.xlsx").FirstOrDefault();
                if (download is not null)
                    break;
                await Task.Delay(100);
            }
            Check(download is not null, "Browser Excel download failed");
            using (var wb = new XLWorkbook(download!))
                Check(wb.Worksheets.Count == 1, "Summary-only export has detail sheet");
            await b.Eval("document.querySelector('#export-close').click();true");
            foreach (var page in new[] { "device", "config", "users", "email" })
            {
                await b.Eval($"document.querySelector('[data-page={page}]').click();true");
                if (page == "device")
                    Check((await b.Eval("[...document.querySelectorAll('#page-device .status-dot')].every(e=>e.textContent==='未接入')")).GetBoolean(), "Invented device state");
                if (page == "config")
                    Check((await b.Eval("[...document.querySelectorAll('#page-config input')].every(e=>e.disabled)")).GetBoolean(), "Placeholder config editable");
                if (page == "email")
                    Check((await b.Eval("[...document.querySelectorAll('#page-email input')].every(e=>e.disabled)")).GetBoolean(), "Placeholder email enabled");
                await b.Screenshot(Path.Combine(folder, page + ".png"));
            }
            await b.Eval("document.querySelector('[data-page=home]').click();true");
            await b.Call("Emulation.setDeviceMetricsOverride", new
            {
                width = 1920,
                height = 1080,
                deviceScaleFactor = 1,
                mobile = false
            });
            await b.Screenshot(Path.Combine(folder, "home-1920.png"));
            await b.Eval("document.querySelector('#account-menu').click();document.querySelector('#logout').click();true");
            await b.Wait("location.pathname==='/login'");
            await b.Eval("document.querySelector('#username').value='viewer';document.querySelector('#password').value='test-viewer-2026';document.querySelector('#auth-form').requestSubmit();true");
            await b.Wait("location.pathname==='/' && document.querySelector('#account-menu')?.textContent.includes('查看员')");
            Check(b.Errors.Count == 0, "Uncaught browser exception");
            File.WriteAllText(Path.Combine(folder, "report.json"), JsonSerializer.Serialize(new
            {
                passed = true,
                frameGeometryMatched = true,
                homeAutoRefresh = true,
                excelDownloaded = true,
                pages = 6,
                viewports = new[] { 1366, 1920 },
                uncaughtErrors = b.Errors,
                referenceGeometry = reference,
                actualGeometry = actual
            }));
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } File.WriteAllText(Path.Combine(folder, "edge.log"), await log); }
    }
    private static void Check(bool ok, string message)
    {
        if (!ok)
            throw new InvalidOperationException(message);
    }

    private sealed class Cdp : IAsyncDisposable
    {
        private readonly ClientWebSocket socket = new();
        private int id;
        public List<string> Errors { get; } = [];
        public Task Connect(string url) => socket.ConnectAsync(new Uri(url), CancellationToken.None);
        public async Task<JsonElement> Call(string method, object? parameters = null)
        {
            var request = ++id;
            await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                id = request,
                method,
                @params = parameters ?? new
                {
                }
            })), WebSocketMessageType.Text, true, CancellationToken.None);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            while (true)
            {
                using var buffer = new MemoryStream();
                var chunk = new byte[16384];
                WebSocketReceiveResult received;
                do
                {
                    received = await socket.ReceiveAsync(chunk, timeout.Token);
                    if (received.MessageType == WebSocketMessageType.Close)
                        throw new IOException("Browser connection closed.");
                    buffer.Write(chunk, 0, received.Count);
                } while (!received.EndOfMessage);
                using var document = JsonDocument.Parse(buffer.ToArray());
                var root = document.RootElement;
                if (root.TryGetProperty("method", out var type) && type.GetString() == "Runtime.exceptionThrown")
                    Errors.Add(root.GetProperty("params").GetProperty("exceptionDetails").ToString());
                if (!root.TryGetProperty("id", out var reply) || reply.GetInt32() != request)
                    continue;
                if (root.TryGetProperty("error", out var error))
                    throw new InvalidOperationException("CDP: " + error);
                return root.GetProperty("result").Clone();
            }
        }
        public async Task<JsonElement> Eval(string expression)
        {
            var reply = await Call("Runtime.evaluate", new
            {
                expression,
                returnByValue = true,
                awaitPromise = true
            });
            if (reply.TryGetProperty("exceptionDetails", out var error))
                throw new InvalidOperationException("Browser evaluation: " + error);
            return reply.GetProperty("result").TryGetProperty("value", out var value) ? value.Clone() : JsonSerializer.SerializeToElement<object?>(null);
        }
        public async Task Wait(string expression)
        {
            for (var attempt = 0; attempt < 150; attempt++)
            {
                try
                {
                    var value = await Eval("Boolean(" + expression + ")");
                    if (value.ValueKind == JsonValueKind.True)
                        return;
                }
                catch (InvalidOperationException) { }
                await Task.Delay(100);
            }
            throw new InvalidOperationException("Browser wait timed out: " + expression);
        }
        public async Task Navigate(string url)
        {
            await Call("Page.navigate", new
            {
                url
            });
            await Wait("document.readyState==='complete'");
        }
        public async Task Screenshot(string path)
        {
            var result = await Call("Page.captureScreenshot", new
            {
                format = "png",
                captureBeyondViewport = false
            });
            File.WriteAllBytes(path, Convert.FromBase64String(result.GetProperty("data").GetString()!));
        }
        public async ValueTask DisposeAsync()
        {
            if (socket.State == WebSocketState.Open)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            socket.Dispose();
        }
    }
}
