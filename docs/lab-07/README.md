# Лаб. 07 — Резильєнтність (Polly), бізнес-метрики та Health Checks

Відповідність доменів: Orders → **Payments** (SQL, ADO.NET + Dapper), Catalog → **Billing** (SQL, EF Core), Reviews → **Disputes** (MongoDB).
Критичність для Aggregator: Payments і Billing — **критичні** (без платежу й рахунків картку не зібрати), Disputes — **некритичний** (Aggregator поверне часткову відповідь із `warnings`).

## 1. Resilience

**Один обробник, без нашарування.** `Platform.ServiceDefaults/Extensions.cs` через `ConfigureHttpClientDefaults` додає Standard Resilience Handler до всіх `HttpClient`. Для клієнтів Aggregator із власними вимогами спершу `RemoveAllResilienceHandlers()`, потім один `AddStandardResilienceHandler(...)` (`Platform.Aggregator/Program.cs`, значення — `Resilience/ClientPolicies.cs`).

**Значення за замовчуванням (ServiceDefaults):** спроба 3 с, загальний 8 с, 2 повтори, експоненційна затримка 300 мс з jitter, повтори вимкнено для POST/PUT/DELETE (`DisableForUnsafeHttpMethods`), вікно breaker 30 с.

### Політики клієнтів Aggregator і обґрунтування
| Параметр | payments-api (критичний) | billing-api (критичний) | disputes-api (некритичний) |
|---|---|---|---|
| Повтори HTTP | 4 | 4 | 1 |
| Затримка (експоненційна, jitter) | 200 мс | 200 мс | 200 мс |
| Таймаут спроби / загальний | 3 с / 15 с | 3 с / 15 с | 2 с / 5 с |
| Circuit breaker: FailureRatio | 0,3 (чутливий) | 0,3 | 0,7 (терпимий) |
| MinimumThroughput / вікно | 5 / 30 с | 5 / 30 с | 10 / 30 с |
| BreakDuration | 10 с | 10 с | 20 с |
| gRPC retry (ServiceConfig): спроб, backoff | 4, 0,2→2 с | 4, 0,2→2 с | 2, 0,2→1 с |
| gRPC-статуси для повтору | Unavailable, ResourceExhausted | те саме | те саме |
| Дедлайн gRPC-виклику | 8 с | 8 с | 3 с |

Чому так:
* **Критичні:** тимчасовий збій краще пережити повторами, ніж віддати користувачу помилку; breaker чутливіший (30 % збоїв), щоб швидко припинити марні виклики й дати сервісу відновитися. Загальний таймаут 15 с вміщує 5 спроб по ≤ 3 с і затримки між ними.
* **Некритичний:** одна повторна спроба й короткі таймаути (2 с / 5 с) — відповідь без спорів кориснішa, ніж повільна відповідь з ними; breaker менш чутливий (70 %), щоб випадкові збої не вимикали сервіс надовго.
* **MinimumThroughput 5–10** замість 100: навчальний стенд має мало трафіку, інакше breaker не спрацював би. `SamplingDuration` (30 с) ≥ 2 × таймаут спроби — вимога валідації.
* **Тільки ідемпотентні повтори:** усі виклики Aggregator — читання (GET / read-only RPC). Для небезпечних HTTP-методів повтори вимкнено.
* **gRPC:** помилки (`Unavailable`, `DeadlineExceeded`) приходять у трейлерах з HTTP 200, тому HTTP-обробник їх не повторює. Повтори за статусами задає `ServiceConfig` каналу (`ConfigureChannel`). HTTP-повтори для gRPC вимкнено (завжди POST) — щоб не було подвійних повторів.
* Переходи breaker логуються: `Circuit OPENED / HALF-OPEN / CLOSED for {Service}` (категорія `Resilience`); події Polly (`OnRetry`, `OnCircuitOpened`…) — у метриці `resilience.polly.strategy.events` (Meter `Polly`, додано в ServiceDefaults) і в логах категорії `Polly`.

**Redis — graceful degradation.** Billing: `Billing.Bll/Caching/TwoLevelCache.cs`, Aggregator: `Platform.Aggregator/Caching/AggregatorCache.cs`. Операції з Redis — у `try/catch` із таймаутом 400 мс; збій логується (`Redis unavailable … falling back to database`), запит читає з БД. У Billing додатково circuit breaker кешу (3 помилки → L2 вимкнено на 20 с).

## 2. Бізнес-метрики (OpenTelemetry Meter API)
Класи-singleton через `IMeterFactory`; MVC-фільтр міряє кожну дію API; Meter-и додано в ServiceDefaults (`AddMeter`).

| Сервіс | Meter | Лічильники | Гістограми | Gauge |
|---|---|---|---|---|
| `Payments.Api/Metrics` | `Payments.Api` | `payments.created`, `payments.status_changed`, `payments.allocations_added` | `payments.operation.duration` (ms), `payments.amount` | `payments.operations.in_flight` |
| `Billing.Api/Metrics` | `Billing.Api` | `invoices.created`, `invoices.status_changed` | `invoices.operation.duration` (ms), `invoices.amount` | `invoices.operations.in_flight`, `billing.cache.l1.entries`, `billing.cache.l1.size` |
| `Disputes.WebApi/Metrics` | `Disputes.Api` | `disputes.opened`, `disputes.comments_added` | `disputes.operation.duration` (ms) | `disputes.operations.in_flight` |
| `Billing.Bll/Caching` | `Billing.Cache` | `cache.hits`, `cache.misses`, `cache.evictions`, `cache.errors`, `cache.invalidations` | — | — |
| Aggregator | `Aggregator.Cache`, `Aggregator.Grpc` | `cache.hits`, `cache.misses`, `cache.errors` | `aggregator.upstream.duration` | — |

**Теги й кардинальність** (однакові назви в усіх сервісах; значення з обмежених наборів):
| Тег | Значення |
|---|---|
| `operation` | `{Controller}.{Action}`, ≈ 20 значень |
| `status` | `success` \| `failure` |
| `payment.status` / `invoice.status` | статуси з доменної моделі (≤ 6) |
| `currency` | UAH, USD, EUR |
| `cache.level` | `l1` \| `l2` \| `all` |
| `cache.name` | вид даних (≈ 6) |
| `service`, `transport` (Aggregator) | 3 сервіси × `http`/`grpc` |

Ідентифікатори платежів, рахунків, клієнтів і CorrelationId у тегах **не** використовуються — кожне унікальне значення створило б окремий часовий ряд. Тривалість записується для **всіх** виконань, включно з невдалими (`status=failure`).

## 3. Health checks
| Endpoint | Призначення | Що перевіряє | Реакція оркестратора |
|---|---|---|---|
| `/health/live` | liveness | лише `self` (без зовнішніх залежностей) | збій → перезапуск процесу |
| `/health/ready` | readiness | перевірки з тегом `ready` | збій → виключення з балансування |
| `/health/startup` | startup | чи завершено запуск | — |
| `/health` | усі перевірки | — | — |

Відповіді — JSON із деталями кожної перевірки (`name`, `status`, `description`, `durationMs`, `tags`, `error`). Degraded → HTTP 200, Unhealthy → 503. Endpoints доступні в усіх середовищах.

| Сервіс | Перевірки `ready` | failureStatus |
|---|---|---|
| Payments | `payments-db` (SQL Server, `SELECT 1`, таймаут 5 с; теги `database`, `sql`, `ready`) | Unhealthy |
| Billing | `billing-db` (SQL Server), `redis-cache` (Redis, таймаут 3 с; теги `cache`, `redis`, `ready`) | Unhealthy / **Degraded** |
| Disputes | `disputes-db` (MongoDB `ping`, таймаут 5 с; теги `database`, `mongodb`, `ready`) | Unhealthy |
| Aggregator | `redis-cache`, `payments-api`, `billing-api`, `disputes-api` (gRPC Health Checking Protocol, дедлайн 2 с) | Degraded / Unhealthy / Degraded / Degraded |

* Redis — необов'язкова залежність, тому збій = **Degraded** (сервіс працює без кешу). Вбудовану Unhealthy-перевірку Aspire вимкнено (`DisableHealthChecks`).
* gRPC-сервіси публікують стандартний `grpc.health.v1.Health` (`AddGrpcHealthChecks` + `MapGrpcHealthChecksService`); Aggregator викликає `Health/Check` через `Health.HealthClient`. Для Aggregator критичний лише Payments (без платежу картки немає); billing/disputes → Degraded, бо відповідь буде часткова.
* AppHost: `WithHttpHealthCheck("/health/ready")` для кожного сервісу → Dashboard показує Healthy / Degraded / Unhealthy, а `WaitFor` чекає на здорову залежність.

## 4. Моніторинг в Aspire Dashboard
| Що | Де | Нормальний діапазон / сигнал тривоги |
|---|---|---|
| Повтори й розмикання | Metrics → `Polly` → `resilience.polly.strategy.events`, фільтр `event.name` | `OnRetry` поодинокі; `OnCircuitOpened` > 0 — сервіс-залежність відмовляє |
| Частка влучань кешу | `Billing.Cache` → `cache.hits` / `cache.misses`, розбивка за `cache.level` | після прогріву > 70 % влучань |
| Помилки Redis | `cache.errors` | 0; зростання — Redis недоступний |
| Затримка операцій | `*.operation.duration` | p95 < 100 мс для читання |
| Затримка downstream | `aggregator.upstream.duration`, фільтр `service`, `transport` | порівнювати HTTP і gRPC |
| Стан ресурсів | Resources | усі зелені; жовтий — Degraded (Redis) |
| Логи breaker | Structured logs, категорія `Resilience` / `Polly` | `Circuit OPENED` — Error |

Типові фільтри: Structured logs → `CorrelationId = <значення>`; категорія `Polly`; рівень Warning+.

## 5. Перевірка (сценарії)
1. `curl http://localhost:5000/api/aggregator/payments/1/full?nocache=true` → 200.
2. Зупинити `disputes-api` на 5–10 с у Dashboard → у логах повтори, відповідь швидко повертається з `warnings`; запустити знову.
3. Серія запитів при зупиненому сервісі → `Circuit OPENED`, подальші запити відхиляються миттєво; після `BreakDuration` → `HALF-OPEN` → `CLOSED`.
4. Зупинити Redis → `GET /health/ready` у Billing і Aggregator: `Degraded` (HTTP 200), сервіси відповідають.
5. Зупинити SQL Server → `/health/live` = 200, `/health/ready` = 503.
6. Створити кілька платежів/рахунків → `payments.created`, `invoices.created` з розбивкою за тегами; `*.operation.duration`.
