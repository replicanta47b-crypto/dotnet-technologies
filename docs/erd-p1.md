# ERD — Проєкт №1 (PaymentsDb)

Діаграма Mermaid (відображається на GitHub і в VS Code з розширенням Mermaid). Для здачі зображенням: відкрийте у [mermaid.live](https://mermaid.live), експортуйте PNG/SVG у `docs/` або відтворіть у draw.io.

```mermaid
erDiagram
    Customers ||--o{ Payments : "1:N  makes"
    Payments ||--|| PaymentDetails : "1:1  has"
    Payments ||--o{ PaymentAllocations : "1:N  allocates"
    Invoices ||--o{ PaymentAllocations : "1:N  paid by"
    Payments ||--o{ PaymentStatusHistory : "1:N  logs"
    Payments ||--o{ PaymentTransactions : "1:N  processed by"

    Customers {
        bigint Id PK
        nvarchar Email UK
        nvarchar FullName
        datetime2 CreatedAt
        nvarchar CreatedBy
        datetime2 UpdatedAt
        nvarchar UpdatedBy
        bit IsDeleted
        rowversion RowVersion
    }
    Payments {
        bigint Id PK
        bigint CustomerId FK
        char Currency "CHECK UAH/USD/EUR"
        nvarchar Status "CHECK 6 statuses"
        datetime2 InitiatedAt
        decimal TotalAmount "CHECK >= 0"
        datetime2 CreatedAt
        nvarchar CreatedBy
        datetime2 UpdatedAt
        nvarchar UpdatedBy
        bit IsDeleted
        rowversion RowVersion
    }
    PaymentDetails {
        bigint PaymentId PK, FK "1:1"
        nvarchar Method "CHECK Card/BankTransfer/Wallet"
        nvarchar PayerAccount
        nvarchar Description
    }
    Invoices {
        bigint Id PK "copy of BillingDb.Invoices.Id, no IDENTITY"
        nvarchar Number "copy via events"
        nvarchar MerchantName "copy via events"
        decimal Amount "CHECK > 0, copy via events"
        char Currency "copy via events"
        datetime2 SyncedAt
    }
    PaymentAllocations {
        bigint PaymentId PK, FK
        bigint InvoiceId PK, FK
        nvarchar InvoiceNumber "SNAPSHOT"
        decimal InvoiceAmount "SNAPSHOT, CHECK > 0"
        decimal AllocatedAmount "own field, CHECK <= InvoiceAmount"
    }
    PaymentStatusHistory {
        bigint Id PK
        bigint PaymentId FK
        nvarchar OldStatus
        nvarchar NewStatus
        datetime2 ChangedAt
        nvarchar ChangedBy
        nvarchar Note
    }
    PaymentTransactions {
        bigint Id PK
        bigint PaymentId FK
        nvarchar Type "CHECK Authorization/Capture"
        decimal Amount "CHECK > 0"
        nvarchar ProviderRef "filtered UNIQUE"
        datetime2 ProcessedAt
    }
```

## Типи зв'язків

| Зв'язок | Тип | Де FK |
|---|---|---|
| Customers → Payments | 1:N | `Payments.CustomerId` |
| Payments ↔ PaymentDetails | 1:1 | `PaymentDetails.PaymentId` (є також PK) |
| Payments ↔ Invoices | M:N | проміжна `PaymentAllocations` (PK `PaymentId, InvoiceId`, власне поле `AllocatedAmount`) |
| Payments → PaymentStatusHistory | 1:N | `PaymentStatusHistory.PaymentId` |
| Payments → PaymentTransactions | 1:N | `PaymentTransactions.PaymentId` |

`InvoiceId` у `PaymentAllocations`/`Invoices` — логічне посилання на Білінг: FK веде лише на **локальну** `dbo.Invoices`, а не на BillingDb.

## Діаграма переходів статусів

```mermaid
stateDiagram-v2
    [*] --> Created
    Created --> Validating
    Validating --> Authorized
    Authorized --> Captured
    Captured --> Settled
    Created --> Cancelled
    Validating --> Cancelled
    Authorized --> Cancelled
    Settled --> [*]
    Cancelled --> [*]
```
