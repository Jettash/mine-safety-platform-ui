using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;

namespace MineSafety.Web;

public sealed record StoredAccount(string Username, string Role, string PasswordHash, string SecurityStamp);

public sealed class Accounts
{
    private readonly object gate = new();
    private readonly string file;
    private readonly PasswordHasher<StoredAccount> hasher = new();
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private StoredAccount[] users = [];
    public string SetupToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public bool Initialized
    {
        get
        {
            lock (gate)
                return users.Length > 0;
        }
    }

    public Accounts(WebOptions options)
    {
        Directory.CreateDirectory(options.StateDirectory);
        file = Path.Combine(options.StateDirectory, "accounts.json");
        if (File.Exists(file))
        {
            users = JsonSerializer.Deserialize<StoredAccount[]>(File.ReadAllText(file), json)
                ?? throw new InvalidOperationException("账号配置文件无效，请先备份并检查。");
            if (users.Length != 2 || users.Any(u => string.IsNullOrWhiteSpace(u.PasswordHash)) ||
                users.Count(u => u.Username == "admin" && u.Role == "Administrator") != 1 ||
                users.Count(u => u.Username == "viewer" && u.Role == "Viewer") != 1)
                throw new InvalidOperationException("账号配置文件内容无效。");
        }
    }
    public void Initialize(SetupRequest request)
    {
        Validate(request.AdminPassword);
        Validate(request.ViewerPassword);
        lock (gate)
        {
            if (users.Length > 0 || File.Exists(file))
                throw new ApiError(409, "ALREADY_INITIALIZED", "账号已初始化，请直接登录。");
            var created = new[] { Make("admin", "Administrator", request.AdminPassword!), Make("viewer", "Viewer", request.ViewerPassword!) };
            var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(created, json));
                File.Move(temporary, file, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            users = created;
        }
    }
    public StoredAccount? Verify(string? username, string? password)
    {
        if (password is null || password.Length > 128)
            return null;
        lock (gate)
        {
            var account = users.FirstOrDefault(u => u.Username.Equals(username?.Trim(), StringComparison.OrdinalIgnoreCase));
            return account is not null && hasher.VerifyHashedPassword(account, account.PasswordHash, password) != PasswordVerificationResult.Failed
                ? account : null;
        }
    }
    public bool Matches(string name, string stamp)
    {
        lock (gate)
            return users.Any(u => u.Username == name && u.SecurityStamp == stamp);
    }
    private StoredAccount Make(string name, string role, string password)
    {
        var user = new StoredAccount(name, role, "", Guid.NewGuid().ToString("N"));
        return user with
        {
            PasswordHash = hasher.HashPassword(user, password)
        };
    }
    private static void Validate(string? password)
    {
        if (password is null || password.Length < 8 || password.Length > 128)
            throw new ApiError(400, "INVALID_PASSWORD", "两个账号的密码均须为 8～128 位。");
    }
}
