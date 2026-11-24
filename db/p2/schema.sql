/* =====================================================================
   Проєкт №2 — Контекст «Білінг» (SQL Server). База: BillingDb.
   Ідемпотентний скрипт. Реалізація в EF Core (Code First) — у Лаб. 03;
   цей DDL — еталон схеми, з якого генеруються сутності/міграції.
   Зв'язки:
     1:N  Merchants -> Invoices
     1:1  Invoices <-> InvoiceDetails   (FK = PK залежної таблиці)
     1:N  Invoices -> InvoiceLines
     M:N  Invoices <-> ServiceCategories через InvoiceCategories
          (власні поля AssignedAt, IsPrimary)
   Ключі: BIGINT IDENTITY (одне джерело генерації — БД білінгу).
   Власник даних Invoice/Merchant: тут. Інші сервіси знають лише Invoices.Id.
   ===================================================================== */
IF DB_ID(N'BillingDb') IS NULL
    CREATE DATABASE BillingDb;
GO
USE BillingDb;
GO

IF OBJECT_ID(N'dbo.Merchants', N'U') IS NULL
CREATE TABLE dbo.Merchants (
    Id           BIGINT IDENTITY(1,1) CONSTRAINT PK_Merchants PRIMARY KEY,
    Name         NVARCHAR(200) NOT NULL CONSTRAINT UQ_Merchants_Name UNIQUE,
    Country      NVARCHAR(100) NULL,
    ContactEmail NVARCHAR(256) NULL,
    IsActive     BIT           NOT NULL CONSTRAINT DF_Merchants_IsActive DEFAULT 1
);
GO

IF OBJECT_ID(N'dbo.ServiceCategories', N'U') IS NULL
CREATE TABLE dbo.ServiceCategories (
    Id   BIGINT IDENTITY(1,1) CONSTRAINT PK_ServiceCategories PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    Slug NVARCHAR(100) NOT NULL CONSTRAINT UQ_ServiceCategories_Slug UNIQUE
);
GO

IF OBJECT_ID(N'dbo.Invoices', N'U') IS NULL
CREATE TABLE dbo.Invoices (
    Id          BIGINT IDENTITY(1,1) CONSTRAINT PK_Invoices PRIMARY KEY,
    Number      NVARCHAR(50)  NOT NULL CONSTRAINT UQ_Invoices_Number UNIQUE,
    MerchantId  BIGINT        NOT NULL
                CONSTRAINT FK_Invoices_Merchants REFERENCES dbo.Merchants(Id), -- без CASCADE: мерчант з рахунками не видаляється
    Currency    CHAR(3)       NOT NULL CONSTRAINT DF_Invoices_Currency DEFAULT 'UAH'
                CONSTRAINT CK_Invoices_Currency CHECK (Currency IN ('UAH','USD','EUR')),
    TotalAmount DECIMAL(18,2) NOT NULL CONSTRAINT CK_Invoices_Total CHECK (TotalAmount > 0),
    DueDate     DATE          NOT NULL,
    Status      NVARCHAR(20)  NOT NULL CONSTRAINT DF_Invoices_Status DEFAULT 'Issued'
                CONSTRAINT CK_Invoices_Status CHECK (Status IN ('Draft','Issued','Paid','Overdue','Void')),
    CreatedAt   DATETIME2     NOT NULL CONSTRAINT DF_Invoices_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy   NVARCHAR(100) NULL,
    UpdatedAt   DATETIME2     NULL,
    UpdatedBy   NVARCHAR(100) NULL,
    IsDeleted   BIT           NOT NULL CONSTRAINT DF_Invoices_IsDeleted DEFAULT 0,
    RowVersion  ROWVERSION     -- два редактори рахунку не затруть зміни один одного
);
GO

IF OBJECT_ID(N'dbo.InvoiceDetails', N'U') IS NULL
CREATE TABLE dbo.InvoiceDetails (
    InvoiceId   BIGINT NOT NULL CONSTRAINT PK_InvoiceDetails PRIMARY KEY
                CONSTRAINT FK_InvoiceDetails_Invoices REFERENCES dbo.Invoices(Id) ON DELETE CASCADE,
    CustomerRef NVARCHAR(100)  NULL,   -- номер особового рахунку платника у мерчанта
    PeriodStart DATE           NULL,
    PeriodEnd   DATE           NULL,
    Note        NVARCHAR(1000) NULL,
    CONSTRAINT CK_InvoiceDetails_Period CHECK (PeriodStart IS NULL OR PeriodEnd IS NULL OR PeriodEnd >= PeriodStart)
);
GO

IF OBJECT_ID(N'dbo.InvoiceLines', N'U') IS NULL
CREATE TABLE dbo.InvoiceLines (
    Id          BIGINT IDENTITY(1,1) CONSTRAINT PK_InvoiceLines PRIMARY KEY,
    InvoiceId   BIGINT NOT NULL
                CONSTRAINT FK_InvoiceLines_Invoices REFERENCES dbo.Invoices(Id) ON DELETE CASCADE,
    Description NVARCHAR(300) NOT NULL,
    Quantity    DECIMAL(10,2) NOT NULL CONSTRAINT CK_InvoiceLines_Qty CHECK (Quantity > 0),
    UnitPrice   DECIMAL(18,2) NOT NULL CONSTRAINT CK_InvoiceLines_Price CHECK (UnitPrice >= 0),
    LineTotal   AS (Quantity * UnitPrice) PERSISTED,
    SortOrder   INT NOT NULL CONSTRAINT DF_InvoiceLines_Sort DEFAULT 0
);
GO

IF OBJECT_ID(N'dbo.InvoiceCategories', N'U') IS NULL
CREATE TABLE dbo.InvoiceCategories (
    InvoiceId  BIGINT NOT NULL
               CONSTRAINT FK_IC_Invoices   REFERENCES dbo.Invoices(Id)          ON DELETE CASCADE,
    CategoryId BIGINT NOT NULL
               CONSTRAINT FK_IC_Categories REFERENCES dbo.ServiceCategories(Id) ON DELETE CASCADE,
    AssignedAt DATETIME2 NOT NULL CONSTRAINT DF_IC_AssignedAt DEFAULT SYSUTCDATETIME(),  -- власне поле зв'язку
    IsPrimary  BIT       NOT NULL CONSTRAINT DF_IC_IsPrimary DEFAULT 0,                   -- власне поле зв'язку
    CONSTRAINT PK_InvoiceCategories PRIMARY KEY (InvoiceId, CategoryId)
);
GO

/* ===================== ІНДЕКСИ (кожен під запит) ===================== */
-- Запит: «рахунки мерчанта за статусом» (кабінет мерчанта); покриває DueDate і TotalAmount
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Invoices_MerchantId_Status' AND object_id = OBJECT_ID(N'dbo.Invoices'))
    CREATE INDEX IX_Invoices_MerchantId_Status ON dbo.Invoices (MerchantId, Status) INCLUDE (DueDate, TotalAmount);
GO
-- Запит: «рахунки, які треба сплатити до певної дати» (нагадування, прострочені); лише активні записи
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Invoices_Status_DueDate' AND object_id = OBJECT_ID(N'dbo.Invoices'))
    CREATE INDEX IX_Invoices_Status_DueDate ON dbo.Invoices (Status, DueDate) INCLUDE (TotalAmount, Currency) WHERE IsDeleted = 0;
GO
-- Запит: «рядки рахунку за порядком»
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_InvoiceLines_Invoice_Sort' AND object_id = OBJECT_ID(N'dbo.InvoiceLines'))
    CREATE INDEX IX_InvoiceLines_Invoice_Sort ON dbo.InvoiceLines (InvoiceId, SortOrder);
GO
-- Запит: «рахунки категорії» (зворотний бік M:N; PK вже покриває напрям Invoice -> Categories)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_IC_CategoryId' AND object_id = OBJECT_ID(N'dbo.InvoiceCategories'))
    CREATE INDEX IX_IC_CategoryId ON dbo.InvoiceCategories (CategoryId, InvoiceId);
GO
-- Інваріант: не більше однієї основної категорії на рахунок
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_IC_Primary' AND object_id = OBJECT_ID(N'dbo.InvoiceCategories'))
    CREATE UNIQUE INDEX UX_IC_Primary ON dbo.InvoiceCategories (InvoiceId) WHERE IsPrimary = 1;
GO
