/* =====================================================================
   Проєкт №1 — збережувані процедури (PaymentsDb). Ідемпотентно: CREATE OR ALTER.
   Коди керованих помилок (THROW):
     50001 Payment not found
     50002 Invalid status transition
     50003 Concurrency conflict (RowVersion не збігається)
     50004 Cannot mark as Authorized without an authorization transaction
     50005 Payment has no allocations
     50006 Customer not found
     50007 Allocations can be changed only while payment is Created
     50008 Invoice not found
     50009 Authorization allowed only for Validating payment
     50010 Authorized amount must equal payment total
     50011 Only Created or Cancelled payment can be deleted
     50012 Invoice currency differs from payment currency
     50013 Invoice is already allocated to this payment
     50014 Allocated amount exceeds invoice amount
   Усі процедури, що пишуть: SET XACT_ABORT ON + явна транзакція.
   ===================================================================== */
USE PaymentsDb;
GO

/* 1) CREATE: новий платіж + деталі (1:1) + запис в історії статусів */
CREATE OR ALTER PROCEDURE dbo.usp_CreatePayment
    @CustomerId   BIGINT,
    @Currency     CHAR(3),
    @Method       NVARCHAR(20),
    @PayerAccount NVARCHAR(50),
    @Description  NVARCHAR(500) = NULL,
    @CreatedBy    NVARCHAR(100) = NULL,
    @NewPaymentId BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Id = @CustomerId AND IsDeleted = 0)
        THROW 50006, 'Customer not found', 1;

    INSERT dbo.Payments (CustomerId, Currency, Status, CreatedBy)
    VALUES (@CustomerId, @Currency, 'Created', @CreatedBy);
    SET @NewPaymentId = SCOPE_IDENTITY();

    INSERT dbo.PaymentDetails (PaymentId, Method, PayerAccount, Description)
    VALUES (@NewPaymentId, @Method, @PayerAccount, @Description);

    INSERT dbo.PaymentStatusHistory (PaymentId, OldStatus, NewStatus, ChangedBy, Note)
    VALUES (@NewPaymentId, NULL, 'Created', @CreatedBy, 'Payment created');

    COMMIT TRANSACTION;
    SELECT @NewPaymentId AS PaymentId;
END
GO

/* 2) UPDATE: додати розподіл платежу на рахунок. InvoiceNumber і InvoiceAmount беруться
      з локальної копії довідника і ФІКСУЮТЬСЯ в PaymentAllocations як знімок
      (надалі не оновлюються). @Amount = NULL означає «сплатити рахунок повністю». */
CREATE OR ALTER PROCEDURE dbo.usp_AddPaymentAllocation
    @PaymentId BIGINT,
    @InvoiceId BIGINT,
    @Amount    DECIMAL(18,2) = NULL,
    @UpdatedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @Status NVARCHAR(30), @PayCurrency CHAR(3);
    SELECT @Status = Status, @PayCurrency = Currency
    FROM dbo.Payments WITH (UPDLOCK) WHERE Id = @PaymentId AND IsDeleted = 0;
    IF @Status IS NULL
        THROW 50001, 'Payment not found', 1;
    IF @Status <> 'Created'
        THROW 50007, 'Allocations can be changed only while payment is Created', 1;

    DECLARE @Number NVARCHAR(50), @InvAmount DECIMAL(18,2), @InvCurrency CHAR(3);
    SELECT @Number = Number, @InvAmount = Amount, @InvCurrency = Currency
    FROM dbo.Invoices WHERE Id = @InvoiceId;
    IF @Number IS NULL
        THROW 50008, 'Invoice not found', 1;
    IF @InvCurrency <> @PayCurrency
        THROW 50012, 'Invoice currency differs from payment currency', 1;
    IF EXISTS (SELECT 1 FROM dbo.PaymentAllocations WHERE PaymentId = @PaymentId AND InvoiceId = @InvoiceId)
        THROW 50013, 'Invoice is already allocated to this payment', 1;

    SET @Amount = COALESCE(@Amount, @InvAmount);
    IF @Amount > @InvAmount
        THROW 50014, 'Allocated amount exceeds invoice amount', 1;

    INSERT dbo.PaymentAllocations (PaymentId, InvoiceId, InvoiceNumber, InvoiceAmount, AllocatedAmount)
    VALUES (@PaymentId, @InvoiceId, @Number, @InvAmount, @Amount);

    UPDATE dbo.Payments
    SET TotalAmount = (SELECT COALESCE(SUM(AllocatedAmount), 0) FROM dbo.PaymentAllocations WHERE PaymentId = @PaymentId),
        UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy
    WHERE Id = @PaymentId;

    COMMIT TRANSACTION;
END
GO

/* 3) READ: платіж з деталями, розподілом, історією та операціями (4 набори результатів) */
CREATE OR ALTER PROCEDURE dbo.usp_GetPaymentWithAllocations
    @PaymentId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.Payments WHERE Id = @PaymentId AND IsDeleted = 0)
        THROW 50001, 'Payment not found', 1;

    SELECT p.Id, p.CustomerId, c.FullName, c.Email, p.Currency, p.Status, p.InitiatedAt, p.TotalAmount,
           d.Method, d.PayerAccount, d.Description, p.RowVersion
    FROM dbo.Payments p
    JOIN dbo.Customers c ON c.Id = p.CustomerId
    LEFT JOIN dbo.PaymentDetails d ON d.PaymentId = p.Id
    WHERE p.Id = @PaymentId;

    SELECT InvoiceId, InvoiceNumber, InvoiceAmount, AllocatedAmount
    FROM dbo.PaymentAllocations WHERE PaymentId = @PaymentId ORDER BY InvoiceId;

    SELECT OldStatus, NewStatus, ChangedAt, ChangedBy, Note
    FROM dbo.PaymentStatusHistory WHERE PaymentId = @PaymentId ORDER BY ChangedAt, Id;

    SELECT Id, Type, Amount, ProviderRef, ProcessedAt
    FROM dbo.PaymentTransactions WHERE PaymentId = @PaymentId ORDER BY ProcessedAt, Id;
END
GO

/* 4) ТРАНЗАКЦІЙНА ЗМІНА СТАТУСУ з перевіркою допустимого переходу.
      Created -> Validating -> Authorized -> Captured -> Settled
      Cancelled — лише з Created / Validating / Authorized (до списання коштів).
      @ExpectedRowVersion (необов'язково) — оптимістична конкурентність. */
CREATE OR ALTER PROCEDURE dbo.usp_ChangePaymentStatus
    @PaymentId          BIGINT,
    @NewStatus          NVARCHAR(30),
    @ChangedBy          NVARCHAR(100) = NULL,
    @Note               NVARCHAR(500) = NULL,
    @ExpectedRowVersion VARBINARY(8)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;                          -- будь-яка помилка відкочує транзакцію
    BEGIN TRANSACTION;

    DECLARE @Old NVARCHAR(30), @Rv VARBINARY(8);
    SELECT @Old = Status, @Rv = RowVersion
    FROM dbo.Payments WITH (UPDLOCK)            -- блокує рядок до кінця транзакції
    WHERE Id = @PaymentId AND IsDeleted = 0;

    IF @Old IS NULL
        THROW 50001, 'Payment not found', 1;

    IF @ExpectedRowVersion IS NOT NULL AND @ExpectedRowVersion <> @Rv
        THROW 50003, 'Concurrency conflict: payment was modified by another user', 1;

    -- таблиця допустимих переходів
    IF NOT EXISTS (
        SELECT 1 FROM (VALUES
            ('Created',    'Validating'),
            ('Created',    'Cancelled'),
            ('Validating', 'Authorized'),
            ('Validating', 'Cancelled'),
            ('Authorized', 'Captured'),
            ('Authorized', 'Cancelled'),
            ('Captured',   'Settled')
        ) AS t(FromStatus, ToStatus)
        WHERE t.FromStatus = @Old AND t.ToStatus = @NewStatus)
        THROW 50002, 'Invalid status transition', 1;

    IF @NewStatus = 'Validating'
       AND NOT EXISTS (SELECT 1 FROM dbo.PaymentAllocations WHERE PaymentId = @PaymentId)
        THROW 50005, 'Payment has no allocations', 1;

    IF @NewStatus = 'Authorized'
       AND NOT EXISTS (SELECT 1 FROM dbo.PaymentTransactions WHERE PaymentId = @PaymentId AND Type = 'Authorization')
        THROW 50004, 'Cannot mark as Authorized without an authorization transaction', 1;

    UPDATE dbo.Payments
    SET Status = @NewStatus, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @ChangedBy
    WHERE Id = @PaymentId;

    INSERT dbo.PaymentStatusHistory (PaymentId, OldStatus, NewStatus, ChangedBy, Note)
    VALUES (@PaymentId, @Old, @NewStatus, @ChangedBy, @Note);

    COMMIT TRANSACTION;
END
GO

/* 5) БІЗНЕС-ОПЕРАЦІЯ: авторизація платежу — запис операції + перехід у Authorized в одній транзакції */
CREATE OR ALTER PROCEDURE dbo.usp_AuthorizePayment
    @PaymentId    BIGINT,
    @Amount       DECIMAL(18,2),
    @ProviderRef  NVARCHAR(100) = NULL,
    @AuthorizedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @Status NVARCHAR(30), @Total DECIMAL(18,2);
    SELECT @Status = Status, @Total = TotalAmount
    FROM dbo.Payments WITH (UPDLOCK) WHERE Id = @PaymentId AND IsDeleted = 0;

    IF @Status IS NULL
        THROW 50001, 'Payment not found', 1;
    IF @Status <> 'Validating'
        THROW 50009, 'Authorization allowed only for Validating payment', 1;
    IF @Amount <> @Total
        THROW 50010, 'Authorized amount must equal payment total', 1;

    INSERT dbo.PaymentTransactions (PaymentId, Type, Amount, ProviderRef, CreatedBy)
    VALUES (@PaymentId, 'Authorization', @Amount, @ProviderRef, @AuthorizedBy);

    EXEC dbo.usp_ChangePaymentStatus @PaymentId = @PaymentId, @NewStatus = 'Authorized',
         @ChangedBy = @AuthorizedBy, @Note = 'Authorization registered';

    COMMIT TRANSACTION;
END
GO

/* 6) DELETE (м'яке): лише для нового або скасованого платежу */
CREATE OR ALTER PROCEDURE dbo.usp_SoftDeletePayment
    @PaymentId BIGINT,
    @DeletedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DECLARE @Status NVARCHAR(30) =
        (SELECT Status FROM dbo.Payments WITH (UPDLOCK) WHERE Id = @PaymentId AND IsDeleted = 0);
    IF @Status IS NULL
        THROW 50001, 'Payment not found', 1;
    IF @Status NOT IN ('Created', 'Cancelled')
        THROW 50011, 'Only Created or Cancelled payment can be deleted', 1;

    UPDATE dbo.Payments
    SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @DeletedBy
    WHERE Id = @PaymentId;

    COMMIT TRANSACTION;
END
GO
