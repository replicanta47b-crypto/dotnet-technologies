using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Platform.Aggregator.Resilience;

/// <summary>
/// Політика стійкості одного downstream-сервісу. Значення підібрано під критичність (обґрунтування — README, розділ «Лаб. 07»).
/// </summary>
/// <param name="Name">Ім'я ресурсу (service discovery), використовується в логах.</param>
/// <param name="Critical">Критичний сервіс: без нього відповідь не має сенсу → більше повторів, чутливіший breaker.</param>
/// <param name="HttpRetries">Повтори на рівні HttpClient (Polly) — для REST-шляху.</param>
/// <param name="FailureRatio">Частка збоїв у вікні SamplingDuration, що розмикає ланцюг.</param>
/// <param name="MinimumThroughput">Мінімум запитів у вікні для оцінки частки (мале число — бо навчальний стенд має мало трафіку).</param>
/// <param name="GrpcMaxAttempts">Усього спроб gRPC-виклику (перша + повтори) за gRPC-статусами.</param>
/// <param name="GrpcDeadline">Дедлайн gRPC-виклику, включно з повторами.</param>
public sealed record ClientPolicy(
    string Name,
    bool Critical,
    int HttpRetries,
    TimeSpan RetryDelay,
    TimeSpan AttemptTimeout,
    TimeSpan TotalTimeout,
    double FailureRatio,
    int MinimumThroughput,
    TimeSpan BreakDuration,
    int GrpcMaxAttempts,
    TimeSpan GrpcInitialBackoff,
    TimeSpan GrpcMaxBackoff,
    TimeSpan GrpcDeadline);

public static class ClientPolicies
{
    /// <summary>Критичний: без платежу картку не зібрати.</summary>
    public static readonly ClientPolicy Payments = new(
        "payments-api", Critical: true,
        HttpRetries: 4, RetryDelay: TimeSpan.FromMilliseconds(200),
        AttemptTimeout: TimeSpan.FromSeconds(3), TotalTimeout: TimeSpan.FromSeconds(15),
        FailureRatio: 0.3, MinimumThroughput: 5, BreakDuration: TimeSpan.FromSeconds(10),
        GrpcMaxAttempts: 4, GrpcInitialBackoff: TimeSpan.FromMilliseconds(200), GrpcMaxBackoff: TimeSpan.FromSeconds(2),
        GrpcDeadline: TimeSpan.FromSeconds(8));

    /// <summary>Критичний (аналог Каталогу): рахунки — основа картки платежу.</summary>
    public static readonly ClientPolicy Billing = Payments with { Name = "billing-api" };

    /// <summary>Некритичний (аналог Відгуків): Aggregator поверне часткову відповідь, тож швидко здаємося.</summary>
    public static readonly ClientPolicy Disputes = new(
        "disputes-api", Critical: false,
        HttpRetries: 1, RetryDelay: TimeSpan.FromMilliseconds(200),
        AttemptTimeout: TimeSpan.FromSeconds(2), TotalTimeout: TimeSpan.FromSeconds(5),
        FailureRatio: 0.7, MinimumThroughput: 10, BreakDuration: TimeSpan.FromSeconds(20),
        GrpcMaxAttempts: 2, GrpcInitialBackoff: TimeSpan.FromMilliseconds(200), GrpcMaxBackoff: TimeSpan.FromSeconds(1),
        GrpcDeadline: TimeSpan.FromSeconds(3));

    /// <summary>Встановлюється після Build(): колбеки breaker викликаються під час роботи, коли DI вже існує.</summary>
    public static ILogger? Logger { get; set; }

    /// <summary>
    /// Налаштування Standard Resilience Handler для клієнта. Викликається ПІСЛЯ RemoveAllResilienceHandlers(),
    /// щоб власний обробник не нашаровувався на стандартний із ConfigureHttpClientDefaults.
    /// </summary>
    /// <param name="grpc">gRPC — це завжди POST, тому HTTP-повтори вимкнено: повторами за gRPC-статусами керує ServiceConfig каналу.</param>
    public static void Apply(HttpStandardResilienceOptions o, ClientPolicy p, bool grpc)
    {
        // --- Retry ---
        o.Retry.MaxRetryAttempts = p.HttpRetries;
        o.Retry.Delay = p.RetryDelay;
        o.Retry.BackoffType = DelayBackoffType.Exponential;   // 200 мс, 400 мс, 800 мс...
        o.Retry.UseJitter = true;                             // розсинхронізація клієнтів (thundering herd)
        o.Retry.DisableForUnsafeHttpMethods();                // POST/PUT/DELETE не повторюємо (дублювання змін); для gRPC — свій retry

        // --- Таймаути: загальний має вміщати повтори, SamplingDuration >= 2 × AttemptTimeout (вимога валідації) ---
        o.AttemptTimeout.Timeout = p.AttemptTimeout;
        o.TotalRequestTimeout.Timeout = p.TotalTimeout;

        // --- Circuit breaker ---
        o.CircuitBreaker.FailureRatio = p.FailureRatio;
        o.CircuitBreaker.MinimumThroughput = p.MinimumThroughput;
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        o.CircuitBreaker.BreakDuration = p.BreakDuration;
        o.CircuitBreaker.OnOpened = args =>
        {
            Logger?.LogError("Circuit OPENED for {Service} ({Transport}) for {BreakSeconds}s: requests are rejected without calling the service",
                p.Name, grpc ? "grpc" : "http", args.BreakDuration.TotalSeconds);
            return ValueTask.CompletedTask;
        };
        o.CircuitBreaker.OnHalfOpened = _ =>
        {
            Logger?.LogWarning("Circuit HALF-OPEN for {Service}: one trial request is allowed", p.Name);
            return ValueTask.CompletedTask;
        };
        o.CircuitBreaker.OnClosed = _ =>
        {
            Logger?.LogInformation("Circuit CLOSED for {Service}: service recovered", p.Name);
            return ValueTask.CompletedTask;
        };
    }

    /// <summary>
    /// gRPC-статуси приходять у трейлерах відповіді з HTTP 200, тому HTTP-обробник їх не повторює.
    /// Повтори за статусами (Unavailable, ResourceExhausted) задаються retry-політикою каналу (ServiceConfig).
    /// Усі наші RPC — читання (ідемпотентні), тож повтори безпечні.
    /// </summary>
    public static ServiceConfig GrpcServiceConfig(ClientPolicy p) => new()
    {
        MethodConfigs =
        {
            new MethodConfig
            {
                Names = { MethodName.Default },
                RetryPolicy = new Grpc.Net.Client.Configuration.RetryPolicy
                {
                    MaxAttempts = p.GrpcMaxAttempts,
                    InitialBackoff = p.GrpcInitialBackoff,
                    MaxBackoff = p.GrpcMaxBackoff,
                    BackoffMultiplier = 2,
                    RetryableStatusCodes = { StatusCode.Unavailable, StatusCode.ResourceExhausted }
                }
            }
        }
    };
}
