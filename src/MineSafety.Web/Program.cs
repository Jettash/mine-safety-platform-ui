using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using MySqlConnector;
using MineSafety.Web;

var app = AppHost.Build(args);
await app.RunAsync();

namespace MineSafety.Web
{
    public static class AppHost
    {
        public static WebApplication Build(string[] args, TimeProvider? clock = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables().AddCommandLine(args);
            var options = builder.Configuration.GetSection("Web").Get<WebOptions>() ?? new();
            options.Normalize(AppContext.BaseDirectory);
            Directory.CreateDirectory(options.StateDirectory);
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(clock ?? TimeProvider.System);
            builder.Services.AddSingleton<Accounts>();
            builder.Services.AddSingleton<RecordRepository>();
            builder.Services.AddSingleton<QuerySnapshots>();
            var protection = builder.Services.AddDataProtection().SetApplicationName("MineSafety.Web")
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(options.StateDirectory, "keys")));
            if (OperatingSystem.IsWindows())
                protection.ProtectKeysWithDpapi();
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(cookie =>
            {
                cookie.Cookie.Name = "MineSafety.Web.Session";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Strict;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.LoginPath = "/login";
                cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
                cookie.SlidingExpiration = true;
                cookie.Events.OnRedirectToLogin = async context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = 401;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            error = "请先登录。",
                            code = "LOGIN_REQUIRED"
                        });
                    }
                    else
                        context.Response.Redirect("/login");
                };
                cookie.Events.OnValidatePrincipal = context =>
                {
                    var store = context.HttpContext.RequestServices.GetRequiredService<Accounts>();
                    if (!store.Matches(context.Principal?.Identity?.Name ?? "", context.Principal?.FindFirstValue("stamp") ?? ""))
                        context.RejectPrincipal();
                    return Task.CompletedTask;
                };
            });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(rate =>
            {
                rate.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
                rate.OnRejected = async (context, cancel) =>
                {
                    context.HttpContext.Response.StatusCode = 429;
                    await context.HttpContext.Response.WriteAsJsonAsync(new
                    {
                        error = "登录尝试过于频繁，请一分钟后再试。",
                        code = "LOGIN_RATE_LIMIT"
                    }, cancel);
                };
            });
            var app = builder.Build();
            _ = app.Services.GetRequiredService<Accounts>();
            app.Use(async (context, next) =>
            {
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
                if (context.Request.Path.StartsWithSegments("/api"))
                    context.Response.Headers.CacheControl = "no-store";
                try
                {
                    if (HttpMethods.IsPost(context.Request.Method) && context.Request.Headers.TryGetValue("Origin", out var origin) &&
                        (!Uri.TryCreate(origin, UriKind.Absolute, out var source) ||
                         !source.Authority.Equals(context.Request.Host.Value, StringComparison.OrdinalIgnoreCase) ||
                         source.Scheme != context.Request.Scheme))
                        throw new ApiError(403, "ORIGIN_REJECTED", "请求来源无效，请从本系统页面操作。");
                    await next();
                }
                catch (ApiError error)
                {
                    if (context.Response.HasStarted)
                        throw;
                    context.Response.StatusCode = error.Status;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = error.Message,
                        error.Code
                    });
                }
                catch (MySqlException error)
                {
                    app.Logger.LogError("MySQL read failed (code {Code})", error.Number);
                    context.Response.StatusCode = 503;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "MySQL 暂时无法读取，请检查数据库服务、连接配置和表结构。",
                        code = "DATABASE_UNAVAILABLE"
                    });
                }
                catch (BadHttpRequestException)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "请求内容无效。",
                        code = "INVALID_REQUEST"
                    });
                }
                catch (Exception error)
                {
                    app.Logger.LogError(error, "Request failed");
                    if (context.Response.HasStarted)
                        throw;
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "服务暂时无法完成请求，请稍后重试。",
                        code = "SERVER_ERROR"
                    });
                }
            });
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();
            // Static assets contain no measurements. All measurement data routes below require authentication.
            app.UseStaticFiles();
            app.MapGet("/", (HttpContext context, Accounts accounts) =>
                !accounts.Initialized ? Results.Redirect("/setup") : context.User.Identity?.IsAuthenticated == true
                    ? Html("index.html") : Results.Redirect("/login"));
            app.MapGet("/login", (Accounts accounts) => accounts.Initialized ? Html("login.html") : Results.Redirect("/setup"));
            app.MapGet("/setup", (HttpContext context, Accounts accounts) =>
                accounts.Initialized ? Results.Redirect("/login") : IsLoopback(context) ? Html("setup.html") : Results.Text("请在服务器本机初始化账号。", statusCode: 403));
            app.MapGet("/api/setup-info", (HttpContext context, Accounts accounts) =>
                new
                {
                    initialized = accounts.Initialized,
                    canSetup = IsLoopback(context),
                    setupToken = !accounts.Initialized && IsLoopback(context) ? accounts.SetupToken : null
                });
            app.MapPost("/api/setup", (HttpContext context, Accounts accounts, SetupRequest request) =>
            {
                if (!IsLoopback(context))
                    throw new ApiError(403, "LOCAL_SETUP_ONLY", "请在服务器本机初始化账号。");
                var supplied = context.Request.Headers["X-Setup-Token"].ToString();
                if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(accounts.SetupToken)))
                    throw new ApiError(403, "INVALID_SETUP_TOKEN", "初始化页面已失效，请刷新。");
                accounts.Initialize(request);
                return Results.Ok(new
                {
                    message = "账号已建立，请使用 admin 或 viewer 登录。"
                });
            });
            app.MapPost("/api/login", async (HttpContext context, Accounts accounts, LoginRequest request) =>
            {
                if (!accounts.Initialized)
                    throw new ApiError(503, "SETUP_REQUIRED", "管理员尚未初始化账号。");
                var account = accounts.Verify(request.Username, request.Password)
                    ?? throw new ApiError(401, "LOGIN_FAILED", "账号或密码不正确。");
                var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, account.Username),
                    new Claim(ClaimTypes.Role, account.Role), new Claim("sid", Guid.NewGuid().ToString("N")),
                    new Claim("stamp", account.SecurityStamp) }, CookieAuthenticationDefaults.AuthenticationScheme);
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
                return Results.Ok(new
                {
                    username = account.Username,
                    role = account.Role
                });
            }).RequireRateLimiting("login");
            var api = app.MapGroup("/api").RequireAuthorization();
            api.MapPost("/logout", async (HttpContext context) => { await context.SignOutAsync(); return Results.Ok(new { message = "已退出登录。" }); });
            api.MapGet("/me", (HttpContext context) => new { username = context.User.Identity!.Name, role = context.User.FindFirstValue(ClaimTypes.Role) });
            api.MapGet("/home", async (HttpContext context, RecordRepository repository, TimeProvider time) =>
            {
                var now = time.GetUtcNow();
                var scope = QueryScope.Today(now);
                var home = await repository.Home(scope, context.RequestAborted);
                return new
                {
                    day = scope.Start[..10],
                    updatedAt = now.ToOffset(QueryScope.ChinaOffset).ToString("yyyy-MM-dd HH:mm:ss"),
                    summary = home.Summary,
                    items = home.Items
                };
            });
            api.MapPost("/queries", async (HttpContext context, QueryRequest request, RecordRepository repository, QuerySnapshots cache) =>
            {
                var scope = QueryScope.Parse(request);
                var query = cache.Add(Owner(context), scope, await repository.Read(scope, context.RequestAborted));
                return QuerySnapshots.Page(query, 1, 20);
            });
            api.MapGet("/queries/{id}", (HttpContext context, string id, int? page, int? pageSize, QuerySnapshots cache) =>
                QuerySnapshots.Page(cache.Get(id, Owner(context)), page ?? 1, pageSize ?? 20));
            api.MapGet("/queries/{id}/records/{eventId}", (HttpContext context, string id, string eventId, QuerySnapshots cache) =>
                cache.Get(id, Owner(context)).Records.FirstOrDefault(r => r.EventId == eventId)
                    ?? throw new ApiError(404, "RECORD_NOT_FOUND", "本次查询中没有这条记录。"));
            api.MapGet("/queries/{id}/report", (HttpContext context, string id, string? grain, QuerySnapshots cache) =>
            {
                var query = cache.Get(id, Owner(context));
                return new
                {
                    query.Scope,
                    query.Summary,
                    rows = Reports.Group(query, grain ?? "day")
                };
            });
            api.MapGet("/queries/{id}/export.xlsx", async (HttpContext context, string id, string? grain, bool? includeRecords, QuerySnapshots cache) =>
            {
                var query = cache.Get(id, Owner(context));
                var bytes = await Reports.Excel(query, grain ?? "day", includeRecords ?? true, context.RequestAborted);
                return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "矿车报表_" + query.Scope.Start[..10] + ".xlsx");
            });
            return app;
        }
        private static string Owner(HttpContext context) => context.User.FindFirstValue("sid")
            ?? throw new ApiError(401, "LOGIN_REQUIRED", "请重新登录。");
        private static bool IsLoopback(HttpContext context)
        {
            var ip = context.Connection.RemoteIpAddress;
            return ip is not null && IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip);
        }
        private static IResult Html(string name) => Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", name), "text/html; charset=utf-8");
    }
}

