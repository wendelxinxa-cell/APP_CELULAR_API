using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/acessos")]
public sealed class AprovacoesAcessoController(
    IEmpresaDatabaseResolver resolver,
    TenantSessionStore sessions) : ControllerBase
{
    [HttpPost("pendentes")]
    public async Task<IActionResult> ListarPendentes()
    {
        if (!AdministradorAtual(out var session)) return Unauthorized(new { mensagem = "Acesso restrito aos administradores da empresa." });
        await using var db = await AbrirEmpresa(session.EmpresaId);

        const string usuariosSql = "SELECT id, nome, funcao, data_cadastro FROM app.usuario WHERE empresa_id=@empresaId AND aprovado=FALSE AND COALESCE(excluido,FALSE)=FALSE ORDER BY data_cadastro;";
        await using var usuariosCmd = new NpgsqlCommand(usuariosSql, db);
        usuariosCmd.Parameters.AddWithValue("empresaId", session.EmpresaId);
        await using var ur = await usuariosCmd.ExecuteReaderAsync();
        var usuarios = new List<object>();
        while (await ur.ReadAsync()) usuarios.Add(new { id = ur.GetInt64(0), nome = ur.GetString(1), funcao = ur.GetString(2), criadoEm = ur.GetDateTime(3) });
        await ur.CloseAsync();

        const string dispositivosSql = """
            SELECT d.id, d.nome_dispositivo, d.criado_em,
                   COALESCE(solicitado.nome, vinculado.nome, 'Usuário não identificado'),
                   CASE WHEN d.usuario_id_solicitado IS NULL THEN 'Novo aparelho' ELSE 'Troca de usuário solicitada' END
            FROM app.dispositivo d
            LEFT JOIN app.usuario vinculado ON vinculado.id=d.usuario_id AND vinculado.empresa_id=d.empresa_id
            LEFT JOIN app.usuario solicitado ON solicitado.id=d.usuario_id_solicitado AND solicitado.empresa_id=d.empresa_id
            WHERE d.empresa_id=@empresaId AND (d.solicitado_em IS NOT NULL OR d.usuario_id_solicitado IS NOT NULL)
            ORDER BY COALESCE(d.solicitado_em, d.criado_em), d.id;
            """;
        await using var dispositivosCmd = new NpgsqlCommand(dispositivosSql, db);
        dispositivosCmd.Parameters.AddWithValue("empresaId", session.EmpresaId);
        await using var dr = await dispositivosCmd.ExecuteReaderAsync();
        var dispositivos = new List<object>();
        while (await dr.ReadAsync()) dispositivos.Add(new { id = dr.GetInt64(0), nome = dr.GetString(1), criadoEm = dr.IsDBNull(2) ? (DateTime?)null : dr.GetDateTime(2), usuario = dr.GetString(3), funcao = dr.GetString(4) });
        return Ok(new { usuarios, dispositivos });
    }

    [HttpPost("usuarios/{id:long}/aprovar")]
    public IActionResult AprovarUsuario(long id)
    {
        if (!AdministradorAtual(out _)) return Unauthorized(new { mensagem = "Acesso restrito aos administradores da empresa." });
        return StatusCode(403, new { mensagem = "Somente o Administrador Master (Zeus) pode aprovar usuários." });
    }

    [HttpPost("dispositivos/{id:long}/aprovar")]
    public IActionResult AprovarDispositivo(long id)
    {
        if (!AdministradorAtual(out _)) return Unauthorized(new { mensagem = "Acesso restrito aos administradores da empresa." });
        return StatusCode(403, new { mensagem = "Somente o Administrador Master (Zeus) pode aprovar aparelhos." });
    }

    private bool AdministradorAtual(out TenantSession session) =>
        sessions.TryGet(Request.Headers.Authorization.ToString(), out session) && session.EhAdministrador;

    private async Task<NpgsqlConnection> AbrirEmpresa(long empresaId)
    {
        var db = new NpgsqlConnection(await resolver.ObterConnectionString(empresaId));
        await db.OpenAsync();
        return db;
    }
}
