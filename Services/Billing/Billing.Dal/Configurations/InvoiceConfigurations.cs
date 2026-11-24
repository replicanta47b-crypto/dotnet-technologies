using Billing.Domain.Entities;
using Billing.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Billing.Dal.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices", t =>
        {
            t.HasCheckConstraint("CK_Invoices_Total", "[TotalAmount] > 0");
            t.HasCheckConstraint("CK_Invoices_Currency", "[Currency] IN ('UAH','USD','EUR')");
            t.HasCheckConstraint("CK_Invoices_Status", "[Status] IN ('Draft','Issued','Paid','Overdue','Void')");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Number).IsRequired().HasMaxLength(50);
        builder.Property(i => i.Currency).IsRequired().HasMaxLength(3).IsFixedLength().IsUnicode(false);   // CHAR(3)
        builder.Property(i => i.TotalAmount).HasColumnType("decimal(18,2)");
        builder.Property(i => i.DueDate).HasColumnType("date");
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(i => i.CreatedBy).HasMaxLength(100);
        builder.Property(i => i.UpdatedBy).HasMaxLength(100);
        builder.Property(i => i.RowVersion).IsRowVersion();                               // оптимістична конкурентність

        builder.HasIndex(i => i.Number).IsUnique().HasDatabaseName("UQ_Invoices_Number");
        builder.HasIndex(i => new { i.MerchantId, i.Status }).HasDatabaseName("IX_Invoices_MerchantId_Status");
        builder.HasIndex(i => new { i.Status, i.DueDate }).HasDatabaseName("IX_Invoices_Status_DueDate")
               .HasFilter("[IsDeleted] = 0");

        // м'яке видалення: видалені рахунки не потрапляють у запити
        builder.HasQueryFilter(i => !i.IsDeleted);

        builder.HasOne(i => i.Merchant)                                                    // 1:N Merchant → Invoices
               .WithMany(m => m.Invoices)
               .HasForeignKey(i => i.MerchantId)
               .OnDelete(DeleteBehavior.Restrict);                                         // мерчант з рахунками не видаляється

        builder.HasOne(i => i.Detail)                                                      // 1:1
               .WithOne()
               .HasForeignKey<InvoiceDetail>(d => d.InvoiceId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(i => i.Lines)                                                      // 1:N
               .WithOne()
               .HasForeignKey(l => l.InvoiceId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class InvoiceDetailConfiguration : IEntityTypeConfiguration<InvoiceDetail>
{
    public void Configure(EntityTypeBuilder<InvoiceDetail> builder)
    {
        builder.ToTable("InvoiceDetails", t => t.HasCheckConstraint("CK_InvoiceDetails_Period",
            "[PeriodStart] IS NULL OR [PeriodEnd] IS NULL OR [PeriodEnd] >= [PeriodStart]"));
        builder.HasKey(d => d.InvoiceId);                                                  // PK є водночас FK
        builder.Property(d => d.CustomerRef).HasMaxLength(100);
        builder.Property(d => d.PeriodStart).HasColumnType("date");
        builder.Property(d => d.PeriodEnd).HasColumnType("date");
        builder.Property(d => d.Note).HasMaxLength(1000);
    }
}

public class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("InvoiceLines", t =>
        {
            t.HasCheckConstraint("CK_InvoiceLines_Qty", "[Quantity] > 0");
            t.HasCheckConstraint("CK_InvoiceLines_Price", "[UnitPrice] >= 0");
        });
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Description).IsRequired().HasMaxLength(300);
        builder.Property(l => l.Quantity).HasColumnType("decimal(10,2)");
        builder.Property(l => l.UnitPrice).HasColumnType("decimal(18,2)");
        builder.Property(l => l.LineTotal)                                                  // обчислюється БД
               .HasColumnType("decimal(29,4)")
               .HasComputedColumnSql("[Quantity] * [UnitPrice]", stored: true);
        builder.HasIndex(l => new { l.InvoiceId, l.SortOrder }).HasDatabaseName("IX_InvoiceLines_Invoice_Sort");
    }
}

public class InvoiceCategoryConfiguration : IEntityTypeConfiguration<InvoiceCategory>
{
    public void Configure(EntityTypeBuilder<InvoiceCategory> builder)
    {
        builder.ToTable("InvoiceCategories");
        builder.HasKey(ic => new { ic.InvoiceId, ic.CategoryId });                          // складений ключ
        builder.Property(ic => ic.AssignedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(ic => ic.Invoice).WithMany(i => i.InvoiceCategories)
               .HasForeignKey(ic => ic.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(ic => ic.Category).WithMany(c => c.InvoiceCategories)
               .HasForeignKey(ic => ic.CategoryId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ic => new { ic.CategoryId, ic.InvoiceId }).HasDatabaseName("IX_IC_CategoryId");
        // інваріант: не більше однієї основної категорії на рахунок
        builder.HasIndex(ic => ic.InvoiceId).IsUnique().HasDatabaseName("UX_IC_Primary")
               .HasFilter("[IsPrimary] = 1");
    }
}
