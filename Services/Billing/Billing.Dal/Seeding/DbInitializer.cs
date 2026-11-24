using Billing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Billing.Dal.Seeding;

/// <summary>
/// Програмний ідемпотентний сідінг: виконується після MigrateAsync().
/// Довідники (мерчанти, категорії) засіваються статично через HasData; тут — залежні записи
/// (рахунки, рядки, деталі, категорії) з перевіркою наявності. Дані збігаються з db/p2/seed.sql (Лаб. 01).
/// </summary>
public static class DbInitializer
{
    public static async Task SeedAsync(BillingDbContext db, CancellationToken ct = default)
    {
        // повторний запуск не створює дублів
        if (await db.Invoices.IgnoreQueryFilters().AnyAsync(ct)) return;

        // (номер, мерчант, сума → рядки, термін, статус, клієнт, період)
        var invoices = new List<Invoice>();

        Invoice Build(string number, long merchantId, DateOnly due, string customerRef,
                      DateOnly periodStart, DateOnly periodEnd, string? note,
                      params (string Description, decimal Qty, decimal Price)[] lines)
        {
            var invoice = new Invoice(number, merchantId, "UAH", due, "seed");
            foreach (var l in lines) invoice.AddLine(l.Description, l.Qty, l.Price);
            invoice.SetDetail(customerRef, periodStart, periodEnd, note);
            invoice.Issue();
            invoices.Add(invoice);
            return invoice;
        }

        var inv1 = Build("INV-2026-0001", 1, new(2026, 10, 10), "ACC-100234", new(2026, 9, 1), new(2026, 9, 30),
            "Показання лічильника передано", ("Електроенергія, кВт·год", 250m, 4.80m), ("Абонентська плата", 1m, 50.50m));
        var inv2 = Build("INV-2026-0002", 2, new(2026, 10, 12), "ACC-770021", new(2026, 9, 1), new(2026, 9, 30), null,
            ("Водопостачання, м³", 12m, 30m), ("Водовідведення, м³", 12m, 5m));
        var inv3 = Build("INV-2026-0003", 3, new(2026, 11, 5), "ACC-550310", new(2026, 10, 1), new(2026, 10, 31), null,
            ("Інтернет 100 Мбіт/с, місяць", 1m, 300m));
        var inv4 = Build("INV-2026-0004", 1, new(2026, 11, 10), "ACC-100234", new(2026, 10, 1), new(2026, 10, 31), null,
            ("Електроенергія, кВт·год", 280m, 4.80m), ("Абонентська плата", 1m, 36.75m));
        var inv5 = Build("INV-2026-0005", 2, new(2026, 11, 12), "ACC-770021", new(2026, 10, 1), new(2026, 10, 31),
            "Є заборгованість за попередній період", ("Водопостачання, м³", 13m, 30m), ("Водовідведення, м³", 13m, 5m));
        var inv6 = Build("INV-2026-0006", 3, new(2026, 12, 5), "ACC-550310", new(2026, 11, 1), new(2026, 11, 30), null,
            ("Інтернет 100 Мбіт/с, місяць", 1m, 300m));

        inv1.MarkPaid();
        inv2.MarkPaid();
        inv3.MarkPaid();
        inv5.MarkOverdue();

        // категорії: 1 Комунальні, 2 Електроенергія, 3 Водопостачання, 4 Зв'язок, 5 Інтернет
        inv1.AssignCategory(2, isPrimary: true);  inv1.AssignCategory(1, isPrimary: false);
        inv2.AssignCategory(3, isPrimary: true);  inv2.AssignCategory(1, isPrimary: false);
        inv3.AssignCategory(5, isPrimary: true);  inv3.AssignCategory(4, isPrimary: false);
        inv4.AssignCategory(2, isPrimary: true);  inv4.AssignCategory(1, isPrimary: false);
        inv5.AssignCategory(3, isPrimary: true);  inv5.AssignCategory(1, isPrimary: false);
        inv6.AssignCategory(5, isPrimary: true);  inv6.AssignCategory(4, isPrimary: false);

        db.Invoices.AddRange(invoices);     // довідники вже є (HasData), тому FK цілісні
        await db.SaveChangesAsync(ct);
    }
}
