using System.Net;
using System.Text;
using CarRental.Data;

namespace CarRental.Services;

/// E-posta istemcileri için tablo tabanlı, satır içi stilli şablonlar.
public static class EmailTemplates
{
    const string Ink = "#0E1013";
    const string Accent = "#FFC21A";
    const string Muted = "#6B7076";
    const string Line = "#ECEAE4";

    static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    static string Layout(string preheader, string body, string baseUrl) => $"""
        <!doctype html>
        <html lang="tr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>GoCar</title></head>
        <body style="margin:0;padding:0;background:#F3F2EE;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:{Ink};">
        <div style="display:none;max-height:0;overflow:hidden;opacity:0;">{H(preheader)}</div>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F3F2EE;padding:32px 12px;">
        <tr><td align="center">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;background:#ffffff;border-radius:20px;overflow:hidden;">
          <tr><td style="background:{Ink};padding:26px 32px;">
            <a href="{baseUrl}" style="text-decoration:none;color:#ffffff;font-size:24px;font-weight:800;letter-spacing:-0.5px;">Go<span style="color:{Accent};">Car</span></a>
            <span style="float:right;color:#9AA0A6;font-size:13px;line-height:30px;">Araç Kiralama</span>
          </td></tr>
          <tr><td style="padding:36px 32px 12px;font-size:15px;line-height:1.6;">
          {body}
          </td></tr>
          <tr><td style="padding:24px 32px 32px;border-top:1px solid {Line};font-size:12px;line-height:1.6;color:{Muted};">
            GoCar Araç Kiralama · 0850 000 00 00 · <a href="{baseUrl}/iletisim" style="color:{Muted};">İletişim</a> · <a href="{baseUrl}/kosullar" style="color:{Muted};">Kiralama koşulları</a><br>
            Bu e-posta, <a href="https://portfolio.neslihannur.com.tr" style="color:{Muted};">Neslihan Nur Han</a> tarafından geliştirilen bir portföy demo projesinden gönderildi; gerçek bir kiralama işlemi yoktur.
          </td></tr>
        </table>
        </td></tr></table>
        </body></html>
        """;

    static string Button(string href, string text) =>
        $"""<table role="presentation" cellpadding="0" cellspacing="0" style="margin:26px 0 8px;"><tr><td style="background:{Accent};border-radius:999px;"><a href="{href}" style="display:inline-block;padding:14px 28px;color:{Ink};font-weight:700;text-decoration:none;font-size:15px;">{H(text)}</a></td></tr></table>""";

    static string Heading(string eyebrow, string title) =>
        $"""<div style="font-size:12px;font-weight:700;letter-spacing:1.5px;text-transform:uppercase;color:{Muted};">{H(eyebrow)}</div><h1 style="margin:6px 0 16px;font-size:26px;line-height:1.25;letter-spacing:-0.5px;">{H(title)}</h1>""";

    static string Row(string label, string value, bool strong = false) =>
        $"""<tr><td style="padding:10px 0;border-bottom:1px solid {Line};color:{Muted};font-size:14px;">{H(label)}</td><td align="right" style="padding:10px 0;border-bottom:1px solid {Line};font-size:14px;{(strong ? "font-weight:800;font-size:17px;" : "font-weight:600;")}">{H(value)}</td></tr>""";

    static string BranchText(string code) => Catalog.FindBranch(code) is { } b ? $"{b.Name} ({b.City})" : code;

    static string TripCard(Reservation r, Car car, string baseUrl) => $"""
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:22px 0;border:1px solid {Line};border-radius:16px;">
          <tr>
            <td width="42%" style="padding:16px;"><img src="{baseUrl}/images/{car.Image}" width="200" alt="{H(car.Name)}" style="display:block;width:100%;max-width:200px;border-radius:10px;"></td>
            <td style="padding:16px 16px 16px 4px;vertical-align:middle;">
              <div style="font-size:12px;color:{Muted};">{H(car.Category)} · {H(car.Transmission)} · {H(car.Fuel)}</div>
              <div style="font-size:19px;font-weight:800;margin-top:2px;">{H(car.Name)}</div>
              <div style="font-size:13px;color:{Muted};margin-top:6px;">Rezervasyon kodu</div>
              <div style="font-size:18px;font-weight:800;letter-spacing:1px;">{H(r.Code)}</div>
            </td>
          </tr>
        </table>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
          {Row("Teslim alış", $"{Catalog.DateTimeShort(r.PickupAt)} · {BranchText(r.PickupBranch)}")}
          {Row("İade", $"{Catalog.DateTimeShort(r.ReturnAt)} · {BranchText(r.ReturnBranch)}")}
          {Row("Süre", $"{r.Days} gün")}
        </table>
        """;

    static string PriceTable(Reservation r)
    {
        var sb = new StringBuilder();
        sb.Append("""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:18px;">""");
        sb.Append(Row($"Kiralama ({r.Days} gün × {Catalog.Money(r.DailyPrice)})", Catalog.Money(r.BaseTotal)));
        if (r.Discount > 0) sb.Append(Row("Uzun kiralama indirimi", "−" + Catalog.Money(r.Discount)));
        foreach (var code in r.Extras)
            if (Catalog.FindExtra(code) is { } e)
                sb.Append(Row(e.Name, Catalog.Money(e.PerDay ? e.Price * r.Days : e.Price)));
        if (r.OneWayFee > 0) sb.Append(Row("Farklı şubede iade", Catalog.Money(r.OneWayFee)));
        sb.Append(Row("Toplam (KDV dahil)", Catalog.Money(r.Total), strong: true));
        sb.Append("</table>");
        sb.Append($"""<p style="font-size:13px;color:{Muted};margin:12px 0 0;">Ödeme araç tesliminde kredi kartı ile alınır. Teslimde kartınızdan {H(Catalog.Money(r.Deposit))} provizyon bloke edilir ve iadeden sonra kaldırılır.</p>""");
        return sb.ToString();
    }

    public static string Confirmed(Reservation r, Car car, string manageUrl, string baseUrl)
    {
        var pickup = Catalog.FindBranch(r.PickupBranch);
        var body = $"""
            {Heading("Rezervasyon onayı", $"Harika seçim, {FirstName(r.FullName)}! {car.Name} sizi bekliyor.")}
            <p style="margin:0;color:{Muted};">Rezervasyonunuz onaylandı. Aşağıda tüm ayrıntıları bulabilirsiniz; takviminize eklemek için ekteki dosyayı açmanız yeterli.</p>
            {TripCard(r, car, baseUrl)}
            {PriceTable(r)}
            {Button(manageUrl, "Rezervasyonu görüntüle")}
            <h3 style="margin:28px 0 8px;font-size:16px;">Teslim alırken yanınızda olsun</h3>
            <ul style="margin:0;padding-left:18px;color:{Ink};font-size:14px;">
              <li>En az {car.MinLicenseYears} yıllık sürücü belgesi ve kimlik/pasaport</li>
              <li>Sürücü adına kredi kartı (provizyon için)</li>
              <li>Rezervasyon kodunuz: <b>{H(r.Code)}</b></li>
            </ul>
            {(pickup is null ? "" : $"""<p style="font-size:14px;margin:18px 0 0;"><b>Teslim noktası:</b> {H(pickup.Name)} – {H(pickup.Address)} ({H(pickup.Hours)})</p>""")}
            <p style="font-size:13px;color:{Muted};margin:18px 0 0;">Teslim saatine kadar ücretsiz iptal edebilirsiniz; bunun için yukarıdaki bağlantıyı kullanmanız yeterli.</p>
            """;
        return Layout($"{r.Code} · {Catalog.DateShort(r.PickupAt)} – {Catalog.DateShort(r.ReturnAt)}", body, baseUrl);
    }

    public static string AdminNew(Reservation r, Car car, string baseUrl)
    {
        var adminUrl = $"{baseUrl}/yonetim/rezervasyonlar/{r.Id}";
        var body = $"""
            {Heading("Yeni rezervasyon", $"{r.Code} · {car.Name}")}
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
              {Row("Müşteri", r.FullName)}
              {Row("E-posta", r.Email)}
              {Row("Telefon", r.Phone)}
              {Row("Teslim", $"{Catalog.DateTimeShort(r.PickupAt)} · {BranchText(r.PickupBranch)}")}
              {Row("İade", $"{Catalog.DateTimeShort(r.ReturnAt)} · {BranchText(r.ReturnBranch)}")}
              {Row("Ek hizmetler", r.Extras.Count == 0 ? "—" : string.Join(", ", r.Extras.Select(c => Catalog.FindExtra(c)?.Name ?? c)))}
              {(string.IsNullOrEmpty(r.FlightNo) ? "" : Row("Uçuş", r.FlightNo))}
              {(string.IsNullOrEmpty(r.Notes) ? "" : Row("Not", r.Notes))}
              {Row("Toplam", Catalog.Money(r.Total), strong: true)}
            </table>
            {Button(adminUrl, "Panelde aç")}
            """;
        return Layout($"{r.FullName} · {Catalog.Money(r.Total)}", body, baseUrl);
    }

    public static string Cancelled(Reservation r, Car car, bool byCustomer, string baseUrl)
    {
        var body = $"""
            {Heading("İptal", "Rezervasyonunuz iptal edildi")}
            <p style="margin:0;color:{Muted};">{(byCustomer
                ? "İsteğiniz üzerine aşağıdaki rezervasyonu iptal ettik. Ödeme teslimde alınacağı için herhangi bir ücret yansımayacak."
                : "Aşağıdaki rezervasyonunuz ekibimiz tarafından iptal edildi. Sorularınız için bu e-postayı yanıtlayabilir ya da bizi arayabilirsiniz.")}</p>
            {(string.IsNullOrEmpty(r.CancelReason) || byCustomer ? "" : $"""<p style="margin:14px 0 0;"><b>Açıklama:</b> {H(r.CancelReason)}</p>""")}
            {TripCard(r, car, baseUrl)}
            {Button(baseUrl + "/filo", "Yeni bir araç seçin")}
            """;
        return Layout($"{r.Code} iptal edildi", body, baseUrl);
    }

    public static string AdminCancelled(Reservation r, Car car, string baseUrl)
    {
        var adminUrl = $"{baseUrl}/yonetim/rezervasyonlar/{r.Id}";
        var body = $"""
            {Heading("Müşteri iptali", $"{r.Code} · {car.Name}")}
            <p style="margin:0;color:{Muted};">{H(r.FullName)}, {Catalog.DateTimeShort(r.PickupAt)} tarihli rezervasyonunu iptal etti.</p>
            {Button(adminUrl, "Panelde aç")}
            """;
        return Layout($"{r.Code} iptal edildi", body, baseUrl);
    }

    public static string Reminder(Reservation r, Car car, string manageUrl, string baseUrl)
    {
        var pickup = Catalog.FindBranch(r.PickupBranch);
        var body = $"""
            {Heading("Hatırlatma", $"{car.Name} yarın teslime hazır")}
            <p style="margin:0;color:{Muted};">Aracınız temizlendi, kontrol edildi ve yakıt deposu dolduruldu. {Catalog.DateTimeShort(r.PickupAt)} saatinde görüşmek üzere!</p>
            {TripCard(r, car, baseUrl)}
            {(pickup is null ? "" : $"""<p style="font-size:14px;margin:0;"><b>Adres:</b> {H(pickup.Address)}, {H(pickup.City)} · {H(pickup.Hours)}</p>""")}
            {(pickup?.IsAirport == true ? $"""<p style="font-size:14px;margin:10px 0 0;color:{Muted};">Uçuşunuz gecikirse endişelenmeyin; {(string.IsNullOrEmpty(r.FlightNo) ? "uçuş numaranızı yanıt olarak gönderirseniz" : $"{H(r.FlightNo)} sefer numaralı uçuşunuzu takip ediyoruz,")} ekibimiz sizi bekler.</p>""" : "")}
            {Button(manageUrl, "Rezervasyon ayrıntıları")}
            """;
        return Layout($"{Catalog.DateTimeShort(r.PickupAt)} · {pickup?.Name}", body, baseUrl);
    }

    public static string PickedUp(Reservation r, Car car, string manageUrl, string baseUrl)
    {
        var body = $"""
            {Heading("Teslim edildi", $"İyi yolculuklar, {FirstName(r.FullName)}!")}
            <p style="margin:0;color:{Muted};">{H(car.Name)} artık sizde. İade: <b style="color:{Ink};">{Catalog.DateTimeShort(r.ReturnAt)}</b>, {H(BranchText(r.ReturnBranch))}.</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:18px;">
              {Row("Günlük km sınırı", car.DailyKmLimit > 0 ? $"{car.DailyKmLimit} km" : "Sınırsız")}
              {Row("Yakıt politikası", "Dolu al, dolu teslim et")}
              {Row("7/24 yol yardım", "0850 000 00 01")}
            </table>
            {Button(manageUrl, "Rezervasyon ayrıntıları")}
            """;
        return Layout($"İade: {Catalog.DateTimeShort(r.ReturnAt)}", body, baseUrl);
    }

    public static string Completed(Reservation r, Car car, string baseUrl)
    {
        var body = $"""
            {Heading("Kiralama tamamlandı", "Bizi tercih ettiğiniz için teşekkürler")}
            <p style="margin:0;color:{Muted};">{H(car.Name)} iade alındı ve provizyon kartınızdan kaldırıldı. Kesin fatura 1 iş günü içinde e-postanıza gelecek.</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:18px;">
              {Row("Rezervasyon", r.Code)}
              {Row("Süre", $"{r.Days} gün")}
              {Row("Toplam", Catalog.Money(r.Total), strong: true)}
            </table>
            {Button(baseUrl + "/filo", "Bir sonraki yolculuğu planla")}
            """;
        return Layout("Kiralamanız tamamlandı", body, baseUrl);
    }

    public static string ContactReceived(ContactMessage m, string baseUrl)
    {
        var corporate = m.Kind == "kurumsal";
        var body = $"""
            {Heading(corporate ? "Kurumsal kiralama" : "İletişim", $"Teşekkürler {FirstName(m.Name)}, mesajınız bize ulaştı")}
            <p style="margin:0;color:{Muted};">{(corporate
                ? "Filo ihtiyacınıza göre hazırlayacağımız teklif, kurumsal satış ekibimiz tarafından en geç 1 iş günü içinde size iletilecek."
                : "Ekibimiz mesajınızı inceleyip mesai saatleri içinde en kısa sürede dönüş yapacak.")}</p>
            <div style="margin:22px 0;padding:16px 18px;background:#F7F6F2;border-radius:14px;font-size:14px;white-space:pre-line;">{H(m.Message)}</div>
            {Button(baseUrl, "GoCar'a dön")}
            """;
        return Layout("Mesajınızı aldık", body, baseUrl);
    }

    public static string AdminContact(ContactMessage m, string baseUrl)
    {
        var adminUrl = $"{baseUrl}/yonetim/mesajlar/{m.Id}";
        var body = $"""
            {Heading(m.Kind == "kurumsal" ? "Kurumsal teklif talebi" : "Yeni mesaj", m.Subject ?? m.Name)}
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
              {Row("Ad soyad", m.Name)}
              {Row("E-posta", m.Email)}
              {(string.IsNullOrEmpty(m.Phone) ? "" : Row("Telefon", m.Phone))}
              {(string.IsNullOrEmpty(m.Company) ? "" : Row("Şirket", m.Company))}
              {(string.IsNullOrEmpty(m.FleetSize) ? "" : Row("Araç ihtiyacı", m.FleetSize))}
            </table>
            <div style="margin:22px 0;padding:16px 18px;background:#F7F6F2;border-radius:14px;font-size:14px;white-space:pre-line;">{H(m.Message)}</div>
            {Button(adminUrl, "Panelde aç")}
            """;
        return Layout(m.Name, body, baseUrl);
    }

    public static string Test(string baseUrl) =>
        Layout("SMTP ayarları çalışıyor", $"{Heading("Deneme", "E-posta gönderimi çalışıyor")}<p style=\"margin:0;color:{Muted};\">Bu mesaj yönetim panelinden gönderildi.</p>", baseUrl);

    static string FirstName(string full) => full.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? full;
}
