using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using CarRental.Data;
using CarRental.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.WebEncoders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDb>(o => o.UseNpgsql(DatabaseUrl.ConnectionString(builder.Configuration)));
// Çerez anahtarları veritabanında; konteyner yeniden kurulunca oturumlar ve formlar bozulmaz.
builder.Services.AddDataProtection().PersistKeysToDbContext<AppDb>().SetApplicationName("CarRental");
builder.Services.AddScoped<Availability>();
builder.Services.AddScoped<Mailer>();
builder.Services.AddScoped<Reservations>();
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddHostedService<HousekeepingWorker>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "gocar_admin";
        o.Cookie.Path = "/yonetim";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.LoginPath = "/yonetim/giris";
        o.LogoutPath = "/yonetim/cikis";
        o.AccessDeniedPath = "/yonetim/giris";
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

// Türkçe karakterler &#x131; gibi kodlanmadan yazılsın.
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.AddControllersWithViews(o => o.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()));

// Spam ve kaba kuvvete karşı IP başına sınırlar.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static RateLimitPartition<string> Fixed(HttpContext ctx, int limit, TimeSpan window) => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "?", _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = window });
    o.AddPolicy("login", ctx => Fixed(ctx, 10, TimeSpan.FromMinutes(1)));
    o.AddPolicy("booking", ctx => Fixed(ctx, 6, TimeSpan.FromHours(1)));
    o.AddPolicy("contact", ctx => Fixed(ctx, 5, TimeSpan.FromHours(1)));
    o.AddPolicy("lookup", ctx => Fixed(ctx, 20, TimeSpan.FromMinutes(10)));
    o.AddPolicy("quote", ctx => Fixed(ctx, 120, TimeSpan.FromMinutes(1)));
});

// Coolify'ın önündeki proxy gerçek ziyaretçi IP'sini ve https bilgisini bu başlıklarla iletir.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDb>().Database.MigrateAsync();
    await Seeder.RunAsync(scope.ServiceProvider, app.Configuration, app.Logger);
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/hata");
}
app.UseStatusCodePagesWithReExecute("/hata", "?kod={0}");

var tr = new CultureInfo("tr-TR");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(tr),
    SupportedCultures = [tr],
    SupportedUICultures = [tr],
    RequestCultureProviders = [],
});

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "SAMEORIGIN";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "public, max-age=604800",
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok("ok"));
app.MapControllers();

app.Run();
