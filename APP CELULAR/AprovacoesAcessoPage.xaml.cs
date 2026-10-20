using APP_CELULAR.Services;

namespace APP_CELULAR;

public partial class AprovacoesAcessoPage : ContentPage
{
    private readonly MasterEmpresasApiService _api = new();
    private string? _tokenZeus;

    public AprovacoesAcessoPage() => InitializeComponent();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!SessaoUsuario.EhDeus)
        {
            await DisplayAlert("Acesso negado", "Somente Zeus pode abrir a tela de aprovações.", "OK");
            await Navigation.PopAsync();
            return;
        }
        await Carregar();
    }

    private async Task Carregar()
    {
        try
        {
            _tokenZeus = SessaoUsuario.TokenMaster ?? throw new InvalidOperationException("Entre novamente como Administrador Master.");
            var pendentes = await _api.ObterAprovacoesPendentes(_tokenZeus);

            var usuariosPendentes = pendentes.Usuarios.ToDictionary(x => (x.EmpresaId, x.Id));
            var linhas = pendentes.Dispositivos
                .Where(x => x.UsuarioId.HasValue)
                .Select(x =>
                {
                    usuariosPendentes.TryGetValue((x.EmpresaId, x.UsuarioId!.Value), out var usuarioPendente);
                    return new SolicitacaoLinha(x.Id, x.EmpresaId, x.UsuarioId.Value,
                        usuarioPendente?.Id, x.Usuario, x.Nome,
                        string.Equals(x.Descricao, "Novo aparelho", StringComparison.OrdinalIgnoreCase)
                            ? "Novo vínculo pendente"
                            : "Troca de aparelho pendente",
                        x.Empresa, x.CriadoEm);
                })
                .OrderBy(x => x.CriadoEm ?? DateTime.MinValue)
                .ToList();
            Solicitacoes.ItemsSource = linhas;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Aprovações", $"Não foi possível carregar as solicitações. Confira se a API está atualizada e se o administrador está conectado.\n\n{ex.Message}", "OK");
        }
    }

    private async void Aprovar_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not SolicitacaoLinha item) return;

        string acao = item.UsuarioPendenteId.HasValue
            ? "aprovar o usuário e vincular este aparelho"
            : "aprovar este aparelho para o usuário";
        string escopo = $"{item.UsuarioExibicao}\n{item.DispositivoExibicao}\n{item.Contexto}\n\nO aparelho anterior deste usuário será desativado.";
        if (!await DisplayAlert("Confirmar vínculo", $"Deseja {acao}?\n\n{escopo}", "Aprovar vínculo", "Cancelar")) return;

        try
        {
            if (item.UsuarioPendenteId.HasValue)
                await _api.AprovarAcessoUsuario(_tokenZeus!, item.EmpresaId, item.UsuarioPendenteId.Value);
            await _api.AprovarAcessoDispositivo(_tokenZeus!, item.EmpresaId, item.DispositivoId);
            await Carregar();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Aprovação", ex.Message, "OK");
        }
    }

    private async void Atualizar_Refreshing(object sender, EventArgs e)
    {
        await Carregar();
        RefreshAprovacoes.IsRefreshing = false;
    }

    private async void Voltar_Clicked(object sender, EventArgs e) => await Navigation.PopAsync();

    private sealed class SolicitacaoLinha
    {
        public long DispositivoId { get; }
        public long EmpresaId { get; }
        public long UsuarioId { get; }
        public long? UsuarioPendenteId { get; }
        public string Tipo { get; }
        public string UsuarioExibicao { get; }
        public string DispositivoExibicao { get; }
        public string Contexto { get; }
        public DateTime? CriadoEm { get; }
        public string Acao => "Aprovar vínculo";

        public SolicitacaoLinha(long dispositivoId, long empresaId, long usuarioId, long? usuarioPendenteId, string usuario,
            string? dispositivo, string tipo, string contexto, DateTime? criadoEm)
        {
            DispositivoId = dispositivoId;
            EmpresaId = empresaId;
            UsuarioId = usuarioId;
            UsuarioPendenteId = usuarioPendenteId;
            Tipo = tipo;
            UsuarioExibicao = $"Usuário: {usuario}";
            DispositivoExibicao = string.IsNullOrWhiteSpace(dispositivo)
                ? "Aparelho: ainda não solicitado"
                : $"Aparelho: {dispositivo}";
            Contexto = contexto;
            CriadoEm = criadoEm;
        }
    }
}
