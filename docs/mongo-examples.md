# Проєкт №3 — приклади документів і запитів (MongoDB, `disputes_db`)

## Колекції

| Колекція | Призначення | Прийом |
|---|---|---|
| `disputes` | Спори за платежами | embedding (`openedBy`, `evidence[]`, `comments[]`), reference (`threadId`, `paymentId`, `invoiceId`), гібрид |
| `disputeThreads` | Тема обговорення спору | reference на `disputes` |
| `threadMessages` | Повідомлення обговорень (необмежена кількість) | reference `threadId` |
| `merchantDisputeStats` | Підсумок спорів мерчанта | денормалізація для читання |

## Приклад 1: повний спір (embedding + reference + гібрид)

```json
{
  "_id": { "$oid": "66f1c2000000000000000001" },
  "paymentId": 1,
  "invoiceId": 1,
  "merchantId": 1,
  "merchantName": "ЕнергоСвіт",
  "amount": 1250.5,
  "currency": "UAH",
  "reason": "amount_mismatch",
  "status": "open",
  "expectedAmount": 1200.0,
  "openedBy": { "userId": "u-1", "displayName": "Олена" },
  "evidence": [
    { "type": "screenshot", "url": "evidence/d1-meter.png", "addedAt": { "$date": "2026-10-15T09:10:00Z" } },
    { "type": "receipt", "url": "evidence/d1-receipt.pdf", "addedAt": { "$date": "2026-10-15T09:12:00Z" } }
  ],
  "comments": [
    { "author": "Підтримка", "text": "Запит передано мерчанту", "createdAt": { "$date": "2026-10-15T10:00:00Z" } }
  ],
  "threadId": { "$oid": "66f1c3000000000000000001" },
  "openedAt": { "$date": "2026-10-15T09:00:00Z" }
}
```

`paymentId`, `invoiceId`, `merchantId` — логічні посилання (без FK); `merchantName` — денормалізація з Білінгу; `amount`/`currency` — знімок на момент відкриття.

## Приклад 2: спір про шахрайство (інший набір полів)

```json
{
  "_id": { "$oid": "66f1c2000000000000000002" },
  "paymentId": 4,
  "invoiceId": 3,
  "merchantId": 3,
  "merchantName": "ТелеЛінк",
  "amount": 300.0,
  "currency": "UAH",
  "reason": "fraud",
  "status": "under_review",
  "openedBy": { "userId": "u-2", "displayName": "Андрій" },
  "fraudDetails": { "deviceId": "dev-9f3a", "ip": "203.0.113.45", "country": "PL", "suspicious": true },
  "evidence": [
    { "type": "bank_statement", "url": "evidence/d2-statement.pdf", "addedAt": { "$date": "2026-11-10T08:00:00Z" } }
  ],
  "threadId": { "$oid": "66f1c3000000000000000002" },
  "openedAt": { "$date": "2026-11-10T07:45:00Z" }
}
```

## Приклад 3: мінімальний спір

```json
{
  "_id": { "$oid": "66f1c2000000000000000003" },
  "paymentId": 4,
  "invoiceId": 3,
  "merchantId": 3,
  "merchantName": "ТелеЛінк",
  "amount": 300.0,
  "currency": "UAH",
  "reason": "other",
  "status": "closed",
  "openedBy": { "userId": "u-2", "displayName": "Андрій" },
  "openedAt": { "$date": "2026-11-12T12:00:00Z" },
  "resolvedAt": { "$date": "2026-11-12T15:00:00Z" }
}
```

## Приклад 4: послугу не надано (serviceProof і tags)

```json
{
  "_id": { "$oid": "66f1c2000000000000000004" },
  "paymentId": 1,
  "invoiceId": 2,
  "merchantId": 2,
  "merchantName": "ВодоКанал Плюс",
  "amount": 420.0,
  "currency": "UAH",
  "reason": "service_not_provided",
  "status": "open",
  "openedBy": { "userId": "u-1", "displayName": "Олена" },
  "serviceProof": { "provider": "ВодоКанал Плюс", "ticketNo": "TCK-88231", "outageDays": 3 },
  "tags": ["відключення води", "жовтень"],
  "openedAt": { "$date": "2026-11-14T10:20:00Z" }
}
```

Усі чотири документи в колекції `disputes` мають різні набори полів: `expectedAmount`, `fraudDetails`, `serviceProof`, `tags`, `evidence`, `comments`, `threadId`, `resolvedAt` є лише в частини з них.

## Приклад 5: повідомлення обговорення (reference)

```json
{
  "_id": { "$oid": "66f1c4000000000000000002" },
  "threadId": { "$oid": "66f1c3000000000000000001" },
  "parentMessageId": { "$oid": "66f1c4000000000000000001" },
  "author": { "userId": "support-1", "displayName": "Підтримка" },
  "text": "Перевіряємо показання з мерчантом",
  "createdAt": { "$date": "2026-10-15T14:00:00Z" }
}
```

## Приклад 6: підсумок за мерчантом

```json
{
  "_id": 3,
  "merchantName": "ТелеЛінк",
  "total": 2,
  "openCount": 1,
  "byReason": { "fraud": 1, "other": 1 },
  "updatedAt": { "$date": "2026-11-12T15:00:00Z" }
}
```

## Запити

```javascript
use disputes_db

// різні набори полів у одній колекції
db.disputes.find({}, { evidence: 1, fraudDetails: 1, serviceProof: 1, tags: 1 })

// спори платежу, найновіші першими (ix_disputes_payment_opened)
db.disputes.find({ paymentId: 1 }).sort({ openedAt: -1 })

// відкриті спори мерчанта (ix_disputes_merchant_status)
db.disputes.find({ merchantId: 3, status: { $in: ["open", "under_review"] } })

// спори про шахрайство з підозрілого пристрою (часткований індекс)
db.disputes.find({ "fraudDetails.deviceId": "dev-9f3a" })

// пагінація повідомлень обговорення (ix_messages_thread_created)
db.threadMessages.find({ threadId: ObjectId("66f1c3000000000000000001") })
  .sort({ createdAt: 1 }).skip(0).limit(20)

// гібрид: спір + тема обговорення (reference розв'язується $lookup)
db.disputes.aggregate([
  { $match: { _id: ObjectId("66f1c2000000000000000001") } },
  { $lookup: { from: "disputeThreads", localField: "threadId", foreignField: "_id", as: "thread" } },
  { $unwind: "$thread" }
])

// aggregation: кількість і сума спорів за причиною
db.disputes.aggregate([
  { $group: { _id: "$reason", count: { $sum: 1 }, totalAmount: { $sum: "$amount" } } },
  { $sort: { count: -1 } }
])

// оновлення дубльованого поля подією з Білінгу (зміна назви мерчанта 3)
db.disputes.updateMany({ merchantId: 3 }, { $set: { merchantName: "ТелеЛінк Груп" } })
db.merchantDisputeStats.updateOne({ _id: 3 }, { $set: { merchantName: "ТелеЛінк Груп" } })

// додати коментар у вбудований масив
db.disputes.updateOne(
  { _id: ObjectId("66f1c2000000000000000003") },
  { $push: { comments: { author: "Підтримка", text: "Спір закрито", createdAt: new Date() } } }
)
```

> Після демонстраційного оновлення `merchantName` поверніть значення «ТелеЛінк» або перезапустіть `seed.js` (він перезапише документи за `_id`).
