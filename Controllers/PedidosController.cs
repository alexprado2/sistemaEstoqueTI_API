using Dapper;
using Npgsql;
using Microsoft.AspNetCore.Mvc;
using SistemaEstoqueTI.Api.Infrastructure;
using SistemaEstoqueTI.Api.Models;

namespace SistemaEstoqueTI.Api.Controllers;

[ApiController]
[Route("api/pedidos")]
public sealed class PedidosController : ControllerBase
{
    [HttpGet]
    public async Task<IResult> Get([FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (user is null) return Results.Unauthorized();

    await using var conn = db.OpenConnection();
    var sql = @"
        SELECT p.id_pedido IdPedido, p.id_usuario IdUsuario, u.nome Usuario,
               p.data_pedido DataPedido, p.status Status,
               COALESCE(SUM(ip.quantidade * ip.preco_unitario), 0) Total,
               COUNT(ip.id_item_pedido) Itens
        FROM pedidos p
        INNER JOIN usuario u ON u.id_usuario = p.id_usuario
        LEFT JOIN itens_pedido ip ON ip.id_pedido = p.id_pedido
        WHERE (@Perfil <> 'VENDEDOR' OR p.id_usuario = @IdUsuario)
        GROUP BY p.id_pedido, p.id_usuario, u.nome, p.data_pedido, p.status
        ORDER BY p.data_pedido DESC";

    var data = await conn.QueryAsync<PedidoDto>(sql, new { user.Perfil, user.IdUsuario });
    return Results.Ok(data);
    }

    [HttpGet("{id:int}")]
    public async Task<IResult> Get_id_int([FromRoute] int id, [FromServices] Db db, [FromServices] SessionStore sessions)
    {

    var user = await Auth.CurrentAsync(HttpContext, sessions);
    if (user is null) return Results.Unauthorized();

    await using var conn = db.OpenConnection();
    var pedido = await conn.QuerySingleOrDefaultAsync<PedidoDto>(@"
        SELECT p.id_pedido IdPedido, p.id_usuario IdUsuario, u.nome Usuario, p.data_pedido DataPedido, p.status Status,
               COALESCE(SUM(ip.quantidade * ip.preco_unitario), 0) Total,
               COUNT(ip.id_item_pedido) Itens
        FROM pedidos p
        INNER JOIN usuario u ON u.id_usuario = p.id_usuario
        LEFT JOIN itens_pedido ip ON ip.id_pedido = p.id_pedido
        WHERE p.id_pedido = @id AND (@Perfil <> 'VENDEDOR' OR p.id_usuario = @IdUsuario)
        GROUP BY p.id_pedido, p.id_usuario, u.nome, p.data_pedido, p.status", new { id, user.Perfil, user.IdUsuario });

    if (pedido is null) return Results.NotFound(new { message = "Pedido não encontrado." });

    var itens = await conn.QueryAsync<ItemPedidoDto>(@"
        SELECT ip.id_item_pedido IdItemPedido, ip.id_pedido IdPedido, ip.id_produto IdProduto, p.nome Produto,
               ip.quantidade Quantidade, ip.preco_unitario PrecoUnitario,
               ip.quantidade * ip.preco_unitario Subtotal
        FROM itens_pedido ip
        INNER JOIN produto p ON p.id_produto = ip.id_produto
        WHERE ip.id_pedido = @id
        ORDER BY p.nome", new { id });

    return Results.Ok(new { pedido, itens });
    }

    [HttpPost]
    public async Task<IResult> Create([FromBody] PedidoRequest request, [FromServices] Db db, [FromServices] SessionStore sessions)
    {
        var user = await Auth.CurrentAsync(HttpContext, sessions);
        if (!Auth.HasRole(user, "ADMIN", "VENDEDOR")) return Auth.Block(user);
        if (request.IdEstoque <= 0) return Results.BadRequest(new { message = "Selecione o estoque de origem." });
        if (request.Itens is null || request.Itens.Count == 0 ||
            request.Itens.Any(i => i.IdProduto <= 0 || i.Quantidade <= 0 || i.PrecoUnitario < 0))
            return Results.BadRequest(new { message = "Itens do pedido inválidos." });

        // Consolidar produtos duplicados impede dupla verificação da mesma quantidade.
        var agrupados = request.Itens.GroupBy(i => i.IdProduto)
            .Select(g => new { IdProduto = g.Key, Quantidade = g.Sum(i => (long)i.Quantidade) })
            .OrderBy(i => i.IdProduto).ToList();
        if (agrupados.Any(i => i.Quantidade > int.MaxValue))
            return Results.BadRequest(new { message = "Quantidade inválida." });

        await using var conn = db.OpenConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM estoque WHERE id_estoque=@id", new { id = request.IdEstoque }, tx) == 0)
                return Results.BadRequest(new { message = "Estoque inexistente." });
            // Locks mantidos até commit: pedidos concorrentes não vendem o mesmo saldo.
            foreach (var item in agrupados)
            {
                var saldo = await conn.ExecuteScalarAsync<int?>(@"
                    SELECT quantidade FROM estoque_produto
                    WHERE id_produto=@IdProduto AND id_estoque=@IdEstoque FOR UPDATE",
                    new { item.IdProduto, IdEstoque = request.IdEstoque }, tx);
                if (saldo is null || saldo.Value < item.Quantidade)
                    {
                        var nomeProduto = await conn.ExecuteScalarAsync<string>(
                            "SELECT nome FROM produto WHERE id_produto = @IdProduto",
                            new { item.IdProduto }, tx
                        );

                        return Results.Conflict(new
                        {
                            message = $"Estoque insuficiente para o produto '{nomeProduto}'. Disponível: {saldo ?? 0}."
                        });
                    }
            }
            var idPedido = await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO pedidos(id_usuario,id_estoque,status) VALUES(@IdUsuario,@IdEstoque,'CONFIRMADO') RETURNING id_pedido;", new { IdUsuario = user!.IdUsuario, request.IdEstoque }, tx);
            foreach (var item in request.Itens)
                await conn.ExecuteAsync(@"
                    INSERT INTO itens_pedido(id_pedido,id_produto,quantidade,preco_unitario)
                    VALUES(@IdPedido,@IdProduto,@Quantidade,@PrecoUnitario)",
                    new { IdPedido = idPedido, item.IdProduto, item.Quantidade, item.PrecoUnitario }, tx);
            foreach (var item in agrupados)
            {
                await conn.ExecuteAsync(@"
                    UPDATE estoque_produto SET quantidade=quantidade-@Quantidade
                    WHERE id_produto=@IdProduto AND id_estoque=@IdEstoque",
                    new { item.IdProduto, item.Quantidade, IdEstoque = request.IdEstoque }, tx);
                await conn.ExecuteAsync(@"
                    INSERT INTO movimentacao_estoque(id_produto,id_estoque,id_usuario,tipo,quantidade)
                    VALUES(@IdProduto,@IdEstoque,@IdUsuario,'SAIDA',@Quantidade)",
                    new { item.IdProduto, item.Quantidade, IdEstoque = request.IdEstoque, IdUsuario = user.IdUsuario }, tx);
            }
            await tx.CommitAsync();
            return Results.Created($"/api/pedidos/{idPedido}", new { idPedido, status = "CONFIRMADO", message = "Pedido criado e estoque atualizado." });
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // Endpoint antigo mantido para informar o novo fluxo aos clientes legados.
    [HttpPut("{id:int}/confirmar")]
    public async Task<IResult> Update__id_int__confirmar([FromRoute] int id, [FromBody] ConfirmarPedidoRequest request, [FromServices] SessionStore sessions)
    {
        var user = await Auth.CurrentAsync(HttpContext, sessions);
        if (!Auth.HasRole(user, "ADMIN", "VENDEDOR")) return Auth.Block(user);
        return Results.Conflict(new { message = "A confirmação não é mais necessária. O pedido é validado e baixado no momento da criação." });
    }

    [HttpPut("{id:int}/cancelar")]
    public async Task<IResult> Update__id_int__cancelar([FromRoute] int id, [FromServices] Db db, [FromServices] SessionStore sessions)
    {
        var user = await Auth.CurrentAsync(HttpContext, sessions);
        if (!Auth.HasRole(user, "ADMIN", "VENDEDOR")) return Auth.Block(user);
        await using var conn = db.OpenConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            var pedido = await conn.QuerySingleOrDefaultAsync<PedidoCancelamento>(@"
                SELECT id_usuario IdUsuario, id_estoque IdEstoque, status Status
                FROM pedidos WHERE id_pedido=@id FOR UPDATE", new { id }, tx);
            if (pedido is null || (user!.Perfil == "VENDEDOR" && pedido.IdUsuario != user.IdUsuario))
                return Results.NotFound(new { message = "Pedido não encontrado." });
            if (pedido.Status == "CANCELADO") return Results.Conflict(new { message = "Pedido já cancelado." });
            if (pedido.Status == "CONFIRMADO")
            {
                if (pedido.IdEstoque is null)
                    return Results.Conflict(new { message = "Pedido antigo sem estoque de origem. Requer ajuste manual antes do cancelamento." });
                var itens = (await conn.QueryAsync<SaldoItem>(@"
                    SELECT id_produto IdProduto, SUM(quantidade) Quantidade FROM itens_pedido
                    WHERE id_pedido=@id GROUP BY id_produto ORDER BY id_produto", new { id }, tx)).ToList();
                foreach (var item in itens)
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO estoque_produto(id_produto,id_estoque,quantidade)
                        VALUES(@IdProduto,@IdEstoque,@Quantidade)
                        ON CONFLICT (id_produto,id_estoque)
                        DO UPDATE SET quantidade=estoque_produto.quantidade+EXCLUDED.quantidade",
                        new { item.IdProduto, IdEstoque = pedido.IdEstoque.Value, item.Quantidade }, tx);
                    await conn.ExecuteAsync(@"
                        INSERT INTO movimentacao_estoque(id_produto,id_estoque,id_usuario,tipo,quantidade)
                        VALUES(@IdProduto,@IdEstoque,@IdUsuario,'ENTRADA',@Quantidade)",
                        new { item.IdProduto, IdEstoque = pedido.IdEstoque.Value, IdUsuario = user.IdUsuario, item.Quantidade }, tx);
                }
            }
            await conn.ExecuteAsync("UPDATE pedidos SET status='CANCELADO' WHERE id_pedido=@id", new { id }, tx);
            await tx.CommitAsync();
            return Results.Ok(new { message = "Pedido cancelado e saldo restituído, quando aplicável." });
        }
        catch { await tx.RollbackAsync(); throw; }
    }
    private sealed class SaldoItem
    {
        public int IdProduto { get; set; }
        public int Quantidade { get; set; }
    }
    private sealed class PedidoCancelamento
    {
        public int IdUsuario { get; set; }
        public int? IdEstoque { get; set; }
        public string Status { get; set; } = "";
    }

}