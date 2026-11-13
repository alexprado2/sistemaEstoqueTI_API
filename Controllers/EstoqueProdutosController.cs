using Dapper;
using Npgsql;
using Microsoft.AspNetCore.Mvc;
using SistemaEstoqueTI.Api.Infrastructure;
using SistemaEstoqueTI.Api.Models;

namespace SistemaEstoqueTI.Api.Controllers;

[ApiController]
[Route("api/estoque-produtos")]
public sealed class EstoqueProdutosController : ControllerBase
{
    [HttpGet]
    public async Task<IResult> Get([FromQuery] int? estoqueId, [FromQuery] int? produtoId, [FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (user is null) return Results.Unauthorized();

    await using var conn = db.OpenConnection();
    var data = await conn.QueryAsync<EstoqueProdutoDto>(@"
        SELECT ep.id_estoque_produto IdEstoqueProduto,
               ep.id_produto IdProduto, p.nome Produto,
               ep.id_estoque IdEstoque, e.nome_local Estoque,
               ep.quantidade Quantidade
        FROM estoque_produto ep
        INNER JOIN produto p ON p.id_produto = ep.id_produto
        INNER JOIN estoque e ON e.id_estoque = ep.id_estoque
        WHERE (@EstoqueId IS NULL OR ep.id_estoque = @EstoqueId)
          AND (@ProdutoId IS NULL OR ep.id_produto = @ProdutoId)
        ORDER BY e.nome_local, p.nome", new { EstoqueId = estoqueId, ProdutoId = produtoId });

    return Results.Ok(data);
    }

}