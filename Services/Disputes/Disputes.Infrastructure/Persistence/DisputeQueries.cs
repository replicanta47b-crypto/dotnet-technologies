using Disputes.Application.Common.Interfaces;
using Disputes.Application.Common.Models;
using Disputes.Application.Dtos;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Disputes.Infrastructure.Persistence;

/// <summary>
/// Сторона читання (CQRS): $match / $sort / $skip / $limit / $project виконує MongoDB, у DTO потрапляють
/// лише потрібні поля. Працює з BsonDocument — без гідрації агрегату й без доменної логіки.
/// </summary>
public class DisputeQueries(MongoDbContext context) : IDisputeQueries
{
    private readonly IMongoCollection<BsonDocument> _collection =
        context.Database.GetCollection<BsonDocument>(MongoDbContext.DisputesCollectionName);

    public async Task<DisputeDetailsDto?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return null;

        var projection = Builders<BsonDocument>.Projection
            .Include("paymentId").Include("invoiceId").Include("merchantId").Include("merchantName")
            .Include("amount").Include("currency").Include("reason").Include("status")
            .Include("openedBy").Include("openedAt").Include("resolvedAt").Include("threadId")
            .Include("version").Include("evidence").Include("comments");

        var doc = await _collection
            .Find(new BsonDocument("_id", objectId))
            .Project<BsonDocument>(projection)
            .FirstOrDefaultAsync(ct);

        return doc is null ? null : ToDetails(doc);
    }

    public async Task<PagedResult<DisputeDto>> GetListAsync(DisputeListFilter filter, CancellationToken ct)
    {
        var match = new BsonDocument();
        if (filter.MerchantId is { } merchantId) match["merchantId"] = merchantId;
        if (filter.PaymentId is { } paymentId) match["paymentId"] = paymentId;
        if (!string.IsNullOrWhiteSpace(filter.Status)) match["status"] = filter.Status.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(filter.Search))   // повнотекстовий пошук по text-індексу
            match["$text"] = new BsonDocument("$search", filter.Search.Trim());

        var total = await _collection.CountDocumentsAsync(match, cancellationToken: ct);

        var direction = filter.Descending ? -1 : 1;
        var sort = new BsonDocument
        {
            { ResolveSortField(filter.SortBy), direction },
            { "_id", direction }   // стабільне впорядкування між сторінками
        };

        var pipeline = new[]
        {
            new BsonDocument("$match", match),
            new BsonDocument("$sort", sort),
            new BsonDocument("$skip", (long)(filter.Page - 1) * filter.PageSize),
            new BsonDocument("$limit", filter.PageSize),
            new BsonDocument("$project", new BsonDocument
            {
                { "paymentId", 1 }, { "invoiceId", 1 }, { "merchantId", 1 }, { "merchantName", 1 },
                { "amount", 1 }, { "currency", 1 }, { "reason", 1 }, { "status", 1 },
                { "openedBy", 1 }, { "openedAt", 1 }, { "version", 1 },
                { "commentsCount", new BsonDocument("$size",
                      new BsonDocument("$ifNull", new BsonArray { "$comments", new BsonArray() })) }
            })
        };

        var docs = await _collection
            .Aggregate(PipelineDefinition<BsonDocument, BsonDocument>.Create(pipeline))
            .ToListAsync(ct);

        return new PagedResult<DisputeDto>(docs.Select(ToDto).ToList(), total, filter.Page, filter.PageSize);
    }

    private static string ResolveSortField(string sortBy) => sortBy.ToLowerInvariant() switch
    {
        "amount" => "amount",
        "status" => "status",
        "merchantname" => "merchantName",
        _ => "openedAt"
    };

    // ---------- BsonDocument → DTO ----------

    private static DisputeDto ToDto(BsonDocument d) => new(
        d["_id"].AsObjectId.ToString(),
        d["paymentId"].ToInt64(),
        d["invoiceId"].ToInt64(),
        d["merchantId"].ToInt64(),
        d["merchantName"].AsString,
        ToDecimal(d["amount"]),
        d["currency"].AsString,
        d["reason"].AsString,
        d["status"].AsString,
        d["openedBy"]["userId"].AsString,
        d["openedBy"]["displayName"].AsString,
        d["openedAt"].ToUniversalTime(),
        d.GetValue("commentsCount", 0).ToInt32(),
        d.GetValue("version", 0).ToInt32());

    private static DisputeDetailsDto ToDetails(BsonDocument d)
    {
        var evidence = d.GetValue("evidence", new BsonArray()).AsBsonArray
            .Select(e => new EvidenceDto(
                e["type"].AsString,
                e.AsBsonDocument.TryGetValue("url", out var url) ? url.AsString : null,
                e["addedAt"].ToUniversalTime()))
            .ToList();

        var comments = d.GetValue("comments", new BsonArray()).AsBsonArray
            .Select(c => new CommentDto(c["author"].AsString, c["text"].AsString, c["createdAt"].ToUniversalTime()))
            .ToList();

        return new DisputeDetailsDto(
            d["_id"].AsObjectId.ToString(),
            d["paymentId"].ToInt64(),
            d["invoiceId"].ToInt64(),
            d["merchantId"].ToInt64(),
            d["merchantName"].AsString,
            ToDecimal(d["amount"]),
            d["currency"].AsString,
            d["reason"].AsString,
            d["status"].AsString,
            d["openedBy"]["userId"].AsString,
            d["openedBy"]["displayName"].AsString,
            d["openedAt"].ToUniversalTime(),
            d.TryGetValue("resolvedAt", out var resolvedAt) ? resolvedAt.ToUniversalTime() : null,
            d.TryGetValue("threadId", out var threadId) ? threadId.AsObjectId.ToString() : null,
            d.GetValue("version", 0).ToInt32(),
            evidence,
            comments);
    }

    /// <summary>Суми в БД можуть бути double (mongosh-seed), int або decimal128 (запис із застосунку).</summary>
    private static decimal ToDecimal(BsonValue value) => value.BsonType switch
    {
        BsonType.Decimal128 => Decimal128.ToDecimal(value.AsDecimal128),
        BsonType.Double => (decimal)value.AsDouble,
        _ => value.ToInt64()
    };
}
