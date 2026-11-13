using SistemaEstoqueTI.Api.Models;
namespace SistemaEstoqueTI.Api.Infrastructure;
public static class Validation
{
public static IResult? ValidateProduto(ProdutoRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Nome))
        return Results.BadRequest(new { message = "Informe o nome do produto." });

    if (request.Preco < 0)
        return Results.BadRequest(new { message = "O preço não pode ser negativo." });

    return null;
}

public static IResult? ValidateUsuario(UsuarioRequest request, bool senhaObrigatoria)
{
    if (string.IsNullOrWhiteSpace(request.Nome) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Perfil))
        return Results.BadRequest(new { message = "Nome, email e perfil são obrigatórios." });

    if (senhaObrigatoria && string.IsNullOrWhiteSpace(request.Senha))
        return Results.BadRequest(new { message = "Senha é obrigatória." });

    var perfil = request.Perfil.Trim().ToUpperInvariant();
    if (perfil is not ("ADMIN" or "FUNCIONARIO" or "VENDEDOR"))
        return Results.BadRequest(new { message = "Perfil inválido." });

    return null;
}


}
