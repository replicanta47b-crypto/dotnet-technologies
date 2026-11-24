/* =====================================================================
   Проєкт №3 — Контекст «Спори» (MongoDB). База: disputes_db.
   Запуск: mongosh "mongodb://localhost:27017" db/p3/collections.js
   Ідемпотентно: колекції створюються або оновлюється валідатор (collMod),
   індекси створюються через createIndex (повторний виклик нічого не змінює).

   Моделювання:
     disputes             — агрегат спору: EMBED openedBy, evidence[], comments[],
                            REFERENCE threadId -> disputeThreads (гібрид)
     disputeThreads       — тема обговорення спору
     threadMessages       — необмежено велика підколекція повідомлень (REFERENCE threadId)
     merchantDisputeStats — денормалізований підсумок спорів мерчанта для швидкого читання
   Дублювання: merchantName (власник — Білінг), amount/currency (знімок з PaymentsDb).
   ===================================================================== */
const ddb = db.getSiblingDB("disputes_db");

function ensureCollection(name, validator) {
  const exists = ddb.getCollectionNames().includes(name);
  const opts = { validator: validator, validationLevel: "strict", validationAction: "error" };
  if (!exists) {
    ddb.createCollection(name, opts);
    print("created: " + name);
  } else {
    ddb.runCommand(Object.assign({ collMod: name }, opts));
    print("validator updated: " + name);
  }
}

/* ---- disputes: схема гнучка — набір додаткових полів залежить від причини спору ---- */
ensureCollection("disputes", { $jsonSchema: {
  bsonType: "object",
  required: ["paymentId", "invoiceId", "merchantId", "merchantName", "amount", "currency",
             "reason", "status", "openedBy", "openedAt"],
  properties: {
    paymentId:    { bsonType: "number", description: "Id платежу в PaymentsDb (без FK)" },
    invoiceId:    { bsonType: "number", description: "Id рахунку в BillingDb (без FK)" },
    merchantId:   { bsonType: "number", description: "Id мерчанта в BillingDb (без FK)" },
    merchantName: { bsonType: "string", description: "денормалізовано з Білінгу" },
    amount:       { bsonType: "number", minimum: 0, description: "знімок суми на момент відкриття спору" },
    currency:     { enum: ["UAH", "USD", "EUR"] },
    reason:       { enum: ["fraud", "service_not_provided", "duplicate", "amount_mismatch", "other"] },
    status:       { enum: ["open", "under_review", "won", "lost", "closed"] },
    openedBy: {
      bsonType: "object",
      required: ["userId", "displayName"],
      properties: { userId: { bsonType: "string" }, displayName: { bsonType: "string" } }
    },
    evidence: {
      bsonType: "array",
      items: {
        bsonType: "object",
        required: ["type", "addedAt"],
        properties: { type: { bsonType: "string" }, url: { bsonType: "string" }, addedAt: { bsonType: "date" } }
      }
    },
    comments: {
      bsonType: "array",
      items: {
        bsonType: "object",
        required: ["author", "text", "createdAt"],
        properties: { author: { bsonType: "string" }, text: { bsonType: "string" }, createdAt: { bsonType: "date" } }
      }
    },
    threadId: { bsonType: "objectId" },
    openedAt: { bsonType: "date" },
    resolvedAt: { bsonType: "date" }
  }
} });

ensureCollection("disputeThreads", { $jsonSchema: {
  bsonType: "object",
  required: ["disputeId", "title", "status", "createdAt"],
  properties: {
    disputeId:     { bsonType: "objectId" },
    title:         { bsonType: "string" },
    status:        { enum: ["open", "closed"] },
    messagesCount: { bsonType: "number", minimum: 0 },
    lastMessageAt: { bsonType: "date" },
    createdAt:     { bsonType: "date" }
  }
} });

ensureCollection("threadMessages", { $jsonSchema: {
  bsonType: "object",
  required: ["threadId", "author", "text", "createdAt"],
  properties: {
    threadId:        { bsonType: "objectId" },
    parentMessageId: { bsonType: "objectId" },
    author: {
      bsonType: "object",
      required: ["userId", "displayName"],
      properties: { userId: { bsonType: "string" }, displayName: { bsonType: "string" } }
    },
    text:      { bsonType: "string" },
    createdAt: { bsonType: "date" }
  }
} });

ensureCollection("merchantDisputeStats", { $jsonSchema: {
  bsonType: "object",
  required: ["_id", "merchantName", "total", "openCount"],
  properties: {
    _id:          { bsonType: "number", description: "merchantId" },
    merchantName: { bsonType: "string" },
    total:        { bsonType: "number", minimum: 0 },
    openCount:    { bsonType: "number", minimum: 0 },
    byReason:     { bsonType: "object" },
    updatedAt:    { bsonType: "date" }
  }
} });

/* ---- Індекси (кожен під запит) ---- */
// «спори за платежем» (картка платежу)
ddb.disputes.createIndex({ paymentId: 1, openedAt: -1 }, { name: "ix_disputes_payment_opened" });
// «спори мерчанта за статусом» (кабінет мерчанта, черга розгляду)
ddb.disputes.createIndex({ merchantId: 1, status: 1 }, { name: "ix_disputes_merchant_status" });
// «спори користувача» (особистий кабінет)
ddb.disputes.createIndex({ "openedBy.userId": 1 }, { name: "ix_disputes_opener" });
// часткові: лише спори про шахрайство, з даними пристрою (антифрод-аналітика)
ddb.disputes.createIndex({ "fraudDetails.deviceId": 1 },
  { name: "ix_disputes_fraud_device_partial", partialFilterExpression: { "fraudDetails.deviceId": { $exists: true } } });
// «обговорення спору»
ddb.disputeThreads.createIndex({ disputeId: 1 }, { name: "ix_threads_dispute", unique: true });
// «повідомлення обговорення в хронологічному порядку» (пагінація)
ddb.threadMessages.createIndex({ threadId: 1, createdAt: 1 }, { name: "ix_messages_thread_created" });

print("disputes_db: collections and indexes are ready");
