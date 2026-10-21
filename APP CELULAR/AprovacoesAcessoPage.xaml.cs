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
        var erros = new List<string>();
        _tokenZeus = SessaoUsuario.TokenMaster;
        if (string.IsNullOrWhiteSpace(_tokenZeus))
        {
            await DisplayAlert("Aprovações", "Entre novamente como Administrador Master.", "OK");
            return;
        }

        AprovacoesAcessoResponse pendentes = new();
        List<CadastroPendente> cadastros = new();
        try { pendentes = await _api.ObterAprovacoesPendentes(_tokenZeus); }
        catch (Exception ex) { erros.Add($"Dispositivos: {ex.Message}"); }
        try { cadastros = await _api.ListarCadastrosPendentes(_tokenZeus); }
        catch (Exception ex) { erros.Add($"Cadastros Web: {ex.Message}"); }

        try
        {
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
            linhas.AddRange(cadastros.Select(c => SolicitacaoLinha.Cadastro(c)));
            Solicitacoes.ItemsSource = linhas.OrderBy(x => x.CriadoEm ?? DateTime.MinValue).ToList();
            if (erros.Count > 0)
                await DisplayAlert("Aprovações carregadas parcialmente", string.Join("\n\n", erros), "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Aprovações", $"Não foi possível exibir as solicitações.\n\n{ex.Message}", "OK");
        }
    }

    private async void Aprovar_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.BindingContext is not SolicitacaoLinha item) return;

        if (item.CadastroId.HasValue)
        {
            await TratarCadastro(item);
            return;
        }

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

    private async Task TratarCadastro(SolicitacaoLinha item)
    {
        if (!item.EmailVerificado)
        {
            await DisplayAlert("E-mail pendente", "Este cadastro só pode ser aprovado depois que a pessoa confirmar o e-mail. O pedido continuará visível aqui.", "OK");
            return;
        }

        var decisao = await DisplayActionSheet($"Cadastro: {item.NomeCadastro}", "Cancelar", "Recusar cadastro", "Aprovar cadastro");
        if (decisao == "Recusar cadastro")
        {
            if (!await DisplayAlert("Recusar cadastro", $"Deseja recusar o pedido de {item.NomeCadastro} ({item.EmailCadastro})?", "Recusar", "Voltar")) return;
            try { await _api.RecusarCadastro(_tokenZeus!, item.CadastroId!.Value); await Carregar(); }
            catch (Exception ex) { await DisplayAlert("Cadastro", ex.Message, "OK"); }
            return;
        }
        if (decisao != "Aprovar cadastro") return;

        try
        {
            var empresas = (await _api.Listar(_tokenZeus!)).Where(x => x.Status.Equals("ATIVA", StringComparison.OrdinalIgnoreCase)).ToList();
            if (empresas.Count == 0) { await DisplayAlert("Cadastro", "Não há empresas ativas disponíveis.", "OK"); return; }
            var empresaEscolhida = await DisplayActionSheet("Selecione a empresa", "Cancelar", null, empresas.Select(x => $"{x.Id} - {x.Nome}").ToArray());
            var empresa = empresas.FirstOrDefault(x => $"{x.Id} - {x.Nome}" == empresaEscolhida);
            if (empresa is null) return;

            var usuarios = (await _api.ObterStatusUsuarios(_tokenZeus!))
                .Where(x => x.EmpresaId == empresa.Id && x.Aprovado && !x.Bloqueado).ToList();
            if (usuarios.Count == 0) { await DisplayAlert("Cadastro", "A empresa selecionada não tem usuários CATCHAT ativos para vincular.", "OK"); return; }
            var usuarioEscolhido = await DisplayActionSheet("Selecione o usuário CATCHAT", "Cancelar", null, usuarios.Select(x => $"{x.Id} - {x.Nome}").ToArray());
            var usuario = usuarios.FirstOrDefault(x => $"{x.Id} - {x.Nome}" == usuarioEscolhido);
            if (usuario is null) return;

            var tipo = await DisplayActionSheet("Tipo de acesso", "Cancelar", null, "Comercial", "Chat");
            if (tipo is not ("Comercial" or "Chat")) return;
            if (!await DisplayAlert("Confirmar aprovação", $"{item.NomeCadastro} ({item.EmailCadastro})\nEmpresa: {empresa.Nome}\nUsuário: {usuario.Nome}\nAcesso: {tipo}\n\nO acesso será liberado após esta aprovação.", "Aprovar", "Cancelar")) return;

            await _api.AprovarCadastro(_tokenZeus!, item.CadastroId!.Value, empresa.Id, usuario.Id, tipo == "Chat" ? "chat" : "comercial");
            await Carregar();
        }
        catch (Exception ex) { await DisplayAlert("Cadastro", ex.Message, "OK"); }
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
        public long? CadastroId { get; }
        public long EmpresaId { get; }
        public long UsuarioId { get; }
        public long? UsuarioPendenteId { get; }
        public string Tipo { get; }
        public string UsuarioExibicao { get; }
        public string DispositivoExibicao { get; }
        public string Contexto { get; }
        public DateTime? CriadoEm { get; }
        public bool EmailVerificado { get; }
        public string? NomeCadastro { get; }
        public string? EmailCadastro { get; }
        public string Acao => CadastroId.HasValue ? "Revisar" : "Aprovar vínculo";

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

        private SolicitacaoLinha(CadastroPendente c)
        {
            CadastroId = c.Id;
            EmailVerificado = c.EmailVerificado;
            NomeCadastro = c.Nome;
            EmailCadastro = c.Email;
            Tipo = "Cadastro pelo Web";
            UsuarioExibicao = $"Pessoa: {c.Nome}";
            DispositivoExibicao = $"E-mail: {c.Email}";
            Contexto = c.EmailVerificado ? "E-mail confirmado · aguardando aprovação do Zeus" : "E-mail ainda não confirmado · aprovação bloqueada";
            CriadoEm = c.CriadaEm;
            EmpresaId = 0;
            UsuarioId = 0;
        }

        public static SolicitacaoLinha Cadastro(CadastroPendente c) => new(c);
    }
}
