namespace SistemaEstoqueTI.Api.Models;
public sealed record SessionUser(int IdUsuario, string Nome, string Email, string Perfil);
public sealed record LoginRequest(string Email, string Senha);
public sealed record ApiKeyCreateRequest(string? Nome, int IdUsuario, string? Escopo, int ExpiraEmDias);
public sealed class ApiKeyInfo
{
    public int IdChave { get; set; }
    public string Nome { get; set; } = "";
    public int IdUsuario { get; set; }
    public string Usuario { get; set; } = "";
    public string Escopo { get; set; } = "";
    public DateTime CriadaEmUtc { get; set; }
    public DateTime ExpiraEmUtc { get; set; }
    public DateTime? RevogadaEmUtc { get; set; }
}

public sealed record ProdutoRequest(string Nome, string? Descricao, decimal Preco, string? Categoria, string? CodigoBarras);
public sealed record EstoqueRequest(string NomeLocal, string? Descricao);
public sealed record MovimentacaoRequest(int IdProduto, int IdEstoque, string Tipo, int Quantidade);
public sealed record PedidoItemRequest(int IdProduto, int Quantidade, decimal PrecoUnitario);
public sealed record PedidoRequest(int IdEstoque, List<PedidoItemRequest> Itens);
public sealed record ConfirmarPedidoRequest(int IdEstoque);
public sealed record UsuarioRequest(string Nome, string Email, string? Senha, string? Perfil, bool Ativo);

public sealed class UsuarioDto
{
    public int IdUsuario { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Senha { get; set; } = string.Empty;
    public string Perfil { get; set; } = string.Empty;
    public bool Ativo { get; set; }
}

public sealed class UsuarioListDto
{
    public int IdUsuario { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Perfil { get; set; } = string.Empty;
    public bool Ativo { get; set; }
}

public sealed class ProdutoDto
{
    public int IdProduto { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public decimal Preco { get; set; }
    public string? Categoria { get; set; }
    public string? CodigoBarras { get; set; }
    public DateTime CriadoEm { get; set; }
}

public sealed class EstoqueDto
{
    public int IdEstoque { get; set; }
    public string NomeLocal { get; set; } = string.Empty;
    public string? Descricao { get; set; }
}

public sealed class EstoqueProdutoDto
{
    public int IdEstoqueProduto { get; set; }
    public int IdProduto { get; set; }
    public string Produto { get; set; } = string.Empty;
    public int IdEstoque { get; set; }
    public string Estoque { get; set; } = string.Empty;
    public int Quantidade { get; set; }
}

public sealed class MovimentacaoDto
{
    public int IdMovimentacao { get; set; }
    public int IdProduto { get; set; }
    public string Produto { get; set; } = string.Empty;
    public int IdEstoque { get; set; }
    public string Estoque { get; set; } = string.Empty;
    public int IdUsuario { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public int Quantidade { get; set; }
    public DateTime DataMovimentacao { get; set; }
}

public sealed class PedidoDto
{
    public int IdPedido { get; set; }
    public int IdUsuario { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public DateTime DataPedido { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int Itens { get; set; }
}

public sealed class ItemPedidoDto
{
    public int IdItemPedido { get; set; }
    public int IdPedido { get; set; }
    public int IdProduto { get; set; }
    public string Produto { get; set; } = string.Empty;
    public int Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal Subtotal { get; set; }
}
