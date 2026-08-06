using Competition.Configuration;
using Competition.Data;
using Competition.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.Configure<CompetitionSettings>(builder.Configuration.GetSection(CompetitionSettings.SectionName));
builder.Services.Configure<AdminAccessSettings>(builder.Configuration.GetSection(AdminAccessSettings.SectionName));
builder.Services.Configure<ThemeSettings>(builder.Configuration.GetSection(ThemeSettings.SectionName));

var connectionString = builder.Configuration.GetConnectionString("CompetitionDb")
    ?? throw new InvalidOperationException(
        "Connection string 'CompetitionDb' is missing. Configure it in user secrets or environment variables.");

builder.Services.AddDbContext<CompetitionDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sqlServerOptions => sqlServerOptions.EnableRetryOnFailure()));

var adminAccess = builder.Configuration
    .GetSection(AdminAccessSettings.SectionName)
    .Get<AdminAccessSettings>() ?? new AdminAccessSettings();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.Cookie.Name = "Competition.Admin";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(adminAccess.CookieLifetimeHours);
    });

builder.Services.AddAuthorization();
builder.Services.AddRazorPages();
builder.Services.AddSingleton<AdminCredentialValidator>();

var app = builder.Build();

app.Logger.LogInformation("Starting Competition app in {Environment}", app.Environment.EnvironmentName);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<ConfigurableAdminAuthorizationMiddleware>();
app.UseAuthorization();
app.MapRazorPages();

app.Run();
