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

            // DamageMark.PhotoId is nullable (a mark can be made against a part with no tagged
            // photo yet, anchored at a fixed position instead), and EF's DEFAULT behaviour for an
            // OPTIONAL foreign key is SetNull on delete, not cascade — unlike every other
            // relationship in this file, which are all required and cascade by convention with no
            // config needed.
            //
            // A photo-anchored mark's X/Y position is meaningless once the photo it points at is
            // gone, so it still needs cleaning up when a photo is deleted — but NOT via a second
            // DB-level cascade FK here. DamageMark already cascades from Vehicle directly (below,
            // by convention), and Photo also cascades from Vehicle — so Vehicle->DamageMark and
            // Vehicle->Photo->DamageMark would be two DB-enforced cascade paths converging on the
            // same table. SQLite allows that silently; SQL Server refuses to even create the
            // table ("may cause cycles or multiple cascade paths"), which is what surfaced this
            // the first time this migration was applied against production. Restrict here means
            // no DB-level action on Photo delete; PhotoEndpoints' delete handler removes the
            // matching DamageMarks itself before removing the Photo, so the end result is
            // unchanged — the difference is only which layer performs it.
            builder.Entity<DamageMark>()
                .HasOne(m => m.Photo)
                .WithMany()
                .HasForeignKey(m => m.PhotoId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
