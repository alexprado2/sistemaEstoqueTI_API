using Dapper;
using Npgsql;
using Microsoft.AspNetCore.Mvc;
using SistemaEstoqueTI.Api.Infrastructure;
using SistemaEstoqueTI.Api.Models;

namespace SistemaEstoqueTI.Api.Controllers;

[ApiController]
[Route("api/produtos")]
public sealed class ProdutosController : ControllerBase
{
    [HttpGet]
    public async Task<IResult> Get([FromQuery] string? busca, [FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (user is null) return Results.Unauthorized();

    await using var conn = db.OpenConnection();
    var sql = @"
        SELECT id_produto IdProduto, nome Nome, descricao Descricao, preco Preco, categoria Categoria,
               codigo_barras CodigoBarras, criado_em CriadoEm
        FROM produto
        WHERE @Busca IS NULL OR @Busca = '' OR nome ILIKE '%' || @Busca || '%'
           OR categoria ILIKE '%' || @Busca || '%' OR codigo_barras ILIKE '%' || @Busca || '%'
        ORDER BY nome";

    var data = await conn.QueryAsync<ProdutoDto>(sql, new { Busca = busca });
    return Results.Ok(data);
    }

    [HttpGet("{id:int}")]
    public async Task<IResult> Get_id_int([FromRoute] int id, [FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (user is null) return Results.Unauthorized();

    await using var conn = db.OpenConnection();
    var item = await conn.QuerySingleOrDefaultAsync<ProdutoDto>(@"
        SELECT id_produto IdProduto, nome Nome, descricao Descricao, preco Preco, categoria Categoria,
               codigo_barras CodigoBarras, criado_em CriadoEm
        FROM produto WHERE id_produto = @id", new { id });

    return item is null ? Results.NotFound(new { message = "Produto não encontrado." }) : Results.Ok(item);
    }

    [HttpPost]
    public async Task<IResult> Create([FromBody] ProdutoRequest request, [FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (!Auth.HasRole(user, "ADMIN")) return Auth.Block(user);

    var validation = Validation.ValidateProduto(request);
    if (validation is not null) return validation;

    await using var conn = db.OpenConnection();
    var id = await conn.ExecuteScalarAsync<int>(@"
        INSERT INTO produto (nome, descricao, preco, categoria, codigo_barras)
        VALUES (@Nome, @Descricao, @Preco, @Categoria, @CodigoBarras) RETURNING id_produto;", request);

    return Results.Created($"/api/produtos/{id}", new { idProduto = id });
    }

    [HttpPut("{id:int}")]
    public async Task<IResult> Update_id_int([FromRoute] int id, [FromBody] ProdutoRequest request, [FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (!Auth.HasRole(user, "ADMIN")) return Auth.Block(user);

    var validation = Validation.ValidateProduto(request);
    if (validation is not null) return validation;

    await using var conn = db.OpenConnection();
    var rows = await conn.ExecuteAsync(@"
        UPDATE produto
           SET nome = @Nome, descricao = @Descricao, preco = @Preco, categoria = @Categoria, codigo_barras = @CodigoBarras
         WHERE id_produto = @Id", new { request.Nome, request.Descricao, request.Preco, request.Categoria, request.CodigoBarras, Id = id });

    return rows == 0 ? Results.NotFound(new { message = "Produto não encontrado." }) : Results.Ok(new { message = "Produto atualizado." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IResult> Delete_id_int([FromRoute] int id, [FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (!Auth.HasRole(user, "ADMIN")) return Auth.Block(user);

    await using var conn = db.OpenConnection();
    try
    {
        var rows = await conn.ExecuteAsync("DELETE FROM produto WHERE id_produto = @id", new { id });
        return rows == 0 ? Results.NotFound(new { message = "Produto não encontrado." }) : Results.Ok(new { message = "Produto removido." });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
    {
        return Results.Conflict(new { message = "Não é possível excluir: produto possui estoque, movimentações ou pedidos vinculados." });
    }
    }

}