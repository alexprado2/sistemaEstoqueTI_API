using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SistemaEstoqueTI.Api.Infrastructure;
using SistemaEstoqueTI.Api.Models;

namespace SistemaEstoqueTI.Api.Controllers;

/// <summary>Chaves de integração de servidor para servidor, gerenciadas somente por ADMIN.</summary>
[ApiController]
[Route("api/integracoes/chaves")]
public sealed class IntegracoesController : ControllerBase
{
    [HttpGet]
    public async Task<IResult> List([FromServices] SessionStore sessions)
    {
        var user = await Auth.CurrentAsync(HttpContext, sessions);
        if (!Auth.HasRole(user, "ADMIN")) return Auth.Block(user);
        return Results.Ok(await sessions.ListApiKeysAsync());
    }

    [HttpPost]
    [EnableRateLimiting("integration-creation")]
    public async Task<IResult> Create([FromBody] ApiKeyCreateRequest request,
        [FromServices] Db db, [FromServices] SessionStore sessions)
    {
        var admin = await Auth.CurrentAsync(HttpContext, sessions);
        if (!Auth.HasRole(admin, "ADMIN")) return Auth.Block(admin);

        var name = request.Nome?.Trim();
        var scope = request.Escopo?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || request.IdUsuario <= 0 ||
            scope is not ("LEITURA" or "ESCRITA_PEDIDOS") || request.ExpiraEmDias is < 1 or > 365)
            return Results.BadRequest(new { message = "Informe nome (até 100 caracteres), idUsuario, escopo LEITURA ou ESCRITA_PEDIDOS e expiraEmDias (1 a 365)." });

        await using var connection = db.OpenConnection();
        var account = await connection.QuerySingleOrDefaultAsync<UsuarioListDto>(@"
            SELECT id_usuario IdUsuario, nome Nome, email Email, perfil Perfil, ativo Ativo
              FROM usuario WHERE id_usuario = @Id LIMIT 1", new { Id = request.IdUsuario });
        if (account is null || !account.Ativo || account.Perfil is not ("FUNCIONARIO" or "VENDEDOR"))
            return Results.BadRequest(new { message = "A chave deve pertencer a um usuário ativo FUNCIONARIO ou VENDEDOR (não ADMIN)." });

        var (token, expires) = await sessions.CreateApiKeyAsync(request.IdUsuario, name, scope, request.ExpiraEmDias);
        // Exibida uma única vez. O banco guarda somente o hash SHA-256.
        return Results.Ok(new { nome = name, idUsuario = request.IdUsuario, escopo = scope,
            token, tokenType = "ApiKey", expiresAtUtc = expires });
    }

    [HttpDelete("{id:int}")]
    public async Task<IResult> Revoke([FromRoute] int id, [FromServices] SessionStore sessions)
    {
        var admin = await Auth.CurrentAsync(HttpContext, sessions);
        if (!Auth.HasRole(admin, "ADMIN")) return Auth.Block(admin);
        if (id <= 0) return Results.BadRequest(new { message = "Identificador inválido." });
        return await sessions.RevokeApiKeyAsync(id)
            ? Results.Ok(new { message = "Chave revogada." })
            : Results.NotFound(new { message = "Chave não encontrada ou já revogada." });
    }
}
