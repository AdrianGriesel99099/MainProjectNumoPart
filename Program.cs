using Azure.Storage.Blobs;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Endpoints;
using MainProjectNumoPart.Services;
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

var app = builder.Build();

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
