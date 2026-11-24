# Опис предметної області та межі контекстів

## Домен

Платіжна платформа для оплати рахунків. Платники сплачують рахунки мерчантів (комунальні послуги, зв'язок, інтернет) карткою, банківським переказом або з гаманця; мерчанти виставляють рахунки; оператори платформи супроводжують платежі (перевірка, авторизація, списання, розрахунок) і розглядають спори. Система розбита на три автономні сервіси, кожен із власною БД, щоб їх можна було розробляти й масштабувати незалежно.

## Три контексти (Bounded Contexts)

| # | Контекст | БД | Технологія | Власні сутності (власник даних) |
|---|---|---|---|---|
| 1 | **Платежі** | `PaymentsDb` | SQL Server (ADO.NET + Dapper) | Customers, Payments, PaymentDetails, PaymentAllocations, PaymentStatusHistory, PaymentTransactions; Invoices (лише локальна копія довідника) |
| 2 | **Білінг** | `BillingDb` | SQL Server (EF Core, Code First) | Merchants, ServiceCategories, Invoices, InvoiceDetails, InvoiceLines, InvoiceCategories |
| 3 | **Спори** | `disputes_db` | MongoDB | disputes, disputeThreads, threadMessages, merchantDisputeStats |

Кожна сутність належить рівно одному контексту. Інші контексти знають про неї лише за ідентифікатором (`invoiceId`, `merchantId`, `paymentId`, `userId`) і, за потреби, зберігають дубльовані поля. **Між базами немає жодного FOREIGN KEY.**

## Агрегати та життєвий цикл

| Контекст | Агрегат (корінь + частини) | Життєвий цикл |
|---|---|---|
| Платежі | `Payment` + `PaymentDetails` (1:1) + `PaymentAllocations` + `PaymentStatusHistory` | Розподіл і деталі не існують без платежу (каскадне видалення). Статус змінюється лише процедурою `usp_ChangePaymentStatus`. Видалення — м'яке. |
| Платежі | `Customer` | Не видаляється каскадом (є історія платежів); м'яке видалення. |
| Платежі | `PaymentTransaction` | Фінансовий слід: без каскаду, без видалення. |
| Білінг | `Invoice` + `InvoiceDetails` (1:1) + `InvoiceLines` + `InvoiceCategories` | Деталі, рядки та зв'язки з категоріями живуть разом із рахунком. |
| Білінг | `Merchant` | Не видаляється, якщо є рахунки (без каскаду). |
| Спори | `Dispute` (+ вбудовані `openedBy`, `evidence[]`, `comments[]`) | Докази й коментарі вбудовані; довге обговорення винесено в `disputeThreads` + `threadMessages` (reference). |
| Спори | `MerchantDisputeStats` | Похідний підсумок за спорами мерчанта (читання без агрегації щоразу). |

## Моделювання зв'язків

- **SQL (обидві БД):** 1:1 — FK є водночас PK залежної таблиці; 1:N — FK на боці «багатьох»; M:N — проміжна таблиця з двома FK та власними полями (`PaymentAllocations.AllocatedAmount`; `InvoiceCategories.AssignedAt`, `IsPrimary`).
- **MongoDB:** embedding — `openedBy`, `evidence[]`, `comments[]`; referencing — `threadId`, `threadMessages.threadId`, `paymentId`; гібрид — спір вбудовує кілька коментарів і посилається на повне обговорення.

## Статуси платежу

`Created → Validating → Authorized → Captured → Settled`; `Cancelled` — лише до списання коштів (з `Created`, `Validating`, `Authorized`). «Списати» (`Captured`) платіж, який не пройшов авторизацію, неможливо — процедура завершується помилкою 50002.

## Політика дублювання даних і узгодженості

| Поле | Де зберігається | Джерело (власник даних) | Політика узгодженості |
|---|---|---|---|
| `PaymentAllocations.InvoiceNumber`, `PaymentAllocations.InvoiceAmount` | PaymentsDb | Invoice (BillingDb) | **Знімок** на момент створення розподілу. Береться з локальної копії при `usp_AddPaymentAllocation`; не оновлюється ніколи. |
| `Invoices.Number`, `Invoices.MerchantName`, `Invoices.Amount`, `Invoices.Currency` (локальна копія довідника) | PaymentsDb | Invoice / Merchant (BillingDb) | **Копія для читання**, оновлюється подіями з Білінгу (eventual consistency, Лаб. 08). Змінюється лише обробником подій. |
| `Invoices.Id` / `PaymentAllocations.InvoiceId` | PaymentsDb | Invoice (BillingDb) | Той самий Id, що в Білінгу; звичайний стовпець без FK на BillingDb. |
| `disputes.paymentId`, `disputeThreads` через `disputeId` | disputes_db | Payment (PaymentsDb) | Лише ідентифікатор, без FK; існування перевіряється прикладним кодом. |
| `disputes.invoiceId`, `disputes.merchantId` | disputes_db | Invoice / Merchant (BillingDb) | Лише ідентифікатор, без FK. |
| `disputes.merchantName`, `merchantDisputeStats.merchantName` | disputes_db | Merchant (BillingDb) | **Копія для читання**, оновлюється подіями з Білінгу (зміна назви — масове оновлення за `merchantId`). |
| `disputes.amount`, `disputes.currency` | disputes_db | PaymentAllocation (PaymentsDb) | **Знімок** на момент відкриття спору, не оновлюється. |
| `disputes.openedBy.userId`, `openedBy.displayName`, `threadMessages.author.*` | disputes_db | Customer (PaymentsDb) | `userId` — ідентифікатор (`u-<Customers.Id>`); `displayName` — **знімок** на момент публікації, не оновлюється. |

Правило проти dual write: кожне дубльоване поле має єдиного власника (колонка «Джерело»); сервіси-споживачі **не редагують** копії самостійно — лише знімок або подія від власника.

## Типи ключів

`BIGINT IDENTITY` у SQL-базах: усі ключі генерує єдине джерело (сама БД), вставка послідовна, ключ компактний. У `Invoices` локальної копії (PaymentsDb) ключ без IDENTITY, бо Id приходить із Білінгу. У MongoDB — `ObjectId` (генерується клієнтом, без центрального лічильника); у `merchantDisputeStats` `_id` — це `merchantId` з Білінгу.
