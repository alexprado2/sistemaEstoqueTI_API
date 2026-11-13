using Dapper;
using Npgsql;
using Microsoft.AspNetCore.Mvc;
using SistemaEstoqueTI.Api.Infrastructure;
using SistemaEstoqueTI.Api.Models;

namespace SistemaEstoqueTI.Api.Controllers;

[ApiController]
[Route("api/estoques")]
public sealed class EstoquesController : ControllerBase
{
    [HttpGet]
    public async Task<IResult> Get([FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (user is null) return Results.Unauthorized();

    await using var conn = db.OpenConnection();
    var data = await conn.QueryAsync<EstoqueDto>(@"
        SELECT id_estoque IdEstoque, nome_local NomeLocal, descricao Descricao
        FROM estoque ORDER BY nome_local");

    return Results.Ok(data);
    }

    [HttpPost]
    public async Task<IResult> Create([FromBody] EstoqueRequest request, [FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (!Auth.HasRole(user, "ADMIN", "FUNCIONARIO")) return Auth.Block(user);

    if (string.IsNullOrWhiteSpace(request.NomeLocal))
        return Results.BadRequest(new { message = "Informe o nome do local de estoque." });

    await using var conn = db.OpenConnection();
    var id = await conn.ExecuteScalarAsync<int>(@"
        INSERT INTO estoque (nome_local, descricao) VALUES (@NomeLocal, @Descricao) RETURNING id_estoque;", request);

    return Results.Created($"/api/estoques/{id}", new { idEstoque = id });
    }

    [HttpPut("{id:int}")]
    public async Task<IResult> Update_id_int([FromRoute] int id, [FromBody] EstoqueRequest request, [FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (!Auth.HasRole(user, "ADMIN", "FUNCIONARIO")) return Auth.Block(user);

    if (string.IsNullOrWhiteSpace(request.NomeLocal))
        return Results.BadRequest(new { message = "Informe o nome do local de estoque." });

    await using var conn = db.OpenConnection();
    var rows = await conn.ExecuteAsync(@"
        UPDATE estoque SET nome_local = @NomeLocal, descricao = @Descricao WHERE id_estoque = @Id",
        new { request.NomeLocal, request.Descricao, Id = id });

    return rows == 0 ? Results.NotFound(new { message = "Estoque não encontrado." }) : Results.Ok(new { message = "Estoque atualizado." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IResult> Delete_id_int([FromRoute] int id, [FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (!Auth.HasRole(user, "ADMIN")) return Auth.Block(user);

    await using var conn = db.OpenConnection();
    try
    {
        var rows = await conn.ExecuteAsync("DELETE FROM estoque WHERE id_estoque = @id", new { id });
        return rows == 0 ? Results.NotFound(new { message = "Estoque não encontrado." }) : Results.Ok(new { message = "Estoque removido." });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
    {
        return Results.Conflict(new { message = "Não é possível excluir: estoque possui produtos ou movimentações vinculadas." });
    }
    }

}