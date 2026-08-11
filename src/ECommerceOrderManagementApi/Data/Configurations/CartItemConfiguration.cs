using ECommerceOrderManagementApi.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerceOrderManagementApi.Data.Configurations;

public sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Quantity).IsRequired();
        builder.ToTable(table =>
            table.HasCheckConstraint("CK_CartItems_Quantity_Positive", "[Quantity] > 0"));
        builder.HasIndex(item => new { item.CartId, item.ProductId }).IsUnique();

        builder.HasOne(item => item.Cart)
            .WithMany(cart => cart.CartItems)
            .HasForeignKey(item => item.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.Product)
            .WithMany(product => product.CartItems)
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
