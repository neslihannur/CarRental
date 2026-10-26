using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using CarRental.Data;
using Microsoft.EntityFrameworkCore;

namespace CarRental.Services;

/// E-postaları giden kutusuna yazar; gönderimi OutboxWorker yapar.
public class Mailer(AppDb db, IConfiguration config)
{
    string BaseUrl => (config["Site:BaseUrl"] ?? "").TrimEnd('/');
    string? NotifyEmail => config["Site:NotifyEmail"] is { Length: > 0 } e ? e : null;

    public string ManageUrl(Reservation r) => $"{BaseUrl}/rezervasyonum/{r.Code}?t={r.AccessToken}";

    void Add(string to, string subject, string html, string kind, Reservation? r = null, string? attachmentName = null, string? attachment = null)
    {
        var now = DateTime.UtcNow;
        db.EmailMessages.Add(new EmailMessage
        {
            To = to, Subject = subject, Html = html, Kind = kind, ReservationId = r?.Id is > 0 ? r.Id : null,
            AttachmentName = attachmentName, AttachmentContent = attachment,
            Status = EmailStatus.Pending, CreatedAt = now, NextAttemptAt = now,
        });
    }

    /// Kaydedilmiş (Id almış) rezervasyon için çağrılır.
    public void ReservationCreated(Reservation r, Car car)
    {
        if (r.IsDemo) return;
        Add(r.Email, $"Rezervasyonunuz onaylandı · {r.Code}", EmailTemplates.Confirmed(r, car, ManageUrl(r), BaseUrl),
            "Onay", r, $"gocar-{r.Code}.ics", Ics.ForReservation(r, car, ManageUrl(r)));
        if (NotifyEmail is { } admin)
            Add(admin, $"Yeni rezervasyon {r.Code} · {car.Name} · {Catalog.Money(r.Total)}",
                EmailTemplates.AdminNew(r, car, BaseUrl), "Yönetici", r);
    }

    public void ReservationCancelled(Reservation r, Car car, bool byCustomer)
    {
        if (r.IsDemo) return;
        Add(r.Email, $"Rezervasyonunuz iptal edildi · {r.Code}", EmailTemplates.Cancelled(r, car, byCustomer, BaseUrl), "İptal", r);
        if (byCustomer && NotifyEmail is { } admin)
            Add(admin, $"Müşteri iptali {r.Code} · {car.Name}",
                EmailTemplates.AdminCancelled(r, car, BaseUrl), "Yönetici", r);
    }

    public void Reminder(Reservation r, Car car)
    {
        if (r.IsDemo) return;
        Add(r.Email, $"Yarın görüşüyoruz · {car.Name} teslime hazır", EmailTemplates.Reminder(r, car, ManageUrl(r), BaseUrl), "Hatırlatma", r);
    }

    public void PickedUp(Reservation r, Car car)
    {
        if (r.IsDemo) return;
        Add(r.Email, $"İyi yolculuklar! · {r.Code}", EmailTemplates.PickedUp(r, car, ManageUrl(r), BaseUrl), "Teslim", r);
    }

    public void Completed(Reservation r, Car car)
    {
        if (r.IsDemo) return;
        Add(r.Email, $"Bizi tercih ettiğiniz için teşekkürler · {r.Code}", EmailTemplates.Completed(r, car, BaseUrl), "İade", r);
    }

    public void ResendConfirmation(Reservation r, Car car) =>
        Add(r.Email, $"Rezervasyon bilgileriniz · {r.Code}", EmailTemplates.Confirmed(r, car, ManageUrl(r), BaseUrl),
            "Onay (tekrar)", r, $"gocar-{r.Code}.ics", Ics.ForReservation(r, car, ManageUrl(r)));

    public void ContactReceived(ContactMessage m)
    {
        var corporate = m.Kind == "kurumsal";
        Add(m.Email, corporate ? "Kurumsal teklif talebinizi aldık" : "Mesajınızı aldık",
            EmailTemplates.ContactReceived(m, BaseUrl), corporate ? "Kurumsal (müşteri)" : "İletişim (müşteri)");
        if (NotifyEmail is { } admin)
            Add(admin, corporate ? $"Kurumsal teklif talebi · {m.Company ?? m.Name}" : $"Yeni mesaj · {m.Name}",
                EmailTemplates.AdminContact(m, BaseUrl), "Yönetici");
    }

    public void Test(string to) =>
        Add(to, "GoCar · Deneme e-postası", EmailTemplates.Test(BaseUrl), "Deneme");
}

public static class Ics
{
    static string Utc(DateTime local) => local.AddHours(-3).ToString("yyyyMMdd'T'HHmmss'Z'");
    static string Esc(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");

    public static string ForReservation(Reservation r, Car car, string manageUrl)
    {
        var pickup = Catalog.FindBranch(r.PickupBranch);
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//GoCar//Rezervasyon//TR\r\nMETHOD:PUBLISH\r\n");
        sb.Append("BEGIN:VEVENT\r\n");
        sb.Append($"UID:{r.Code}@gocar\r\nDTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}\r\n");
        sb.Append($"DTSTART:{Utc(r.PickupAt)}\r\nDTEND:{Utc(r.ReturnAt)}\r\n");
        sb.Append($"SUMMARY:{Esc($"GoCar · {car.Name} ({r.Code})")}\r\n");
        sb.Append($"LOCATION:{Esc(pickup is null ? r.PickupBranch : $"GoCar {pickup.Name} – {pickup.Address}")}\r\n");
        sb.Append($"DESCRIPTION:{Esc($"Rezervasyon kodu: {r.Code}\nTeslim: {r.PickupAt:dd.MM.yyyy HH:mm}\nİade: {r.ReturnAt:dd.MM.yyyy HH:mm}\nYönet: {manageUrl}")}\r\n");
        sb.Append("BEGIN:VALARM\r\nTRIGGER:-PT3H\r\nACTION:DISPLAY\r\nDESCRIPTION:Araç teslim saatine 3 saat kaldı\r\nEND:VALARM\r\n");
        sb.Append("END:VEVENT\r\nEND:VCALENDAR\r\n");
        return sb.ToString();
    }
}

/// Giden kutusundaki e-postaları SMTP ile gönderir; hata olursa artan aralıklarla 5 kez dener.
public class OutboxWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<OutboxWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { await SendPendingAsync(stop); }
            catch (Exception ex) when (!stop.IsCancellationRequested) { log.LogError(ex, "Giden kutusu işlenemedi"); }
            await Task.Delay(TimeSpan.FromSeconds(10), stop).ContinueWith(_ => { });
        }
    }

    async Task SendPendingAsync(CancellationToken stop)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var now = DateTime.UtcNow;
        var batch = await db.EmailMessages
            .Where(m => m.Status == EmailStatus.Pending && m.NextAttemptAt <= now)
            .OrderBy(m => m.Id).Take(20).ToListAsync(stop);
        if (batch.Count == 0) return;

        var smtp = config.GetSection("Smtp");
        var pickup = smtp["PickupDirectory"];
        var configured = !string.IsNullOrEmpty(smtp["Host"]) || !string.IsNullOrEmpty(pickup);

        foreach (var m in batch)
        {
            if (!configured)
            {
                m.Status = EmailStatus.Skipped;
                m.LastError = "SMTP ayarlanmamış";
                continue;
            }
            try
            {
                await SendAsync(smtp, pickup, m, stop);
                m.Status = EmailStatus.Sent;
                m.SentAt = DateTime.UtcNow;
                m.LastError = null;
                log.LogInformation("E-posta gönderildi: {Kind} → {To}", m.Kind, m.To);
            }
            catch (Exception ex) when (!stop.IsCancellationRequested)
            {
                m.Attempts++;
                m.LastError = ex.Message;
                m.Status = m.Attempts >= 5 ? EmailStatus.Failed : EmailStatus.Pending;
                m.NextAttemptAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, m.Attempts));
                log.LogWarning(ex, "E-posta gönderilemedi ({Attempts}. deneme): {To}", m.Attempts, m.To);
            }
        }
        await db.SaveChangesAsync(stop);
    }

    static async Task SendAsync(IConfigurationSection smtp, string? pickup, EmailMessage m, CancellationToken stop)
    {
        var from = smtp["FromEmail"] is { Length: > 0 } f ? f : smtp["User"] is { Length: > 0 } u ? u : "gocar@localhost";
        using var msg = new MailMessage
        {
            From = new MailAddress(from, smtp["FromName"] ?? "GoCar", Encoding.UTF8),
            Subject = m.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = m.Html,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true,
        };
        msg.To.Add(m.To);
        if (smtp["ReplyTo"] is { Length: > 0 } replyTo) msg.ReplyToList.Add(replyTo);
        if (m.AttachmentContent is { } content)
        {
            var att = Attachment.CreateAttachmentFromString(content, new ContentType("text/calendar; charset=utf-8; method=PUBLISH"));
            att.Name = m.AttachmentName ?? "takvim.ics";
            msg.Attachments.Add(att);
        }

        using var client = new SmtpClient();
        if (!string.IsNullOrEmpty(pickup))
        {
            Directory.CreateDirectory(pickup);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = Path.GetFullPath(pickup);
        }
        else
        {
            client.Host = smtp["Host"]!;
            client.Port = int.TryParse(smtp["Port"], out var port) ? port : 587;
            client.EnableSsl = true;
            client.Credentials = new NetworkCredential(smtp["User"], smtp["Password"]);
        }
        await client.SendMailAsync(msg, stop);
    }
}
