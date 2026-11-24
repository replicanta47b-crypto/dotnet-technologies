/* =====================================================================
   Проєкт №3 — seed-дані MongoDB (disputes_db).
   Запуск: mongosh "mongodb://localhost:27017" db/p3/seed.js
   Ідемпотентно: replaceOne з upsert за фіксованими _id — повторний запуск
   не створює дублікатів. Спочатку виконайте collections.js.
   paymentId 1..4, invoiceId 1..6, merchantId 1..3 відповідають seed-даним
   PaymentsDb / BillingDb (лише логічні посилання, без FK).
   userId "u-<N>" відповідає Customers.Id = N у PaymentsDb.
   ===================================================================== */
const sdb = db.getSiblingDB("disputes_db");

function upsert(coll, doc) {
  sdb.getCollection(coll).replaceOne({ _id: doc._id }, doc, { upsert: true });
}

const D1 = ObjectId("66f1c2000000000000000001");
const D2 = ObjectId("66f1c2000000000000000002");
const D3 = ObjectId("66f1c2000000000000000003");
const D4 = ObjectId("66f1c2000000000000000004");
const T1 = ObjectId("66f1c3000000000000000001");
const T2 = ObjectId("66f1c3000000000000000002");

/* ---------- disputes: документи РІЗНОЇ структури в одній колекції ---------- */

// D1: «повний» документ — вбудований openedBy, масиви evidence і comments,
//     reference на disputeThreads (гібридний підхід), додаткове поле expectedAmount
upsert("disputes", {
  _id: D1,
  paymentId: 1, invoiceId: 1, merchantId: 1,
  merchantName: "ЕнергоСвіт",            // денормалізовано з Білінгу
  amount: 1250.50, currency: "UAH",      // знімок на момент відкриття спору
  reason: "amount_mismatch", status: "open",
  expectedAmount: 1200.00,
  openedBy: { userId: "u-1", displayName: "Олена" },
  evidence: [
    { type: "screenshot", url: "evidence/d1-meter.png",   addedAt: new Date("2026-10-15T09:10:00Z") },
    { type: "receipt",    url: "evidence/d1-receipt.pdf", addedAt: new Date("2026-10-15T09:12:00Z") }
  ],
  comments: [
    { author: "Підтримка", text: "Запит передано мерчанту", createdAt: new Date("2026-10-15T10:00:00Z") },
    { author: "Олена",     text: "Показання лічильника 250 кВт·год", createdAt: new Date("2026-10-15T11:30:00Z") }
  ],
  threadId: T1,
  openedAt: new Date("2026-10-15T09:00:00Z")
});

// D2: шахрайство — вкладений об'єкт fraudDetails (є лише в цьому типі спорів)
upsert("disputes", {
  _id: D2,
  paymentId: 4, invoiceId: 3, merchantId: 3,
  merchantName: "ТелеЛінк",
  amount: 300.00, currency: "UAH",
  reason: "fraud", status: "under_review",
  openedBy: { userId: "u-2", displayName: "Андрій" },
  fraudDetails: { deviceId: "dev-9f3a", ip: "203.0.113.45", country: "PL", suspicious: true },
  evidence: [
    { type: "bank_statement", url: "evidence/d2-statement.pdf", addedAt: new Date("2026-11-10T08:00:00Z") }
  ],
  threadId: T2,
  openedAt: new Date("2026-11-10T07:45:00Z")
});

// D3: мінімальний спір — без evidence, comments, threadId
upsert("disputes", {
  _id: D3,
  paymentId: 4, invoiceId: 3, merchantId: 3,
  merchantName: "ТелеЛінк",
  amount: 300.00, currency: "UAH",
  reason: "other", status: "closed",
  openedBy: { userId: "u-2", displayName: "Андрій" },
  openedAt: new Date("2026-11-12T12:00:00Z"),
  resolvedAt: new Date("2026-11-12T15:00:00Z")
});

// D4: послугу не надано — serviceProof і tags (поля, яких немає в інших документах)
upsert("disputes", {
  _id: D4,
  paymentId: 1, invoiceId: 2, merchantId: 2,
  merchantName: "ВодоКанал Плюс",
  amount: 420.00, currency: "UAH",
  reason: "service_not_provided", status: "open",
  openedBy: { userId: "u-1", displayName: "Олена" },
  serviceProof: { provider: "ВодоКанал Плюс", ticketNo: "TCK-88231", outageDays: 3 },
  tags: ["відключення води", "жовтень"],
  openedAt: new Date("2026-11-14T10:20:00Z")
});

/* ---------- disputeThreads (reference на disputes) ---------- */
upsert("disputeThreads", {
  _id: T1, disputeId: D1, title: "Невідповідність суми за електроенергію", status: "open",
  messagesCount: 3, lastMessageAt: new Date("2026-10-16T09:15:00Z"),
  createdAt: new Date("2026-10-15T10:05:00Z")
});
upsert("disputeThreads", {
  _id: T2, disputeId: D2, title: "Підозріла операція за послуги зв'язку", status: "open",
  messagesCount: 1, lastMessageAt: new Date("2026-11-10T09:00:00Z"),
  createdAt: new Date("2026-11-10T08:30:00Z")
});

/* ---------- threadMessages (необмежена підколекція — через reference) ---------- */
upsert("threadMessages", {
  _id: ObjectId("66f1c4000000000000000001"), threadId: T1,
  author: { userId: "u-1", displayName: "Олена" },
  text: "У рахунку 250 кВт·год, а нараховано більше", createdAt: new Date("2026-10-15T10:10:00Z")
});
upsert("threadMessages", {
  _id: ObjectId("66f1c4000000000000000002"), threadId: T1,
  parentMessageId: ObjectId("66f1c4000000000000000001"),
  author: { userId: "support-1", displayName: "Підтримка" },
  text: "Перевіряємо показання з мерчантом", createdAt: new Date("2026-10-15T14:00:00Z")
});
upsert("threadMessages", {
  _id: ObjectId("66f1c4000000000000000003"), threadId: T1,
  parentMessageId: ObjectId("66f1c4000000000000000001"),
  author: { userId: "merchant-1", displayName: "ЕнергоСвіт" },
  text: "Перерахунок буде зроблено в наступному періоді", createdAt: new Date("2026-10-16T09:15:00Z")
});
upsert("threadMessages", {
  _id: ObjectId("66f1c4000000000000000004"), threadId: T2,
  author: { userId: "u-2", displayName: "Андрій" },
  text: "Я не здійснював цієї оплати з-за кордону", createdAt: new Date("2026-11-10T09:00:00Z")
});

/* ---------- merchantDisputeStats: денормалізований підсумок (_id = merchantId) ----------
   openCount = спори зі статусом open або under_review */
upsert("merchantDisputeStats", {
  _id: 1, merchantName: "ЕнергоСвіт", total: 1, openCount: 1,
  byReason: { amount_mismatch: 1 }, updatedAt: new Date("2026-10-15T09:00:00Z")
});
upsert("merchantDisputeStats", {
  _id: 2, merchantName: "ВодоКанал Плюс", total: 1, openCount: 1,
  byReason: { service_not_provided: 1 }, updatedAt: new Date("2026-11-14T10:20:00Z")
});
upsert("merchantDisputeStats", {
  _id: 3, merchantName: "ТелеЛінк", total: 2, openCount: 1,
  byReason: { fraud: 1, other: 1 }, updatedAt: new Date("2026-11-12T15:00:00Z")
});

print("disputes: " + sdb.disputes.countDocuments() +
      ", disputeThreads: " + sdb.disputeThreads.countDocuments() +
      ", threadMessages: " + sdb.threadMessages.countDocuments() +
      ", merchantDisputeStats: " + sdb.merchantDisputeStats.countDocuments());
