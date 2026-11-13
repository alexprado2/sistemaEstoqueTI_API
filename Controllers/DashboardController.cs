using Dapper;
using Npgsql;
using Microsoft.AspNetCore.Mvc;
using SistemaEstoqueTI.Api.Infrastructure;
using SistemaEstoqueTI.Api.Models;

namespace SistemaEstoqueTI.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController : ControllerBase
{
    [HttpGet("resumo")]
    public async Task<IResult> Get_resumo([FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (user is null) return Results.Unauthorized();

    await using var conn = db.OpenConnection();
    var resumo = new
    {
        produtos = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM produto"),
        locaisEstoque = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM estoque"),
        quantidadeTotal = await conn.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantidade), 0) FROM estoque_produto"),
        pedidosAtivos = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pedidos WHERE status IN ('ABERTO', 'CONFIRMADO')"),
        movimentacoesHoje = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM movimentacao_estoque WHERE DATE(data_movimentacao) = CURRENT_DATE")
    };

    return Results.Ok(resumo);
    }

}