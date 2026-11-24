using Disputes.Domain.Entities;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Disputes.Infrastructure.Persistence;

/// <summary>
/// Контекст MongoDB: одна IMongoDatabase на застосунок (MongoClient потокобезпечний і має власний пул з'єднань).
/// Реєструється як Singleton. З'єднання встановлюється ліниво — під час першого звернення.
/// </summary>
public class MongoDbContext
{
    public const string DisputesCollectionName = "disputes";
    public const string ThreadsCollectionName = "disputeThreads";

    public MongoDbContext(IOptions<MongoDbOptions> options)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
            throw new InvalidOperationException("MongoDb:ConnectionString is not configured.");
        if (string.IsNullOrWhiteSpace(settings.DatabaseName))
            throw new InvalidOperationException("MongoDb:DatabaseName is not configured.");

        Client = new MongoClient(settings.ConnectionString);
        Database = Client.GetDatabase(settings.DatabaseName);
    }

    public IMongoClient Client { get; }
    public IMongoDatabase Database { get; }

    public IMongoCollection<Dispute> Disputes => Database.GetCollection<Dispute>(DisputesCollectionName);
    public IMongoCollection<DisputeThread> Threads => Database.GetCollection<DisputeThread>(ThreadsCollectionName);
}
