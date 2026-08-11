using ECommerceOrderManagementApi.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerceOrderManagementApi.Data.Configurations;

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.UnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(item => item.TotalPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(item => item.Quantity).IsRequired();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_OrderItems_UnitPrice_Positive", "[UnitPrice] > 0");
            table.HasCheckConstraint("CK_OrderItems_Quantity_Positive", "[Quantity] > 0");
            table.HasCheckConstraint("CK_OrderItems_TotalPrice_Positive", "[TotalPrice] > 0");
        });

        builder.HasOne(item => item.Order)
            .WithMany(order => order.OrderItems)
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.Product)
            .WithMany(product => product.OrderItems)
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
