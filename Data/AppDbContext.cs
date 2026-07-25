using MainProjectNumoPart.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Data
{
    public class AppDbContext : IdentityDbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Vehicle> Vehicles => Set<Vehicle>();
        public DbSet<Photo> Photos => Set<Photo>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Vehicle>(v =>
            {
                // Filtered unique indexes: two vehicles may both have a null Vin (or null Reg),
                // but a non-null value must be unique so search-by-identifier is unambiguous.
                v.HasIndex(x => x.Vin).IsUnique().HasFilter("\"Vin\" IS NOT NULL");
                v.HasIndex(x => x.Reg).IsUnique().HasFilter("\"Reg\" IS NOT NULL");
            });

            // Unique, not just indexed: this is the backstop against the concurrent-upload race
            // where two requests targeting the same vehicle+stage both read the same "next"
            // sequence number before either commits. Without .IsUnique(), that race succeeds
            // silently (two Photo rows, one overwritten blob). With it, the second SaveChangesAsync
            // throws a catchable constraint violation instead — see Upload.cshtml.cs's retry logic,
            // added during Task 10's review after this exact race was flagged.
            builder.Entity<Photo>()
                .HasIndex(p => new { p.VehicleId, p.Stage, p.SequenceNumber })
                .IsUnique();
        }
    }
}
