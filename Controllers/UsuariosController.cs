using Dapper;
using Npgsql;
using Microsoft.AspNetCore.Mvc;
using SistemaEstoqueTI.Api.Infrastructure;
using SistemaEstoqueTI.Api.Models;

namespace SistemaEstoqueTI.Api.Controllers;

[ApiController]
[Route("api/usuarios")]
public sealed class UsuariosController : ControllerBase
{
    [HttpGet]
    public async Task<IResult> Get([FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (!Auth.HasRole(user, "ADMIN")) return Auth.Block(user);

    await using var conn = db.OpenConnection();
    var data = await conn.QueryAsync<UsuarioListDto>(@"
        SELECT id_usuario IdUsuario, nome Nome, email Email, perfil Perfil, ativo Ativo
        FROM usuario ORDER BY nome");

    return Results.Ok(data);
    }

    [HttpPost]
    public async Task<IResult> Create([FromBody] UsuarioRequest request, [FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (!Auth.HasRole(user, "ADMIN")) return Auth.Block(user);

    var validation = Validation.ValidateUsuario(request, true);
    if (validation is not null) return validation;

    await using var conn = db.OpenConnection();
    try
    {
        var id = await conn.ExecuteScalarAsync<int>(@"
            INSERT INTO usuario (nome, email, senha, perfil, ativo)
            VALUES (@Nome, @Email, @SenhaHash, @Perfil, @Ativo) RETURNING id_usuario;", new
        {
            request.Nome,
            Email = request.Email.Trim(),
            SenhaHash = PasswordHasher.Hash(request.Senha!),
            Perfil = request.Perfil!.Trim().ToUpperInvariant(),
            request.Ativo
        });

        return Results.Created($"/api/usuarios/{id}", new { idUsuario = id });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        return Results.Conflict(new { message = "Este email já está cadastrado." });
    }
    }

    [HttpPut("{id:int}")]
    public async Task<IResult> Update_id_int([FromRoute] int id, [FromBody] UsuarioRequest request, [FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (!Auth.HasRole(user, "ADMIN")) return Auth.Block(user);

    var validation = Validation.ValidateUsuario(request, false);
    if (validation is not null) return validation;

    await using var conn = db.OpenConnection();
    try
    {
        var rows = await conn.ExecuteAsync(@"
            UPDATE usuario
               SET nome = @Nome, email = @Email, perfil = @Perfil, ativo = @Ativo
             WHERE id_usuario = @Id", new
        {
            request.Nome,
            Email = request.Email.Trim(),
            Perfil = request.Perfil!.Trim().ToUpperInvariant(),
            request.Ativo,
            Id = id
        });

        if (!string.IsNullOrWhiteSpace(request.Senha))
        {
            await conn.ExecuteAsync("UPDATE usuario SET senha = @Senha WHERE id_usuario = @Id",
                new { Senha = PasswordHasher.Hash(request.Senha), Id = id });
        }

        if (rows != 0) await sessions.RevokeUserAsync(id);
        return rows == 0 ? Results.NotFound(new { message = "Usuário não encontrado." }) : Results.Ok(new { message = "Usuário atualizado." });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        return Results.Conflict(new { message = "Este email já está cadastrado." });
    }
    }

    [HttpDelete("{id:int}")]
    public async Task<IResult> Delete_id_int([FromRoute] int id, [FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (!Auth.HasRole(user, "ADMIN")) return Auth.Block(user);

    if (user!.IdUsuario == id)
        return Results.BadRequest(new { message = "Você não pode desativar o próprio usuário logado." });

    await using var conn = db.OpenConnection();
    var rows = await conn.ExecuteAsync("UPDATE usuario SET ativo = FALSE WHERE id_usuario = @id", new { id });
    if (rows != 0) await sessions.RevokeUserAsync(id);
    return rows == 0 ? Results.NotFound(new { message = "Usuário não encontrado." }) : Results.Ok(new { message = "Usuário desativado." });
    }

}