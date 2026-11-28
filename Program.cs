using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using System.Threading.Channels;
using Loom.Data;
using Microsoft.AspNetCore.Identity;
using Loom.Models;
using Loom.Services;
using Loom.Services.Analytics;
using Loom.Services.Images;
using Loom.Services.StockNotifications;


var resetAdminPassword = args is ["--reset-admin-password", _];
var builder = WebApplication.CreateBuilder(resetAdminPassword ? [] : args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=shop.db"));
builder.Services.AddScoped<OrderCancellationService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<LoginCodes>();
builder.Services.AddSingleton<StoreEmail>();
builder.Services.AddSingleton(Channel.CreateBounded<LoginCodeDelivery.Message>(
    new BoundedChannelOptions(100) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait }));
builder.Services.AddSingleton<LoginCodeDelivery>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<LoginCodeDelivery>());
builder.Services.AddScoped<StockNotificationService>();
builder.Services.AddHttpClient<IImageProcessingClient, ImageProcessingClient>((services, client) =>
{
    var baseUrl = services.GetRequiredService<IConfiguration>()["ImageProcessing:BaseUrl"];
    if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var imageServiceUri) &&
        (imageServiceUri.Scheme == Uri.UriSchemeHttp || imageServiceUri.Scheme == Uri.UriSchemeHttps))
    {
        client.BaseAddress = imageServiceUri;
    }

    client.Timeout = TimeSpan.FromSeconds(45);
});
builder.Services.AddHttpClient<IAnalyticsProcessingClient, AnalyticsProcessingClient>((services, client) =>
{
    var baseUrl = services.GetRequiredService<IConfiguration>()["AnalyticsProcessing:BaseUrl"];
    if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var analyticsServiceUri) &&
        (analyticsServiceUri.Scheme == Uri.UriSchemeHttp || analyticsServiceUri.Scheme == Uri.UriSchemeHttps))
    {
        client.BaseAddress = analyticsServiceUri;
    }

    client.Timeout = TimeSpan.FromSeconds(10);
});

    builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Упрощённые правила пароля для учебного проекта
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireDigit = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
});

// Add services to the container.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("email-login", context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(5) }));
    options.AddPolicy("password-recovery", context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 8, Window = TimeSpan.FromMinutes(10) }));
});

var app = builder.Build();

if (resetAdminPassword)
{
    using var scope = app.Services.CreateScope();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var admin = await users.FindByEmailAsync(args[1]);
    if (admin == null || !await users.IsInRoleAsync(admin, "Admin"))
    {
        Console.Error.WriteLine("Администратор не найден.");
        Environment.ExitCode = 1;
        return;
    }

    var password = Console.ReadLine() ?? "";
    var token = await users.GeneratePasswordResetTokenAsync(admin);
    var result = await users.ResetPasswordAsync(admin, token, password);
    Console.WriteLine(result.Succeeded ? "Пароль администратора обновлён." : string.Join(" ", result.Errors.Select(error => error.Description)));
    Environment.ExitCode = result.Succeeded ? 0 : 1;
    return;
}

// Создаём роль Admin и назначаем её пользователю из настроек
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
        await db.Database.MigrateAsync();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    if (!await roleManager.RoleExistsAsync("Admin"))
        await roleManager.CreateAsync(new IdentityRole("Admin"));

    var adminEmail = app.Configuration["AdminEmail"];
    if (!string.IsNullOrEmpty(adminEmail))
    {
        var admin = await userManager.FindByEmailAsync(adminEmail);

                
        if (admin != null && !await userManager.IsInRoleAsync(admin, "Admin"))
            await userManager.AddToRoleAsync(admin, "Admin");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapGet("/health/ready", async (ApplicationDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ok" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.Run();

public partial class Program;
