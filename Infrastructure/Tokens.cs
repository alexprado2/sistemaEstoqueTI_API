using System.Security.Cryptography;
using System.Text;
using Dapper;
using SistemaEstoqueTI.Api.Models;

namespace SistemaEstoqueTI.Api.Infrastructure;

/// <summary>
/// API Keys aleatórias: chaves de usuários emitidas no login (sti_u_) e chaves
/// restritas para integrações (sti_i_). No PostgreSQL ficam somente seus hashes.
/// A classe conserva o nome SessionStore para facilitar a migração do projeto.
/// </summary>
public sealed class SessionStore
{
    private readonly Db _db;
    private static readonly TimeSpan LoginKeyLifetime = TimeSpan.FromDays(30);
    private const string LoginPrefix = "sti_u_";
    private const string IntegrationPrefix = "sti_i_";

    public SessionStore(Db db) => _db = db;

    public async Task<(string Token, DateTimeOffset ExpiresAtUtc)> CreateAsync(SessionUser user)
    {
        var key = LoginPrefix + GenerateSecret();
        var expires = DateTimeOffset.UtcNow.Add(LoginKeyLifetime);
        await using var connection = _db.OpenConnection();
        await connection.ExecuteAsync("DELETE FROM api_sessoes WHERE expira_em_utc <= NOW()");
        await connection.ExecuteAsync(@"
            INSERT INTO api_sessoes (token_hash, id_usuario, criada_em_utc, expira_em_utc)
            VALUES (@TokenHash, @IdUsuario, NOW(), @ExpiresAt)",
            new { TokenHash = Hash(key), user.IdUsuario, ExpiresAt = expires.UtcDateTime });
        return (key, expires);
    }

    public async Task<SessionUser?> ResolveAsync(string key, HttpContext context)
    {
        if (key.StartsWith(LoginPrefix, StringComparison.Ordinal))
            return await GetSessionAsync(key);
        if (key.StartsWith(IntegrationPrefix, StringComparison.Ordinal))
            return await GetApiKeyUserAsync(key, context);
        return null;
    }

    // Chave de usuário emitida no login: todas as permissões dependem do perfil no BD.
    public async Task<SessionUser?> GetSessionAsync(string key)
    {
        if (!ValidKey(key, LoginPrefix)) return null;
        await using var connection = _db.OpenConnection();
        return await connection.QuerySingleOrDefaultAsync<SessionUser>(@"
            SELECT u.id_usuario IdUsuario, u.nome Nome, u.email Email, u.perfil Perfil
              FROM api_sessoes s
              INNER JOIN usuario u ON u.id_usuario = s.id_usuario
             WHERE s.token_hash = @TokenHash
               AND s.expira_em_utc > NOW() AND u.ativo = TRUE
             LIMIT 1", new { TokenHash = Hash(key) });
    }

    public async Task RemoveAsync(string key)
    {
        if (!ValidKey(key, LoginPrefix)) return;
        await using var connection = _db.OpenConnection();
        await connection.ExecuteAsync("DELETE FROM api_sessoes WHERE token_hash = @TokenHash",
            new { TokenHash = Hash(key) });
    }

    public async Task RevokeUserAsync(int userId)
    {
        await using var connection = _db.OpenConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync("DELETE FROM api_sessoes WHERE id_usuario = @UserId",
            new { UserId = userId }, tx);
        await connection.ExecuteAsync(@"
            UPDATE api_chaves SET revogada_em_utc = NOW()
             WHERE id_usuario = @UserId AND revogada_em_utc IS NULL",
            new { UserId = userId }, tx);
        await tx.CommitAsync();
    }

    public async Task<(string Token, DateTimeOffset ExpiresAtUtc)> CreateApiKeyAsync(
        int userId, string name, string scope, int days)
    {
        var key = IntegrationPrefix + GenerateSecret();
        var expires = DateTimeOffset.UtcNow.AddDays(days);
        await using var connection = _db.OpenConnection();
        await connection.ExecuteAsync(@"
            INSERT INTO api_chaves (token_hash, id_usuario, nome, escopo, criada_em_utc, expira_em_utc)
            VALUES (@TokenHash, @UserId, @Name, @Scope, NOW(), @ExpiresAt)",
            new { TokenHash = Hash(key), UserId = userId, Name = name, Scope = scope,
                ExpiresAt = expires.UtcDateTime });
        return (key, expires);
    }

    public async Task<IEnumerable<ApiKeyInfo>> ListApiKeysAsync()
    {
        await using var connection = _db.OpenConnection();
        return await connection.QueryAsync<ApiKeyInfo>(@"
            SELECT c.id_chave IdChave, c.nome Nome, c.id_usuario IdUsuario,
                   u.nome Usuario, c.escopo Escopo, c.criada_em_utc CriadaEmUtc,
                   c.expira_em_utc ExpiraEmUtc, c.revogada_em_utc RevogadaEmUtc
              FROM api_chaves c INNER JOIN usuario u ON u.id_usuario = c.id_usuario
             ORDER BY c.id_chave DESC LIMIT 200");
    }

    public async Task<bool> RevokeApiKeyAsync(int id)
    {
        await using var connection = _db.OpenConnection();
        return await connection.ExecuteAsync(@"
            UPDATE api_chaves SET revogada_em_utc = NOW()
             WHERE id_chave = @Id AND revogada_em_utc IS NULL", new { Id = id }) > 0;
    }

    private async Task<SessionUser?> GetApiKeyUserAsync(string key, HttpContext context)
    {
        if (!ValidKey(key, IntegrationPrefix)) return null;
        await using var connection = _db.OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ApiKeyRow>(@"
            SELECT u.id_usuario IdUsuario, u.nome Nome, u.email Email,
                   u.perfil Perfil, c.escopo Escopo
              FROM api_chaves c INNER JOIN usuario u ON u.id_usuario = c.id_usuario
             WHERE c.token_hash = @TokenHash
               AND c.revogada_em_utc IS NULL
               AND c.expira_em_utc > NOW() AND u.ativo = TRUE
             LIMIT 1", new { TokenHash = Hash(key) });

        if (row is null || row.Perfil is not ("VENDEDOR" or "FUNCIONARIO") ||
            !IsIntegrationRouteAllowed(context, row.Escopo)) return null;
        return new SessionUser(row.IdUsuario, row.Nome, row.Email, row.Perfil);
    }

    private static bool IsIntegrationRouteAllowed(HttpContext context, string scope)
    {
        var method = context.Request.Method;
        var path = context.Request.Path;
        if (HttpMethods.IsGet(method))
            return path.StartsWithSegments("/api/dashboard", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWithSegments("/api/produtos", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWithSegments("/api/estoques", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWithSegments("/api/estoque-produtos", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWithSegments("/api/movimentacoes", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWithSegments("/api/pedidos", StringComparison.OrdinalIgnoreCase);
        return scope == "ESCRITA_PEDIDOS" && HttpMethods.IsPost(method) &&
               string.Equals(path.Value, "/api/pedidos", StringComparison.OrdinalIgnoreCase);
    }

    private static string GenerateSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Hash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(key)));
    private static bool ValidKey(string key, string prefix) =>
        key.StartsWith(prefix, StringComparison.Ordinal) && key.Length == prefix.Length + 43 &&
        key[prefix.Length..].All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

    public sealed class ApiKeyRow
    {
        public int IdUsuario { get; set; }
        public string Nome { get; set; } = "";
        public string Email { get; set; } = "";
        public string Perfil { get; set; } = "";
        public string Escopo { get; set; } = "";
    }
}
