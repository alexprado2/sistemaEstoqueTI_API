using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SistemaEstoqueTI.Api.Infrastructure;
using SistemaEstoqueTI.Api.Models;

namespace SistemaEstoqueTI.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("auth-login")]
    public async Task<IResult> Login([FromBody] LoginRequest request,
        [FromServices] Db db, [FromServices] SessionStore sessions)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Senha))
            return Results.BadRequest(new { message = "Informe email e senha." });

        await using var conn = db.OpenConnection();
        var user = await conn.QuerySingleOrDefaultAsync<UsuarioDto>(@"
            SELECT id_usuario IdUsuario, nome Nome, email Email, senha Senha, perfil Perfil, ativo Ativo
              FROM usuario WHERE email = @Email LIMIT 1", new { Email = request.Email.Trim() });
        if (user is null || !user.Ativo || !PasswordHasher.Verify(request.Senha, user.Senha))
            return Results.Unauthorized();

        var session = new SessionUser(user.IdUsuario, user.Nome, user.Email, user.Perfil);
        var (token, expiresAtUtc) = await sessions.CreateAsync(session);
        return Results.Ok(new { user = session, token, tokenType = "ApiKey", expiresAtUtc });
    }

    [HttpPost("logout")]
    public async Task<IResult> Logout([FromServices] SessionStore sessions)
    {
        var token = Auth.ApiKeyToken(HttpContext);
        // Somente a chave de login do usuário pode executar logout.
        if (token is null || await sessions.GetSessionAsync(token) is null)
            return Results.Unauthorized();
        await sessions.RemoveAsync(token);
        return Results.Ok(new { message = "Logout realizado." });
    }

    [HttpGet("me")]
    public async Task<IResult> Me([FromServices] SessionStore sessions)
    {
        var token = Auth.ApiKeyToken(HttpContext);
        var user = token is null ? null : await sessions.GetSessionAsync(token);
        return user is null ? Results.Unauthorized() : Results.Ok(new { user });
    }
}
