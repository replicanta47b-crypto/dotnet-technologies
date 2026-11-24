# ERD — Проєкт №2 (BillingDb)

Діаграма Mermaid (GitHub / VS Code). Для здачі зображенням: експорт з [mermaid.live](https://mermaid.live) у PNG/SVG у `docs/` або відтворення в draw.io.

```mermaid
erDiagram
    Merchants ||--o{ Invoices : "1:N  issues"
    Invoices ||--|| InvoiceDetails : "1:1  has"
    Invoices ||--o{ InvoiceLines : "1:N  consists of"
    Invoices ||--o{ InvoiceCategories : "M:N via"
    ServiceCategories ||--o{ InvoiceCategories : "M:N via"

    Merchants {
        bigint Id PK
        nvarchar Name UK
        nvarchar Country
        nvarchar ContactEmail
        bit IsActive
    }
    ServiceCategories {
        bigint Id PK
        nvarchar Name
        nvarchar Slug UK
    }
    Invoices {
        bigint Id PK
        nvarchar Number UK
        bigint MerchantId FK
        char Currency "CHECK UAH/USD/EUR"
        decimal TotalAmount "CHECK > 0"
        date DueDate
        nvarchar Status "CHECK Draft/Issued/Paid/Overdue/Void"
        datetime2 CreatedAt
        nvarchar CreatedBy
        datetime2 UpdatedAt
        nvarchar UpdatedBy
        bit IsDeleted
        rowversion RowVersion
    }
    InvoiceDetails {
        bigint InvoiceId PK, FK "1:1"
        nvarchar CustomerRef
        date PeriodStart
        date PeriodEnd "CHECK >= PeriodStart"
        nvarchar Note
    }
    InvoiceLines {
        bigint Id PK
        bigint InvoiceId FK
        nvarchar Description
        decimal Quantity "CHECK > 0"
        decimal UnitPrice "CHECK >= 0"
        decimal LineTotal "computed"
        int SortOrder
    }
    InvoiceCategories {
        bigint InvoiceId PK, FK
        bigint CategoryId PK, FK
        datetime2 AssignedAt "own field"
        bit IsPrimary "own field"
    }
```

## Типи зв'язків

| Зв'язок | Тип | Де FK |
|---|---|---|
| Merchants → Invoices | 1:N | `Invoices.MerchantId` |
| Invoices ↔ InvoiceDetails | 1:1 | `InvoiceDetails.InvoiceId` (є також PK) |
| Invoices → InvoiceLines | 1:N | `InvoiceLines.InvoiceId` |
| Invoices ↔ ServiceCategories | M:N | проміжна `InvoiceCategories` (PK `InvoiceId, CategoryId`, власні поля `AssignedAt`, `IsPrimary`) |

## Унікальність, перевірки, індекси

- UNIQUE: `Merchants.Name`, `ServiceCategories.Slug`, `Invoices.Number`; відфільтрований унікальний індекс `UX_IC_Primary` (не більше однієї основної категорії на рахунок).
- CHECK: `TotalAmount > 0`, `Quantity > 0`, `UnitPrice >= 0`, `Currency IN (UAH, USD, EUR)`, `Status IN (...)`, `PeriodEnd >= PeriodStart`.
- Індекси під запити — у коментарях `db/p2/schema.sql`.
- Міграції: схема готова до Code First (Лаб. 03): сутності й Fluent API відтворюють цей DDL, а зміни проходять через `dotnet ef migrations`.
