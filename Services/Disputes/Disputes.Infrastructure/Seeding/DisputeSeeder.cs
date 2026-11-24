using Disputes.Domain.Entities;
using Disputes.Domain.Enums;
using Disputes.Domain.ValueObjects;
using Disputes.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Disputes.Infrastructure.Seeding;

/// <summary>
/// Початкові дані для колекцій disputes та disputeThreads. Ідемпотентність: якщо колекція вже містить документи
/// (наприклад, після db/p3/seed.js з Лаб. 01) — нічого не вставляється. Вставка — bulk (InsertMany).
/// paymentId / invoiceId / merchantId відповідають seed-даним PaymentsDb і BillingDb (логічні посилання, без FK).
/// </summary>
public class DisputeSeeder(MongoDbContext context, ILogger<DisputeSeeder> logger) : IDataSeeder
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await context.Disputes.EstimatedDocumentCountAsync(cancellationToken: ct) > 0)
        {
            logger.LogInformation("Collection '{Collection}' is not empty — seeding skipped",
                MongoDbContext.DisputesCollectionName);
            return;
        }

        var disputes = new List<Dispute>();
        var threads = new List<DisputeThread>();

        Add(new Dispute(2, 4, 1, "ЕнергоСвіт", 980.00m, "UAH", DisputeReason.Duplicate,
                new Author("u-3", "Марія")),
            "Подвійне списання за електроенергію",
            d =>
            {
                d.AddEvidence("bank_statement", "evidence/seed-duplicate.pdf");
                d.AddComment("Підтримка", "Запит на перевірку подвійного списання передано мерчанту");
            });

        Add(new Dispute(3, 5, 2, "ВодоКанал Плюс", 420.00m, "UAH", DisputeReason.ServiceNotProvided,
                new Author("u-2", "Андрій")),
            "Послугу не надано",
            d => d.AddComment("Андрій", "Води не було три дні"));

        Add(new Dispute(3, 6, 3, "ТелеЛінк", 150.00m, "USD", DisputeReason.Fraud,
                new Author("u-4", "Ірина")),
            "Підозріла операція", null);

        await context.Disputes.InsertManyAsync(disputes, cancellationToken: ct);
        await context.Threads.InsertManyAsync(threads, cancellationToken: ct);

        logger.LogInformation("Seeded {Disputes} disputes and {Threads} threads", disputes.Count, threads.Count);

        void Add(Dispute dispute, string threadTitle, Action<Dispute>? configure)
        {
            configure?.Invoke(dispute);
            var thread = new DisputeThread(dispute.Id, threadTitle);
            dispute.AttachThread(thread.Id);
            disputes.Add(dispute);
            threads.Add(thread);
        }
    }
}
