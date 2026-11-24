# Лаб. 01 — Проєктування баз даних для мікросервісної архітектури

Домен: **платіжна платформа для оплати рахунків**. Платники сплачують рахунки мерчантів (комунальні послуги, зв'язок, інтернет); мерчанти виставляють рахунки; оператори супроводжують платежі (перевірка, авторизація, списання, розрахунок) і розглядають спори. Система розбита на три автономні сервіси, кожен із власною БД.

## Три контексти (Bounded Contexts)

| # | Контекст | БД | Технологія | Сутності |
|---|---|---|---|---|
| 1 | Платежі | `PaymentsDb` | SQL Server (ADO.NET + Dapper) | Customers, Payments, PaymentDetails, PaymentAllocations, PaymentStatusHistory, PaymentTransactions, Invoices (локальна копія довідника) |
| 2 | Білінг | `BillingDb` | SQL Server (EF Core) | Merchants, ServiceCategories, Invoices, InvoiceDetails, InvoiceLines, InvoiceCategories |
| 3 | Спори | `disputes_db` | MongoDB | disputes, disputeThreads, threadMessages, merchantDisputeStats |

Кожна сутність належить рівно одному контексту. Між базами немає жодного FOREIGN KEY — лише логічні посилання за Id. Деталі: [docs/contexts.md](docs/contexts.md).

## Структура репозиторію

```
README.md
docs/
  contexts.md        опис домену, контексти, агрегати, політика дублювання
  erd-p1.md          ERD Проєкту №1 (Mermaid) + діаграма статусів
  erd-p2.md          ERD Проєкту №2 (Mermaid)
  mongo-examples.md  приклади документів, запити, aggregation
db/
  p1/  schema.sql · procedures.sql · seed.sql · checks.sql
  p2/  schema.sql · seed.sql
  p3/  collections.js · seed.js
```

## Вимоги

SQL Server 2022 (Developer/Express), SSMS (або `sqlcmd`), MongoDB Community Server, `mongosh`. У командах і SSMS замініть `localhost` на назву свого екземпляра SQL Server, якщо він іменований (наприклад, `localhost\MSSQLSERVER01`; для `sqlcmd` тоді замість `-U sa -P ...` можна використати `-E`). Обидві СУБД можна підняти в Docker:

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourStrong!Passw0rd" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
docker run -d -p 27017:27017 --name mongo mongo:7
```

## Розгортання з нуля

Усі скрипти **ідемпотентні**: їх можна запускати повторно, дублікатів не буде. У SSMS кожен файл відкривається й виконується цілком (F5).

### Проєкт №1 — PaymentsDb

1. Виконати `db/p1/schema.sql` (створює БД `PaymentsDb`, таблиці, обмеження та індекси).
2. Виконати `db/p1/procedures.sql` (6 збережуваних процедур).
3. Виконати `db/p1/seed.sql` (платники, довідник рахунків, 4 демо-платежі).
4. За бажанням виконати `db/p1/checks.sql` (запити перевірки).

Через `sqlcmd` (прапорець `-I` обов'язковий: фільтровані індекси вимагають QUOTED_IDENTIFIER ON; у SSMS він увімкнений за замовчуванням):

```bash
sqlcmd -S localhost -U sa -P "YourStrong!Passw0rd" -C -I -i db/p1/schema.sql
sqlcmd -S localhost -U sa -P "YourStrong!Passw0rd" -C -I -i db/p1/procedures.sql
sqlcmd -S localhost -U sa -P "YourStrong!Passw0rd" -C -I -i db/p1/seed.sql
```

### Проєкт №2 — BillingDb

1. Виконати `db/p2/schema.sql` (створює БД `BillingDb`).
2. Виконати `db/p2/seed.sql` (3 мерчанти, 5 категорій, 6 рахунків з деталями, рядками та категоріями).

Id рахунків 1–6 збігаються з `Invoices.Id` у `PaymentsDb` і `invoiceId` у MongoDB.

### Проєкт №3 — MongoDB (`disputes_db`)

```bash
mongosh "mongodb://localhost:27017" db/p3/collections.js
mongosh "mongodb://localhost:27017" db/p3/seed.js
```

`collections.js` створює колекції з JSON Schema валідацією та індексами, `seed.js` додає документи (upsert за фіксованими `_id`).

## Збережувані процедури (Проєкт №1)

| Процедура | Призначення |
|---|---|
| `usp_CreatePayment` | створення платежу + деталі (1:1) + запис історії |
| `usp_AddPaymentAllocation` | додавання розподілу на рахунок; фіксує **знімок** `InvoiceNumber`, `InvoiceAmount`; перераховує суму |
| `usp_GetPaymentWithAllocations` | читання платежу, розподілу, історії, операцій |
| `usp_ChangePaymentStatus` | **транзакційна зміна статусу** з перевіркою допустимого переходу та `RowVersion` |
| `usp_AuthorizePayment` | авторизація: запис операції + перехід у `Authorized` в одній транзакції |
| `usp_SoftDeletePayment` | м'яке видалення (лише `Created`/`Cancelled`) |

Допустимі переходи: `Created → Validating → Authorized → Captured → Settled`; `Cancelled` — лише до списання коштів. Недопустимий перехід завершується керованою помилкою `THROW 50002`.

Приклад:

```sql
EXEC dbo.usp_ChangePaymentStatus @PaymentId = 2, @NewStatus = 'Captured';
-- Msg 50002: Invalid status transition
```

## Ключі, обмеження, індекси

- Ключі: `BIGINT IDENTITY` (одне джерело генерації — БД); у локальній копії `Invoices` (PaymentsDb) Id приходить із Білінгу; у MongoDB — `ObjectId`.
- Обмеження: PK, FK, UNIQUE, CHECK, DEFAULT; каскад лише для частин агрегату (розподіл, деталі, історія, рядки рахунку), не для платників, мерчантів і фінансових операцій.
- Аудит: `CreatedAt/CreatedBy/UpdatedAt/UpdatedBy/IsDeleted` + `RowVersion` (оптимістична конкурентність) для `Customers`, `Payments` (PaymentsDb) та `Invoices` (BillingDb).
- Кожен індекс має коментар із запитом, під який створений (`db/p1/schema.sql`, `db/p2/schema.sql`, `db/p3/collections.js`).

## ERD

- Проєкт №1 (PaymentsDb): [docs/erd-p1.md](docs/erd-p1.md), зображення — [docs/erd-p1.png](docs/erd-p1.png)
- Проєкт №2 (BillingDb): [docs/erd-p2.md](docs/erd-p2.md), зображення — [docs/erd-p2.png](docs/erd-p2.png)

![ERD PaymentsDb](docs/erd-p1.png)

![ERD BillingDb](docs/erd-p2.png)

## Політика дублювання даних

| Поле | Де зберігається | Джерело (власник даних) | Політика узгодженості |
|---|---|---|---|
| `PaymentAllocations.InvoiceNumber`, `PaymentAllocations.InvoiceAmount` | PaymentsDb | Invoice (BillingDb) | Знімок на момент створення розподілу — не оновлюється |
| `Invoices.Number`, `MerchantName`, `Amount`, `Currency` (локальна копія довідника) | PaymentsDb | Invoice / Merchant (BillingDb) | Копія для читання, оновлюється подіями з Білінгу |
| `Invoices.Id` / `PaymentAllocations.InvoiceId` | PaymentsDb | Invoice (BillingDb) | Той самий Id, звичайний стовпець без FK на BillingDb |
| `disputes.paymentId` | disputes_db | Payment (PaymentsDb) | Лише ідентифікатор, без FK; перевіряється прикладним кодом |
| `disputes.invoiceId`, `disputes.merchantId` | disputes_db | Invoice / Merchant (BillingDb) | Лише ідентифікатор, без FK |
| `disputes.merchantName`, `merchantDisputeStats.merchantName` | disputes_db | Merchant (BillingDb) | Копія для читання, оновлюється подіями з Білінгу |
| `disputes.amount`, `disputes.currency` | disputes_db | PaymentAllocation (PaymentsDb) | Знімок на момент відкриття спору, не оновлюється |
| `disputes.openedBy.userId`, `openedBy.displayName`, `threadMessages.author.*` | disputes_db | Customer (PaymentsDb) | `userId` — ідентифікатор; `displayName` — знімок на момент публікації |

Кожне дубльоване поле має єдиного власника; споживачі не редагують копії самостійно (немає dual write).

## Самоперевірка перед здачею

1. У `PaymentsDb` і `BillingDb`: `SELECT * FROM sys.foreign_keys;` — усі `referenced` таблиці в межах тієї ж БД.
2. Виклик `usp_ChangePaymentStatus @PaymentId = 2, @NewStatus = 'Captured'` повертає помилку 50002 і не змінює статус.
3. `db.disputes.find()` у `disputes_db` повертає документи з різними наборами полів (`evidence`, `fraudDetails`, `serviceProof`, `tags`).
4. Повторний запуск `seed.sql` / `seed.js` не збільшує кількість рядків і документів.
