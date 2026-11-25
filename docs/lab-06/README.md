# Лаб. 06 — gRPC і дворівневе кешування (Memory + Redis)

## Що додано
| Тема | Де |
|---|---|
| Redis в AppHost (`AddRedis("redis").WithDataVolume()`, `WithReference/WaitFor`) | `Platform.AppHost/AppHost.cs` |
| `.proto`-контракти (proto3, `package`, `csharp_namespace`) | `Services/{Billing.Api,Payments.Api,Disputes.WebApi}/Protos/*.proto` |
| `<Protobuf GrpcServices="Server">` у сервісах, `"Client"` в Aggregator (той самий файл через `Link`) | `*.csproj` |
| gRPC-сервіси — адаптери над BLL/MediatR, `RpcException` (NotFound / InvalidArgument) | `*/Grpc/*GrpcService.cs` |
| `AddGrpc`, `MapGrpcService`, Kestrel `Http1AndHttp2`, https-профіль (HTTP/2 через ALPN) | `Program.cs`, `appsettings.json`, `launchSettings.json` |
| Серверний interceptor: метод, peer, статус, тривалість, CorrelationId | `Platform.ServiceDefaults/GrpcInterceptors.cs` |
| L1 `IMemoryCache` (SizeLimit, Size, sliding + absolute, пріоритети, callback витіснення) | `Billing.Bll/Caching/TwoLevelCache.cs`, `CachePolicy.cs` |
| L2 `IDistributedCache` через Aspire (`AddRedisDistributedCache("redis")`), JSON, префікс `billing:`, TTL за типом даних | `Billing.Api/Program.cs`, `CachePolicy.cs`, `CacheKeys.cs` |
| Cache-aside + декоратори сервісів, інвалідація після POST/PUT/DELETE | `CachedInvoiceService.cs`, `CachedMerchantService.cs` |
| Метрики `cache.hits` / `cache.misses` / `cache.evictions` з тегом `cache.level` (Meter `Billing.Cache`) | `CacheMetrics.cs`, `ServiceDefaults/Extensions.cs` (`AddMeter`) |
| Aggregator: `AddGrpcClient` ×3 з service discovery, `Task.WhenAll`, `CancellationToken`, дедлайн 5 с, `RpcException` → `warnings` | `Platform.Aggregator/Program.cs`, `Services/*` |
| Кеш готової картки платежу в Redis (TTL 30 с, часткові відповіді не кешуються) | `Platform.Aggregator/Caching/AggregatorCache.cs` |

## Кешування
```
запит → L1 (пам'ять, TTL 2 хв / sliding 45 с)
      → L2 (Redis, TTL 10 хв)
      → БД;  результат записується в обидва рівні на зворотному шляху
```
* **Ключі:** `billing:invoice:{id}`, `billing:merchant:{id}`, `billing:merchants:all`, `billing:invoices:overdue`.
* **Інвалідація:** оплата/скасування/перенесення/категорія/видалення рахунку скидають картку рахунку, список прострочених і картку мерчанта; створення мерчанта — список мерчантів. У лог пишеться причина (`Cache INVALIDATE … reason: …`).
* **Redis недоступний:** таймаут 400 мс + circuit breaker (3 помилки → L2 вимкнено на 20 с); сервіс працює з L1 + БД.
* **Cache stampede:** для одного ключа фабрика виконується один раз (striped lock).
* Логи: `Cache HIT L1/L2`, `Cache MISS … loading from source`.

## gRPC
| Сервіс | RPC |
|---|---|
| `billing.v1.BillingService` | `GetInvoice`, `GetInvoices` (батч) |
| `payments.v1.PaymentsService` | `GetPayment` |
| `disputes.v1.DisputesService` | `GetDisputesByPayment` |

`decimal` передається рядком (InvariantCulture), дати — ISO 8601. Немає сутності → `NotFound`, некоректний id → `InvalidArgument`, неочікуване → `Internal` без деталей.

## Aggregator
`GET /api/aggregator/payments/{id}/full?transport=grpc|http&nocache=true`
Заголовки відповіді: `X-Transport`, `X-Cache` (HIT/MISS/BYPASS), `X-Elapsed-Ms`.

## Запуск
```
dotnet dev-certs https --trust
dotnet build Platform.slnx
dotnet run --project Platform.AppHost
```
Gateway: `http://localhost:5000`. У Development Aggregator приймає dev-сертифікат, тож довіра до нього не обов'язкова.

## Самоперевірка
1. Двічі `GET /api/invoices/1` → у логах `MISS`, потім `HIT L1`; у трейсі другого запиту немає SQL-спана.
2. `POST /api/invoices/1/pay`, знову `GET` → `INVALIDATE … reason`, далі `MISS`.
3. Зупинити `redis` → запити працюють, у логах `L2 (Redis) … failed`.
4. `…/full?nocache=true&transport=http` і `&transport=grpc` → порівняти `X-Elapsed-Ms`.
5. Зупинити `billing-api` → `200` з `warnings`.
6. Dashboard → Metrics → `Billing.Cache` → `cache.hits` (розбивка за `cache.level`).

## Знімки екрана і вимірювання
| Файл | Що показує |
|---|---|
| `1a-trace-cache-miss-sql.jpg` | Перший `GET /api/billing/invoices/{id}`: промах кешу → два SQL-спани `mssql BillingDb` |
| `1b-trace-cache-hit-no-sql.jpg` | Повторний запит: влучання в кеш, у трейсі лише `billing-api`, **без SQL-спана** (3 мс) |
| `2-trace-aggregator-grpc.jpg` | Картка платежу через gRPC: `POST /payments.v1…`, `/billing.v1…` ×2, `/disputes.v1…` — паралельно; всередині SQL і MongoDB-спани |
| `3-metric-cache-hits.jpg` | Метрика `cache.hits` (Meter `Billing.Cache`) в Aspire Dashboard |

Результати перевірок (через Gateway `http://localhost:5000`):
* Інвалідація: `POST …/pay` → наступний `GET` одразу повертає `Paid` (промах 16 мс), далі знову влучання (7 мс).
* Картка платежу: `nocache` — gRPC 19–21 мс, HTTP 15–22 мс; з кешем Redis — `X-Cache: HIT`, 2 мс. На тестових даних (1 платіж, 2 рахунки) різниця транспортів в межах похибки; gRPC виграє зі збільшенням кількості викликів і розміру повідомлень.
* Неіснуючий платіж → `404`.
* Redis зупинено: усі запити далі повертають `200` (L1 + БД), у логах `L2 (Redis) … failed`; затримка зростає до ~0,8 с через таймаут L2 (400 мс), поки не відкриється запобіжник.
* `billing-api` зупинено: `GET …/full?transport=grpc&nocache=true` → `200`, платіж є, `invoices: []`, `warnings: ["billing-api is unavailable (DeadlineExceeded)"]` (дедлайн gRPC-виклику 5 с); після запуску сервісу відповідь знову повна.
