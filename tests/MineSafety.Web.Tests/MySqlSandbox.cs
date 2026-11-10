using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using MySqlConnector;

namespace MineSafety.Web.Tests;

// Own loopback-only server, own datadir, random port. Never accepts a production connection.
public sealed class MySqlSandbox : IAsyncDisposable
{
    private Process? process;
    public required string Root
    {
        get; init;
    }
    public required string ConnectionString
    {
        get; init;
    }
    public static async Task<MySqlSandbox> Start(string output)
    {
        var bin = Environment.GetEnvironmentVariable("MINE_TEST_MYSQL_BIN") ?? @"E:\MineTruckMySQL\mysql-8.4.13\bin";
        var executable = Path.Combine(bin, "mysqld.exe");
        if (!File.Exists(executable))
            throw new InvalidOperationException("Set MINE_TEST_MYSQL_BIN to the MySQL 8.4 bin directory for isolated acceptance tests.");
        var root = Path.Combine(Path.GetFullPath(output), "mysql-sandbox");
        Directory.CreateDirectory(root);
        var data = Path.Combine(root, "data");
        async Task RunInitialization()
        {
            var start = StartInfo(executable, ["--no-defaults", "--initialize-insecure", "--basedir=" + Path.GetDirectoryName(bin), "--datadir=" + data, "--console"]);
            using var init = Process.Start(start)!;
            var stdout = init.StandardOutput.ReadToEndAsync();
            var stderr = init.StandardError.ReadToEndAsync();
            await init.WaitForExitAsync();
            File.WriteAllText(Path.Combine(root, "initialize.log"), await stdout + await stderr);
            if (init.ExitCode != 0)
                throw new InvalidOperationException("Isolated MySQL initialization failed; see initialize.log.");
        }
        if (!Directory.Exists(Path.Combine(data, "mysql")))
            await RunInitialization();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var server = Process.Start(StartInfo(executable, ["--no-defaults","--basedir="+Path.GetDirectoryName(bin),"--datadir="+data,
            "--bind-address=127.0.0.1","--port="+port,"--mysqlx=OFF","--console"]))!;
        var outTask = server.StandardOutput.ReadToEndAsync();
        var errTask = server.StandardError.ReadToEndAsync();
        var connection = new MySqlConnectionStringBuilder { Server = "127.0.0.1", Port = (uint)port, UserID = "root", ConnectionTimeout = 1, Pooling = false, AllowPublicKeyRetrieval = true };
        try
        {
            var ready = false;
            for (int i = 0; i < 150; i++)
            {
                try
                {
                    await using var c = new MySqlConnection(connection.ConnectionString);
                    await c.OpenAsync();
                    var actual = (string)(await new MySqlCommand("SELECT @@datadir", c).ExecuteScalarAsync())!;
                    if (!Path.GetFullPath(actual).TrimEnd('\\', '/').Equals(Path.GetFullPath(data), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Test datadir guard failed.");
                    ready = true;
                    break;
                }
                catch (MySqlException) { if (server.HasExited) break; await Task.Delay(200); }
            }
            if (!ready)
                throw new InvalidOperationException("Isolated MySQL did not start.");
            var result = new MySqlSandbox { Root = root, ConnectionString = connection.ConnectionString, process = server };
            result.logs = async () => File.WriteAllText(Path.Combine(root, "server.log"), await outTask + await errTask);
            return result;
        }
        catch { if (!server.HasExited) server.Kill(true); File.WriteAllText(Path.Combine(root, "server.log"), await outTask + await errTask); server.Dispose(); throw; }
    }
    private Func<Task>? logs;
    private static ProcessStartInfo StartInfo(string exe, IEnumerable<string> args)
    {
        var p = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args)
            p.ArgumentList.Add(a);
        return p;
    }
    public async ValueTask DisposeAsync()
    {
        if (process is null)
            return;
        if (!process.HasExited)
        {
            try
            {
                await using var c = new MySqlConnection(ConnectionString);
                await c.OpenAsync();
                await new MySqlCommand("SHUTDOWN", c).ExecuteNonQueryAsync();
            }
            catch (MySqlException) { }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); }
        }
        if (logs is not null)
            await logs();
        process.Dispose();
    }
}
