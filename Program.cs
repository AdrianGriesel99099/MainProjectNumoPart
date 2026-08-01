using Azure.Storage.Blobs;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Endpoints;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

// Local dev / Azurite: SQLite, a plain file, zero setup.
// Production: Azure SQL Database. SQLite's locking model doesn't work reliably over a network
// file share (confirmed against Azure Files — every write hit "database is locked"), so
// production uses a real managed database instead. Authentication=Active Directory Managed
// Identity in the connection string means Microsoft.Data.SqlClient acquires and refreshes the
// token itself — no credential of any kind is stored anywhere for this.
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (builder.Environment.IsDevelopment())
    {
        options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=app.db");
    }
    else
    {
        options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    }
});

builder.Services.AddScoped<VehicleLookupService>();
builder.Services.AddScoped<PhotoSequenceAllocator>();
builder.Services.AddScoped<UserAdminService>();
builder.Services.AddScoped<VehicleDeletionService>();
builder.Services.AddScoped<VehicleEditService>();
builder.Services.AddScoped<PhotoTaggingService>();

// Minimal-API JSON defaults to reading enums as NUMBERS, so a body of {"part":"FrontBumper"}
// is rejected with a 400 while {"part":null} succeeds — a split failure that is easy to miss.
// Accepting names keeps the wire format readable and matches what the UI naturally sends.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// BlobServiceClient is a singleton (thread-safe, expensive to construct); IPhotoStorage
// wraps it and is registered per-scope to match the other services above.
//  - Local dev / Azurite: BlobStorage:ConnectionString is set (see appsettings.Development.json)
//    and carries an account key, so a plain connection-string client is used.
//  - Production: no connection string is configured; BlobStorage:ServiceUri plus Managed
//    Identity (DefaultAzureCredential) is used instead, since there is no account key to store.
builder.Services.AddSingleton(sp =>
{
    var connectionString = builder.Configuration["BlobStorage:ConnectionString"];
    return !string.IsNullOrEmpty(connectionString)
        ? new BlobServiceClient(connectionString)
        : new BlobServiceClient(new Uri(builder.Configuration["BlobStorage:ServiceUri"]!), new Azure.Identity.DefaultAzureCredential());
});
builder.Services.AddScoped<IPhotoStorage, BlobPhotoStorage>();

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

// Role claims are baked into the auth cookie at sign-in. RemoveFromRolesAsync/AddToRoleAsync do
// NOT regenerate the security stamp on their own, and this validator's default re-check interval
// is 30 MINUTES — so without this (plus the UpdateSecurityStampAsync call in UserAdminService),
// an admin you just demoted keeps full admin powers, including vehicle deletion, for up to half
// an hour. That is precisely the scenario you demote someone for. One minute bounds the window;
// at this app's user count it costs one indexed read per active user per minute.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(1);
});

// Production only: Container Apps' scale-to-zero destroys and re-creates the container on every
// cold start. Without persisting the Data Protection key ring somewhere durable, each cold start
// generates a fresh key, invalidating every existing auth cookie and antiforgery token — forcing
// re-authentication constantly. Persisted to the app-data blob container (same storage account
// and Managed Identity already used for photos) and encrypted at rest with a Key Vault-held key.
// Local dev deliberately skips this and uses ASP.NET Core's default local-filesystem key storage.
if (!builder.Environment.IsDevelopment())
{
    var credential = new Azure.Identity.DefaultAzureCredential();
    var keysBlobUri = new Uri($"{builder.Configuration["BlobStorage:ServiceUri"]!.TrimEnd('/')}/app-data/keys.xml");
    var dataProtectionKeyUri = new Uri(builder.Configuration["KeyVault:DataProtectionKeyUri"]!);

    builder.Services.AddDataProtection()
        .PersistKeysToAzureBlobStorage(keysBlobUri, credential)
        .ProtectKeysWithAzureKeyVault(dataProtectionKeyUri, credential);
}

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 25 * 1024 * 1024 * 10; // headroom for a multi-file batch; per-file cap is enforced in code
});

// FormOptions above only governs the multipart *form* reader. Kestrel enforces its own,
// separate request body ceiling (KestrelServerOptions.Limits.MaxRequestBodySize, default
// ~30,000,000 bytes) before the request ever reaches form parsing — verified against a running
// instance: a batch with two 16MB files (32MB total, each file individually under the app's own
// 25MB per-file cap) had its connection reset by Kestrel, never reaching UploadModel at all. Both
// limits need raising together for a multi-file batch to actually get the headroom intended above.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 25 * 1024 * 1024 * 10;
});

var app = builder.Build();

// Azure Container Apps terminates TLS at its ingress and forwards plain HTTP to the container,
// setting X-Forwarded-Proto to record the original scheme. Without trusting that header, Kestrel
// sees every request as HTTP and UseHttpsRedirection below redirects it to https:// — which the
// client re-requests through the same TLS-terminating ingress, forwarded as HTTP again: an
// infinite redirect loop. KnownNetworks/KnownProxies must be cleared via .Clear() (an empty
// collection initializer here would NOT clear ASP.NET Core's built-in loopback defaults — it
// just adds zero extra items to them) because Container Apps' ingress isn't a fixed, known IP the
// way an on-prem reverse proxy would be — this is the documented pattern for exactly this hosting
// model (Container Apps, App Service, and similar PaaS ingress).
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.MapPhotoEndpoints();
app.MapVehicleEndpoints();

await AdminSeeder.SeedInitialAdminAsync(app.Services);

app.MapPost("/account/logout", async (SignInManager<IdentityUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/");
}).RequireAuthorization();

app.Run();
