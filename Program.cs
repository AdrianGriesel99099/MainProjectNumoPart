using Azure.Storage.Blobs;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Endpoints;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=app.db"));

builder.Services.AddScoped<VehicleLookupService>();
builder.Services.AddScoped<PhotoSequenceAllocator>();

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

await AdminSeeder.SeedInitialAdminAsync(app.Services);

app.MapPost("/account/logout", async (SignInManager<IdentityUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/");
}).RequireAuthorization();

app.Run();
