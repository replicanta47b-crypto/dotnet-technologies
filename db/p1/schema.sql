/* =====================================================================
   Проєкт №1 — Контекст «Платежі» (SQL Server). База: PaymentsDb.
   Скрипт ідемпотентний: можна запускати повторно.
   Зв'язки всередині БД:
     1:N  Customers -> Payments
     1:1  Payments <-> PaymentDetails   (FK є водночас PK залежної таблиці)
     M:N  Payments <-> Invoices через PaymentAllocations (один платіж може
          закривати кілька рахунків, один рахунок — оплачуватися кількома платежами)
     1:N  Payments -> PaymentStatusHistory, Payments -> PaymentTransactions
   Посилання на Білінг: Invoices.Id та PaymentAllocations.InvoiceId — звичайні
   стовпці БЕЗ FOREIGN KEY на BillingDb (FK веде лише на локальну копію
   довідника dbo.Invoices у цій же БД).
   Ключі: BIGINT IDENTITY — послідовна вставка з одного джерела (БД).
   ===================================================================== */
IF DB_ID(N'PaymentsDb') IS NULL
    CREATE DATABASE PaymentsDb;
GO
USE PaymentsDb;
GO

/* ---------- Customers (платники) ---------- */
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
CREATE TABLE dbo.Customers (
    Id          BIGINT IDENTITY(1,1) CONSTRAINT PK_Customers PRIMARY KEY,
    Email       NVARCHAR(256) NOT NULL CONSTRAINT UQ_Customers_Email UNIQUE,
    FullName    NVARCHAR(200) NOT NULL,
    -- аудит, м'яке видалення, конкурентність (Крок 6)
    CreatedAt   DATETIME2     NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy   NVARCHAR(100) NULL,
    UpdatedAt   DATETIME2     NULL,
    UpdatedBy   NVARCHAR(100) NULL,
    IsDeleted   BIT           NOT NULL CONSTRAINT DF_Customers_IsDeleted DEFAULT 0,
    RowVersion  ROWVERSION
);
GO

/* ---------- Invoices: ЛОКАЛЬНА КОПІЯ довідника рахунків з Білінгу ----------
   Id збігається з Id у BillingDb (власник даних — Білінг).
   Number, MerchantName, Amount, Currency оновлюються подіями з Білінгу (Лаб. 08).
   Без IDENTITY: Id приходить ззовні. */
IF OBJECT_ID(N'dbo.Invoices', N'U') IS NULL
CREATE TABLE dbo.Invoices (
    Id           BIGINT        NOT NULL CONSTRAINT PK_Invoices PRIMARY KEY,
    Number       NVARCHAR(50)  NOT NULL,
    MerchantName NVARCHAR(200) NOT NULL,
    Amount       DECIMAL(18,2) NOT NULL CONSTRAINT CK_Invoices_Amount CHECK (Amount > 0),
    Currency     CHAR(3)       NOT NULL CONSTRAINT DF_Invoices_Currency DEFAULT 'UAH'
                 CONSTRAINT CK_Invoices_Currency CHECK (Currency IN ('UAH','USD','EUR')),
    SyncedAt     DATETIME2     NOT NULL CONSTRAINT DF_Invoices_SyncedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt    DATETIME2     NULL
);
GO

/* ---------- Payments (1:N від Customers) ---------- */
IF OBJECT_ID(N'dbo.Payments', N'U') IS NULL
CREATE TABLE dbo.Payments (
    Id          BIGINT IDENTITY(1,1) CONSTRAINT PK_Payments PRIMARY KEY,
    CustomerId  BIGINT        NOT NULL
                CONSTRAINT FK_Payments_Customers REFERENCES dbo.Customers(Id), -- без CASCADE: історія платежів клієнта не видаляється
    Currency    CHAR(3)       NOT NULL CONSTRAINT DF_Payments_Currency DEFAULT 'UAH'
                CONSTRAINT CK_Payments_Currency CHECK (Currency IN ('UAH','USD','EUR')),
    Status      NVARCHAR(30)  NOT NULL CONSTRAINT DF_Payments_Status DEFAULT 'Created'
                CONSTRAINT CK_Payments_Status CHECK (Status IN
                   ('Created','Validating','Authorized','Captured','Settled','Cancelled')),
    InitiatedAt DATETIME2     NOT NULL CONSTRAINT DF_Payments_InitiatedAt DEFAULT SYSUTCDATETIME(),
    TotalAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Payments_Total DEFAULT 0
                CONSTRAINT CK_Payments_Total CHECK (TotalAmount >= 0),
    CreatedAt   DATETIME2     NOT NULL CONSTRAINT DF_Payments_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy   NVARCHAR(100) NULL,
    UpdatedAt   DATETIME2     NULL,
    UpdatedBy   NVARCHAR(100) NULL,
    IsDeleted   BIT           NOT NULL CONSTRAINT DF_Payments_IsDeleted DEFAULT 0,
    RowVersion  ROWVERSION    -- оптимістична конкурентність: два оператори не затруть зміни один одного
);
GO

/* ---------- PaymentDetails (1:1 з Payments) ---------- */
IF OBJECT_ID(N'dbo.PaymentDetails', N'U') IS NULL
CREATE TABLE dbo.PaymentDetails (
    PaymentId    BIGINT         NOT NULL CONSTRAINT PK_PaymentDetails PRIMARY KEY
                 CONSTRAINT FK_PaymentDetails_Payments REFERENCES dbo.Payments(Id) ON DELETE CASCADE, -- деталі не існують без платежу
    Method       NVARCHAR(20)   NOT NULL CONSTRAINT CK_PaymentDetails_Method CHECK (Method IN ('Card','BankTransfer','Wallet')),
    PayerAccount NVARCHAR(50)   NOT NULL,   -- маскований номер картки / рахунку
    Description  NVARCHAR(500)  NULL
);
GO

/* ---------- PaymentAllocations (M:N Payments <-> Invoices) ---------- */
IF OBJECT_ID(N'dbo.PaymentAllocations', N'U') IS NULL
CREATE TABLE dbo.PaymentAllocations (
    PaymentId       BIGINT        NOT NULL
                    CONSTRAINT FK_PA_Payments REFERENCES dbo.Payments(Id) ON DELETE CASCADE, -- розподіл — частина агрегату Payment
    InvoiceId       BIGINT        NOT NULL
                    CONSTRAINT FK_PA_Invoices REFERENCES dbo.Invoices(Id),                    -- локальний FK, не на BillingDb
    -- ЗНІМОК на момент створення платежу: НЕ оновлюється подіями з Білінгу
    InvoiceNumber   NVARCHAR(50)  NOT NULL,
    InvoiceAmount   DECIMAL(18,2) NOT NULL CONSTRAINT CK_PA_InvoiceAmount CHECK (InvoiceAmount > 0),
    AllocatedAmount DECIMAL(18,2) NOT NULL CONSTRAINT CK_PA_Allocated CHECK (AllocatedAmount > 0),  -- власне поле зв'язку
    CONSTRAINT PK_PaymentAllocations PRIMARY KEY (PaymentId, InvoiceId),
    CONSTRAINT CK_PA_AllocatedLEInvoice CHECK (AllocatedAmount <= InvoiceAmount)
);
GO

/* ---------- PaymentStatusHistory (1:N від Payments) ---------- */
IF OBJECT_ID(N'dbo.PaymentStatusHistory', N'U') IS NULL
CREATE TABLE dbo.PaymentStatusHistory (
    Id         BIGINT IDENTITY(1,1) CONSTRAINT PK_PaymentStatusHistory PRIMARY KEY,
    PaymentId  BIGINT        NOT NULL
               CONSTRAINT FK_PSH_Payments REFERENCES dbo.Payments(Id) ON DELETE CASCADE,
    OldStatus  NVARCHAR(30)  NULL,
    NewStatus  NVARCHAR(30)  NOT NULL,
    ChangedAt  DATETIME2     NOT NULL CONSTRAINT DF_PSH_ChangedAt DEFAULT SYSUTCDATETIME(),
    ChangedBy  NVARCHAR(100) NULL,
    Note       NVARCHAR(500) NULL
);
GO

/* ---------- PaymentTransactions (1:N від Payments): операції з платіжним провайдером ---------- */
IF OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NULL
CREATE TABLE dbo.PaymentTransactions (
    Id          BIGINT IDENTITY(1,1) CONSTRAINT PK_PaymentTransactions PRIMARY KEY,
    PaymentId   BIGINT        NOT NULL
                CONSTRAINT FK_PT_Payments REFERENCES dbo.Payments(Id), -- без CASCADE: фінансовий слід
    Type        NVARCHAR(20)  NOT NULL CONSTRAINT CK_PT_Type CHECK (Type IN ('Authorization','Capture')),
    Amount      DECIMAL(18,2) NOT NULL CONSTRAINT CK_PT_Amount CHECK (Amount > 0),
    ProviderRef NVARCHAR(100) NULL,
    ProcessedAt DATETIME2     NOT NULL CONSTRAINT DF_PT_ProcessedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy   NVARCHAR(100) NULL
);
GO

/* =====================================================================
   ІНДЕКСИ — кожен під конкретний запит
   ===================================================================== */
-- Запит: «платежі клієнта з певним статусом» (історія платежів, фільтр за статусом)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Payments_CustomerId_Status' AND object_id = OBJECT_ID(N'dbo.Payments'))
    CREATE INDEX IX_Payments_CustomerId_Status ON dbo.Payments (CustomerId, Status);
GO
-- Запит: «активні (не видалені) платежі клієнта, найновіші спочатку»; фільтрований, покриває Status і TotalAmount
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Payments_Active' AND object_id = OBJECT_ID(N'dbo.Payments'))
    CREATE INDEX IX_Payments_Active ON dbo.Payments (CustomerId, InitiatedAt DESC)
        INCLUDE (Status, TotalAmount) WHERE IsDeleted = 0;
GO
-- Запит: «якими платежами оплачується цей рахунок» (зворотний бік M:N)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PA_InvoiceId' AND object_id = OBJECT_ID(N'dbo.PaymentAllocations'))
    CREATE INDEX IX_PA_InvoiceId ON dbo.PaymentAllocations (InvoiceId);
GO
-- Запит: «історія статусів платежу в хронологічному порядку»
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PSH_PaymentId_ChangedAt' AND object_id = OBJECT_ID(N'dbo.PaymentStatusHistory'))
    CREATE INDEX IX_PSH_PaymentId_ChangedAt ON dbo.PaymentStatusHistory (PaymentId, ChangedAt);
GO
-- Запит: «операції платежу» та перевірка наявності авторизації перед статусом Authorized
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PT_PaymentId_Type' AND object_id = OBJECT_ID(N'dbo.PaymentTransactions'))
    CREATE INDEX IX_PT_PaymentId_Type ON dbo.PaymentTransactions (PaymentId, Type);
GO
-- Запит: захист від повторної обробки операції за референсом провайдера (унікальний, лише коли референс заданий)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PT_ProviderRef' AND object_id = OBJECT_ID(N'dbo.PaymentTransactions'))
    CREATE UNIQUE INDEX UX_PT_ProviderRef ON dbo.PaymentTransactions (ProviderRef) WHERE ProviderRef IS NOT NULL;
GO
-- Запит: «пошук платника за поштою» покриває UNIQUE-обмеження UQ_Customers_Email (окремий індекс не потрібен)
