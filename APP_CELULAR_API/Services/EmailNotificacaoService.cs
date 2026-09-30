using System.Net;
using System.Net.Mail;

namespace APP_CELULAR_API.Services;

public sealed class EmailNotificacaoService(IConfiguration configuration)
{
    public bool EstaConfigurado =>
        !string.IsNullOrWhiteSpace(configuration["Email:SmtpHost"]) &&
        !string.IsNullOrWhiteSpace(configuration["Email:Username"]) &&
        !string.IsNullOrWhiteSpace(configuration["Email:Password"]) &&
        !string.IsNullOrWhiteSpace(configuration["Email:FromAddress"]);

    public async Task Enviar(string destino, string assunto, string mensagem)
    {
        if (!EstaConfigurado)
            throw new InvalidOperationException("O envio de e-mail ainda não está configurado no servidor.");
        string host = configuration["Email:SmtpHost"]!;
        int port = int.TryParse(configuration["Email:SmtpPort"], out int parsedPort) ? parsedPort : 587;
        string username = configuration["Email:Username"]!;
        string password = configuration["Email:Password"]!;
        string from = configuration["Email:FromAddress"]!;
        using var mail = new MailMessage(from, destino, assunto, mensagem) { IsBodyHtml = false };
        using var smtp = new SmtpClient(host, port)
        {
            EnableSsl = !bool.TryParse(configuration["Email:EnableSsl"], out bool ssl) || ssl,
            Credentials = new NetworkCredential(username, password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        await smtp.SendMailAsync(mail);
    }
}
