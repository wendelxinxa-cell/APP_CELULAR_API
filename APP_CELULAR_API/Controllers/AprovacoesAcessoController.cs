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
            WHERE d.empresa_id=@empresaId AND (d.ativo=FALSE OR d.usuario_id_solicitado IS NOT NULL)
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
    public async Task<IActionResult> AprovarUsuario(long id)
    {
        if (!AdministradorAtual(out var session)) return Unauthorized(new { mensagem = "Acesso restrito aos administradores da empresa." });
        await using var db = await AbrirEmpresa(session.EmpresaId);
        const string sql = "UPDATE app.usuario SET aprovado=TRUE WHERE id=@id AND empresa_id=@empresaId AND aprovado=FALSE AND COALESCE(excluido,FALSE)=FALSE RETURNING nome;";
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("id", id); cmd.Parameters.AddWithValue("empresaId", session.EmpresaId);
        var nome = await cmd.ExecuteScalarAsync();
        return nome is null ? NotFound(new { mensagem = "Solicitação pendente não encontrada." }) : Ok(new { sucesso = true, mensagem = $"Usuário {nome} aprovado." });
    }

    [HttpPost("dispositivos/{id:long}/aprovar")]
    public async Task<IActionResult> AprovarDispositivo(long id)
    {
        if (!AdministradorAtual(out var session)) return Unauthorized(new { mensagem = "Acesso restrito aos administradores da empresa." });
        await using var db = await AbrirEmpresa(session.EmpresaId);
        const string sql = """
            UPDATE app.dispositivo
            SET usuario_id=COALESCE(usuario_id_solicitado, usuario_id),
                usuario_id_solicitado=NULL,
                solicitado_em=NULL,
                ativo=TRUE,
                aprovado_por_usuario_id=@aprovadorId,
                aprovado_em=NOW()
            WHERE id=@id AND empresa_id=@empresaId
              AND (ativo=FALSE OR usuario_id_solicitado IS NOT NULL)
            RETURNING nome_dispositivo;
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("id", id); cmd.Parameters.AddWithValue("empresaId", session.EmpresaId);
        cmd.Parameters.AddWithValue("aprovadorId", session.UsuarioId);
        var nome = await cmd.ExecuteScalarAsync();
        if (nome is null) return NotFound(new { mensagem = "Solicitação pendente não encontrada." });
        sessions.RevokeDeviceSessions(session.EmpresaId, id);
        return Ok(new { sucesso = true, mensagem = $"Aparelho {nome} aprovado." });
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
