using Microsoft.EntityFrameworkCore;

namespace CampusTransit.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<TransitRoute> Routes => Set<TransitRoute>();
    public DbSet<Stop> Stops => Set<Stop>();
    public DbSet<Shuttle> Shuttles => Set<Shuttle>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ServiceReport> ServiceReports => Set<ServiceReport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.Email).HasMaxLength(200);
            entity.Property(u => u.FullName).HasMaxLength(120);
            entity.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<TransitRoute>(entity =>
        {
            entity.HasIndex(r => r.Code).IsUnique();
            entity.Property(r => r.Code).HasMaxLength(12);
            entity.Property(r => r.Name).HasMaxLength(80);
            entity.Property(r => r.ColorHex).HasMaxLength(9);
            entity.HasMany(r => r.Stops)
                  .WithOne(s => s.TransitRoute!)
                  .HasForeignKey(s => s.TransitRouteId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Stop>(entity =>
        {
            entity.Property(s => s.Name).HasMaxLength(80);
            entity.HasIndex(s => new { s.TransitRouteId, s.Sequence });
        });

        modelBuilder.Entity<Shuttle>(entity =>
        {
            entity.HasIndex(s => s.Code).IsUnique();
            entity.Property(s => s.Code).HasMaxLength(12);
            entity.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            entity.Ignore(s => s.SeatsAvailable);
            entity.HasOne(s => s.TransitRoute)
                  .WithMany(r => r.Shuttles)
                  .HasForeignKey(s => s.TransitRouteId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(s => s.Driver)
                  .WithMany()
                  .HasForeignKey(s => s.DriverId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.HasIndex(r => r.Reference).IsUnique();
            entity.Property(r => r.Reference).HasMaxLength(16);
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(r => r.AppUser).WithMany(u => u.Reservations).HasForeignKey(r => r.AppUserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(r => r.TransitRoute).WithMany(r => r.Reservations).HasForeignKey(r => r.TransitRouteId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(r => r.Shuttle).WithMany().HasForeignKey(r => r.ShuttleId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(r => r.Stop).WithMany().HasForeignKey(r => r.StopId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ServiceReport>(entity =>
        {
            entity.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
            entity.Property(r => r.Message).HasMaxLength(400);
            entity.HasOne(r => r.Shuttle).WithMany().HasForeignKey(r => r.ShuttleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(r => r.Driver).WithMany().HasForeignKey(r => r.DriverId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
