using Competition.Configuration;
using Competition.Data;
using Competition.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.Configure<CompetitionSettings>(builder.Configuration.GetSection(CompetitionSettings.SectionName));
builder.Services.Configure<AdminAccessSettings>(builder.Configuration.GetSection(AdminAccessSettings.SectionName));
builder.Services.Configure<ThemeSettings>(builder.Configuration.GetSection(ThemeSettings.SectionName));

var configuredConnectionString = builder.Configuration.GetConnectionString("CompetitionDb")
    ?? throw new InvalidOperationException(
        "Connection string 'CompetitionDb' is missing. Configure it in user secrets or environment variables.");

var databaseSettings = builder.Configuration
    .GetSection(DatabaseSettings.SectionName)
    .Get<DatabaseSettings>() ?? new DatabaseSettings();

var connectionStringBuilder = new SqlConnectionStringBuilder(configuredConnectionString)
{
    ConnectTimeout = Math.Clamp(databaseSettings.ConnectTimeoutSeconds, 1, 60)
};
var effectiveConnectionString = connectionStringBuilder.ConnectionString;

// Keep diagnostics aligned with the connection string actually passed to SqlClient.
builder.Configuration["ConnectionStrings:CompetitionDb"] = effectiveConnectionString;

builder.Services.AddDbContext<CompetitionDbContext>(options =>
    options.UseSqlServer(
        effectiveConnectionString,
        sqlServerOptions =>
        {
            sqlServerOptions.CommandTimeout(Math.Clamp(databaseSettings.CommandTimeoutSeconds, 1, 300));
            sqlServerOptions.EnableRetryOnFailure(
                Math.Clamp(databaseSettings.MaxRetryCount, 0, 10),
                TimeSpan.FromSeconds(Math.Clamp(databaseSettings.MaxRetryDelaySeconds, 1, 60)),
                errorNumbersToAdd: null);
        }));

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
builder.Services.AddRazorPages()
    .AddMvcOptions(options =>
    {
        options.ModelBindingMessageProvider.SetValueIsInvalidAccessor(value => $"Hodnota „{value}“ není platná.");
        options.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor((value, field) =>
            $"Hodnota „{value}“ není pro pole {field} platná.");
        options.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(field =>
            $"Pole {field} musí obsahovat číslo.");
    });
builder.Services.AddSingleton<AdminCredentialValidator>();
builder.Services.AddScoped<IEditionAdministrationService, EditionAdministrationService>();
builder.Services.AddScoped<ICompetitorAdministrationService, CompetitorAdministrationService>();
builder.Services.AddScoped<IDisciplineAdministrationService, DisciplineAdministrationService>();
builder.Services.AddScoped<IPhaseSetupService, PhaseSetupService>();

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
