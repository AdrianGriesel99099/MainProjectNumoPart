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
        public DbSet<PhotoComment> PhotoComments => Set<PhotoComment>();
        public DbSet<VehicleUpdate> VehicleUpdates => Set<VehicleUpdate>();
        public DbSet<DamageMark> DamageMarks => Set<DamageMark>();

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

            // DamageMark.PhotoId is nullable (a mark can be anchored to the generic part diagram
            // instead of a specific photo), and EF's DEFAULT behaviour for an OPTIONAL foreign
            // key is SetNull on delete, not cascade — unlike every other relationship in this
            // file, which are all required and cascade by convention with no config needed. A
            // photo-anchored mark's X/Y position is meaningless once the photo it points at is
            // gone, so this one relationship needs an explicit override to actually cascade.
            builder.Entity<DamageMark>()
                .HasOne(m => m.Photo)
                .WithMany()
                .HasForeignKey(m => m.PhotoId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
