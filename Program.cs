using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.HttpOverrides;

namespace CarRental
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllersWithViews();
            // Türkçe karakterler HTML'de &#x...; yerine olduğu gibi yazılsın
            builder.Services.AddSingleton(HtmlEncoder.Create(UnicodeRanges.All));
            builder.Services.Configure<ForwardedHeadersOptions>(o =>
            {
                // Coolify'daki Traefik önde; HTTPS orada sonlanıyor
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.KnownNetworks.Clear();
                o.KnownProxies.Clear();
            });

            var app = builder.Build();

            app.UseForwardedHeaders();
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
            }
            app.UseStatusCodePagesWithReExecute("/Home/Error");

            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public,max-age=604800",
            });

            app.UseRouting();

            app.UseAuthorization();

            app.MapGet("/health", () => "ok");
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
