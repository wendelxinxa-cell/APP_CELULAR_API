namespace WebPrototype.Services;

/// <summary>
/// Estado temporário usado para validar o protótipo web sem acessar o SQLite
/// isolado do aplicativo MAUI nem a API que ainda não está configurada para web.
/// Cada sessão interativa recebe seus próprios dados em memória.
/// </summary>
public sealed class DemoPortalState
{
    public const string UsuarioTeste = "teste";
    public const string SenhaTeste = "Teste@123";

    public string? UsuarioLogado { get; private set; }

    public List<MaterialWeb> Materiais { get; } =
    [
        new() { Id = 1, Nome = "Cobre", Unidade = "KG", EstoqueAtual = 24.50m },
        new() { Id = 2, Nome = "Alumínio", Unidade = "KG", EstoqueAtual = 18m },
        new() { Id = 3, Nome = "Ferro", Unidade = "KG", EstoqueAtual = 42.75m },
        new() { Id = 4, Nome = "Fio elétrico", Unidade = "M", EstoqueAtual = 120m },
        new() { Id = 5, Nome = "Óleo", Unidade = "L", EstoqueAtual = 8.25m }
    ];

    public bool Entrar(string usuario, string senha)
    {
        if (!string.Equals(usuario.Trim(), UsuarioTeste, StringComparison.OrdinalIgnoreCase)
            || senha != SenhaTeste)
        {
            return false;
        }

        UsuarioLogado = usuario.Trim();
        return true;
    }

    public void Sair() => UsuarioLogado = null;

    public void Adicionar(string nome, string unidade)
    {
        var proximoId = Materiais.Count == 0 ? 1 : Materiais.Max(item => item.Id) + 1;
        Materiais.Add(new MaterialWeb
        {
            Id = proximoId,
            Nome = nome.Trim(),
            Unidade = unidade,
            EstoqueAtual = 0
        });
    }

    public void Atualizar(MaterialWeb material, string nome, string unidade)
    {
        material.Nome = nome.Trim();
        material.Unidade = unidade;
    }

    public void Excluir(MaterialWeb material) => Materiais.Remove(material);
}

public sealed class MaterialWeb
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Unidade { get; set; } = "KG";
    public decimal EstoqueAtual { get; set; }
}
