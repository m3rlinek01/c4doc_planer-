using System.Net;
using C4GuestPass.Domain;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Utils;

namespace C4GuestPass.Services;

public interface IGuestMailer
{
    Task SendInvitationAsync(Visit visit, string hostCompany, string zoneName, CancellationToken ct);
}

public sealed class GuestMailer(
    IOptions<MailOptions> mail, IOptions<GuestPassOptions> app, QrRenderer qr, ILogger<GuestMailer> log) : IGuestMailer
{
    private static readonly TimeZoneInfo Tz = TryTz("Europe/Warsaw");

    public async Task SendInvitationAsync(Visit v, string hostCompany, string zoneName, CancellationToken ct)
    {
        var msg = Build(v, hostCompany, zoneName);
        var o = mail.Value;
        if (string.Equals(o.Mode, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(o.Host ?? throw new InvalidOperationException("Mail:Host not set"), o.Port, o.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, ct);
            if (!string.IsNullOrEmpty(o.User)) await smtp.AuthenticateAsync(o.User, o.Password ?? "", ct);
            await smtp.SendAsync(msg, ct);
            await smtp.DisconnectAsync(true, ct);
        }
        else
        {
            Directory.CreateDirectory(o.PickupDirectory);
            var path = Path.Combine(o.PickupDirectory, $"{DateTime.UtcNow:yyyyMMddHHmmss}_{v.Id:N}.eml");
            await msg.WriteToAsync(path, ct);
            log.LogInformation("Mail saved to {Path} (Pickup mode)", path);
        }
    }

    internal MimeMessage Build(Visit v, string hostCompany, string profileName)
    {
        var o = mail.Value;
        var site = app.Value.SiteName;
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(string.IsNullOrEmpty(hostCompany) ? o.FromName : $"{hostCompany} via {o.FromName}", o.FromAddress));
        msg.To.Add(new MailboxAddress(v.FullName, v.Email));
        msg.Subject = $"Zaproszenie od {hostCompany} – kod wejścia {Fmt(v.ValidFrom):dd.MM.yyyy}";

        var builder = new BodyBuilder();
        var png = qr.Png(v.AccessCode);
        var img = builder.LinkedResources.Add("qr.png", png, new ContentType("image", "png"));
        img.ContentId = MakeId();
        builder.Attachments.Add($"kod-wejscia-{Fmt(v.ValidFrom):yyyyMMdd}.png", png, new ContentType("image", "png"));

        string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var window = $"{Fmt(v.ValidFrom):dd.MM.yyyy HH:mm} – {Fmt(v.ValidTo):dd.MM.yyyy HH:mm}";
        builder.HtmlBody = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;max-width:520px;margin:auto;color:#1c2430">
              <h2 style="margin-bottom:4px">Dzień dobry, {E(v.FirstName)}!</h2>
              <p><b>{E(hostCompany)}</b> zaprasza Cię do <b>{E(site)}</b>{(string.IsNullOrEmpty(v.HostName) ? "" : $" na spotkanie z <b>{E(v.HostName)}</b>")}.</p>
              <p>Zamiast karty dostępu przyłóż poniższy kod QR do czytnika przy wejściu:</p>
              <p style="text-align:center"><img src="cid:{img.ContentId}" width="260" height="260" alt="Kod QR"/></p>
              <table style="border-collapse:collapse;width:100%">
                <tr><td style="padding:4px 0;color:#5b6675">Ważny</td><td><b>{window}</b></td></tr>
                <tr><td style="padding:4px 0;color:#5b6675">Strefa</td><td>{E(profileName)}</td></tr>
              </table>
              <p style="font-size:13px;color:#5b6675">Kod jest osobisty i ważny tylko w podanym czasie. Nie przekazuj go innym osobom.
              Ustaw jasność ekranu na maksimum i trzymaj telefon ok. 10 cm od czytnika.</p>
            </div>
            """;
        builder.TextBody = $"Dzień dobry {v.FirstName},\n{hostCompany} zaprasza. Twój kod wejścia do {site} (QR w załączniku) jest ważny: {window}.\nStrefa: {profileName}.";
        msg.Body = builder.ToMessageBody();
        return msg;
    }

    private static string MakeId() => MimeUtils.GenerateMessageId();
    private static DateTime Fmt(DateTimeOffset d) => TimeZoneInfo.ConvertTime(d, Tz).DateTime;
    private static TimeZoneInfo TryTz(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { return TimeZoneInfo.Local; }
    }
}
