namespace APP_CELULAR
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute(
                nameof(MateriaisPage),
                typeof(MateriaisPage));

            Routing.RegisterRoute(
                nameof(PessoasPage),
                typeof(PessoasPage));

            Routing.RegisterRoute(
                nameof(ComprasPage),
                typeof(ComprasPage));

            Routing.RegisterRoute(
              nameof(NovaCompraPage),
              typeof(NovaCompraPage));

            Routing.RegisterRoute(
               nameof(DetalhesCompraPage),
               typeof(DetalhesCompraPage));

            Routing.RegisterRoute(
                nameof(VendasPage),
                typeof(VendasPage));

            Routing.RegisterRoute(
                nameof(NovaVendaPage),
                typeof(NovaVendaPage));

            Routing.RegisterRoute(
                nameof(DetalhesVendaPage),
                typeof(DetalhesVendaPage));

            Routing.RegisterRoute(
                nameof(UsuariosPage),
                typeof(UsuariosPage));

            Routing.RegisterRoute(
                nameof(NovoUsuarioPage),
                typeof(NovoUsuarioPage));

            Routing.RegisterRoute(
                nameof(DetalhaUsuariosPage),
                typeof(DetalhaUsuariosPage));

        }
    }
}