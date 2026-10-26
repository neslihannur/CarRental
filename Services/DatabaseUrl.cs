using Npgsql;

namespace CarRental.Services;

/// Veritabanı bağlantısı: ConnectionStrings:Default (Npgsql biçimi) ya da
/// DATABASE_URL (Coolify'ın verdiği postgres://kullanıcı:şifre@sunucu:5432/veritabanı biçimi).
public static class DatabaseUrl
{
    public static string ConnectionString(IConfiguration config)
    {
        if (config.GetConnectionString("Default") is { Length: > 0 } cs) return cs;
        if (config["DATABASE_URL"] is { Length: > 0 } url) return FromUrl(url);
        throw new InvalidOperationException("Veritabanı bağlantısı yok: ConnectionStrings__Default ya da DATABASE_URL tanımlayın.");
    }

    public static string FromUrl(string url)
    {
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')) is { Length: > 0 } db ? db : "postgres",
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
        }.ConnectionString;
    }
}
