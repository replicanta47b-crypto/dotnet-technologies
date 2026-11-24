namespace Disputes.Infrastructure.Persistence;

/// <summary>Секція MongoDb в appsettings.json. Пул з'єднань і таймаути задаються в рядку підключення.</summary>
public class MongoDbOptions
{
    public string ConnectionString { get; set; } = "";
    public string DatabaseName { get; set; } = "";
}
