using ECommerceOrderManagementApi.Data;
using ECommerceOrderManagementApi.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace ECommerceOrderManagementApi.Tests.Data;

public sealed class AppDbContextModelTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public void Model_CanBeCreated()
    {
        using var context = CreateContext();

        Assert.NotNull(context.Model.FindEntityType(typeof(User)));
        Assert.Equal(8, context.Model.GetEntityTypes().Count());
    }

    [Theory]
    [InlineData(typeof(User), nameof(User.NormalizedEmail))]
    [InlineData(typeof(Cart), nameof(Cart.UserId))]
    public void Model_HasExpectedSingleColumnUniqueIndex(Type entityType, string propertyName)
    {
        using var context = CreateContext();
        var metadata = context.Model.FindEntityType(entityType)!;

        Assert.Contains(metadata.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([propertyName]));
    }

    [Fact]
    public void CartItem_HasUniqueCartAndProductIndex()
    {
        using var context = CreateContext();
        var metadata = context.Model.FindEntityType(typeof(CartItem))!;

        Assert.Contains(metadata.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(CartItem.CartId), nameof(CartItem.ProductId)]));
    }

    [Theory]
    [InlineData(typeof(Product), nameof(Product.Price))]
    [InlineData(typeof(Order), nameof(Order.TotalAmount))]
    [InlineData(typeof(OrderItem), nameof(OrderItem.UnitPrice))]
    [InlineData(typeof(OrderItem), nameof(OrderItem.TotalPrice))]
    public void MonetaryProperties_UseDecimal18_2(Type entityType, string propertyName)
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(entityType)!.FindProperty(propertyName)!;

        Assert.Equal(18, property.GetPrecision());
        Assert.Equal(2, property.GetScale());
    }

    [Theory]
    [InlineData(typeof(Order), nameof(Order.UserId), DeleteBehavior.Restrict)]
    [InlineData(typeof(OrderItem), nameof(OrderItem.ProductId), DeleteBehavior.Restrict)]
    [InlineData(typeof(Product), nameof(Product.CategoryId), DeleteBehavior.Restrict)]
    [InlineData(typeof(CartItem), nameof(CartItem.ProductId), DeleteBehavior.Restrict)]
    [InlineData(typeof(OrderItem), nameof(OrderItem.OrderId), DeleteBehavior.Cascade)]
    public void Relationships_UseDeliberateDeleteBehavior(
        Type entityType,
        string foreignKeyProperty,
        DeleteBehavior expectedBehavior)
    {
        using var context = CreateContext();
        var metadata = context.Model.FindEntityType(entityType)!;
        var foreignKey = metadata.GetForeignKeys().Single(key =>
            key.Properties.Single().Name == foreignKeyProperty);

        Assert.Equal(expectedBehavior, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void HistoricalOrderRelationships_ArePresent()
    {
        using var context = CreateContext();
        var orderItem = context.Model.FindEntityType(typeof(OrderItem))!;

        Assert.Contains(orderItem.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(Order));
        Assert.Contains(orderItem.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(Product));
        Assert.NotNull(orderItem.FindProperty(nameof(OrderItem.ProductName)));
    }
}
