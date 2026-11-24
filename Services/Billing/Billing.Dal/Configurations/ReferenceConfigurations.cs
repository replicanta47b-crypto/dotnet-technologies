using Billing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Billing.Dal.Configurations;

public class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        builder.ToTable("Merchants");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Country).HasMaxLength(100);
        builder.Property(m => m.ContactEmail).HasMaxLength(256);
        builder.HasIndex(m => m.Name).IsUnique().HasDatabaseName("UQ_Merchants_Name");

        // статичний (модельний) сідінг довідника з фіксованими Id; анонімні об'єкти — бо setter-и приватні
        builder.HasData(
            new { Id = 1L, Name = "ЕнергоСвіт", Country = "Україна", ContactEmail = "billing@energosvit.example", IsActive = true },
            new { Id = 2L, Name = "ВодоКанал Плюс", Country = "Україна", ContactEmail = "billing@vodokanal.example", IsActive = true },
            new { Id = 3L, Name = "ТелеЛінк", Country = "Україна", ContactEmail = "billing@telelink.example", IsActive = true });
    }
}

public class ServiceCategoryConfiguration : IEntityTypeConfiguration<ServiceCategory>
{
    public void Configure(EntityTypeBuilder<ServiceCategory> builder)
    {
        builder.ToTable("ServiceCategories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Slug).IsRequired().HasMaxLength(100);
        builder.HasIndex(c => c.Slug).IsUnique().HasDatabaseName("UQ_ServiceCategories_Slug");

        builder.HasData(
            new { Id = 1L, Name = "Комунальні послуги", Slug = "utilities" },
            new { Id = 2L, Name = "Електроенергія", Slug = "electricity" },
            new { Id = 3L, Name = "Водопостачання", Slug = "water" },
            new { Id = 4L, Name = "Зв'язок", Slug = "telecom" },
            new { Id = 5L, Name = "Інтернет", Slug = "internet" });
    }
}
