namespace APP_CELULAR.Models;

public class ClienteChequeResumo
{
    public int PessoaId { get; set; }

    public string ClienteNome { get; set; } = "";

    public int QuantidadeCheques { get; set; }

    public decimal TotalAberto { get; set; }

    public string QuantidadeTexto =>
        $"{QuantidadeCheques} cheque(s)";

    public string TotalTexto =>
        $"Saldo R$ {TotalAberto:N2}";
}
