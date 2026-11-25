# Лабораторна робота 8. Подієва архітектура на RabbitMQ

Платформа з Лаб. 05–07 доповнена асинхронною комунікацією: **Білінг** (власник даних про рахунки й мерчантів) публікує
події, а **Платежі** й **Спори** оновлюють власні копії даних — так, як це визначено в таблиці «Політика дублювання даних» (Лаб. 01).

| Роль із методички | У нашому домені | Що робить |
|---|---|---|
| Каталог (Лаб. 03, EF Core) | **Billing** (`Services/Billing`) | видавець: `InvoiceIssued`, `InvoiceAmountChanged`, `MerchantRenamed` |
| Замовлення (Лаб. 02, Dapper) | **Payments** (`Services/Payments`) | споживач: оновлює локальну копію `dbo.Invoices` |
| Відгуки (Лаб. 04, MongoDB) | **Disputes** (`Services/Disputes`) | споживач: оновлює `disputes.merchantName` |
| `ProductPriceChanged` | `InvoiceAmountChangedEvent` | сума рахунку змінилась (донарахування) |
| `ProductRenamed` | `MerchantRenamedEvent` | мерчанта перейменовано |

## Архітектура

```
 HTTP PUT /api/merchants/{id}/name            ┌─────────────────────────┐
 HTTP POST /api/invoices/{id}/surcharges ──►  │ Billing.Api (видавець)  │  1) SaveChanges (EF Core)
 HTTP POST /api/invoices                      │ Billing.Bll → publisher │  2) PublishAsync (лише після успіху)
                                              └───────────┬─────────────┘
                                                          │ exchange «billing-events» (topic, durable)
                          ┌───────────────────────────────┴───────────────────────────────┐
         binding invoice.* + merchant.*                                          binding merchant.*
                          ▼                                                                ▼
              queue payments.billing-sync                                      queue disputes.billing-sync
              (DLQ: payments.billing-sync.dlq)                                 (DLQ: disputes.billing-sync.dlq)
                          ▼                                                                ▼
            Payments.Api: EventConsumerService                               Disputes.WebApi: EventConsumerService
            handlers → dbo.Invoices + dbo.ProcessedEvents                    handler → disputes.merchantName + processedEvents
```

### Проєкт `Platform.EventBus` (спільна бібліотека)

| Файл | Призначення |
|---|---|
| `IntegrationEvent.cs` | базовий незмінний `record`: `EventId`, `OccurredAt`, `CorrelationId`, `SchemaVersion` |
| `Contracts/BillingEvents.cs` | контракти подій, routing keys, черги споживачів (`BillingEventsTopology`) |
| `Topology.cs` | декларативна топологія: topic exchange, durable-черги, bindings, DLX (direct) + DLQ на кожну чергу |
| `RabbitMqEventPublisher.cs` | `IEventPublisher`: JSON (camelCase), persistent, `MessageId = EventId`, `Type`, заголовки `x-correlation-id`, `traceparent` |
| `EventConsumerService.cs` | `BackgroundService`: `BasicQos`, `AsyncEventingBasicConsumer`, `autoAck: false`, ack / nack / retry |
| `TracePropagation.cs` | Inject/Extract W3C `traceparent` через заголовки, Producer- і Consumer-span (`Platform.Messaging`) |
| `TopologyInitializer.cs` | видавець оголошує exchange і черги всіх споживачів при старті — події не губляться, поки споживач вимкнений |
| `RabbitMqHealthCheck.cs` | readiness брокера (`Degraded`, якщо недоступний) |
| `EventBusExtensions.cs` | `AddEventBus()` → `AddRabbitMQClient("messaging")` (Aspire.RabbitMQ.Client, RabbitMQ.Client 7.x) |

### Де що лежить у сервісах

| Сервіс | Шлях |
|---|---|
| Billing | `Billing.Bll/Events/BillingEventPublisher.cs` (публікація ПІСЛЯ `SaveChangesAsync`), `Billing.Api/IntegrationEvents/` (реєстрація видавця й топології), `Billing.Domain` — `Merchant.Rename`, `Invoice.AddSurcharge` |
| Payments | `Payments.Api/IntegrationEvents/` (`BillingEventHandlers.cs`, `IdempotentEventProcessor.cs`, реєстрація споживача), `Payments.Dal` — `ProcessedEventRepository`, `InvoiceRepository.UpsertSnapshotAsync / RenameMerchantAsync`, `db/p1/schema.sql` — таблиця `ProcessedEvents` |
| Disputes | `Disputes.WebApi/IntegrationEvents/` (`MerchantRenamedHandler.cs`, реєстрація споживача) |
| AppHost | `Platform.AppHost/AppHost.cs`: `AddRabbitMQ("messaging").WithDataVolume().WithManagementPlugin()` |

## Топологія

| Об'єкт | Параметри |
|---|---|
| exchange `billing-events` | topic, durable |
| exchange `billing-events.dlx` | direct, durable (dead letter exchange) |
| queue `payments.billing-sync` | durable, bindings `invoice.*`, `merchant.*`, `x-dead-letter-exchange = billing-events.dlx`, `x-dead-letter-routing-key = payments.billing-sync.dlq` |
| queue `disputes.billing-sync` | durable, binding `merchant.*`, DLX аналогічно |
| queue `*.dlq` | durable, binding до DLX за ключем = ім'я DLQ (в кожного споживача своя DLQ) |

Топологію оголошує **код під час старту** (видавець — усю, споживач — свою частину); повторне оголошення з тими самими
параметрами безпечне. Параметри наявної черги вручну не змінюйте — брокер відхилить наступне оголошення.

## Події

| Подія | Routing key | Коли | Поля |
|---|---|---|---|
| `InvoiceIssuedEvent` | `invoice.issued` | рахунок створено й випущено | `InvoiceId, Number, MerchantId, MerchantName, Amount, Currency` |
| `InvoiceAmountChangedEvent` | `invoice.amount_changed` | донарахування до рахунку | `InvoiceId, Number, MerchantId, MerchantName, OldAmount, NewAmount, Currency` |
| `MerchantRenamedEvent` | `merchant.renamed` | мерчанта перейменовано | `MerchantId, OldName, NewName` |

Усі події успадковують `IntegrationEvent` (`EventId`, `OccurredAt`, `CorrelationId`, `SchemaVersion`), незмінні (`record`) і самодостатні.

## Як дані потрапляють у копії (політика з Лаб. 01)

| Копія | Подія | Що відбувається | Що НЕ змінюється |
|---|---|---|---|
| `PaymentsDb.dbo.Invoices` (Number, MerchantName, Amount, Currency) | `InvoiceIssued`, `InvoiceAmountChanged` | upsert копії | — |
| `PaymentsDb.dbo.Invoices.MerchantName` | `MerchantRenamed` | `UPDATE … WHERE MerchantName = @OldName` | — |
| `disputes.merchantName` (усі спори мерчанта) | `MerchantRenamed` | `UpdateMany` за `merchantId` | — |
| `PaymentAllocations.InvoiceNumber / InvoiceAmount` | — | **не чіпаємо** | знімок на момент розподілу (аналог `OrderItems.UnitPrice`) |
| `disputes.amount / currency` | — | **не чіпаємо** | знімок на момент відкриття спору |

**Out-of-order.** Копія зберігає час останньої застосованої зміни: у Payments це `Invoices.UpdatedAt` (оновлення застосовується лише
якщо `UpdatedAt IS NULL OR UpdatedAt <= OccurredAt`), у Disputes — колекція `merchantNameVersions`. Запізніла подія не перезаписує новіші дані.

## Помилки: повтори, DLQ, ідемпотентність

| Ситуація | Рішення | Результат |
|---|---|---|
| Успіх | `BasicAckAsync` | повідомлення видалено з черги |
| Битий JSON, невідомий тип події/версія, порушення бізнес-правила (`PermanentEventException`) | `BasicNackAsync(requeue: false)` | → DLX → `*.dlq`, без повторів |
| Тимчасовий збій (БД недоступна, таймаут) | повідомлення **перепублікується** в ту саму чергу із заголовком `x-retry-count + 1`, оригінал підтверджується; пауза 0,5 с → 1 с → 2 с | після `MaxRetries = 3` → DLQ |
| Дублікат доставки (той самий `EventId`) | запис у `ProcessedEvents` порушує PK → обробку пропущено | дані не змінюються вдруге, у лог пишеться `Duplicate … skipped` |

*Чому повтор — це перепублікація, а не `requeue`:* повернене в чергу повідомлення приходить без змін, лічильник у заголовку збільшити
не можна. *Чому в чергу напряму (default exchange), а не в `billing-events`:* інакше повтор отримали б усі черги, прив'язані до exchange.

**Ідемпотентність.** Payments: в одній SQL-транзакції `INSERT dbo.ProcessedEvents(EventId)` → бізнес-зміна → `COMMIT`
(`IdempotentEventProcessor`). Disputes: у MongoDB контейнера немає replica set, тож транзакцій немає — порядок «перевірка
`processedEvents` → ідемпотентний `$set` → запис `EventId`», падіння посередині дає лише безпечний повторний `$set`.

## Трасування

`RabbitMqEventPublisher` створює Producer-span, його контекст (`traceparent`) іде в заголовки повідомлення; `EventConsumerService`
витягує контекст і запускає Consumer-span `payments.billing-sync process` з тегами `messaging.system`, `messaging.destination.name`,
`messaging.message.id`, `event.type`, `correlation.id`. У Dashboard (Traces) один trace містить:
`PUT /api/merchants/{id}/name → UPDATE Merchants → billing-events publish → payments.billing-sync process → UPDATE Invoices` і
паралельну гілку `disputes.billing-sync process → MongoDB update`. `ActivitySource "Platform.Messaging"` підписано в `Platform.ServiceDefaults`.

## Як перевірити (кроки)

1. `dotnet run --project Platform.AppHost` → у Dashboard з'являється ресурс **messaging** (RabbitMQ) із посиланням на Management UI
   (логін/пароль `guest` / `guest`).
2. Management UI → Exchanges: `billing-events`, `billing-events.dlx`; Queues: `payments.billing-sync`, `disputes.billing-sync` та їхні `.dlq`.
3. **Перейменування мерчанта** (через gateway, `http://localhost:5000`):
   ```
   PUT /api/merchants/1/name   {"name": "Нова назва мерчанта"}
   GET /api/invoices/{id}      → Payments: MerchantName у копії змінилась
   GET /api/disputes/{id}      → Disputes: merchantName змінилась
   ```
4. **Зміна суми:** `POST /api/billing/invoices/{id}/surcharges {"description":"Пеня","amount":50}` → у Payments `GET /api/invoices/{id}` показує нову `Amount`;
   `PaymentAllocations.InvoiceAmount` у раніше створених платежах не змінюється.
5. **Дублікат:** Management UI → Exchange `billing-events` → *Publish message* із routing key `merchant.renamed`, властивості
   `type=MerchantRenamedEvent`, `message_id=<guid>`, тіло з тим самим `eventId` двічі → у логу Payments/Disputes: `Duplicate MerchantRenamed … skipped`.
6. **DLQ:** *Publish message* у `billing-events` з routing key `merchant.renamed`, `type=MerchantRenamedEvent`, тіло `{ not json` → повідомлення в `payments.billing-sync.dlq` і `disputes.billing-sync.dlq`.
7. **Перезапуск споживача:** зупинити `payments-api` в Dashboard → кілька разів змінити назви/суми → запустити → усі події обробляються (черга накопичувала їх).
8. **Тимчасовий збій:** зупинити SQL Server під час обробки → у логах Payments `retry 1/3 … 2/3 … 3/3`, потім повідомлення в `payments.billing-sync.dlq`.

## Обмеження (свідомо)

- **Outbox не реалізовано** (за методичкою — за бажанням). Подія публікується після `SaveChangesAsync`; якщо застосунок упаде між ними або брокер
  недоступний, подію буде втрачено, а помилку записано в лог (`Failed to publish … the change is saved`). Повну гарантію дає Transactional Outbox.
- Публікація без publisher confirms (`mandatory: false`): достатньо для навчального стенду.
- Повтори блокують споживача на час паузи (prefetch 10); для виробництва — окрема retry-черга з TTL.
