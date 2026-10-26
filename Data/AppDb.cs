using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CarRental.Data;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Car> Cars => Set<Car>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ReservationEvent> ReservationEvents => Set<ReservationEvent>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Car>(e =>
        {
            e.HasIndex(c => c.Slug).IsUnique();
            e.Property(c => c.DailyPrice).HasPrecision(12, 2);
            e.Property(c => c.Deposit).HasPrecision(12, 2);
            e.Ignore(c => c.Name);
        });

        b.Entity<Reservation>(e =>
        {
            e.HasIndex(r => r.Code).IsUnique();
            e.HasIndex(r => new { r.CarId, r.PickupAt, r.ReturnAt });
            e.HasIndex(r => r.Email);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            // Teslim/iade saatleri Türkiye saati; saat dilimi dönüşümü olmasın.
            e.Property(r => r.PickupAt).HasColumnType("timestamp without time zone");
            e.Property(r => r.ReturnAt).HasColumnType("timestamp without time zone");
            foreach (var p in new[] { "DailyPrice", "BaseTotal", "Discount", "ExtrasTotal", "OneWayFee", "Total", "Deposit" })
                e.Property(p).HasPrecision(12, 2);
            e.HasMany(r => r.Events).WithOne().HasForeignKey(ev => ev.ReservationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EmailMessage>(e =>
        {
            e.Property(m => m.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(m => new { m.Status, m.NextAttemptAt });
        });

        b.Entity<AdminUser>().HasIndex(u => u.Email).IsUnique();
    }
}
