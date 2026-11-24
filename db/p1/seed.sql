/* =====================================================================
   Проєкт №1 — seed-дані (PaymentsDb). Ідемпотентно: повторний запуск
   не створює дублікатів (перевірка наявності перед вставкою).
   Invoices.Id збігаються з Id у BillingDb (db/p2/seed.sql).
   ===================================================================== */
USE PaymentsDb;
GO

/* ---- Customers: за унікальним Email ---- */
INSERT dbo.Customers (Email, FullName, CreatedBy)
SELECT v.Email, v.FullName, N'seed'
FROM (VALUES
    (N'olena@example.com',  N'Олена Коваленко'),
    (N'andriy@example.com', N'Андрій Мельник'),
    (N'iryna@example.com',  N'Ірина Шевченко')
) AS v(Email, FullName)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Customers c WHERE c.Email = v.Email);
GO

/* ---- Invoices: локальна копія довідника Білінгу (Id з BillingDb) ---- */
INSERT dbo.Invoices (Id, Number, MerchantName, Amount, Currency)
SELECT v.Id, v.Number, v.MerchantName, v.Amount, v.Currency
FROM (VALUES
    (1, N'INV-2026-0001', N'ЕнергоСвіт',     1250.50, 'UAH'),
    (2, N'INV-2026-0002', N'ВодоКанал Плюс',  420.00, 'UAH'),
    (3, N'INV-2026-0003', N'ТелеЛінк',        300.00, 'UAH'),
    (4, N'INV-2026-0004', N'ЕнергоСвіт',     1380.75, 'UAH'),
    (5, N'INV-2026-0005', N'ВодоКанал Плюс',  455.00, 'UAH'),
    (6, N'INV-2026-0006', N'ТелеЛінк',        300.00, 'UAH')
) AS v(Id, Number, MerchantName, Amount, Currency)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Invoices i WHERE i.Id = v.Id);
GO

/* ---- Демо-платежі (створюються через процедури; лише якщо ще не засіяні) ---- */
IF NOT EXISTS (SELECT 1 FROM dbo.Payments WHERE CreatedBy = N'seed')
BEGIN
    DECLARE @c1 BIGINT = (SELECT Id FROM dbo.Customers WHERE Email = N'olena@example.com');
    DECLARE @c2 BIGINT = (SELECT Id FROM dbo.Customers WHERE Email = N'andriy@example.com');
    DECLARE @c3 BIGINT = (SELECT Id FROM dbo.Customers WHERE Email = N'iryna@example.com');
    DECLARE @p1 BIGINT, @p2 BIGINT, @p3 BIGINT, @p4 BIGINT, @total DECIMAL(18,2);

    -- Платіж 1: повний цикл Created -> ... -> Settled, оплачує два рахунки
    EXEC dbo.usp_CreatePayment @CustomerId = @c1, @Currency = 'UAH', @Method = 'Card',
         @PayerAccount = N'**** 4242', @Description = N'Комунальні послуги за жовтень',
         @CreatedBy = N'seed', @NewPaymentId = @p1 OUTPUT;
    EXEC dbo.usp_AddPaymentAllocation @PaymentId = @p1, @InvoiceId = 1, @UpdatedBy = N'seed';
    EXEC dbo.usp_AddPaymentAllocation @PaymentId = @p1, @InvoiceId = 2, @UpdatedBy = N'seed';
    EXEC dbo.usp_ChangePaymentStatus @PaymentId = @p1, @NewStatus = 'Validating', @ChangedBy = N'seed';
    SELECT @total = TotalAmount FROM dbo.Payments WHERE Id = @p1;
    EXEC dbo.usp_AuthorizePayment @PaymentId = @p1, @Amount = @total,
         @ProviderRef = N'AUTH-SEED-0001', @AuthorizedBy = N'seed';
    EXEC dbo.usp_ChangePaymentStatus @PaymentId = @p1, @NewStatus = 'Captured', @ChangedBy = N'seed';
    EXEC dbo.usp_ChangePaymentStatus @PaymentId = @p1, @NewStatus = 'Settled',  @ChangedBy = N'seed';

    -- Платіж 2: новий, з розподілом (лишається Created)
    EXEC dbo.usp_CreatePayment @CustomerId = @c2, @Currency = 'UAH', @Method = 'BankTransfer',
         @PayerAccount = N'UA21****0001', @CreatedBy = N'seed', @NewPaymentId = @p2 OUTPUT;
    EXEC dbo.usp_AddPaymentAllocation @PaymentId = @p2, @InvoiceId = 4, @UpdatedBy = N'seed';

    -- Платіж 3: скасований до списання коштів
    EXEC dbo.usp_CreatePayment @CustomerId = @c3, @Currency = 'UAH', @Method = 'Wallet',
         @PayerAccount = N'wallet-iryna', @CreatedBy = N'seed', @NewPaymentId = @p3 OUTPUT;
    EXEC dbo.usp_AddPaymentAllocation @PaymentId = @p3, @InvoiceId = 5, @UpdatedBy = N'seed';
    EXEC dbo.usp_ChangePaymentStatus @PaymentId = @p3, @NewStatus = 'Cancelled',
         @ChangedBy = N'seed', @Note = N'Платник передумав';

    -- Платіж 4: повний цикл, один рахунок
    EXEC dbo.usp_CreatePayment @CustomerId = @c2, @Currency = 'UAH', @Method = 'Card',
         @PayerAccount = N'**** 1111', @Description = N'Інтернет за листопад',
         @CreatedBy = N'seed', @NewPaymentId = @p4 OUTPUT;
    EXEC dbo.usp_AddPaymentAllocation @PaymentId = @p4, @InvoiceId = 3, @UpdatedBy = N'seed';
    EXEC dbo.usp_ChangePaymentStatus @PaymentId = @p4, @NewStatus = 'Validating', @ChangedBy = N'seed';
    SELECT @total = TotalAmount FROM dbo.Payments WHERE Id = @p4;
    EXEC dbo.usp_AuthorizePayment @PaymentId = @p4, @Amount = @total,
         @ProviderRef = N'AUTH-SEED-0004', @AuthorizedBy = N'seed';
    EXEC dbo.usp_ChangePaymentStatus @PaymentId = @p4, @NewStatus = 'Captured', @ChangedBy = N'seed';
    EXEC dbo.usp_ChangePaymentStatus @PaymentId = @p4, @NewStatus = 'Settled',  @ChangedBy = N'seed';
END
GO
