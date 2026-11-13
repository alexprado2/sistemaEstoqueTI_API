using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;
using SistemaEstoqueTI.Api.Models;

namespace SistemaEstoqueTI.Api.Infrastructure;

/// <summary>Conexão PostgreSQL, com suporte à URL interna fornecida pelo Render.</summary>
public sealed class Db
{
    private readonly string _connectionString;

    public Db(IConfiguration configuration)
    {
        var raw = configuration["Database:Url"] ?? configuration["DATABASE_URL"]
            ?? configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
                !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            {
                // ADO.NET: Host=...;Port=...;Database=...;Username=...;Password=...
                _connectionString = new NpgsqlConnectionStringBuilder(raw).ConnectionString;
                return;
            }

            var url = new Uri(raw);
            var credentials = url.UserInfo.Split(':', 2);
            if (credentials.Length != 2 || string.IsNullOrWhiteSpace(url.Host))
                throw new InvalidOperationException("Database__Url: formato PostgreSQL inválido.");

            var cs = new NpgsqlConnectionStringBuilder
            {
                Host = url.Host,
                Port = url.IsDefaultPort ? 5432 : url.Port,
                Database = Uri.UnescapeDataString(url.AbsolutePath.TrimStart('/')),
                Username = Uri.UnescapeDataString(credentials[0]),
                Password = Uri.UnescapeDataString(credentials[1]),
                SslMode = SslMode.Prefer // conexão privada entre serviços do Render
            };
            foreach (var part in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = part.Split('=', 2);
                if (fields.Length == 2 && Uri.UnescapeDataString(fields[0]).Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                    cs.SslMode = ParseSslMode(Uri.UnescapeDataString(fields[1]));
            }
            _connectionString = cs.ConnectionString;
            return;
        }

        var host = configuration["Database:Host"];
        var username = configuration["Database:User"];
        var password = configuration["Database:Password"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || password is null)
            throw new InvalidOperationException("Configure Database__Url ou Database__Host, Database__User e Database__Password.");

        _connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(configuration["Database:Port"], out var port) ? port : 5432,
            Database = configuration["Database:Name"] ?? "sistema_estoque_ti",
            Username = username,
            Password = password,
            SslMode = ParseSslMode(configuration["Database:SslMode"] ?? "Prefer")
        }.ConnectionString;
    }

    private static SslMode ParseSslMode(string value)
    {
        if (Enum.TryParse<SslMode>(value.Replace("-", "").Replace("_", ""), ignoreCase: true, out var parsed))
            return parsed;
        throw new InvalidOperationException("Database__SslMode inválido. Use Disable, Prefer, Require, VerifyCA ou VerifyFull.");
    }

    public NpgsqlConnection OpenConnection() => new(_connectionString);
}

/// <summary>Autenticação por API Key enviada no cabeçalho X-API-Key.</summary>
public static class Auth
{
    public static async Task<SessionUser?> CurrentAsync(HttpContext context, SessionStore keys)
    {
        var key = ApiKeyToken(context);
        return key is null ? null : await keys.ResolveAsync(key, context);
    }

    public static string? ApiKeyToken(HttpContext context)
    {
        var key = context.Request.Headers["X-API-Key"].ToString().Trim();
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    public static bool HasRole(SessionUser? user, params string[] roles) =>
        user is not null && (roles.Length == 0 || roles.Contains(user.Perfil, StringComparer.OrdinalIgnoreCase));

    public static IResult Block(SessionUser? user) =>
        user is null ? Results.Unauthorized() : Results.StatusCode(StatusCodes.Status403Forbidden);
}

public static class PasswordHasher
{
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 3) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            byte[] actual;
            if (parts[0] == "PBKDF2")
                actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
            else if (parts[0] == "SHA256")
                actual = SHA256.HashData(Encoding.UTF8.GetBytes(parts[1] + password));
            else return false;
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(Db db, IConfiguration configuration, IHostEnvironment environment)
    {
        await using var conn = db.OpenConnection();
        await conn.OpenAsync();

        // A base PostgreSQL do Render já existe; criamos apenas as tabelas e índices.
        // Instruções CREATE TABLE IF NOT EXISTS preservam registros em reinícios/deploys.
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "database", "postgresql.sql");
        if (!File.Exists(schemaPath))
            throw new FileNotFoundException("Não foi encontrado o schema PostgreSQL.", schemaPath);
        await conn.ExecuteAsync(await File.ReadAllTextAsync(schemaPath));

        var users = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM usuario");
        if (users == 0)
        {
            var email = configuration["BootstrapAdmin:Email"]?.Trim();
            var password = configuration["BootstrapAdmin:Password"];
            if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
            {
                if (email.Length > 120 || !email.Contains('@') || password.Length < 12)
                    throw new InvalidOperationException("BootstrapAdmin__Email inválido ou BootstrapAdmin__Password menor que 12 caracteres.");
                await conn.ExecuteAsync(@"
                    INSERT INTO usuario (nome, email, senha, perfil, ativo)
                    VALUES ('Administrador', @Email, @Senha, 'ADMIN', TRUE)
                    ON CONFLICT (email) DO NOTHING",
                    new { Email = email, Senha = PasswordHasher.Hash(password) });
            }
        }

        if (configuration.GetValue<bool>("SeedDemoUsers"))
        {
            var demos = new[]
            {
                (Nome: "Administrador", Email: "admin@sistema.com", Perfil: "ADMIN", Password: configuration["DemoUsers:AdminPassword"]),
                (Nome: "Funcionário Estoque", Email: "estoque@sistema.com", Perfil: "FUNCIONARIO", Password: configuration["DemoUsers:FuncionarioPassword"]),
                (Nome: "Vendedor", Email: "vendas@sistema.com", Perfil: "VENDEDOR", Password: configuration["DemoUsers:VendedorPassword"])
            };
            foreach (var demo in demos)
            {
                var exists = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM usuario WHERE email = @Email", new { demo.Email });
                if (exists > 0) continue;

                var password = demo.Password;
                if (string.IsNullOrWhiteSpace(password))
                {
                    if (environment.IsDevelopment()) password = "123456";
                    else throw new InvalidOperationException($"Falta senha para {demo.Email}: configure DemoUsers__*Password no Render.");
                }
                if (!environment.IsDevelopment() && password.Length < 12)
                    throw new InvalidOperationException($"A senha de {demo.Email} precisa ter ao menos 12 caracteres em Production.");

                await conn.ExecuteAsync(@"
                    INSERT INTO usuario (nome, email, senha, perfil, ativo)
                    VALUES (@Nome, @Email, @Senha, @Perfil, TRUE)
                    ON CONFLICT (email) DO NOTHING",
                    new { demo.Nome, demo.Email, demo.Perfil, Senha = PasswordHasher.Hash(password) });
            }
        }

        if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM usuario") == 0)
            throw new InvalidOperationException("Nenhum usuário: habilite SeedDemoUsers e configure as senhas ou defina BootstrapAdmin__Email e BootstrapAdmin__Password.");

        if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM estoque") == 0)
            await conn.ExecuteAsync(@"
                INSERT INTO estoque (nome_local, descricao) VALUES
                ('Estoque Principal', 'Local padrão para produtos de TI'),
                ('Almoxarifado', 'Área secundária de armazenamento');");
    }
}
