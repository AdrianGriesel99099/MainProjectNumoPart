# Vehicle Photo Documentation System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the vehicle photo documentation web app end-to-end against **local infrastructure only** (Azurite for blob storage, a local SQLite file) — no Azure resource is created or paid for during this plan. Deployment to the Azure footprint in [2026-07-25-azure-infrastructure-design.md](../specs/2026-07-25-azure-infrastructure-design.md) is a separate, later plan.

**Architecture:** ASP.NET Core Razor Pages (.NET 8) on the existing `MainProjectNumoPart` project. EF Core + SQLite for `Vehicle`/`Photo`/Identity data. A single `IPhotoStorage` abstraction wraps the Azure Blob SDK so the same code talks to Azurite locally and real Blob Storage in production — only the `BlobServiceClient` construction differs, via configuration. Images are served through short-lived SAS-redirect endpoints rather than proxied through the app.

**Tech Stack:** ASP.NET Core 8 Razor Pages, EF Core 8 (SQLite provider), ASP.NET Core Identity, Azure.Storage.Blobs, SixLabors.ImageSharp, MetadataExtractor, xUnit.

## Global Constraints

- .NET 8, ASP.NET Core Razor Pages — follow the existing block-scoped `namespace X { }` style already used in `Pages/Index.cshtml.cs`, not file-scoped namespaces.
- Only JPEG and PNG uploads are accepted; 25MB maximum size per file.
- Exactly five fixed stages: `Checkin`, `Quote`, `Progress`, `Checkout`, `Extra` (not "Exstra").
- SAS tokens for image access are read-only and expire after 15 minutes.
- The app is designed to run as a single instance (Container Apps will be capped at 1 replica in production) — code may assume no concurrent-writer races on SQLite.
- Any authenticated user may upload, browse, and download. Deleting a photo is admin-only.
- No public registration route exists anywhere in the app.
- Local development never touches real Azure — Azurite for blob storage (`UseDevelopmentStorage=true`), a local SQLite file, default (non-Key-Vault) ASP.NET Core Data Protection.

---

## File Structure

```
MainProjectNumoPart.sln                          modify — add test project
MainProjectNumoPart.csproj                        modify — NuGet packages, added per-task
Program.cs                                        modify — DI wiring, added per-task
appsettings.Development.json                      modify — local connection strings, seed admin
.gitignore                                        modify — app.db

Data/AppDbContext.cs                              new — EF Core + Identity context
Models/Stage.cs                                   new — enum
Models/Vehicle.cs                                 new
Models/Photo.cs                                   new

Services/PhotoNaming.cs                           new — pure folder/filename logic
Services/VehicleLookupService.cs                  new — find-or-create, search
Services/PhotoSequenceAllocator.cs                new — per-vehicle/stage sequence numbers
Services/IPhotoStorage.cs                         new — storage abstraction
Services/BlobPhotoStorage.cs                      new — Azure Blob SDK implementation
Services/AdminSeeder.cs                           new — first-run admin bootstrap
Services/ThumbnailGenerator.cs                    new — ImageSharp thumbnail generation
Services/ExifDateReader.cs                        new — MetadataExtractor date-taken read
Services/PhotoFilterQuery.cs                      new — All Photos filter logic

Endpoints/PhotoEndpoints.cs                       new — thumbnail/original/delete/download routes

Pages/Account/Login.cshtml (+.cs)                 new
Pages/Admin/CreateUser.cshtml (+.cs)               new
Pages/Index.cshtml (+.cs)                          modify — becomes VIN/Reg search + recent vehicles
Pages/Upload.cshtml (+.cs)                         new
Pages/Vehicles/Details.cshtml (+.cs)               new — the vehicle job page
Pages/Photos/Index.cshtml (+.cs)                   new — All Photos filtered grid
Pages/Shared/_Layout.cshtml                        modify — nav bar
wwwroot/css/site.css                               modify — styling per approved mockups

MainProjectNumoPart.Tests/MainProjectNumoPart.Tests.csproj   new
MainProjectNumoPart.Tests/TestDbContextFactory.cs             new
MainProjectNumoPart.Tests/FakePhotoStorage.cs                 new
MainProjectNumoPart.Tests/PhotoNamingTests.cs                 new
MainProjectNumoPart.Tests/VehicleLookupServiceTests.cs        new
MainProjectNumoPart.Tests/PhotoSequenceAllocatorTests.cs      new
MainProjectNumoPart.Tests/AdminSeederTests.cs                 new
MainProjectNumoPart.Tests/PhotoFilterQueryTests.cs            new
MainProjectNumoPart.Tests/BlobPhotoStorageTests.cs             new — needs Azurite running

docs/LOCAL_DEV.md                                 new
```

---

### Task 1: Solution Scaffolding and Test Project

**Files:**
- Modify: `MainProjectNumoPart.sln`
- Create: `MainProjectNumoPart.Tests/MainProjectNumoPart.Tests.csproj`
- Create: `MainProjectNumoPart.Tests/TestDbContextFactory.cs`

**Interfaces:**
- Produces: `MainProjectNumoPart.Tests.TestDbContextFactory.CreateInMemory()` returning an open, migrated `AppDbContext` for use by every later test task. (`AppDbContext` itself doesn't exist yet — this task only creates the test project and proves the harness runs; `TestDbContextFactory`'s real body is written in Task 2 once `AppDbContext` exists.)

- [ ] **Step 1: Create the test project**

```bash
dotnet new xunit -n MainProjectNumoPart.Tests -o MainProjectNumoPart.Tests
dotnet sln MainProjectNumoPart.sln add MainProjectNumoPart.Tests/MainProjectNumoPart.Tests.csproj
dotnet add MainProjectNumoPart.Tests/MainProjectNumoPart.Tests.csproj reference MainProjectNumoPart.csproj
dotnet add MainProjectNumoPart.Tests/MainProjectNumoPart.Tests.csproj package Microsoft.Data.Sqlite
```

Delete the scaffolded `MainProjectNumoPart.Tests/UnitTest1.cs` — it's a placeholder and won't be used.

- [ ] **Step 2: Write a trivial test to prove the harness works**

```csharp
// MainProjectNumoPart.Tests/SmokeTests.cs
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void TestProjectRuns()
        {
            Assert.True(true);
        }
    }
}
```

- [ ] **Step 3: Run it to verify the harness works**

Run: `dotnet test MainProjectNumoPart.Tests`
Expected: 1 passed.

- [ ] **Step 4: Commit**

```bash
git add MainProjectNumoPart.sln MainProjectNumoPart.Tests
git commit -m "Add test project scaffolding"
```

---

### Task 2: Domain Models, DbContext, and Initial Migration

**Files:**
- Create: `Models/Stage.cs`
- Create: `Models/Vehicle.cs`
- Create: `Models/Photo.cs`
- Create: `Data/AppDbContext.cs`
- Modify: `MainProjectNumoPart.csproj` (packages)
- Modify: `MainProjectNumoPart.Tests/TestDbContextFactory.cs` (real body)
- Create: `MainProjectNumoPart.Tests/VehiclePhotoModelTests.cs`
- Modify: `.gitignore` (SQLite file)

**Interfaces:**
- Produces: `Stage` enum (`Checkin`, `Quote`, `Progress`, `Checkout`, `Extra`); `Vehicle` (`Id: int`, `Vin: string?`, `Reg: string?`, `MakeModel: string?`, `BlobFolderName: string`, `CreatedAtUtc: DateTime`, `Photos: List<Photo>`); `Photo` (`Id: int`, `VehicleId: int`, `Vehicle: Vehicle`, `Stage: Stage`, `FileName: string`, `BlobPathOriginal: string`, `BlobPathThumbnail: string`, `ContentType: string`, `SizeBytes: long`, `UploadedAtUtc: DateTime`, `DateTakenUtc: DateTime?`, `SequenceNumber: int`, `UploaderId: string`); `AppDbContext : IdentityDbContext` with `DbSet<Vehicle> Vehicles` and `DbSet<Photo> Photos`.
- Produces: `MainProjectNumoPart.Tests.TestDbContextFactory.CreateInMemory()` — every later test task depends on this.

- [ ] **Step 1: Add EF Core and Identity packages**

```bash
dotnet add MainProjectNumoPart.csproj package Microsoft.EntityFrameworkCore.Sqlite
dotnet add MainProjectNumoPart.csproj package Microsoft.EntityFrameworkCore.Design
dotnet add MainProjectNumoPart.csproj package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet tool install --global dotnet-ef
```

(If `dotnet-ef` is already installed, that last command will report so — that's fine, continue.)

- [ ] **Step 2: Write the models**

```csharp
// Models/Stage.cs
namespace MainProjectNumoPart.Models
{
    public enum Stage
    {
        Checkin,
        Quote,
        Progress,
        Checkout,
        Extra
    }
}
```

```csharp
// Models/Vehicle.cs
using System.Collections.Generic;

namespace MainProjectNumoPart.Models
{
    public class Vehicle
    {
        public int Id { get; set; }
        public string? Vin { get; set; }
        public string? Reg { get; set; }
        public string? MakeModel { get; set; }
        public string BlobFolderName { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; }

        public List<Photo> Photos { get; set; } = new();
    }
}
```

```csharp
// Models/Photo.cs
namespace MainProjectNumoPart.Models
{
    public class Photo
    {
        public int Id { get; set; }
        public int VehicleId { get; set; }
        public Vehicle Vehicle { get; set; } = null!;
        public Stage Stage { get; set; }
        public string FileName { get; set; } = null!;
        public string BlobPathOriginal { get; set; } = null!;
        public string BlobPathThumbnail { get; set; } = null!;
        public string ContentType { get; set; } = null!;
        public long SizeBytes { get; set; }
        public DateTime UploadedAtUtc { get; set; }
        public DateTime? DateTakenUtc { get; set; }
        public int SequenceNumber { get; set; }
        public string UploaderId { get; set; } = null!;
    }
}
```

- [ ] **Step 3: Write the DbContext**

```csharp
// Data/AppDbContext.cs
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

            builder.Entity<Photo>()
                .HasIndex(p => new { p.VehicleId, p.Stage, p.SequenceNumber });
        }
    }
}
```

- [ ] **Step 4: Create and apply the initial migration**

```bash
dotnet ef migrations add InitialCreate --project MainProjectNumoPart.csproj
```

This requires `Program.cs` to have a registered `AppDbContext` for the `dotnet ef` design-time tooling to find. Add this now (the rest of the DI wiring for connection strings comes in Task 6 alongside Identity; for now, just enough to let migrations run):

```csharp
// Program.cs — add before `var app = builder.Build();`
builder.Services.AddDbContext<Data.AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=app.db"));
```

Re-run the migrations command if it failed before this edit:

```bash
dotnet ef migrations add InitialCreate --project MainProjectNumoPart.csproj
```

Expected: a `Migrations/` folder is created with `InitialCreate` migration files.

- [ ] **Step 5: Add `app.db` to `.gitignore`**

```
# MainProjectNumoPart.gitignore additions
app.db
app.db-shm
app.db-wal
```

- [ ] **Step 6: Write the test DB factory**

```csharp
// MainProjectNumoPart.Tests/TestDbContextFactory.cs
using MainProjectNumoPart.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Tests
{
    public static class TestDbContextFactory
    {
        // Real SQLite (not EF's InMemory provider) so unique-index and constraint
        // behavior in tests matches what production SQLite actually enforces.
        public static AppDbContext CreateInMemory()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new AppDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }
    }
}
```

- [ ] **Step 7: Write a test proving the schema round-trips a Vehicle and Photo**

```csharp
// MainProjectNumoPart.Tests/VehiclePhotoModelTests.cs
using System;
using System.Linq;
using MainProjectNumoPart.Models;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class VehiclePhotoModelTests
    {
        [Fact]
        public void SavesAndReloadsVehicleWithPhoto()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var vehicle = new Vehicle
            {
                Vin = "1HGBH41JXMN109186",
                BlobFolderName = "1HGBH41JXMN109186",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();

            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id,
                Stage = Stage.Checkin,
                FileName = "1HGBH41JXMN109186-VIN-001.jpg",
                BlobPathOriginal = "1HGBH41JXMN109186/Checkin/1HGBH41JXMN109186-VIN-001.jpg",
                BlobPathThumbnail = "1HGBH41JXMN109186/Checkin/1HGBH41JXMN109186-VIN-001.jpg",
                ContentType = "image/jpeg",
                SizeBytes = 12345,
                UploadedAtUtc = DateTime.UtcNow,
                SequenceNumber = 1,
                UploaderId = "user-1"
            });
            db.SaveChanges();

            var reloaded = db.Vehicles.Include(v => v.Photos).Single();
            Assert.Single(reloaded.Photos);
            Assert.Equal(Stage.Checkin, reloaded.Photos[0].Stage);
        }

        [Fact]
        public void RejectsDuplicateVin()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            db.Vehicles.Add(new Vehicle { Vin = "SAMEVIN", BlobFolderName = "SAMEVIN", CreatedAtUtc = DateTime.UtcNow });
            db.SaveChanges();

            db.Vehicles.Add(new Vehicle { Vin = "SAMEVIN", BlobFolderName = "SAMEVIN-2", CreatedAtUtc = DateTime.UtcNow });

            Assert.ThrowsAny<Exception>(() => db.SaveChanges());
        }

        [Fact]
        public void AllowsMultipleVehiclesWithNullVin()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            db.Vehicles.Add(new Vehicle { Reg = "REG1", BlobFolderName = "REG1", CreatedAtUtc = DateTime.UtcNow });
            db.Vehicles.Add(new Vehicle { Reg = "REG2", BlobFolderName = "REG2", CreatedAtUtc = DateTime.UtcNow });

            db.SaveChanges(); // Should not throw — both have null Vin.

            Assert.Equal(2, db.Vehicles.Count());
        }
    }
}
```

Add `using Microsoft.EntityFrameworkCore;` to the top of that file for `.Include`.

- [ ] **Step 8: Run the tests**

Run: `dotnet test MainProjectNumoPart.Tests`
Expected: all pass, including the duplicate-VIN rejection and the null-VIN-allowed case.

- [ ] **Step 9: Commit**

```bash
git add Models Data Migrations MainProjectNumoPart.csproj Program.cs .gitignore MainProjectNumoPart.Tests
git commit -m "Add Vehicle/Photo domain models, DbContext, and initial migration"
```

---

### Task 3: Photo Naming Logic

**Files:**
- Create: `Services/PhotoNaming.cs`
- Create: `MainProjectNumoPart.Tests/PhotoNamingTests.cs`

**Interfaces:**
- Consumes: nothing (pure, no dependencies).
- Produces: `PhotoNaming.ResolveBlobFolderName(string? vin, string? reg) : string` (throws `ArgumentException` if both null/blank); `PhotoNaming.BuildFileName(string? vin, string? reg, int sequenceNumber, string extension) : string`. Task 4 and Task 10 both call these exactly.

- [ ] **Step 1: Write the failing tests**

```csharp
// MainProjectNumoPart.Tests/PhotoNamingTests.cs
using System;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class PhotoNamingTests
    {
        [Fact]
        public void ResolveBlobFolderName_PrefersVinOverReg()
        {
            Assert.Equal("1HGBH41JXMN109186", PhotoNaming.ResolveBlobFolderName("1HGBH41JXMN109186", "CA481329"));
        }

        [Fact]
        public void ResolveBlobFolderName_FallsBackToReg()
        {
            Assert.Equal("CA481329", PhotoNaming.ResolveBlobFolderName(null, "CA481329"));
        }

        [Fact]
        public void ResolveBlobFolderName_ThrowsWhenNeitherProvided()
        {
            Assert.Throws<ArgumentException>(() => PhotoNaming.ResolveBlobFolderName(null, null));
        }

        [Fact]
        public void ResolveBlobFolderName_ThrowsWhenBothBlank()
        {
            Assert.Throws<ArgumentException>(() => PhotoNaming.ResolveBlobFolderName("  ", ""));
        }

        [Fact]
        public void BuildFileName_VinAndReg()
        {
            var name = PhotoNaming.BuildFileName("1HGBH41JXMN109186", "CA481329", 1, ".jpg");
            Assert.Equal("1HGBH41JXMN109186-VIN-CA481329-Reg-001.jpg", name);
        }

        [Fact]
        public void BuildFileName_VinOnly()
        {
            var name = PhotoNaming.BuildFileName("1HGBH41JXMN109186", null, 1, ".jpg");
            Assert.Equal("1HGBH41JXMN109186-VIN-001.jpg", name);
        }

        [Fact]
        public void BuildFileName_RegOnly()
        {
            var name = PhotoNaming.BuildFileName(null, "CA481329", 1, ".jpg");
            Assert.Equal("CA481329-Reg-001.jpg", name);
        }

        [Fact]
        public void BuildFileName_PadsSequenceToThreeDigits()
        {
            var name = PhotoNaming.BuildFileName("VIN1", null, 42, ".png");
            Assert.Equal("VIN1-VIN-042.png", name);
        }

        [Fact]
        public void BuildFileName_ThrowsWhenNeitherProvided()
        {
            Assert.Throws<ArgumentException>(() => PhotoNaming.BuildFileName(null, null, 1, ".jpg"));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test MainProjectNumoPart.Tests --filter PhotoNamingTests`
Expected: compile error (`PhotoNaming` doesn't exist yet) or FAIL.

- [ ] **Step 3: Implement**

```csharp
// Services/PhotoNaming.cs
using System;

namespace MainProjectNumoPart.Services
{
    public static class PhotoNaming
    {
        public static string ResolveBlobFolderName(string? vin, string? reg)
        {
            if (!string.IsNullOrWhiteSpace(vin)) return vin.Trim();
            if (!string.IsNullOrWhiteSpace(reg)) return reg.Trim();
            throw new ArgumentException("At least one of VIN or Reg is required.");
        }

        public static string BuildFileName(string? vin, string? reg, int sequenceNumber, string extension)
        {
            var hasVin = !string.IsNullOrWhiteSpace(vin);
            var hasReg = !string.IsNullOrWhiteSpace(reg);
            var seq = sequenceNumber.ToString("000");

            string identifierPart;
            if (hasVin && hasReg)
                identifierPart = $"{vin!.Trim()}-VIN-{reg!.Trim()}-Reg";
            else if (hasVin)
                identifierPart = $"{vin!.Trim()}-VIN";
            else if (hasReg)
                identifierPart = $"{reg!.Trim()}-Reg";
            else
                throw new ArgumentException("At least one of VIN or Reg is required.");

            return $"{identifierPart}-{seq}{extension}";
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test MainProjectNumoPart.Tests --filter PhotoNamingTests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add Services/PhotoNaming.cs MainProjectNumoPart.Tests/PhotoNamingTests.cs
git commit -m "Add VIN/Reg blob folder and filename generation logic"
```

---

### Task 4: Vehicle Lookup and Photo Sequence Services

**Files:**
- Create: `Services/VehicleLookupService.cs`
- Create: `Services/PhotoSequenceAllocator.cs`
- Create: `MainProjectNumoPart.Tests/VehicleLookupServiceTests.cs`
- Create: `MainProjectNumoPart.Tests/PhotoSequenceAllocatorTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (Task 2), `PhotoNaming.ResolveBlobFolderName` (Task 3).
- Produces: `VehicleLookupService(AppDbContext db)` with `Task<Vehicle> FindOrCreateAsync(string? vin, string? reg, CancellationToken ct = default)` (adds to the context but does **not** call `SaveChangesAsync` — the caller controls the transaction boundary) and `Task<Vehicle?> FindBySearchTermAsync(string term, CancellationToken ct = default)`. `PhotoSequenceAllocator(AppDbContext db)` with `Task<int> NextSequenceNumberAsync(int vehicleId, Stage stage, CancellationToken ct = default)`. Task 10 (Upload) is the primary consumer of both; Task 11 (job page) consumes `FindBySearchTermAsync`.

- [ ] **Step 1: Write the failing tests**

```csharp
// MainProjectNumoPart.Tests/VehicleLookupServiceTests.cs
using System.Threading.Tasks;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class VehicleLookupServiceTests
    {
        [Fact]
        public async Task FindOrCreateAsync_CreatesNewVehicleWhenNotFound()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var vehicle = await service.FindOrCreateAsync("1HGBH41JXMN109186", "CA481329");
            await db.SaveChangesAsync();

            Assert.NotEqual(0, vehicle.Id);
            Assert.Equal("1HGBH41JXMN109186", vehicle.BlobFolderName);
        }

        [Fact]
        public async Task FindOrCreateAsync_FindsExistingByVin()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var first = await service.FindOrCreateAsync("1HGBH41JXMN109186", null);
            await db.SaveChangesAsync();

            var second = await service.FindOrCreateAsync("1HGBH41JXMN109186", null);
            await db.SaveChangesAsync();

            Assert.Equal(first.Id, second.Id);
        }

        [Fact]
        public async Task FindOrCreateAsync_BackfillsRegWithoutChangingFolderName()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var created = await service.FindOrCreateAsync(null, "CA481329");
            await db.SaveChangesAsync();
            var originalFolder = created.BlobFolderName;

            // The VIN turns up later for the same car.
            var found = await service.FindOrCreateAsync("1HGBH41JXMN109186", "CA481329");
            await db.SaveChangesAsync();

            Assert.Equal(created.Id, found.Id);
            Assert.Equal("1HGBH41JXMN109186", found.Vin);
            Assert.Equal(originalFolder, found.BlobFolderName); // Folder name never changes.
        }

        [Fact]
        public async Task FindBySearchTermAsync_MatchesEitherIdentifier()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            await service.FindOrCreateAsync("1HGBH41JXMN109186", "CA481329");
            await db.SaveChangesAsync();

            var byVin = await service.FindBySearchTermAsync("1HGBH41JXMN109186");
            var byReg = await service.FindBySearchTermAsync("CA481329");

            Assert.NotNull(byVin);
            Assert.NotNull(byReg);
            Assert.Equal(byVin!.Id, byReg!.Id);
        }

        [Fact]
        public async Task FindBySearchTermAsync_ReturnsNullWhenNotFound()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var result = await service.FindBySearchTermAsync("NOTHINGHERE");

            Assert.Null(result);
        }
    }
}
```

```csharp
// MainProjectNumoPart.Tests/PhotoSequenceAllocatorTests.cs
using System;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class PhotoSequenceAllocatorTests
    {
        [Fact]
        public async Task NextSequenceNumberAsync_StartsAtOne()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();

            var allocator = new PhotoSequenceAllocator(db);
            var next = await allocator.NextSequenceNumberAsync(vehicle.Id, Stage.Checkin);

            Assert.Equal(1, next);
        }

        [Fact]
        public async Task NextSequenceNumberAsync_IncrementsPerVehicleAndStage()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();

            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id, Stage = Stage.Checkin, SequenceNumber = 1,
                FileName = "a.jpg", BlobPathOriginal = "a", BlobPathThumbnail = "a",
                ContentType = "image/jpeg", UploadedAtUtc = DateTime.UtcNow, UploaderId = "u1"
            });
            await db.SaveChangesAsync();

            var allocator = new PhotoSequenceAllocator(db);

            Assert.Equal(2, await allocator.NextSequenceNumberAsync(vehicle.Id, Stage.Checkin));
            Assert.Equal(1, await allocator.NextSequenceNumberAsync(vehicle.Id, Stage.Quote)); // different stage, own counter
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test MainProjectNumoPart.Tests --filter "VehicleLookupServiceTests|PhotoSequenceAllocatorTests"`
Expected: compile errors — the services don't exist yet.

- [ ] **Step 3: Implement**

```csharp
// Services/VehicleLookupService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Services
{
    public class VehicleLookupService
    {
        private readonly AppDbContext _db;

        public VehicleLookupService(AppDbContext db)
        {
            _db = db;
        }

        // Adds (or updates) the tracked entity but does not save — the caller
        // decides when to commit, since Upload needs to add Photos in the same transaction.
        public async Task<Vehicle> FindOrCreateAsync(string? vin, string? reg, CancellationToken ct = default)
        {
            var normalizedVin = string.IsNullOrWhiteSpace(vin) ? null : vin.Trim();
            var normalizedReg = string.IsNullOrWhiteSpace(reg) ? null : reg.Trim();

            if (normalizedVin is null && normalizedReg is null)
                throw new ArgumentException("At least one of VIN or Reg is required.");

            var existing = await _db.Vehicles.FirstOrDefaultAsync(v =>
                (normalizedVin != null && v.Vin == normalizedVin) ||
                (normalizedReg != null && v.Reg == normalizedReg), ct);

            if (existing is not null)
            {
                // A car logged Reg-only can have its VIN discovered later.
                // BlobFolderName is never touched — that's what keeps existing blobs valid.
                if (normalizedVin is not null && existing.Vin is null) existing.Vin = normalizedVin;
                if (normalizedReg is not null && existing.Reg is null) existing.Reg = normalizedReg;
                return existing;
            }

            var vehicle = new Vehicle
            {
                Vin = normalizedVin,
                Reg = normalizedReg,
                BlobFolderName = PhotoNaming.ResolveBlobFolderName(normalizedVin, normalizedReg),
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.Vehicles.Add(vehicle);
            return vehicle;
        }

        public async Task<Vehicle?> FindBySearchTermAsync(string term, CancellationToken ct = default)
        {
            var normalized = term.Trim();
            return await _db.Vehicles.FirstOrDefaultAsync(v => v.Vin == normalized || v.Reg == normalized, ct);
        }
    }
}
```

```csharp
// Services/PhotoSequenceAllocator.cs
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Services
{
    public class PhotoSequenceAllocator
    {
        private readonly AppDbContext _db;

        public PhotoSequenceAllocator(AppDbContext db)
        {
            _db = db;
        }

        // A plain max+1 query is safe here because the app runs as a single instance
        // (see Global Constraints) — there is no concurrent writer to race against.
        public async Task<int> NextSequenceNumberAsync(int vehicleId, Stage stage, CancellationToken ct = default)
        {
            var max = await _db.Photos
                .Where(p => p.VehicleId == vehicleId && p.Stage == stage)
                .Select(p => (int?)p.SequenceNumber)
                .MaxAsync(ct);

            return (max ?? 0) + 1;
        }
    }
}
```

Add `using System.Linq;` to `PhotoSequenceAllocator.cs`.

- [ ] **Step 4: Register both services for DI**

```csharp
// Program.cs — add before `var app = builder.Build();`
builder.Services.AddScoped<Services.VehicleLookupService>();
builder.Services.AddScoped<Services.PhotoSequenceAllocator>();
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test MainProjectNumoPart.Tests --filter "VehicleLookupServiceTests|PhotoSequenceAllocatorTests"`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add Services/VehicleLookupService.cs Services/PhotoSequenceAllocator.cs Program.cs MainProjectNumoPart.Tests
git commit -m "Add vehicle find-or-create lookup and photo sequence allocation"
```

---

### Task 5: Blob Storage Abstraction (Azurite-backed)

**Files:**
- Create: `Services/IPhotoStorage.cs`
- Create: `Services/BlobPhotoStorage.cs`
- Create: `MainProjectNumoPart.Tests/FakePhotoStorage.cs`
- Create: `MainProjectNumoPart.Tests/BlobPhotoStorageTests.cs`
- Modify: `MainProjectNumoPart.csproj` (packages)
- Modify: `Program.cs` (DI wiring)
- Modify: `appsettings.Development.json`

**Interfaces:**
- Produces: `IPhotoStorage` with `UploadOriginalAsync`, `UploadThumbnailAsync`, `DeleteOriginalAsync`, `DeleteThumbnailAsync`, `OpenOriginalReadAsync`, `GetThumbnailReadUrlAsync`, `GetOriginalReadUrlAsync` — every signature listed in Step 3 below. `BlobPhotoStorage` implements it against real Blob Storage / Azurite. `FakePhotoStorage` (test project) implements it in-memory for tests that don't need a running Azurite. Tasks 6, 10, 11, 13 all consume `IPhotoStorage` by interface only.

- [ ] **Step 1: Add packages**

```bash
dotnet add MainProjectNumoPart.csproj package Azure.Storage.Blobs
dotnet add MainProjectNumoPart.csproj package Azure.Identity
```

- [ ] **Step 2: Install and start Azurite for local testing**

Visual Studio 2022 bundles Azurite: **Tools → Azurite → Start Azurite**. (Cross-platform alternative if not using Visual Studio: `npm install -g azurite` then run `azurite` in a terminal, or `docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite`.) Leave it running for the rest of this task and for manual testing in later tasks.

- [ ] **Step 3: Write the interface**

```csharp
// Services/IPhotoStorage.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MainProjectNumoPart.Services
{
    public interface IPhotoStorage
    {
        Task UploadOriginalAsync(string blobPath, Stream content, string contentType, CancellationToken ct = default);
        Task UploadThumbnailAsync(string blobPath, Stream content, string contentType, CancellationToken ct = default);
        Task DeleteOriginalAsync(string blobPath, CancellationToken ct = default);
        Task DeleteThumbnailAsync(string blobPath, CancellationToken ct = default);
        Task<Stream> OpenOriginalReadAsync(string blobPath, CancellationToken ct = default);
        Task<Uri> GetThumbnailReadUrlAsync(string blobPath, TimeSpan validFor, CancellationToken ct = default);
        Task<Uri> GetOriginalReadUrlAsync(string blobPath, TimeSpan validFor, CancellationToken ct = default);
    }
}
```

- [ ] **Step 4: Write the failing integration test**

```csharp
// MainProjectNumoPart.Tests/BlobPhotoStorageTests.cs
// Requires Azurite running locally (see Task 5, Step 2). If Azurite isn't running,
// these tests fail with a connection error, not a false pass.
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class BlobPhotoStorageTests
    {
        private static BlobPhotoStorage CreateStorage()
        {
            var client = new BlobServiceClient("UseDevelopmentStorage=true");
            return new BlobPhotoStorage(client);
        }

        [Fact]
        public async Task UploadAndOpenOriginal_RoundTrips()
        {
            var storage = CreateStorage();
            var path = $"test-vehicle/Checkin/roundtrip-{Guid.NewGuid()}.jpg";
            var content = Encoding.UTF8.GetBytes("fake jpeg bytes");

            await storage.UploadOriginalAsync(path, new MemoryStream(content), "image/jpeg");

            await using var readBack = await storage.OpenOriginalReadAsync(path);
            using var ms = new MemoryStream();
            await readBack.CopyToAsync(ms);

            Assert.Equal(content, ms.ToArray());
        }

        [Fact]
        public async Task DeleteOriginal_RemovesBlob()
        {
            var storage = CreateStorage();
            var path = $"test-vehicle/Checkin/delete-{Guid.NewGuid()}.jpg";
            await storage.UploadOriginalAsync(path, new MemoryStream(Encoding.UTF8.GetBytes("x")), "image/jpeg");

            await storage.DeleteOriginalAsync(path);

            await Assert.ThrowsAnyAsync<Exception>(() => storage.OpenOriginalReadAsync(path));
        }

        [Fact]
        public async Task GetThumbnailReadUrlAsync_ProducesAWorkingSasUrl()
        {
            var storage = CreateStorage();
            var path = $"test-vehicle/Checkin/sas-{Guid.NewGuid()}.jpg";
            var content = Encoding.UTF8.GetBytes("thumbnail bytes");
            await storage.UploadThumbnailAsync(path, new MemoryStream(content), "image/jpeg");

            var url = await storage.GetThumbnailReadUrlAsync(path, TimeSpan.FromMinutes(15));

            using var http = new System.Net.Http.HttpClient();
            var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var downloaded = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(content, downloaded);
        }
    }
}
```

- [ ] **Step 5: Run to verify it fails**

Run: `dotnet test MainProjectNumoPart.Tests --filter BlobPhotoStorageTests`
Expected: compile error — `BlobPhotoStorage` doesn't exist yet.

- [ ] **Step 6: Implement**

```csharp
// Services/BlobPhotoStorage.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace MainProjectNumoPart.Services
{
    public class BlobPhotoStorage : IPhotoStorage
    {
        private const string OriginalsContainer = "originals";
        private const string ThumbnailsContainer = "thumbnails";

        private readonly BlobServiceClient _serviceClient;
        private readonly BlobContainerClient _originals;
        private readonly BlobContainerClient _thumbnails;

        public BlobPhotoStorage(BlobServiceClient serviceClient)
        {
            _serviceClient = serviceClient;
            _originals = serviceClient.GetBlobContainerClient(OriginalsContainer);
            _thumbnails = serviceClient.GetBlobContainerClient(ThumbnailsContainer);
        }

        public Task UploadOriginalAsync(string blobPath, Stream content, string contentType, CancellationToken ct = default)
            => UploadAsync(_originals, blobPath, content, contentType, ct);

        public Task UploadThumbnailAsync(string blobPath, Stream content, string contentType, CancellationToken ct = default)
            => UploadAsync(_thumbnails, blobPath, content, contentType, ct);

        private static async Task UploadAsync(BlobContainerClient container, string blobPath, Stream content, string contentType, CancellationToken ct)
        {
            await container.CreateIfNotExistsAsync(cancellationToken: ct);
            var blob = container.GetBlobClient(blobPath);
            await blob.UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            }, ct);
        }

        public async Task DeleteOriginalAsync(string blobPath, CancellationToken ct = default)
            => await _originals.GetBlobClient(blobPath).DeleteIfExistsAsync(cancellationToken: ct);

        public async Task DeleteThumbnailAsync(string blobPath, CancellationToken ct = default)
            => await _thumbnails.GetBlobClient(blobPath).DeleteIfExistsAsync(cancellationToken: ct);

        public async Task<Stream> OpenOriginalReadAsync(string blobPath, CancellationToken ct = default)
        {
            var blob = _originals.GetBlobClient(blobPath);
            var response = await blob.DownloadStreamingAsync(cancellationToken: ct);
            return response.Value.Content;
        }

        public Task<Uri> GetThumbnailReadUrlAsync(string blobPath, TimeSpan validFor, CancellationToken ct = default)
            => GetReadUrlAsync(_thumbnails, blobPath, validFor, ct);

        public Task<Uri> GetOriginalReadUrlAsync(string blobPath, TimeSpan validFor, CancellationToken ct = default)
            => GetReadUrlAsync(_originals, blobPath, validFor, ct);

        // Two auth paths, both producing a read-only, time-limited URL:
        //  - Local dev / Azurite: the connection string carries an account key, so a
        //    standard account-key SAS works directly off the BlobClient.
        //  - Production: the app authenticates via Managed Identity, which has no
        //    account key at all. A "user delegation SAS" is Azure's supported
        //    alternative — it's signed with a short-lived key obtained via Azure AD
        //    instead of the account key, so no key is ever stored anywhere.
        private async Task<Uri> GetReadUrlAsync(BlobContainerClient container, string blobPath, TimeSpan validFor, CancellationToken ct)
        {
            var blob = container.GetBlobClient(blobPath);
            var now = DateTimeOffset.UtcNow;

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = container.Name,
                BlobName = blobPath,
                Resource = "b",
                StartsOn = now.AddMinutes(-5),
                ExpiresOn = now.Add(validFor)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            if (_serviceClient.CanGenerateAccountSasUri)
            {
                return blob.GenerateSasUri(sasBuilder);
            }

            var delegationKey = await _serviceClient.GetUserDelegationKeyAsync(now.AddMinutes(-5), now.Add(validFor), ct);
            var sasQuery = sasBuilder.ToSasQueryParameters(delegationKey.Value, _serviceClient.AccountName);

            var uriBuilder = new UriBuilder(blob.Uri) { Query = sasQuery.ToString() };
            return uriBuilder.Uri;
        }
    }
}
```

- [ ] **Step 7: Wire up DI and local configuration**

```csharp
// Program.cs — add before `var app = builder.Build();`
builder.Services.AddSingleton(sp =>
{
    var connectionString = builder.Configuration["BlobStorage:ConnectionString"];
    return !string.IsNullOrEmpty(connectionString)
        ? new BlobServiceClient(connectionString)
        : new BlobServiceClient(new Uri(builder.Configuration["BlobStorage:ServiceUri"]!), new Azure.Identity.DefaultAzureCredential());
});
builder.Services.AddScoped<Services.IPhotoStorage, Services.BlobPhotoStorage>();
```

Add `using Azure.Storage.Blobs;` to the top of `Program.cs`.

```json
// appsettings.Development.json — add
{
  "BlobStorage": {
    "ConnectionString": "UseDevelopmentStorage=true"
  }
}
```

(Merge this key into the existing JSON object — don't replace the file.)

- [ ] **Step 8: Write the in-memory fake for tests that don't need Azurite**

```csharp
// MainProjectNumoPart.Tests/FakePhotoStorage.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Services;

namespace MainProjectNumoPart.Tests
{
    public class FakePhotoStorage : IPhotoStorage
    {
        public Dictionary<string, byte[]> Originals { get; } = new();
        public Dictionary<string, byte[]> Thumbnails { get; } = new();

        public async Task UploadOriginalAsync(string blobPath, Stream content, string contentType, CancellationToken ct = default)
            => Originals[blobPath] = await ReadAllAsync(content, ct);

        public async Task UploadThumbnailAsync(string blobPath, Stream content, string contentType, CancellationToken ct = default)
            => Thumbnails[blobPath] = await ReadAllAsync(content, ct);

        private static async Task<byte[]> ReadAllAsync(Stream content, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            return ms.ToArray();
        }

        public Task DeleteOriginalAsync(string blobPath, CancellationToken ct = default)
        {
            Originals.Remove(blobPath);
            return Task.CompletedTask;
        }

        public Task DeleteThumbnailAsync(string blobPath, CancellationToken ct = default)
        {
            Thumbnails.Remove(blobPath);
            return Task.CompletedTask;
        }

        public Task<Stream> OpenOriginalReadAsync(string blobPath, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream(Originals[blobPath]));

        public Task<Uri> GetThumbnailReadUrlAsync(string blobPath, TimeSpan validFor, CancellationToken ct = default)
            => Task.FromResult(new Uri($"https://fake.local/thumbnails/{blobPath}"));

        public Task<Uri> GetOriginalReadUrlAsync(string blobPath, TimeSpan validFor, CancellationToken ct = default)
            => Task.FromResult(new Uri($"https://fake.local/originals/{blobPath}"));
    }
}
```

- [ ] **Step 9: Run the integration tests (Azurite must be running)**

Run: `dotnet test MainProjectNumoPart.Tests --filter BlobPhotoStorageTests`
Expected: all pass. If they fail with a connection error, confirm Azurite is running (Step 2).

- [ ] **Step 10: Commit**

```bash
git add Services/IPhotoStorage.cs Services/BlobPhotoStorage.cs Program.cs MainProjectNumoPart.csproj appsettings.Development.json MainProjectNumoPart.Tests
git commit -m "Add blob storage abstraction with Azurite-backed implementation"
```

---

### Task 6: Photo Serving Endpoints (Thumbnail/Original)

**Files:**
- Create: `Endpoints/PhotoEndpoints.cs`
- Modify: `Program.cs`

**Interfaces:**
- Consumes: `AppDbContext.Photos` (Task 2), `IPhotoStorage.GetThumbnailReadUrlAsync` / `GetOriginalReadUrlAsync` (Task 5).
- Produces: `GET /api/photos/{id}/thumbnail` and `GET /api/photos/{id}/original`, both requiring authentication, both 302-redirecting to a fresh 15-minute SAS URL. `MainProjectNumoPart.Endpoints.PhotoEndpoints.MapPhotoEndpoints(this WebApplication app)`. Tasks 11 and 12 render `<img src="/api/photos/{id}/thumbnail">` relying on this route existing.

- [ ] **Step 1: Write the endpoint mapping**

```csharp
// Endpoints/PhotoEndpoints.cs
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Services;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Endpoints
{
    public static class PhotoEndpoints
    {
        private static readonly TimeSpan SasLifetime = TimeSpan.FromMinutes(15);

        public static void MapPhotoEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/photos").RequireAuthorization();

            group.MapGet("/{id:int}/thumbnail", async (int id, AppDbContext db, IPhotoStorage storage) =>
            {
                var photo = await db.Photos.FindAsync(id);
                if (photo is null) return Results.NotFound();
                var url = await storage.GetThumbnailReadUrlAsync(photo.BlobPathThumbnail, SasLifetime);
                return Results.Redirect(url.ToString());
            });

            group.MapGet("/{id:int}/original", async (int id, AppDbContext db, IPhotoStorage storage) =>
            {
                var photo = await db.Photos.FindAsync(id);
                if (photo is null) return Results.NotFound();
                var url = await storage.GetOriginalReadUrlAsync(photo.BlobPathOriginal, SasLifetime);
                return Results.Redirect(url.ToString());
            });
        }
    }
}
```

- [ ] **Step 2: Wire it into the request pipeline**

```csharp
// Program.cs — add after `app.MapRazorPages();` and before `app.Run();`
app.MapPhotoEndpoints();
```

Add `using MainProjectNumoPart.Endpoints;` to the top of `Program.cs`.

- [ ] **Step 3: Verify manually**

There's no seeded photo yet (Upload doesn't exist until Task 10), so full end-to-end verification of this route happens naturally once Task 10 and 11 are done and a photo can actually be uploaded and viewed. For now:

Run: `dotnet build`
Expected: builds with no errors — confirms the endpoint mapping and DI types line up.

- [ ] **Step 4: Commit**

```bash
git add Endpoints/PhotoEndpoints.cs Program.cs
git commit -m "Add SAS-redirect endpoints for serving photo thumbnails and originals"
```

---

### Task 7: Identity Wiring and Admin Seeding

**Files:**
- Modify: `Program.cs`
- Modify: `appsettings.Development.json`
- Create: `Services/AdminSeeder.cs`
- Create: `MainProjectNumoPart.Tests/AdminSeederTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (Task 2).
- Produces: an `"Admin"` `IdentityRole` guaranteed to exist at startup; `AdminSeeder.SeedInitialAdminAsync(IServiceProvider services)` — creates exactly one admin account from `InitialAdmin:Email` / `InitialAdmin:Password` config **only if zero users exist**, throwing `InvalidOperationException` if those keys are missing at that point. Task 8 (Login) and Task 9 (admin create-user page) both depend on `UserManager<IdentityUser>` / `SignInManager<IdentityUser>` being registered by this task.

- [ ] **Step 1: Write the failing test**

```csharp
// MainProjectNumoPart.Tests/AdminSeederTests.cs
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class AdminSeederTests
    {
        private static ServiceProvider BuildServices(string? adminEmail, string? adminPassword)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();

            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
            services.AddLogging();
            services.AddIdentity<IdentityUser, IdentityRole>()
                .AddEntityFrameworkStores<AppDbContext>()
                .AddDefaultTokenProviders();

            var configData = new System.Collections.Generic.Dictionary<string, string?>();
            if (adminEmail is not null) configData["InitialAdmin:Email"] = adminEmail;
            if (adminPassword is not null) configData["InitialAdmin:Password"] = adminPassword;
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configData).Build());

            var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

            return provider;
        }

        [Fact]
        public async Task SeedsAdminWhenNoUsersExist()
        {
            var services = BuildServices("admin@workshop.local", "Str0ng!Passw0rd");

            await AdminSeeder.SeedInitialAdminAsync(services);

            using var scope = services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var admin = await userManager.FindByEmailAsync("admin@workshop.local");

            Assert.NotNull(admin);
            Assert.True(await userManager.IsInRoleAsync(admin!, "Admin"));
        }

        [Fact]
        public async Task DoesNotReseedWhenUsersAlreadyExist()
        {
            var services = BuildServices("admin@workshop.local", "Str0ng!Passw0rd");
            await AdminSeeder.SeedInitialAdminAsync(services);

            // Second call must not throw or duplicate — it should see an existing user and no-op.
            await AdminSeeder.SeedInitialAdminAsync(services);

            using var scope = services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            Assert.Single(userManager.Users);
        }

        [Fact]
        public async Task ThrowsWhenNoUsersAndNoConfig()
        {
            var services = BuildServices(null, null);

            await Assert.ThrowsAsync<InvalidOperationException>(() => AdminSeeder.SeedInitialAdminAsync(services));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test MainProjectNumoPart.Tests --filter AdminSeederTests`
Expected: compile error — `AdminSeeder` doesn't exist yet.

- [ ] **Step 3: Implement**

```csharp
// Services/AdminSeeder.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MainProjectNumoPart.Services
{
    public static class AdminSeeder
    {
        public static async Task SeedInitialAdminAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            if (!await roleManager.RoleExistsAsync("Admin"))
            {
                await roleManager.CreateAsync(new IdentityRole("Admin"));
            }

            if (userManager.Users.Any())
            {
                return; // Already bootstrapped — never re-seed.
            }

            var email = config["InitialAdmin:Email"];
            var password = config["InitialAdmin:Password"];

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException(
                    "No user accounts exist and InitialAdmin:Email/InitialAdmin:Password are not configured. " +
                    "Set them in appsettings.Development.json locally, or as environment configuration in production.");
            }

            var admin = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            var result = await userManager.CreateAsync(admin, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Failed to seed initial admin: " + string.Join("; ", result.Errors.Select(e => e.Description)));
            }

            await userManager.AddToRoleAsync(admin, "Admin");
        }
    }
}
```

- [ ] **Step 4: Wire Identity and the seeder into Program.cs**

```csharp
// Program.cs — add before `var app = builder.Build();`
builder.Services.AddIdentity<Microsoft.AspNetCore.Identity.IdentityUser, Microsoft.AspNetCore.Identity.IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<Data.AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});
```

```csharp
// Program.cs — add after `var app = builder.Build();`, before `app.Run();`
await Services.AdminSeeder.SeedInitialAdminAsync(app.Services);

app.MapPost("/account/logout", async (Microsoft.AspNetCore.Identity.SignInManager<Microsoft.AspNetCore.Identity.IdentityUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/");
}).RequireAuthorization();
```

Also add `app.UseAuthentication();` immediately before the existing `app.UseAuthorization();` line — Identity requires both, in that order.

- [ ] **Step 5: Add local admin credentials to configuration**

```json
// appsettings.Development.json — add
{
  "InitialAdmin": {
    "Email": "admin@workshop.local",
    "Password": "ChangeMe123!"
  }
}
```

(Merge into the existing JSON object. This file is already `.gitignore`d for secrets? No — check: it currently is not, and template `appsettings.Development.json` files are typically committed. Since this is a throwaway local dev password for a machine only you use, that's acceptable; production will use real environment configuration, not this file — see `docs/LOCAL_DEV.md` in Task 14.)

- [ ] **Step 6: Create and apply a migration for the Identity + Role tables**

The `InitialCreate` migration from Task 2 already included Identity tables (via `IdentityDbContext`), but adding `IdentityRole` support doesn't change the schema — `IdentityDbContext` (non-generic) already includes role tables. Confirm no new migration is needed:

Run: `dotnet ef migrations add AddRoleSupport --project MainProjectNumoPart.csproj`
Expected: EF reports no model changes were detected (or produces an empty migration). If empty, delete the generated migration files — nothing to apply.

- [ ] **Step 7: Run the tests**

Run: `dotnet test MainProjectNumoPart.Tests --filter AdminSeederTests`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add Services/AdminSeeder.cs Program.cs appsettings.Development.json MainProjectNumoPart.Tests
git commit -m "Wire up ASP.NET Core Identity with roles and first-run admin seeding"
```

---

### Task 8: Login Page

**Files:**
- Create: `Pages/Account/Login.cshtml`
- Create: `Pages/Account/Login.cshtml.cs`

**Interfaces:**
- Consumes: `SignInManager<IdentityUser>` (registered in Task 7).
- Produces: `GET/POST /Account/Login`. Every other authenticated page (Tasks 9-13) relies on unauthenticated requests redirecting here (`ConfigureApplicationCookie` in Task 7 already points `LoginPath` here).

- [ ] **Step 1: Write the page model**

```csharp
// Pages/Account/Login.cshtml.cs
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MainProjectNumoPart.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;

        public LoginModel(SignInManager<IdentityUser> signInManager)
        {
            _signInManager = signInManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? ErrorMessage { get; set; }

        public class InputModel
        {
            [Required]
            public string EmailOrUsername { get; set; } = "";

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; } = "";
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var result = await _signInManager.PasswordSignInAsync(
                Input.EmailOrUsername, Input.Password, isPersistent: true, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                return LocalRedirect(returnUrl ?? "/");
            }

            ErrorMessage = result.IsLockedOut
                ? "Too many failed attempts. Try again later."
                : "Incorrect email/username or password.";
            return Page();
        }
    }
}
```

- [ ] **Step 2: Write the view, matching the approved mockup**

```cshtml
@* Pages/Account/Login.cshtml *@
@page
@model MainProjectNumoPart.Pages.Account.LoginModel
@{
    ViewData["Title"] = "Sign in";
    Layout = null;
}
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <title>Sign in — Workshop</title>
    <link rel="stylesheet" href="~/lib/bootstrap/dist/css/bootstrap.min.css" />
    <link rel="stylesheet" href="~/css/site.css" />
</head>
<body class="login-body">
    <div class="login-frame">
        <div class="login-card">
            <div class="login-logo">Workshop</div>
            <div class="login-sub">Sign in to continue</div>

            @if (Model.ErrorMessage is not null)
            {
                <div class="alert alert-danger py-2">@Model.ErrorMessage</div>
            }

            <form method="post">
                <div asp-validation-summary="ModelOnly" class="text-danger"></div>
                <label class="login-field-label" asp-for="Input.EmailOrUsername">Email or username</label>
                <input asp-for="Input.EmailOrUsername" class="form-control login-input" autofocus />

                <label class="login-field-label" asp-for="Input.Password">Password</label>
                <input asp-for="Input.Password" class="form-control login-input" />

                <button type="submit" class="btn login-btn w-100">Sign in</button>
            </form>

            <div class="login-lock">🔒 Secured — HTTPS, private access only</div>
            <div class="login-footer">
                This is a private system.<br />
                Accounts are created by an admin — no public sign-up.
            </div>
        </div>
    </div>
</body>
</html>
```

- [ ] **Step 3: Add the login page styles**

```css
/* wwwroot/css/site.css — append */
.login-body {
    margin: 0;
    background: linear-gradient(160deg, #1b2030 0%, #2b3352 100%);
    min-height: 100vh;
    display: flex;
    align-items: center;
    justify-content: center;
    font-family: -apple-system, Segoe UI, Roboto, sans-serif;
}

.login-card {
    width: 340px;
    background: rgba(255, 255, 255, 0.98);
    border-radius: 12px;
    padding: 30px 28px 26px;
    box-shadow: 0 20px 50px rgba(0, 0, 0, 0.35);
}

.login-logo {
    text-align: center;
    font-weight: 700;
    font-size: 1.2rem;
    letter-spacing: -0.02em;
    color: #111;
}

.login-sub {
    text-align: center;
    font-size: 0.82rem;
    color: #666;
    margin-bottom: 18px;
}

.login-field-label {
    font-size: 0.75rem;
    font-weight: 600;
    color: #444;
    margin-bottom: 4px;
    display: block;
}

.login-input {
    margin-bottom: 14px;
}

.login-btn {
    background: #2f6fed;
    color: #fff;
    font-weight: 600;
}

.login-btn:hover {
    background: #2559c4;
    color: #fff;
}

.login-lock {
    text-align: center;
    font-size: 0.7rem;
    color: #5a9c5a;
    margin-top: 12px;
}

.login-footer {
    text-align: center;
    font-size: 0.72rem;
    color: #888;
    margin-top: 14px;
    line-height: 1.4;
}
```

- [ ] **Step 4: Verify manually**

Run: `dotnet run --project MainProjectNumoPart.csproj`

Navigate to `/Account/Login`. Sign in with the seeded admin credentials from `appsettings.Development.json` (Task 7, Step 5). Expected: successful sign-in redirects to `/`. Wrong password shows "Incorrect email/username or password." Confirm attempting five wrong passwords in a row triggers the lockout message (Identity's default lockout threshold).

- [ ] **Step 5: Commit**

```bash
git add Pages/Account wwwroot/css/site.css
git commit -m "Add login page"
```

---

### Task 9: Admin Create-User Page

**Files:**
- Create: `Pages/Admin/CreateUser.cshtml`
- Create: `Pages/Admin/CreateUser.cshtml.cs`

**Interfaces:**
- Consumes: `UserManager<IdentityUser>` (Task 7).
- Produces: `GET/POST /Admin/CreateUser`, restricted to the `Admin` role — the only way (besides the Task 7 seed) that new accounts get created, closing the invite-only loop.

- [ ] **Step 1: Write the page model**

```csharp
// Pages/Admin/CreateUser.cshtml.cs
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MainProjectNumoPart.Pages.Admin
{
    [Authorize(Roles = "Admin")]
    public class CreateUserModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;

        public CreateUserModel(UserManager<IdentityUser> userManager)
        {
            _userManager = userManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? SuccessMessage { get; set; }

        public class InputModel
        {
            [Required, EmailAddress]
            public string Email { get; set; } = "";

            [Required, MinLength(8)]
            public string TemporaryPassword { get; set; } = "";
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = new IdentityUser { UserName = Input.Email, Email = Input.Email, EmailConfirmed = true };
            var result = await _userManager.CreateAsync(user, Input.TemporaryPassword);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return Page();
            }

            SuccessMessage = $"Account created for {Input.Email}.";
            Input = new InputModel();
            return Page();
        }
    }
}
```

- [ ] **Step 2: Write the view**

```cshtml
@* Pages/Admin/CreateUser.cshtml *@
@page
@model MainProjectNumoPart.Pages.Admin.CreateUserModel
@{
    ViewData["Title"] = "Create account";
}

<h2>Create account</h2>

@if (Model.SuccessMessage is not null)
{
    <div class="alert alert-success">@Model.SuccessMessage</div>
}

<form method="post" class="col-md-4">
    <div asp-validation-summary="ModelOnly" class="text-danger"></div>
    <div class="mb-3">
        <label asp-for="Input.Email" class="form-label"></label>
        <input asp-for="Input.Email" class="form-control" />
        <span asp-validation-for="Input.Email" class="text-danger"></span>
    </div>
    <div class="mb-3">
        <label asp-for="Input.TemporaryPassword" class="form-label">Temporary password</label>
        <input asp-for="Input.TemporaryPassword" class="form-control" />
        <span asp-validation-for="Input.TemporaryPassword" class="text-danger"></span>
    </div>
    <button type="submit" class="btn btn-primary">Create account</button>
</form>
```

- [ ] **Step 3: Verify manually**

Run the app, sign in as the seeded admin, navigate to `/Admin/CreateUser`, create a second account, sign out, sign back in as the new account. Expected: succeeds. Then, while signed in as the non-admin account, navigate directly to `/Admin/CreateUser`. Expected: redirected away (403/access denied), confirming the role restriction works.

- [ ] **Step 4: Commit**

```bash
git add Pages/Admin
git commit -m "Add admin-only account creation page"
```

---

### Task 10: Upload Page

**Files:**
- Create: `Services/ThumbnailGenerator.cs`
- Create: `Services/ExifDateReader.cs`
- Create: `Pages/Upload.cshtml`
- Create: `Pages/Upload.cshtml.cs`
- Modify: `MainProjectNumoPart.csproj` (packages)

**Interfaces:**
- Consumes: `PhotoNaming.BuildFileName` (Task 3), `VehicleLookupService.FindOrCreateAsync` (Task 4), `PhotoSequenceAllocator.NextSequenceNumberAsync` (Task 4), `IPhotoStorage` (Task 5).
- Produces: `GET/POST /Upload`, `ThumbnailGenerator.CreateThumbnailAsync(Stream source, CancellationToken ct = default) : Task<MemoryStream>` (always JPEG output regardless of input format), `ExifDateReader.TryReadDateTaken(Stream imageStream) : DateTime?`.

- [ ] **Step 1: Add image processing packages**

```bash
dotnet add MainProjectNumoPart.csproj package SixLabors.ImageSharp
dotnet add MainProjectNumoPart.csproj package MetadataExtractor
```

- [ ] **Step 2: Implement the thumbnail generator**

```csharp
// Services/ThumbnailGenerator.cs
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace MainProjectNumoPart.Services
{
    public static class ThumbnailGenerator
    {
        private const int MaxDimension = 400;

        // Always JPEG, regardless of the source format — this is why BlobPathThumbnail
        // can differ in extension from BlobPathOriginal for PNG uploads.
        public static async Task<MemoryStream> CreateThumbnailAsync(Stream sourceImage, CancellationToken ct = default)
        {
            using var image = await Image.LoadAsync(sourceImage, ct);

            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(MaxDimension, MaxDimension)
            }));

            var output = new MemoryStream();
            await image.SaveAsync(output, new JpegEncoder { Quality = 80 }, ct);
            output.Position = 0;
            return output;
        }
    }
}
```

- [ ] **Step 3: Implement the EXIF reader**

```csharp
// Services/ExifDateReader.cs
using System;
using System.IO;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace MainProjectNumoPart.Services
{
    public static class ExifDateReader
    {
        public static DateTime? TryReadDateTaken(Stream imageStream)
        {
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(imageStream);
                var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

                if (subIfd is not null && subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTime))
                {
                    return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                }
            }
            catch (ImageProcessingException)
            {
                // Not every JPEG carries parseable EXIF, and PNGs never do —
                // absence is the expected case, not a failure.
            }

            return null;
        }
    }
}
```

- [ ] **Step 4: Write the upload page model**

```csharp
// Pages/Upload.cshtml.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MainProjectNumoPart.Pages
{
    [Authorize]
    public class UploadModel : PageModel
    {
        private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/png"
        };
        private const long MaxFileSizeBytes = 25 * 1024 * 1024;

        private readonly AppDbContext _db;
        private readonly VehicleLookupService _vehicles;
        private readonly PhotoSequenceAllocator _sequencer;
        private readonly IPhotoStorage _storage;
        private readonly UserManager<IdentityUser> _userManager;

        public UploadModel(AppDbContext db, VehicleLookupService vehicles, PhotoSequenceAllocator sequencer,
            IPhotoStorage storage, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _vehicles = vehicles;
            _sequencer = sequencer;
            _storage = storage;
            _userManager = userManager;
        }

        [BindProperty] public string? Vin { get; set; }
        [BindProperty] public string? Reg { get; set; }
        [BindProperty] public Stage Stage { get; set; }
        [BindProperty] public DateTime? DateTaken { get; set; }
        [BindProperty] public List<IFormFile> Files { get; set; } = new();

        public string? ErrorMessage { get; set; }

        public void OnGet(int? vehicleId)
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrWhiteSpace(Vin) && string.IsNullOrWhiteSpace(Reg))
            {
                ErrorMessage = "Enter a VIN, a registration number, or both.";
                return Page();
            }

            if (Files.Count == 0)
            {
                ErrorMessage = "Choose at least one photo.";
                return Page();
            }

            foreach (var file in Files)
            {
                if (!AllowedContentTypes.Contains(file.ContentType))
                {
                    ErrorMessage = $"{file.FileName}: only JPEG and PNG are supported.";
                    return Page();
                }
                if (file.Length > MaxFileSizeBytes)
                {
                    ErrorMessage = $"{file.FileName}: exceeds the 25MB limit.";
                    return Page();
                }
            }

            var userId = _userManager.GetUserId(User)!;
            var vehicle = await _vehicles.FindOrCreateAsync(Vin, Reg);
            await _db.SaveChangesAsync(); // assigns vehicle.Id before it's used in blob paths below

            var uploadedBlobPaths = new List<(string original, string thumbnail)>();

            try
            {
                foreach (var file in Files)
                {
                    var sequenceNumber = await _sequencer.NextSequenceNumberAsync(vehicle.Id, Stage);
                    var originalExtension = Path.GetExtension(file.FileName);
                    var originalFileName = PhotoNaming.BuildFileName(vehicle.Vin, vehicle.Reg, sequenceNumber, originalExtension);
                    var thumbnailFileName = PhotoNaming.BuildFileName(vehicle.Vin, vehicle.Reg, sequenceNumber, ".jpg");

                    var blobPathOriginal = $"{vehicle.BlobFolderName}/{Stage}/{originalFileName}";
                    var blobPathThumbnail = $"{vehicle.BlobFolderName}/{Stage}/{thumbnailFileName}";

                    using var buffered = new MemoryStream();
                    await using (var uploadStream = file.OpenReadStream())
                    {
                        await uploadStream.CopyToAsync(buffered);
                    }
                    buffered.Position = 0;

                    DateTime? dateTaken = DateTaken.HasValue
                        ? DateTime.SpecifyKind(DateTaken.Value, DateTimeKind.Utc)
                        : ExifDateReader.TryReadDateTaken(buffered);
                    buffered.Position = 0;

                    using var thumbnailStream = await ThumbnailGenerator.CreateThumbnailAsync(buffered);
                    buffered.Position = 0;

                    await _storage.UploadOriginalAsync(blobPathOriginal, buffered, file.ContentType);
                    await _storage.UploadThumbnailAsync(blobPathThumbnail, thumbnailStream, "image/jpeg");
                    uploadedBlobPaths.Add((blobPathOriginal, blobPathThumbnail));

                    _db.Photos.Add(new Photo
                    {
                        VehicleId = vehicle.Id,
                        Stage = Stage,
                        FileName = originalFileName,
                        BlobPathOriginal = blobPathOriginal,
                        BlobPathThumbnail = blobPathThumbnail,
                        ContentType = file.ContentType,
                        SizeBytes = file.Length,
                        UploadedAtUtc = DateTime.UtcNow,
                        DateTakenUtc = dateTaken,
                        SequenceNumber = sequenceNumber,
                        UploaderId = userId
                    });
                }

                await _db.SaveChangesAsync();
            }
            catch
            {
                // The DB save (or a later blob write in the loop) failed after some blobs
                // already landed — remove them so storage doesn't silently accumulate
                // untracked files that still cost money.
                foreach (var (original, thumbnail) in uploadedBlobPaths)
                {
                    await _storage.DeleteOriginalAsync(original);
                    await _storage.DeleteThumbnailAsync(thumbnail);
                }
                throw;
            }

            return RedirectToPage("/Vehicles/Details", new { id = vehicle.Id });
        }
    }
}
```

- [ ] **Step 5: Write the view**

```cshtml
@* Pages/Upload.cshtml *@
@page
@model MainProjectNumoPart.Pages.UploadModel
@{
    ViewData["Title"] = "Upload photos";
}

<h2>Upload photos</h2>

@if (Model.ErrorMessage is not null)
{
    <div class="alert alert-danger">@Model.ErrorMessage</div>
}

<form method="post" enctype="multipart/form-data" class="col-md-6">
    <div class="mb-3">
        <label class="form-label">Stage</label>
        <select asp-for="Stage" class="form-select"
                asp-items="Html.GetEnumSelectList<MainProjectNumoPart.Models.Stage>()"></select>
    </div>
    <div class="mb-3">
        <label asp-for="Vin" class="form-label">VIN</label>
        <input asp-for="Vin" class="form-control" />
    </div>
    <div class="mb-3">
        <label asp-for="Reg" class="form-label">Registration</label>
        <input asp-for="Reg" class="form-control" />
    </div>
    <div class="mb-3">
        <label asp-for="DateTaken" class="form-label">Date taken (optional — read from the photo when possible)</label>
        <input asp-for="DateTaken" type="date" class="form-control" />
    </div>
    <div class="mb-3">
        <label class="form-label">Photos</label>
        <input asp-for="Files" type="file" class="form-control" multiple accept="image/jpeg,image/png" />
    </div>
    <button type="submit" class="btn btn-primary">Upload</button>
</form>
```

- [ ] **Step 6: Register the DbContext connection and IFormFile size limits**

```csharp
// Program.cs — inside builder.Services.Configure<FormOptions> (add if not present) before var app = builder.Build();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 25 * 1024 * 1024 * 10; // headroom for a multi-file batch; per-file cap is enforced in code
});
```

- [ ] **Step 7: Verify manually**

Run the app (Azurite running, per Task 5), sign in, go to `/Upload`. Upload two JPEGs and one PNG against the same new VIN, stage `Checkin`. Expected: redirected to the vehicle's page (Task 11 — if that page doesn't exist yet at the point you run this, you'll get a 404 on redirect, which is expected until Task 11 lands; confirm instead by inspecting the SQLite file and Azurite containers directly, e.g. via Azure Storage Explorer, to see the three blobs and three `Photo` rows with sequence numbers 1, 2, 3).

- [ ] **Step 8: Commit**

```bash
git add Services/ThumbnailGenerator.cs Services/ExifDateReader.cs Pages/Upload.cshtml Pages/Upload.cshtml.cs Program.cs MainProjectNumoPart.csproj
git commit -m "Add upload page: validation, EXIF/thumbnail generation, blob + DB save with orphan cleanup"
```

---

### Task 11: Search, Home Page, and Vehicle Job Page

**Files:**
- Modify: `Pages/Index.cshtml`
- Modify: `Pages/Index.cshtml.cs`
- Create: `Pages/Vehicles/Details.cshtml`
- Create: `Pages/Vehicles/Details.cshtml.cs`
- Modify: `Endpoints/PhotoEndpoints.cs` (add delete route)

**Interfaces:**
- Consumes: `VehicleLookupService.FindBySearchTermAsync` (Task 4), `AppDbContext.Vehicles`/`.Photos` (Task 2), `/api/photos/{id}/thumbnail` (Task 6).
- Produces: `GET /` (search + recent vehicles), `GET /Vehicles/Details?id={id}` (job page), `DELETE /api/photos/{id}` restricted to the `Admin` role.

- [ ] **Step 1: Rewrite the home page model**

```csharp
// Pages/Index.cshtml.cs
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Pages
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly Services.VehicleLookupService _vehicles;

        public IndexModel(AppDbContext db, Services.VehicleLookupService vehicles)
        {
            _db = db;
            _vehicles = vehicles;
        }

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        public string? NotFoundMessage { get; set; }
        public System.Collections.Generic.List<Vehicle> RecentVehicles { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!string.IsNullOrWhiteSpace(Search))
            {
                var vehicle = await _vehicles.FindBySearchTermAsync(Search);
                if (vehicle is not null)
                {
                    return RedirectToPage("/Vehicles/Details", new { id = vehicle.Id });
                }
                NotFoundMessage = $"No vehicle found matching \"{Search}\".";
            }

            RecentVehicles = await _db.Vehicles
                .OrderByDescending(v => v.CreatedAtUtc)
                .Take(10)
                .ToListAsync();

            return Page();
        }
    }
}
```

- [ ] **Step 2: Rewrite the home page view**

```cshtml
@* Pages/Index.cshtml *@
@page
@model MainProjectNumoPart.Pages.IndexModel
@{
    ViewData["Title"] = "Search";
}

<div class="search-hero">
    <form method="get" class="search-form">
        <input type="text" name="Search" value="@Model.Search" class="form-control form-control-lg"
               placeholder="Search by VIN or registration number" autofocus />
        <button type="submit" class="btn btn-primary btn-lg">Search</button>
    </form>
    @if (Model.NotFoundMessage is not null)
    {
        <p class="text-danger mt-2">@Model.NotFoundMessage</p>
    }
</div>

<h3 class="mt-4">Recently added vehicles</h3>
<table class="table">
    <thead><tr><th>VIN</th><th>Reg</th><th>Make/Model</th><th>Added</th></tr></thead>
    <tbody>
    @foreach (var vehicle in Model.RecentVehicles)
    {
        <tr>
            <td><a asp-page="/Vehicles/Details" asp-route-id="@vehicle.Id">@vehicle.Vin</a></td>
            <td><a asp-page="/Vehicles/Details" asp-route-id="@vehicle.Id">@vehicle.Reg</a></td>
            <td>@vehicle.MakeModel</td>
            <td>@vehicle.CreatedAtUtc.ToString("d MMM yyyy")</td>
        </tr>
    }
    </tbody>
</table>
```

- [ ] **Step 3: Write the vehicle job page model**

```csharp
// Pages/Vehicles/Details.cshtml.cs
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Pages.Vehicles
{
    [Authorize]
    public class DetailsModel : PageModel
    {
        private readonly AppDbContext _db;

        public DetailsModel(AppDbContext db)
        {
            _db = db;
        }

        public Vehicle Vehicle { get; set; } = null!;
        public ILookup<Stage, Photo> PhotosByStage { get; set; } = null!;

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var vehicle = await _db.Vehicles.Include(v => v.Photos).FirstOrDefaultAsync(v => v.Id == id);
            if (vehicle is null) return NotFound();

            Vehicle = vehicle;
            PhotosByStage = vehicle.Photos
                .OrderBy(p => p.SequenceNumber)
                .ToLookup(p => p.Stage);

            return Page();
        }
    }
}
```

- [ ] **Step 4: Write the vehicle job page view**

```cshtml
@* Pages/Vehicles/Details.cshtml *@
@page "{id:int}"
@model MainProjectNumoPart.Pages.Vehicles.DetailsModel
@using MainProjectNumoPart.Models
@{
    ViewData["Title"] = Model.Vehicle.Vin ?? Model.Vehicle.Reg;
}

<div class="job-head">
    <div>
        <div class="job-plate">@(Model.Vehicle.MakeModel ?? "Vehicle") — @Model.Vehicle.Reg</div>
        <div class="job-vin">@Model.Vehicle.Vin</div>
    </div>
    <a class="btn btn-primary" asp-page="/Upload" asp-route-vehicleId="@Model.Vehicle.Id">+ Upload</a>
</div>

@foreach (var stage in Enum.GetValues<Stage>())
{
    var photos = Model.PhotosByStage[stage].ToList();
    <h3 class="stage-heading">@stage <span class="stage-count">(@photos.Count)</span></h3>
    @if (photos.Count == 0)
    {
        <p class="text-muted">No photos yet.</p>
    }
    else
    {
        <div class="photo-strip">
            @foreach (var photo in photos)
            {
                <div class="photo-tile" data-photo-id="@photo.Id">
                    <img src="/api/photos/@photo.Id/thumbnail" loading="lazy" />
                    <span class="photo-seq">@photo.SequenceNumber.ToString("000")</span>
                    @if (User.IsInRole("Admin"))
                    {
                        <button type="button" class="photo-delete" data-photo-id="@photo.Id" title="Delete">×</button>
                    }
                </div>
            }
        </div>
    }
}

@if (User.IsInRole("Admin"))
{
    <script>
        document.querySelectorAll('.photo-delete').forEach(btn => {
            btn.addEventListener('click', async () => {
                if (!confirm('Delete this photo? This cannot be undone.')) return;
                const id = btn.dataset.photoId;
                const response = await fetch(`/api/photos/${id}`, { method: 'DELETE' });
                if (response.ok) {
                    document.querySelector(`.photo-tile[data-photo-id="${id}"]`).remove();
                } else {
                    alert('Delete failed.');
                }
            });
        });
    </script>
}
```

- [ ] **Step 5: Add the delete endpoint (admin-only)**

```csharp
// Endpoints/PhotoEndpoints.cs — add inside MapPhotoEndpoints, after the existing two routes
group.MapDelete("/{id:int}", async (int id, Data.AppDbContext db, Services.IPhotoStorage storage) =>
{
    var photo = await db.Photos.FindAsync(id);
    if (photo is null) return Results.NotFound();

    await storage.DeleteOriginalAsync(photo.BlobPathOriginal);
    await storage.DeleteThumbnailAsync(photo.BlobPathThumbnail);
    db.Photos.Remove(photo);
    await db.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Admin"));
```

No new `using` is needed in `PhotoEndpoints.cs` for this step — `FindAsync` is a member of `DbSet<T>` itself, already in scope through `AppDbContext`.

**On CSRF:** this delete route and the download route added in Task 13 don't use antiforgery tokens. That's a deliberate call, not an oversight: both are called via `fetch`/form-POST with the DELETE verb or a non-simple content type, so a cross-origin request would either fail CORS preflight (nothing here enables CORS for other origins) or fail to attach the auth cookie under Identity's default `SameSite=Lax` cookie policy. If this ever moves behind a reverse proxy that relaxes CORS, or the auth cookie's `SameSite` setting is changed, revisit this.

- [ ] **Step 6: Add job page and delete-button styles**

```css
/* wwwroot/css/site.css — append */
.job-head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
.job-plate { font-size: 1.3rem; font-weight: 700; }
.job-vin { font-family: ui-monospace, Menlo, Consolas, monospace; font-size: 0.8rem; opacity: 0.6; }
.stage-heading { text-transform: uppercase; font-size: 0.85rem; letter-spacing: 0.04em; opacity: 0.7; margin-top: 1.5rem; }
.stage-count { font-weight: 400; opacity: 0.6; }
.photo-strip { display: grid; grid-template-columns: repeat(auto-fill, minmax(140px, 1fr)); gap: 10px; }
.photo-tile { position: relative; border-radius: 8px; overflow: hidden; aspect-ratio: 4/3; background: #eee; }
.photo-tile img { width: 100%; height: 100%; object-fit: cover; display: block; }
.photo-seq { position: absolute; right: 6px; bottom: 6px; background: rgba(0,0,0,0.6); color: #fff; font-size: 0.65rem; padding: 2px 6px; border-radius: 999px; }
.photo-delete { position: absolute; top: 4px; right: 4px; width: 22px; height: 22px; border-radius: 50%; border: none; background: rgba(0,0,0,0.6); color: #fff; font-size: 0.9rem; line-height: 1; cursor: pointer; }
.search-hero { max-width: 600px; margin: 3rem auto 0; }
.search-form { display: flex; gap: 8px; }
```

- [ ] **Step 7: Verify manually**

Run the app, sign in, upload a couple of photos (Task 10), then search for the VIN or Reg used. Expected: lands on the job page, thumbnails render (proving the Task 6 SAS-redirect endpoint works end-to-end), and — signed in as admin — the × delete button removes a photo from both the page and (check via Azure Storage Explorer against Azurite) both blob containers.

- [ ] **Step 8: Commit**

```bash
git add Pages/Index.cshtml Pages/Index.cshtml.cs Pages/Vehicles Endpoints/PhotoEndpoints.cs wwwroot/css/site.css
git commit -m "Add VIN/Reg search, home page, and vehicle job page with admin delete"
```

---

### Task 12: All Photos Filtered Grid

**Files:**
- Create: `Services/PhotoFilterQuery.cs`
- Create: `Pages/Photos/Index.cshtml`
- Create: `Pages/Photos/Index.cshtml.cs`
- Create: `MainProjectNumoPart.Tests/PhotoFilterQueryTests.cs`

**Interfaces:**
- Consumes: `AppDbContext.Photos` (Task 2), `/api/photos/{id}/thumbnail` (Task 6).
- Produces: `PhotoFilterQuery.Apply(IQueryable<Photo> query, PhotoFilter filter) : IQueryable<Photo>`, `PhotoFilter` (`Stage?`, `VinOrReg: string?`, `UploadedFrom/To: DateTime?`, `TakenFrom/To: DateTime?`). `GET /Photos`.

- [ ] **Step 1: Write the failing filter tests**

```csharp
// MainProjectNumoPart.Tests/PhotoFilterQueryTests.cs
using System;
using System.Linq;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class PhotoFilterQueryTests
    {
        private static void SeedPhoto(Data.AppDbContext db, Vehicle vehicle, Stage stage, DateTime uploaded, DateTime? taken)
        {
            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id, Stage = stage, FileName = "f.jpg",
                BlobPathOriginal = "o", BlobPathThumbnail = "t", ContentType = "image/jpeg",
                UploadedAtUtc = uploaded, DateTakenUtc = taken, SequenceNumber = 1, UploaderId = "u1"
            });
        }

        [Fact]
        public void Apply_FiltersByStage()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            SeedPhoto(db, vehicle, Stage.Checkin, DateTime.UtcNow, null);
            SeedPhoto(db, vehicle, Stage.Quote, DateTime.UtcNow, null);
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter { Stage = Stage.Checkin }).ToList();

            Assert.Single(result);
            Assert.Equal(Stage.Checkin, result[0].Stage);
        }

        [Fact]
        public void Apply_FiltersByVinOrReg()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v1 = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            var v2 = new Vehicle { Vin = "V2", BlobFolderName = "V2", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.AddRange(v1, v2);
            db.SaveChanges();
            SeedPhoto(db, v1, Stage.Checkin, DateTime.UtcNow, null);
            SeedPhoto(db, v2, Stage.Checkin, DateTime.UtcNow, null);
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter { VinOrReg = "V1" }).ToList();

            Assert.Single(result);
        }

        [Fact]
        public void Apply_FiltersByUploadedDateRange()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            SeedPhoto(db, vehicle, Stage.Checkin, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null);
            SeedPhoto(db, vehicle, Stage.Checkin, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), null);
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter
            {
                UploadedFrom = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
            }).ToList();

            Assert.Single(result);
        }

        [Fact]
        public void Apply_FiltersByTakenDateRange_ExcludingNulls()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            SeedPhoto(db, vehicle, Stage.Checkin, DateTime.UtcNow, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SeedPhoto(db, vehicle, Stage.Checkin, DateTime.UtcNow, null); // no EXIF date — must be excluded

            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter
            {
                TakenFrom = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }).ToList();

            Assert.Single(result);
        }

        [Fact]
        public void Apply_WithNoFilters_ReturnsAllOrderedByUploadedDescending()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            SeedPhoto(db, vehicle, Stage.Checkin, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null);
            SeedPhoto(db, vehicle, Stage.Checkin, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), null);
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter()).ToList();

            Assert.Equal(2, result.Count);
            Assert.True(result[0].UploadedAtUtc > result[1].UploadedAtUtc);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test MainProjectNumoPart.Tests --filter PhotoFilterQueryTests`
Expected: compile error — `PhotoFilterQuery` doesn't exist yet.

- [ ] **Step 3: Implement**

```csharp
// Services/PhotoFilterQuery.cs
using System;
using System.Linq;
using MainProjectNumoPart.Models;

namespace MainProjectNumoPart.Services
{
    public class PhotoFilter
    {
        public Stage? Stage { get; set; }
        public string? VinOrReg { get; set; }
        public DateTime? UploadedFrom { get; set; }
        public DateTime? UploadedTo { get; set; }
        public DateTime? TakenFrom { get; set; }
        public DateTime? TakenTo { get; set; }
    }

    public static class PhotoFilterQuery
    {
        public static IQueryable<Photo> Apply(IQueryable<Photo> query, PhotoFilter filter)
        {
            if (filter.Stage.HasValue)
                query = query.Where(p => p.Stage == filter.Stage.Value);

            if (!string.IsNullOrWhiteSpace(filter.VinOrReg))
            {
                var term = filter.VinOrReg.Trim();
                query = query.Where(p => p.Vehicle.Vin == term || p.Vehicle.Reg == term);
            }

            if (filter.UploadedFrom.HasValue)
                query = query.Where(p => p.UploadedAtUtc >= filter.UploadedFrom.Value);
            if (filter.UploadedTo.HasValue)
                query = query.Where(p => p.UploadedAtUtc <= filter.UploadedTo.Value);

            if (filter.TakenFrom.HasValue)
                query = query.Where(p => p.DateTakenUtc != null && p.DateTakenUtc >= filter.TakenFrom.Value);
            if (filter.TakenTo.HasValue)
                query = query.Where(p => p.DateTakenUtc != null && p.DateTakenUtc <= filter.TakenTo.Value);

            return query.OrderByDescending(p => p.UploadedAtUtc);
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test MainProjectNumoPart.Tests --filter PhotoFilterQueryTests`
Expected: all pass.

- [ ] **Step 5: Write the page model**

```csharp
// Pages/Photos/Index.cshtml.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Pages.Photos
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _db;

        public IndexModel(AppDbContext db)
        {
            _db = db;
        }

        [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public Stage? Stage { get; set; }
        [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public string? VinOrReg { get; set; }
        [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public DateTime? UploadedFrom { get; set; }
        [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public DateTime? UploadedTo { get; set; }
        [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public DateTime? TakenFrom { get; set; }
        [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public DateTime? TakenTo { get; set; }

        public List<Photo> Photos { get; set; } = new();

        public async Task OnGetAsync()
        {
            var filter = new PhotoFilter
            {
                Stage = Stage,
                VinOrReg = VinOrReg,
                UploadedFrom = UploadedFrom,
                UploadedTo = UploadedTo,
                TakenFrom = TakenFrom,
                TakenTo = TakenTo
            };

            Photos = await PhotoFilterQuery.Apply(_db.Photos.Include(p => p.Vehicle), filter)
                .Take(120)
                .ToListAsync();
        }
    }
}
```

- [ ] **Step 6: Write the view**

```cshtml
@* Pages/Photos/Index.cshtml *@
@page
@model MainProjectNumoPart.Pages.Photos.IndexModel
@using MainProjectNumoPart.Models
@{
    ViewData["Title"] = "All photos";
}

<form method="get" class="filter-bar mb-3">
    <select name="Stage" class="form-select form-select-sm" style="width:auto">
        <option value="">All stages</option>
        @foreach (var stage in Enum.GetValues<Stage>())
        {
            <option value="@stage" selected="@(Model.Stage == stage)">@stage</option>
        }
    </select>
    <input type="text" name="VinOrReg" value="@Model.VinOrReg" placeholder="VIN or Reg" class="form-control form-control-sm" style="width:160px" />
    <label class="small">Uploaded</label>
    <input type="date" name="UploadedFrom" value="@Model.UploadedFrom?.ToString("yyyy-MM-dd")" class="form-control form-control-sm" style="width:150px" />
    <input type="date" name="UploadedTo" value="@Model.UploadedTo?.ToString("yyyy-MM-dd")" class="form-control form-control-sm" style="width:150px" />
    <label class="small">Taken</label>
    <input type="date" name="TakenFrom" value="@Model.TakenFrom?.ToString("yyyy-MM-dd")" class="form-control form-control-sm" style="width:150px" />
    <input type="date" name="TakenTo" value="@Model.TakenTo?.ToString("yyyy-MM-dd")" class="form-control form-control-sm" style="width:150px" />
    <button type="submit" class="btn btn-sm btn-primary">Filter</button>
    <a asp-page="/Photos/Index" class="btn btn-sm btn-outline-secondary">Clear</a>
</form>

<div class="photo-grid">
    @foreach (var photo in Model.Photos)
    {
        <a class="photo-tile" asp-page="/Vehicles/Details" asp-route-id="@photo.VehicleId">
            <img src="/api/photos/@photo.Id/thumbnail" loading="lazy" />
            <span class="photo-seq">@photo.Stage</span>
        </a>
    }
</div>
```

- [ ] **Step 7: Add grid styles**

```css
/* wwwroot/css/site.css — append */
.filter-bar { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
.photo-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(160px, 1fr)); gap: 10px; }
```

- [ ] **Step 8: Verify manually**

Upload photos against a couple of different vehicles/stages, then visit `/Photos`. Expected: all appear; filtering by stage, VIN/Reg, and date ranges narrows the grid correctly; "Clear" resets to the unfiltered view.

- [ ] **Step 9: Commit**

```bash
git add Services/PhotoFilterQuery.cs Pages/Photos wwwroot/css/site.css MainProjectNumoPart.Tests/PhotoFilterQueryTests.cs
git commit -m "Add All Photos page with stage/VIN-Reg/date filtering"
```

---

### Task 13: Multi-Select and Zip Download

**Files:**
- Create: `wwwroot/js/multiselect.js`
- Modify: `Pages/Vehicles/Details.cshtml`
- Modify: `Endpoints/PhotoEndpoints.cs`

**Interfaces:**
- Consumes: `IPhotoStorage.OpenOriginalReadAsync` (Task 5), the `.photo-tile[data-photo-id]` markup already present from Task 11.
- Produces: `POST /api/photos/download` (form-encoded `ids`, streams a zip). Client-side: a sticky selection bar on the vehicle job page.

- [ ] **Step 1: Add the zip download endpoint**

```csharp
// Endpoints/PhotoEndpoints.cs — add inside MapPhotoEndpoints, after the delete route
group.MapPost("/download", async (HttpRequest request, HttpResponse response, Data.AppDbContext db, Services.IPhotoStorage storage) =>
{
    var ids = request.Form["ids"]
        .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
        .Where(v => v.HasValue)
        .Select(v => v!.Value)
        .ToList();

    if (ids.Count == 0) return Results.BadRequest("No photos selected.");

    var photos = await db.Photos.Where(p => ids.Contains(p.Id)).ToListAsync();

    response.ContentType = "application/zip";
    response.Headers.ContentDisposition = $"attachment; filename=\"photos-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip\"";

    using (var archive = new System.IO.Compression.ZipArchive(response.Body, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var photo in photos)
        {
            // No compression: JPEGs/PNGs are already compressed, so re-compressing just burns CPU for no size benefit.
            var entry = archive.CreateEntry($"{photo.Stage}/{photo.FileName}", System.IO.Compression.CompressionLevel.NoCompression);
            await using var entryStream = entry.Open();
            await using var sourceStream = await storage.OpenOriginalReadAsync(photo.BlobPathOriginal);
            await sourceStream.CopyToAsync(entryStream);
        }
    }

    return Results.Empty;
});
```

Add `using System.Linq;` and `using Microsoft.EntityFrameworkCore;` to `PhotoEndpoints.cs` if not already present.

- [ ] **Step 2: Write the client-side selection script**

```javascript
// wwwroot/js/multiselect.js
(function () {
    const selected = new Set();
    const bar = document.getElementById('selection-bar');
    if (!bar) return; // Page has no photos to select (e.g. a brand-new vehicle).

    const countEl = document.getElementById('selection-count');
    const form = document.getElementById('selection-download-form');

    function refreshBar() {
        if (selected.size === 0) {
            bar.classList.add('d-none');
            return;
        }
        bar.classList.remove('d-none');
        countEl.textContent = `${selected.size} selected`;

        form.querySelectorAll('input[name="ids"]').forEach(el => el.remove());
        selected.forEach(id => {
            const input = document.createElement('input');
            input.type = 'hidden';
            input.name = 'ids';
            input.value = id;
            form.appendChild(input);
        });
    }

    document.querySelectorAll('.photo-tile').forEach(tile => {
        tile.addEventListener('click', (e) => {
            if (e.target.classList.contains('photo-delete')) return; // let delete handle its own click
            const id = tile.dataset.photoId;
            if (selected.has(id)) {
                selected.delete(id);
                tile.classList.remove('selected');
            } else {
                selected.add(id);
                tile.classList.add('selected');
            }
            refreshBar();
        });
    });

    document.getElementById('selection-clear')?.addEventListener('click', () => {
        selected.clear();
        document.querySelectorAll('.photo-tile.selected').forEach(t => t.classList.remove('selected'));
        refreshBar();
    });
})();
```

- [ ] **Step 3: Add the selection bar and script reference to the job page**

```cshtml
@* Pages/Vehicles/Details.cshtml — add just before the closing of the page content, after the stage loop *@

<form id="selection-download-form" method="post" action="/api/photos/download"></form>

<div id="selection-bar" class="selection-bar d-none">
    <span id="selection-count"></span>
    <span class="flex-spacer"></span>
    <button type="button" id="selection-clear" class="btn btn-sm btn-outline-light">Clear</button>
    <button type="submit" form="selection-download-form" class="btn btn-sm btn-primary">⬇ Download zip</button>
</div>

<script src="~/js/multiselect.js"></script>
```

(Photo tiles already carry `data-photo-id` from Task 11 — no markup change needed there. Clicking a tile now toggles selection; the existing × delete button still works independently since the script ignores clicks on it.)

- [ ] **Step 4: Add selection bar and tile-selected styles**

```css
/* wwwroot/css/site.css — append */
.photo-tile.selected { outline: 3px solid #2f6fed; outline-offset: -3px; }
.selection-bar {
    position: sticky;
    bottom: 0;
    background: #1f2430;
    color: #fff;
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 10px 16px;
    margin-top: 12px;
    border-radius: 8px;
}
.flex-spacer { flex: 1; }
```

- [ ] **Step 5: Verify manually**

On a vehicle job page with several photos, click 3-4 tiles across different stages. Expected: each gets a blue outline, the bottom bar appears showing the count, "Clear" deselects everything, and "Download zip" produces a `.zip` file whose internal structure has one folder per stage (open it and confirm — e.g. `Checkin/1HGBH41JXMN109186-VIN-001.jpg`).

- [ ] **Step 6: Commit**

```bash
git add wwwroot/js/multiselect.js Pages/Vehicles/Details.cshtml Endpoints/PhotoEndpoints.cs wwwroot/css/site.css
git commit -m "Add multi-select and streaming zip download, preserving stage folders"
```

---

### Task 14: Local Development Documentation

**Files:**
- Create: `docs/LOCAL_DEV.md`

**Interfaces:**
- Consumes: nothing — this is documentation tying together configuration introduced in Tasks 2, 5, and 7.
- Produces: a runnable checklist for anyone (including a future you) setting this up on a new machine.

- [ ] **Step 1: Write the doc**

```markdown
# Local Development Setup

This app runs entirely locally with no Azure account or spend. Everything in
production (Blob Storage, Key Vault, Container Apps) is swapped in later via
configuration only — see `docs/superpowers/specs/2026-07-25-azure-infrastructure-design.md`.

## Prerequisites

- .NET 8 SDK
- Azurite (Azure Storage emulator). Easiest: Visual Studio 2022 → **Tools → Azurite → Start Azurite**.
  Alternative: `npm install -g azurite` then run `azurite` in a terminal, or
  `docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite`.

## First-time setup

1. Start Azurite (see above) and leave it running.
2. Restore and build:
   ```bash
   dotnet restore
   dotnet build
   ```
3. Apply migrations to create the local SQLite database:
   ```bash
   dotnet ef database update --project MainProjectNumoPart.csproj
   ```
   This creates `app.db` in the project root (gitignored — it's your local data, not shared).
4. Confirm `appsettings.Development.json` has an `InitialAdmin` section with an email/password —
   this account is auto-created the first time the app runs against an empty database.
5. Run the app:
   ```bash
   dotnet run --project MainProjectNumoPart.csproj
   ```
6. Sign in at `/Account/Login` with the `InitialAdmin` credentials.

## Running tests

```bash
dotnet test MainProjectNumoPart.Tests
```

Most tests need nothing beyond the .NET SDK. `BlobPhotoStorageTests` needs Azurite running —
they'll fail with a connection error (not a false pass) if it isn't.

## Resetting local data

Delete `app.db` (and `app.db-shm`/`app.db-wal` if present), then re-run
`dotnet ef database update`. To clear uploaded photos too, stop Azurite, delete its
data (the `__azurite_db*__.json` files and `__blobstorage__` folder in whatever directory
you started it from), and restart it.

## Manual smoke checklist

After any significant change, confirm end-to-end by hand:

- [ ] Log in as the seeded admin.
- [ ] Upload 2-3 photos (mix of JPEG and PNG) against a new VIN, no Reg.
- [ ] Search by that VIN — lands on the vehicle's job page, thumbnails render.
- [ ] Upload another photo against the same VIN plus a Reg this time — confirm it lands
      on the *same* vehicle (find-or-create matched by VIN) and the Reg now shows on the page.
- [ ] Search by that Reg — same vehicle is found.
- [ ] Visit `/Photos`, filter by stage and by VIN — results narrow correctly.
- [ ] Select 2+ photos on the job page and download a zip — confirm the stage-folder
      structure inside it.
- [ ] As a non-admin account, confirm the × delete button is not visible, and a direct
      `DELETE /api/photos/{id}` request is rejected.
- [ ] As admin, delete a photo — confirms it disappears from both the page and Azurite storage.
```

- [ ] **Step 2: Commit**

```bash
git add docs/LOCAL_DEV.md
git commit -m "Add local development setup and smoke-test documentation"
```

---

## Self-Review

**Spec coverage:**
- Local-first, no Azure spend — Tasks 1-14 use only Azurite + local SQLite. ✅
- Domain model (Vehicle, Photo, Stage, BlobFolderName immutability) — Task 2, 4. ✅
- Blob folder/filename scheme, all three VIN/Reg permutations — Task 3 (tested), Task 10 (applied). ✅
- Screens: Login (8), Search/home (11), Vehicle job page (11), Upload (10), All photos (12) — all five present. ✅
- Multi-select + streaming zip download with stage folders — Task 13. ✅
- SAS-based image serving, 15-minute expiry — Task 6. ✅
- Access control: any authenticated user uploads/browses/downloads (all `[Authorize]`, no role restriction); deletion admin-only — Task 11 delete route. ✅
- Admin bootstrapping — Task 7. ✅
- Orphan blob cleanup on save failure — Task 10. ✅
- Testing: unit tests for naming, sequencing, filters, EXIF-adjacent seeding; Azurite integration test for storage — Tasks 3, 4, 12, 5. ✅
- Local dev docs — Task 14. ✅

**Gap found and fixed during planning:** the spec's Access Control section requires deletion to be admin-only, but no screen or endpoint for it was in the original five-screen list. Added a minimal admin-only delete affordance to the job page (Task 11) rather than leaving the requirement unimplemented.

**Placeholder scan:** no TBD/TODO markers; every step has runnable code or an exact command.

**Type consistency check:** `IPhotoStorage` signatures (Task 5) match every call site (Tasks 6, 10, 11, 13) — all SAS methods are `Task<Uri>` and awaited. `PhotoNaming.BuildFileName`'s four-parameter signature is used identically in its own tests (Task 3) and in Upload (Task 10). `Photo.BlobPathOriginal`/`BlobPathThumbnail` are populated as potentially-different strings in Task 10 (correctly — thumbnails are always re-encoded as `.jpg`, so a PNG original's paths diverge in extension) and consumed identically by name in Tasks 6, 11, 13. `VehicleLookupService.FindOrCreateAsync` is documented as not calling `SaveChangesAsync`, and Task 10 explicitly calls it before using `vehicle.Id`.

---

## Deliberately Deferred to a Later Plan

- All actual Azure provisioning (Container Apps, Key Vault, Azure Files, Managed Identity, the nightly SQLite backup job) — see the infrastructure spec. Nothing here creates or costs anything in Azure.
- Data Protection key persistence to Blob Storage + Key Vault encryption — only relevant once Key Vault exists; local dev uses ASP.NET Core's default local-filesystem key storage.
- CI/CD (GitHub Actions build/push/deploy).
- Sending/emailing image selections, albums, public share links, vehicle service history, admin-managed custom stages — all explicitly out of scope per the application spec.
