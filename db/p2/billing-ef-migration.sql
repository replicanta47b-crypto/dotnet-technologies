IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    CREATE TABLE [Merchants] (
        [Id] bigint NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [Country] nvarchar(100) NULL,
        [ContactEmail] nvarchar(256) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Merchants] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    CREATE TABLE [ServiceCategories] (
        [Id] bigint NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [Slug] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_ServiceCategories] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    CREATE TABLE [Invoices] (
        [Id] bigint NOT NULL IDENTITY,
        [Number] nvarchar(50) NOT NULL,
        [MerchantId] bigint NOT NULL,
        [Currency] char(3) NOT NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [DueDate] date NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(100) NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Invoices] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Invoices_Currency] CHECK ([Currency] IN ('UAH','USD','EUR')),
        CONSTRAINT [CK_Invoices_Status] CHECK ([Status] IN ('Draft','Issued','Paid','Overdue','Void')),
        CONSTRAINT [CK_Invoices_Total] CHECK ([TotalAmount] > 0),
        CONSTRAINT [FK_Invoices_Merchants_MerchantId] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    CREATE TABLE [InvoiceDetails] (
        [InvoiceId] bigint NOT NULL,
        [CustomerRef] nvarchar(100) NULL,
        [PeriodStart] date NULL,
        [PeriodEnd] date NULL,
        [Note] nvarchar(1000) NULL,
        CONSTRAINT [PK_InvoiceDetails] PRIMARY KEY ([InvoiceId]),
        CONSTRAINT [CK_InvoiceDetails_Period] CHECK ([PeriodStart] IS NULL OR [PeriodEnd] IS NULL OR [PeriodEnd] >= [PeriodStart]),
        CONSTRAINT [FK_InvoiceDetails_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [Invoices] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    CREATE TABLE [InvoiceLines] (
        [Id] bigint NOT NULL IDENTITY,
        [InvoiceId] bigint NOT NULL,
        [Description] nvarchar(300) NOT NULL,
        [Quantity] decimal(10,2) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [LineTotal] AS [Quantity] * [UnitPrice] PERSISTED,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_InvoiceLines] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_InvoiceLines_Price] CHECK ([UnitPrice] >= 0),
        CONSTRAINT [CK_InvoiceLines_Qty] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_InvoiceLines_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [Invoices] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    CREATE TABLE [InvoiceCategories] (
        [InvoiceId] bigint NOT NULL,
        [CategoryId] bigint NOT NULL,
        [AssignedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [IsPrimary] bit NOT NULL,
        CONSTRAINT [PK_InvoiceCategories] PRIMARY KEY ([InvoiceId], [CategoryId]),
        CONSTRAINT [FK_InvoiceCategories_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [Invoices] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_InvoiceCategories_ServiceCategories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [ServiceCategories] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ContactEmail', N'Country', N'IsActive', N'Name') AND [object_id] = OBJECT_ID(N'[Merchants]'))
        SET IDENTITY_INSERT [Merchants] ON;
    EXEC(N'INSERT INTO [Merchants] ([Id], [ContactEmail], [Country], [IsActive], [Name])
    VALUES (CAST(1 AS bigint), N''billing@energosvit.example'', N''Україна'', CAST(1 AS bit), N''ЕнергоСвіт''),
    (CAST(2 AS bigint), N''billing@vodokanal.example'', N''Україна'', CAST(1 AS bit), N''ВодоКанал Плюс''),
    (CAST(3 AS bigint), N''billing@telelink.example'', N''Україна'', CAST(1 AS bit), N''ТелеЛінк'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'ContactEmail', N'Country', N'IsActive', N'Name') AND [object_id] = OBJECT_ID(N'[Merchants]'))
        SET IDENTITY_INSERT [Merchants] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name', N'Slug') AND [object_id] = OBJECT_ID(N'[ServiceCategories]'))
        SET IDENTITY_INSERT [ServiceCategories] ON;
    EXEC(N'INSERT INTO [ServiceCategories] ([Id], [Name], [Slug])
    VALUES (CAST(1 AS bigint), N''Комунальні послуги'', N''utilities''),
    (CAST(2 AS bigint), N''Електроенергія'', N''electricity''),
    (CAST(3 AS bigint), N''Водопостачання'', N''water''),
    (CAST(4 AS bigint), N''Зв''''язок'', N''telecom''),
    (CAST(5 AS bigint), N''Інтернет'', N''internet'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name', N'Slug') AND [object_id] = OBJECT_ID(N'[ServiceCategories]'))
        SET IDENTITY_INSERT [ServiceCategories] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_IC_CategoryId] ON [InvoiceCategories] ([CategoryId], [InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_IC_Primary] ON [InvoiceCategories] ([InvoiceId]) WHERE [IsPrimary] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_InvoiceLines_Invoice_Sort] ON [InvoiceLines] ([InvoiceId], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Invoices_MerchantId_Status] ON [Invoices] ([MerchantId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Invoices_Status_DueDate] ON [Invoices] ([Status], [DueDate]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_Invoices_Number] ON [Invoices] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_Merchants_Name] ON [Merchants] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_ServiceCategories_Slug] ON [ServiceCategories] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261124120000_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261124120000_InitialCreate', N'9.0.23');
END;

COMMIT;
GO

