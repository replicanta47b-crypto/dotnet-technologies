using System.Collections.Concurrent;

namespace Auth.Api.Services;

public interface IEmailService
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}

public sealed record SentEmail(DateTime AtUtc, string To, string Subject, string Body);

/// <summary>
/// Mock-розсилка (за завданням): лист не відправляється, а друкується в лог і лишається в пам'яті (останні 50).
/// У Development їх можна подивитись: GET /auth/dev/outbox — звідти беремо userId і token для verify-email / reset-password.
/// </summary>
public sealed class MockEmailService(ILogger<MockEmailService> logger) : IEmailService
{
    private static readonly ConcurrentQueue<SentEmail> Outbox = new();

    public static IReadOnlyCollection<SentEmail> Sent => Outbox.Reverse().ToArray();

    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        Outbox.Enqueue(new SentEmail(DateTime.UtcNow, to, subject, body));
        while (Outbox.Count > 50) Outbox.TryDequeue(out _);
        logger.LogInformation("[MOCK EMAIL] To: {To} | Subject: {Subject}\n{Body}", to, subject, body);
        return Task.CompletedTask;
    }
}
