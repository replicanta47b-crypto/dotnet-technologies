using Disputes.Domain.Common;
using Disputes.Domain.Entities;
using Disputes.Domain.Enums;
using Disputes.Domain.ValueObjects;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace Disputes.Infrastructure.Persistence;

/// <summary>
/// BSON-мапінг доменних класів через BsonClassMap — домен лишається чистим (жодних [Bson*]-атрибутів).
/// Реєструється один раз при старті, до першого звернення до БД.
/// </summary>
public static class MongoMappings
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
            return;

        // Єдина конвенція імен полів (camelCase, як у документах Лаб. 01), невідомі поля ігноруються,
        // null-значення не записуються (інакше $jsonSchema-валідатор відхилив би null замість date/object).
        ConventionRegistry.Register(
            "DisputesConventions",
            new ConventionPack
            {
                new CamelCaseElementNameConvention(),
                new IgnoreExtraElementsConvention(true),
                new IgnoreIfNullConvention(true)
            },
            type => type.Namespace is not null && type.Namespace.StartsWith("Disputes.Domain"));

        // ---- базова сутність: Id → _id, аудит, Version ----
        BsonClassMap.RegisterClassMap<BaseEntity>(cm =>
        {
            cm.AutoMap();
            UseUtcDates(cm);
        });

        // ---- агрегат Dispute ----
        BsonClassMap.RegisterClassMap<Dispute>(cm =>
        {
            cm.AutoMap();
            cm.MapField("_evidence").SetElementName("evidence");   // приватні поля колекцій
            cm.MapField("_comments").SetElementName("comments");
            // flexible schema: невідомі поля документа (fraudDetails, tags, ...) не губляться при ReplaceOne.
            // Спершу прибираємо властивість зі звичайних полів — вона мапиться лише як extra elements.
            cm.UnmapProperty("ExtraElements");
            cm.MapExtraElementsProperty("ExtraElements");
            cm.MapProperty(d => d.Reason).SetSerializer(new SnakeCaseEnumSerializer<DisputeReason>());
            cm.MapProperty(d => d.Status).SetSerializer(new SnakeCaseEnumSerializer<DisputeStatus>());
            cm.MapProperty(d => d.Amount).SetSerializer(new DecimalSerializer(BsonType.Decimal128));
            UseUtcDates(cm);
        });

        BsonClassMap.RegisterClassMap<DisputeThread>(cm =>
        {
            cm.AutoMap();
            cm.MapProperty(t => t.Status).SetSerializer(new SnakeCaseEnumSerializer<ThreadStatus>());
            UseUtcDates(cm);
        });

        // ---- Value Objects: незмінні, створюються через конструктор (MapCreator) ----
        BsonClassMap.RegisterClassMap<Author>(cm =>
        {
            cm.MapProperty(a => a.UserId).SetElementName("userId");
            cm.MapProperty(a => a.DisplayName).SetElementName("displayName");
            cm.MapCreator(a => new Author(a.UserId, a.DisplayName));
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<EvidenceItem>(cm =>
        {
            cm.MapProperty(e => e.Type).SetElementName("type");
            cm.MapProperty(e => e.Url).SetElementName("url").SetIgnoreIfNull(true);
            cm.MapProperty(e => e.AddedAt).SetElementName("addedAt")
                .SetSerializer(new DateTimeSerializer(DateTimeKind.Utc));
            cm.MapCreator(e => new EvidenceItem(e.Type, e.Url, e.AddedAt));
            cm.SetIgnoreExtraElements(true);
        });

        BsonClassMap.RegisterClassMap<Comment>(cm =>
        {
            cm.MapProperty(c => c.AuthorName).SetElementName("author");   // у документі поле називається author
            cm.MapProperty(c => c.Text).SetElementName("text");
            cm.MapProperty(c => c.CreatedAt).SetElementName("createdAt")
                .SetSerializer(new DateTimeSerializer(DateTimeKind.Utc));
            cm.MapCreator(c => new Comment(c.AuthorName, c.Text, c.CreatedAt));
            cm.SetIgnoreExtraElements(true);
        });
    }

    /// <summary>Дати читаються як UTC (за замовчуванням драйвер повертає локальний час).</summary>
    private static void UseUtcDates(BsonClassMap classMap)
    {
        foreach (var member in classMap.DeclaredMemberMaps)
        {
            if (member.MemberType == typeof(DateTime))
                member.SetSerializer(new DateTimeSerializer(DateTimeKind.Utc));
            else if (member.MemberType == typeof(DateTime?))
                member.SetSerializer(new NullableSerializer<DateTime>(new DateTimeSerializer(DateTimeKind.Utc)));
        }
    }
}
