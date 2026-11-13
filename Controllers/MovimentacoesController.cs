using Dapper;
using Npgsql;
using Microsoft.AspNetCore.Mvc;
using SistemaEstoqueTI.Api.Infrastructure;
using SistemaEstoqueTI.Api.Models;

namespace SistemaEstoqueTI.Api.Controllers;

[ApiController]
[Route("api/movimentacoes")]
public sealed class MovimentacoesController : ControllerBase
{
    [HttpGet]
    public async Task<IResult> Get([FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (user is null) return Results.Unauthorized();

    await using var conn = db.OpenConnection();
    var data = await conn.QueryAsync<MovimentacaoDto>(@"
        SELECT m.id_movimentacao IdMovimentacao, m.id_produto IdProduto, p.nome Produto,
               m.id_estoque IdEstoque, e.nome_local Estoque, m.id_usuario IdUsuario, u.nome Usuario,
               m.tipo Tipo, m.quantidade Quantidade, m.data_movimentacao DataMovimentacao
        FROM movimentacao_estoque m
        INNER JOIN produto p ON p.id_produto = m.id_produto
        INNER JOIN estoque e ON e.id_estoque = m.id_estoque
        INNER JOIN usuario u ON u.id_usuario = m.id_usuario
        ORDER BY m.data_movimentacao DESC
        LIMIT 80");

    return Results.Ok(data);
    }

    [HttpPost]
    public async Task<IResult> Create([FromBody] MovimentacaoRequest request, [FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (!Auth.HasRole(user, "ADMIN", "FUNCIONARIO")) return Auth.Block(user);

    if (request.IdProduto <= 0 || request.IdEstoque <= 0 || request.Quantidade <= 0)
        return Results.BadRequest(new { message = "Produto, estoque e quantidade são obrigatórios." });

    var tipo = request.Tipo?.Trim().ToUpperInvariant();
    if (tipo is not ("ENTRADA" or "SAIDA"))
        return Results.BadRequest(new { message = "Tipo deve ser ENTRADA ou SAIDA." });

    await using var conn = db.OpenConnection();
    await conn.OpenAsync();
    await using var tx = await conn.BeginTransactionAsync();

    try
    {
        await conn.ExecuteAsync(@"
            INSERT INTO estoque_produto (id_produto, id_estoque, quantidade)
            VALUES (@IdProduto, @IdEstoque, 0)
            ON CONFLICT (id_produto, id_estoque) DO NOTHING",
            new { request.IdProduto, request.IdEstoque }, tx);

        var atual = await conn.ExecuteScalarAsync<int>(@"
            SELECT quantidade FROM estoque_produto WHERE id_produto = @IdProduto AND id_estoque = @IdEstoque FOR UPDATE",
            new { request.IdProduto, request.IdEstoque }, tx);

        if (tipo == "SAIDA" && atual < request.Quantidade)
            return Results.Conflict(new { message = $"Quantidade insuficiente. Disponível: {atual}." });

        var novaQuantidade = tipo == "ENTRADA" ? atual + request.Quantidade : atual - request.Quantidade;
        await conn.ExecuteAsync(@"
            UPDATE estoque_produto SET quantidade = @NovaQuantidade
            WHERE id_produto = @IdProduto AND id_estoque = @IdEstoque",
            new { NovaQuantidade = novaQuantidade, request.IdProduto, request.IdEstoque }, tx);

        await conn.ExecuteAsync(@"
            INSERT INTO movimentacao_estoque (id_produto, id_estoque, id_usuario, tipo, quantidade)
            VALUES (@IdProduto, @IdEstoque, @IdUsuario, @Tipo, @Quantidade)",
            new { request.IdProduto, request.IdEstoque, IdUsuario = user!.IdUsuario, Tipo = tipo, request.Quantidade }, tx);

        await tx.CommitAsync();
        return Results.Ok(new { message = "Movimentação registrada.", quantidadeAtual = novaQuantidade });
    }
    catch
    {
        await tx.RollbackAsync();
        throw;
    }
    }

}