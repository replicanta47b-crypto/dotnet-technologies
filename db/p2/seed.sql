/* =====================================================================
   Проєкт №2 — seed-дані BillingDb. Ідемпотентно (NOT EXISTS за Id/PK).
   Id рахунків 1..6 збігаються з Invoices.Id у PaymentsDb та invoiceId у MongoDB.
   ===================================================================== */
USE BillingDb;
GO

SET IDENTITY_INSERT dbo.Merchants ON;
INSERT dbo.Merchants (Id, Name, Country, ContactEmail)
SELECT v.Id, v.Name, v.Country, v.ContactEmail
FROM (VALUES
    (1, N'ЕнергоСвіт',     N'Україна', N'billing@energosvit.example'),
    (2, N'ВодоКанал Плюс', N'Україна', N'billing@vodokanal.example'),
    (3, N'ТелеЛінк',       N'Україна', N'billing@telelink.example')
) AS v(Id, Name, Country, ContactEmail)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Merchants m WHERE m.Id = v.Id);
SET IDENTITY_INSERT dbo.Merchants OFF;
GO

SET IDENTITY_INSERT dbo.ServiceCategories ON;
INSERT dbo.ServiceCategories (Id, Name, Slug)
SELECT v.Id, v.Name, v.Slug
FROM (VALUES
    (1, N'Комунальні послуги', N'utilities'),
    (2, N'Електроенергія',     N'electricity'),
    (3, N'Водопостачання',     N'water'),
    (4, N'Зв''язок',           N'telecom'),
    (5, N'Інтернет',           N'internet')
) AS v(Id, Name, Slug)
WHERE NOT EXISTS (SELECT 1 FROM dbo.ServiceCategories c WHERE c.Id = v.Id);
SET IDENTITY_INSERT dbo.ServiceCategories OFF;
GO

SET IDENTITY_INSERT dbo.Invoices ON;
INSERT dbo.Invoices (Id, Number, MerchantId, Currency, TotalAmount, DueDate, Status, CreatedBy)
SELECT v.Id, v.Number, v.MerchantId, v.Currency, v.TotalAmount, CONVERT(DATE, v.DueDate), v.Status, N'seed'
FROM (VALUES
    (1, N'INV-2026-0001', 1, 'UAH', 1250.50, '2026-10-10', N'Paid'),
    (2, N'INV-2026-0002', 2, 'UAH',  420.00, '2026-10-12', N'Paid'),
    (3, N'INV-2026-0003', 3, 'UAH',  300.00, '2026-11-05', N'Paid'),
    (4, N'INV-2026-0004', 1, 'UAH', 1380.75, '2026-11-10', N'Issued'),
    (5, N'INV-2026-0005', 2, 'UAH',  455.00, '2026-11-12', N'Overdue'),
    (6, N'INV-2026-0006', 3, 'UAH',  300.00, '2026-12-05', N'Issued')
) AS v(Id, Number, MerchantId, Currency, TotalAmount, DueDate, Status)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Invoices i WHERE i.Id = v.Id);
SET IDENTITY_INSERT dbo.Invoices OFF;
GO

INSERT dbo.InvoiceDetails (InvoiceId, CustomerRef, PeriodStart, PeriodEnd, Note)
SELECT v.InvoiceId, v.CustomerRef, CONVERT(DATE, v.PeriodStart), CONVERT(DATE, v.PeriodEnd), v.Note
FROM (VALUES
    (1, N'ACC-100234', '2026-09-01', '2026-09-30', N'Показання лічильника передано'),
    (2, N'ACC-770021', '2026-09-01', '2026-09-30', NULL),
    (3, N'ACC-550310', '2026-10-01', '2026-10-31', NULL),
    (4, N'ACC-100234', '2026-10-01', '2026-10-31', NULL),
    (5, N'ACC-770021', '2026-10-01', '2026-10-31', N'Є заборгованість за попередній період'),
    (6, N'ACC-550310', '2026-11-01', '2026-11-30', NULL)
) AS v(InvoiceId, CustomerRef, PeriodStart, PeriodEnd, Note)
WHERE NOT EXISTS (SELECT 1 FROM dbo.InvoiceDetails d WHERE d.InvoiceId = v.InvoiceId);
GO

-- Рядки рахунків: сума LineTotal збігається з Invoices.TotalAmount
INSERT dbo.InvoiceLines (InvoiceId, Description, Quantity, UnitPrice, SortOrder)
SELECT v.InvoiceId, v.Description, v.Quantity, v.UnitPrice, v.SortOrder
FROM (VALUES
    (1, N'Електроенергія, кВт·год',       250.00, 4.80, 0),
    (1, N'Абонентська плата',               1.00, 50.50, 1),
    (2, N'Водопостачання, м³',             12.00, 30.00, 0),
    (2, N'Водовідведення, м³',             12.00,  5.00, 1),
    (3, N'Інтернет 100 Мбіт/с, місяць',     1.00, 300.00, 0),
    (4, N'Електроенергія, кВт·год',       280.00, 4.80, 0),
    (4, N'Абонентська плата',               1.00, 36.75, 1),
    (5, N'Водопостачання, м³',             13.00, 30.00, 0),
    (5, N'Водовідведення, м³',             13.00,  5.00, 1),
    (6, N'Інтернет 100 Мбіт/с, місяць',     1.00, 300.00, 0)
) AS v(InvoiceId, Description, Quantity, UnitPrice, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.InvoiceLines l WHERE l.InvoiceId = v.InvoiceId AND l.SortOrder = v.SortOrder);
GO

INSERT dbo.InvoiceCategories (InvoiceId, CategoryId, AssignedAt, IsPrimary)
SELECT v.InvoiceId, v.CategoryId, CONVERT(DATETIME2, v.AssignedAt), v.IsPrimary
FROM (VALUES
    (1, 2, '2026-10-01T09:00:00', 1), (1, 1, '2026-10-01T09:00:00', 0),
    (2, 3, '2026-10-01T09:05:00', 1), (2, 1, '2026-10-01T09:05:00', 0),
    (3, 5, '2026-11-01T08:00:00', 1), (3, 4, '2026-11-02T12:00:00', 0),
    (4, 2, '2026-11-01T09:00:00', 1), (4, 1, '2026-11-01T09:00:00', 0),
    (5, 3, '2026-11-01T09:05:00', 1), (5, 1, '2026-11-01T09:05:00', 0),
    (6, 5, '2026-12-01T08:00:00', 1), (6, 4, '2026-12-02T12:00:00', 0)
) AS v(InvoiceId, CategoryId, AssignedAt, IsPrimary)
WHERE NOT EXISTS (SELECT 1 FROM dbo.InvoiceCategories ic
                  WHERE ic.InvoiceId = v.InvoiceId AND ic.CategoryId = v.CategoryId);
GO

-- Перевірка: рахунки з категоріями (M:N), сумою рядків і датою призначення категорії
SELECT i.Id, i.Number, m.Name AS Merchant, c.Name AS Category, ic.AssignedAt, ic.IsPrimary,
       i.TotalAmount, (SELECT SUM(LineTotal) FROM dbo.InvoiceLines l WHERE l.InvoiceId = i.Id) AS LinesSum
FROM dbo.Invoices i
JOIN dbo.Merchants m ON m.Id = i.MerchantId
JOIN dbo.InvoiceCategories ic ON ic.InvoiceId = i.Id
JOIN dbo.ServiceCategories c ON c.Id = ic.CategoryId
ORDER BY i.Id, ic.IsPrimary DESC;
GO
