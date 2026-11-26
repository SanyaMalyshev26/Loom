using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Loom.Controllers;
using Loom.Data;
using Loom.Models;
using Loom.Services;
using Loom.Services.StockNotifications;
using Xunit;

namespace Loom.Tests;

// Controller/service regression tests using the actual SQLite migrations.
public sealed class CheckoutStockRegressionTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"loom-review-{Guid.NewGuid():N}.db");
    private int variantId;
    private int addressId;
    private ApplicationDbContext Open() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={path}").Options);

    public async Task InitializeAsync()
    {
        await using var db = Open();
        await db.Database.MigrateAsync();
        db.Users.Add(new ApplicationUser { Id = "review", UserName = "review@example.invalid" });
        var address = new Address { UserId = "review", City = "Test", Street = "Test" };
        var variant = new ProductVariant { ProductId = 1, Sku = "REVIEW", Size = "M", Color = "Black", Stock = 2 };
        db.AddRange(address, variant);
        await db.SaveChangesAsync();
        variantId = variant.Id;
        addressId = address.Id;
    }

    private async Task Checkout(string? key = null, int quantity = 1)
    {
        await using var db = Open();
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "review") }, "review"));
        if (key != null) http.Request.Headers["Idempotency-Key"] = key;
        var controller = new OrdersController(db) { ControllerContext = new ControllerContext { HttpContext = http } };
        var result = await controller.Create(new CheckoutRequest {
            AddressId = addressId,
            Lines = new() { new CartLine { VariantId = variantId, Qty = quantity } }
        });
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Legacy_checkout_without_key_remains_backward_compatible()
    {
        await Checkout();
        await Checkout();
        await using var db = Open();
        Assert.Equal(2, await db.Orders.CountAsync());
        Assert.Equal(0, await db.ProductVariants.Where(v => v.Id == variantId).Select(v => v.Stock).SingleAsync());
    }

    [Fact]
    public async Task Same_key_prevents_duplicate_order()
    {
        await Checkout("review-checkout-0001");
        await Checkout("review-checkout-0001");
        await using var db = Open();
        Assert.Equal(1, await db.Orders.CountAsync());
        Assert.Equal(1, await db.ProductVariants.Where(v => v.Id == variantId).Select(v => v.Stock).SingleAsync());
    }

    [Fact]
    public async Task Cancellation_restores_stock_and_notifies_subscribers()
    {
        await Checkout(quantity: 2);
        await using var db = Open();
        db.StockNotifications.Add(new StockNotification { UserId = "review", ProductId = 1, Size = "M" });
        await db.SaveChangesAsync();
        var orderId = await db.Orders.Select(o => o.Id).SingleAsync();
        Assert.Equal(OrderCancellationResult.Cancelled, await new OrderCancellationService(db, new StockNotificationService(db)).CancelAsync(orderId, "review"));
        Assert.Equal(2, await db.ProductVariants.Where(v => v.Id == variantId).Select(v => v.Stock).SingleAsync());
        Assert.NotNull(await db.StockNotifications.Select(n => n.NotifiedAt).SingleAsync());
    }

    [Fact]
    public async Task Stale_admin_form_preserves_the_stock_after_checkout()
    {
        const int stockInOldForm = 2;
        await Checkout();
        await using var db = Open();
        var http = new DefaultHttpContext();
        var controller = new ProductsAdminController(db, null!, new StockNotificationService(db),
            NullLogger<ProductsAdminController>.Instance, new ConfigurationBuilder().Build())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new EmptyTempDataProvider())
        };
        await controller.UpdateVariant(1, variantId, stockInOldForm, true, stockInOldForm, true, default);
        Assert.Equal(1, await db.ProductVariants.Where(v => v.Id == variantId).Select(v => v.Stock).SingleAsync());
        Assert.Contains("уже изменились", controller.TempData["Error"]?.ToString());
        Assert.Single(await db.Orders.ToListAsync());
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" }) File.Delete(path + suffix);
        return Task.CompletedTask;
    }
    private sealed class EmptyTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
