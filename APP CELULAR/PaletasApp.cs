namespace APP_CELULAR;

public sealed class PaletaApp
{
    public required string Id { get; init; }
    public required string Nome { get; init; }
    public required string Descricao { get; init; }
    public required Color Primaria { get; init; }
    public required Color Suave { get; init; }
    public required Color Fundo { get; init; }
    public required Color Superficie { get; init; }
    public required Color BordaPadrao { get; init; }
    public required Color Texto { get; init; }
    public required Color TextoSecundario { get; init; }
    public required Color BalaoOutro { get; init; }
    public required Color BalaoVoce { get; init; }
    public required Color TextoOutro { get; init; }
    public required Color TextoVoce { get; init; }
    public required Color Accent { get; init; }
    public bool Selecionada { get; set; }
    public Color Borda => Selecionada ? Primaria : BordaPadrao;
    public string Indicador => Selecionada ? "✓ Em uso" : "Toque para aplicar";
}

public static class PaletasAppService
{
    private static readonly IReadOnlyList<PaletaApp> Paletas =
    [
        Criar("clean", "Minimalista Clean", "Clara, limpa e objetiva", "#3B82F6", "#EEF4FB", "#F5F6F8", "#FFFFFF", "#DCE2E9", "#252B34", "#707985", "#FFFFFF", "#3B82F6", "#252B34", "#FFFFFF", "#2583E8"),
        Criar("noite", "Noite Acolhedora", "Modo escuro com contraste suave", "#4B6BFA", "#25272B", "#17191C", "#222428", "#3A3D43", "#F5F6F8", "#B9BDC5", "#303238", "#315EF5", "#F5F6F8", "#FFFFFF", "#4B6BFA"),
        Criar("natureza", "Natureza Calma", "Tons naturais e equilibrados", "#4F9C82", "#D9EDE3", "#F3F8F4", "#FFFFFF", "#C9DED2", "#29483B", "#647D70", "#E5F1E8", "#247044", "#29483B", "#FFFFFF", "#4F9C82"),
        Criar("pastel", "Pastel Sonhador", "Suave e acolhedora", "#D8B6EA", "#F2E6F6", "#FFF7FB", "#FFFFFF", "#EAD7EF", "#493D50", "#806F88", "#F1E7F5", "#B8DBE8", "#493D50", "#FFFFFF", "#D47BB8"),
        Criar("aquario", "Aquário Fresco", "Ciano leve com contraste marinho", "#26BED5", "#D2F2F7", "#EAF9FC", "#FFFFFF", "#B6E5ED", "#17414A", "#5D8088", "#DDF5F7", "#079BAE", "#17414A", "#FFFFFF", "#20B9C9"),
        Criar("solar", "Solar e Acolhedor", "Creme, pêssego e laranja suave", "#F4B567", "#FFF0D1", "#FFF9EA", "#FFFEF8", "#F0DAB0", "#514235", "#8C7966", "#FFF1D5", "#F17C58", "#514235", "#FFFFFF", "#E88952"),
        Criar("lavanda", "Lavanda Suave", "Lavanda clara com roxo vibrante", "#C9A6D8", "#F0E4F5", "#FBF5FC", "#FFFFFF", "#E2CDEA", "#442D4D", "#826C8A", "#F1E6F5", "#941FB0", "#442D4D", "#FFFFFF", "#A62DC2"),
        Criar("terra-rosa", "Terra Rosa", "Rosa queimado com vermelho acolhedor", "#D83A43", "#F5D7DB", "#FFF1F2", "#FFFFFF", "#E8BFC4", "#4B2D31", "#89696D", "#F7E0E3", "#E94E5C", "#4B2D31", "#FFFFFF", "#D93643"),
        Criar("oceano-profundo", "Oceano Profundo", "Azul-petróleo escuro e turquesa", "#0BA7BC", "#263F46", "#142B32", "#1C363D", "#36545B", "#E6F3F4", "#A7C0C4", "#2A464D", "#087F91", "#EDF8F8", "#FFFFFF", "#B3D82E"),
        Criar("coral-vibrante", "Coral Vibrante", "Coral, menta e amarelo solar", "#F2A66A", "#FFE7D5", "#FFF5E9", "#FFFDF9", "#F0D4C4", "#4C3A33", "#8B756B", "#FFEADF", "#159B8A", "#4C3A33", "#FFFFFF", "#F2C844")
    ];

    public static IReadOnlyList<PaletaApp> ObterPaletas(string? selecionada = null) =>
        Paletas.Select(p => new PaletaApp
        {
            Id = p.Id, Nome = p.Nome, Descricao = p.Descricao,
            Primaria = p.Primaria, Suave = p.Suave, Fundo = p.Fundo, Superficie = p.Superficie,
            BordaPadrao = p.BordaPadrao, Texto = p.Texto, TextoSecundario = p.TextoSecundario,
            BalaoOutro = p.BalaoOutro, BalaoVoce = p.BalaoVoce, TextoOutro = p.TextoOutro,
            TextoVoce = p.TextoVoce, Accent = p.Accent,
            Selecionada = string.Equals(p.Id, NormalizarId(selecionada), StringComparison.OrdinalIgnoreCase)
        }).ToList();

    public static void AplicarPreferenciaAtual()
    {
        var paleta = ObterPaleta(Preferences.Default.Get(ChavePreferencia(), "clean"));
        var recursos = Application.Current?.Resources;
        if (recursos is null) return;
        recursos["Primary"] = paleta.Primaria;
        recursos["PrimaryDark"] = paleta.Primaria;
        recursos["Secondary"] = paleta.Suave;
        recursos["Tertiary"] = paleta.Accent;
        recursos["BrandPrimary"] = paleta.Primaria;
        recursos["BrandAccent"] = paleta.Accent;
        recursos["BrandSoft"] = paleta.Suave;
        recursos["BrandBackground"] = paleta.Fundo;
        recursos["BrandOnPrimary"] = paleta.TextoVoce;
        recursos["BrandSurface"] = paleta.Superficie;
        recursos["BrandBorder"] = paleta.BordaPadrao;
        recursos["BrandText"] = paleta.Texto;
        recursos["BrandMuted"] = paleta.TextoSecundario;
        recursos["BrandBubbleIncoming"] = paleta.BalaoOutro;
        recursos["BrandBubbleOutgoing"] = paleta.BalaoVoce;
        recursos["BrandBubbleIncomingText"] = paleta.TextoOutro;
        recursos["BrandBubbleOutgoingText"] = paleta.TextoVoce;
#if ANDROID
        try
        {
            var cor = paleta.Primaria;
            var corAndroid = Android.Graphics.Color.Argb(255,
                (int)(cor.Red * 255), (int)(cor.Green * 255), (int)(cor.Blue * 255));
            Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window?.SetStatusBarColor(corAndroid);
            Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window?.SetNavigationBarColor(corAndroid);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Não foi possível aplicar o tema às barras do Android: {ex.Message}");
        }
#endif
    }

    public static string ObterPreferenciaAtual() => NormalizarId(Preferences.Default.Get(ChavePreferencia(), "clean"));

    public static Color CorAtual(string recurso, Color fallback)
    {
        if (Application.Current?.Resources.TryGetValue(recurso, out var valor) == true && valor is Color cor)
            return cor;
        return fallback;
    }

    public static void SalvarPreferencia(string paletaId)
    {
        var paleta = Paletas.FirstOrDefault(p => p.Id == paletaId);
        if (paleta is null) return;
        Preferences.Default.Set(ChavePreferencia(), paleta.Id);
        AplicarPreferenciaAtual();
    }

    private static string NormalizarId(string? id) => Paletas.Any(p => p.Id == id) ? id! : "clean";

    private static string ChavePreferencia()
    {
        var usuario = SessaoUsuario.UsuarioAtual;
        if (usuario is null) return "paleta_app_empresa_0_usuario_anonimo";
        var identidade = usuario.IdServidor?.ToString() ?? Uri.EscapeDataString(usuario.Nome.Trim().ToUpperInvariant());
        return $"paleta_app_empresa_{usuario.EmpresaId}_usuario_{identidade}";
    }

    private static PaletaApp ObterPaleta(string id) => Paletas.FirstOrDefault(p => p.Id == id) ?? Paletas[0];

    private static PaletaApp Criar(string id, string nome, string descricao, string primaria, string suave,
        string fundo, string superficie, string borda, string texto, string textoSecundario,
        string balaoOutro, string balaoVoce, string textoOutro, string textoVoce, string accent) => new()
    {
        Id = id, Nome = nome, Descricao = descricao,
        Primaria = Color.FromArgb(primaria), Suave = Color.FromArgb(suave), Fundo = Color.FromArgb(fundo),
        Superficie = Color.FromArgb(superficie), BordaPadrao = Color.FromArgb(borda),
        Texto = Color.FromArgb(texto), TextoSecundario = Color.FromArgb(textoSecundario),
        BalaoOutro = Color.FromArgb(balaoOutro), BalaoVoce = Color.FromArgb(balaoVoce),
        TextoOutro = Color.FromArgb(textoOutro), TextoVoce = Color.FromArgb(textoVoce),
        Accent = Color.FromArgb(accent)
    };
}
