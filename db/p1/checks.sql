/* =====================================================================
   Проєкт №1 — запити перевірки (PaymentsDb). Запускати після seed.sql.
   ===================================================================== */
USE PaymentsDb;
GO

-- 1) FK: жоден не веде за межі цієї БД
SELECT fk.name AS FkName,
       OBJECT_NAME(fk.parent_object_id)     AS ChildTable,
       OBJECT_NAME(fk.referenced_object_id) AS ParentTable
FROM sys.foreign_keys fk
ORDER BY ChildTable;

-- 2) READ: платіж з розподілом (4 набори результатів)
EXEC dbo.usp_GetPaymentWithAllocations @PaymentId = 1;

-- 3) Керована помилка: «списати» неавторизований платіж (№2 — Created) -> 50002
BEGIN TRY
    EXEC dbo.usp_ChangePaymentStatus @PaymentId = 2, @NewStatus = 'Captured';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- XACT_ABORT ON у процедурі лишає транзакцію «неспроможною» (3930) — закриваємо її
    SELECT ERROR_NUMBER() AS ErrNo, ERROR_MESSAGE() AS ErrMsg;   -- 50002 Invalid status transition
END CATCH;

-- 4) Керована помилка: скасувати вже списаний/розрахований платіж (№1) -> 50002
BEGIN TRY
    EXEC dbo.usp_ChangePaymentStatus @PaymentId = 1, @NewStatus = 'Cancelled';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- XACT_ABORT ON у процедурі лишає транзакцію «неспроможною» (3930) — закриваємо її
    SELECT ERROR_NUMBER() AS ErrNo, ERROR_MESSAGE() AS ErrMsg;
END CATCH;

-- 5) Оптимістична конкурентність: застарілий RowVersion -> 50003
BEGIN TRY
    EXEC dbo.usp_ChangePaymentStatus @PaymentId = 2, @NewStatus = 'Validating',
         @ExpectedRowVersion = 0x0000000000000001;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- XACT_ABORT ON у процедурі лишає транзакцію «неспроможною» (3930) — закриваємо її
    SELECT ERROR_NUMBER() AS ErrNo, ERROR_MESSAGE() AS ErrMsg;
END CATCH;

-- 6) Знімок не залежить від довідника: змінюємо суму в копії — PaymentAllocations не змінюється
UPDATE dbo.Invoices SET Amount = Amount + 1 WHERE Id = 1;
SELECT i.Amount AS InvoiceCopyAmount, pa.InvoiceAmount AS SnapshotAmount
FROM dbo.Invoices i JOIN dbo.PaymentAllocations pa ON pa.InvoiceId = i.Id AND pa.PaymentId = 1
WHERE i.Id = 1;
UPDATE dbo.Invoices SET Amount = Amount - 1 WHERE Id = 1;   -- повернути як було

-- 7) Пагінація активних платежів клієнта (використовує IX_Payments_Active)
SELECT Id, Status, InitiatedAt, TotalAmount
FROM dbo.Payments
WHERE CustomerId = 1 AND IsDeleted = 0
ORDER BY InitiatedAt DESC
OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY;

-- 8) Платежі клієнта з певним статусом (IX_Payments_CustomerId_Status)
SELECT Id, InitiatedAt FROM dbo.Payments WHERE CustomerId = 2 AND Status = 'Settled';

-- 9) Валюта: рахунок в іншій валюті відхиляється (50012); подвійний розподіл — 50013
BEGIN TRY
    EXEC dbo.usp_AddPaymentAllocation @PaymentId = 2, @InvoiceId = 4;   -- вже розподілено -> 50013
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- XACT_ABORT ON у процедурі лишає транзакцію «неспроможною» (3930) — закриваємо її
    SELECT ERROR_NUMBER() AS ErrNo, ERROR_MESSAGE() AS ErrMsg;
END CATCH;

-- 10) М'яке видалення: Settled видалити не можна (50011), Cancelled — можна
BEGIN TRY
    EXEC dbo.usp_SoftDeletePayment @PaymentId = 1;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- XACT_ABORT ON у процедурі лишає транзакцію «неспроможною» (3930) — закриваємо її
    SELECT ERROR_NUMBER() AS ErrNo, ERROR_MESSAGE() AS ErrMsg;
END CATCH;
