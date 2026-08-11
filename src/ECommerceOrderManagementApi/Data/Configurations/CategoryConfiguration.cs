using ECommerceOrderManagementApi.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerceOrderManagementApi.Data.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasKey(category => category.Id);
        builder.Property(category => category.Name).HasMaxLength(150).IsRequired();
        builder.Property(category => category.NormalizedName).HasMaxLength(150).IsRequired();
        builder.Property(category => category.Description).HasMaxLength(1000);
        builder.HasIndex(category => category.NormalizedName).IsUnique();
    }
}
