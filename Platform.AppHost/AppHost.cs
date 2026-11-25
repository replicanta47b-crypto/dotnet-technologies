// AppHost: оркестрація всієї платформи однією командою (бізнес-логіки тут немає).
// БД і Redis → сервіси → Aggregator → Gateway; усе адресується логічними іменами (service discovery).
var builder = DistributedApplication.CreateBuilder(args);

// ---- контейнери баз даних (Docker Desktop має бути запущений) ----
// Пароль SQL Server — параметр-секрет: dotnet user-secrets set "Parameters:sql-password" "<пароль>" --project Platform.AppHost
// (або вводиться в Dashboard під час першого запуску). У коді пароля немає.
var sqlPassword = builder.AddParameter("sql-password", secret: true);

var sql = builder.AddSqlServer("sql", sqlPassword)
                 .WithDataVolume();                          // дані переживають перезапуск
var paymentsDb = sql.AddDatabase("PaymentsDb");              // Лаб. 02: SQL + ADO.NET/Dapper
var billingDb = sql.AddDatabase("BillingDb");                // Лаб. 03: SQL + EF Core

var mongo = builder.AddMongoDB("mongo")
                   .WithDataVolume();                        // окремий volume для MongoDB
var disputesDb = mongo.AddDatabase("DisputesDb");            // Лаб. 04: MongoDB + Clean Architecture

// ---- Лаб. 06: Redis — розподілений кеш (L2) для Billing і Aggregator; рядок підключення ConnectionStrings:redis передає Aspire ----
var redis = builder.AddRedis("redis")
                   .WithDataVolume();                        // кеш переживає перезапуск контейнера

// ---- Лаб. 08: RabbitMQ — брокер повідомлень. WithDataVolume: черги й повідомлення переживають перезапуск контейнера;
// WithManagementPlugin: веб-інтерфейс керування (exchanges, черги, DLQ) — посилання «messaging» у Dashboard.
// Облікові дані фіксовані (лише локальна розробка): з volume брокер запам'ятовує користувача при першому старті, а згенерований
// Aspire випадковий пароль змінювався б між запусками → помилка автентифікації. Логін/пароль для Management UI: guest / guest. ----
var rabbitUser = builder.AddParameter("messaging-username", "guest");
var rabbitPassword = builder.AddParameter("messaging-password", "guest", secret: true);
var rabbit = builder.AddRabbitMQ("messaging", rabbitUser, rabbitPassword)
                    .WithDataVolume()
                    .WithManagementPlugin();

// Лаб. 07: WithHttpHealthCheck("/health/ready") — Dashboard показує Healthy / Degraded / Unhealthy кожного ресурсу, а WaitFor
// чекає, поки залежність стане ЗДОРОВОЮ (readiness: БД, кеш, downstream), а не лише запуститься.
// ---- мікросервіси: ім'я ресурсу (payments-api) = логічне ім'я для service discovery ----
// Профіль "https": окрім http (REST через Gateway) сервіс слухає https, де за ALPN узгоджується HTTP/2 для gRPC.
var payments = builder.AddProject<Projects.Payments_Api>("payments-api", launchProfileName: "https")
                      .WithReference(paymentsDb).WaitFor(paymentsDb)
                      .WithReference(rabbit).WaitFor(rabbit)                       // Лаб. 08: СПОЖИВАЧ подій Білінгу (копія Invoices)
                      .WithEnvironment("Database__InitializeOnStartup", "true")    // порожня БД у контейнері → db/p1 з Лаб. 01
                      .WithHttpHealthCheck("/health/ready", endpointName: "http");

var billing = builder.AddProject<Projects.Billing_Api>("billing-api", launchProfileName: "https")
                     .WithReference(billingDb).WaitFor(billingDb)                  // міграції й seed EF Core виконує сам сервіс
                     .WithReference(redis).WaitFor(redis)                          // Лаб. 06: сервіс із кешем (L1 + L2)
                     .WithReference(rabbit).WaitFor(rabbit)                        // Лаб. 08: ВИДАВЕЦЬ подій (invoice.*, merchant.*)
                     .WithHttpHealthCheck("/health/ready", endpointName: "http");

var disputes = builder.AddProject<Projects.Disputes_WebApi>("disputes-api", launchProfileName: "https")
                      .WithReference(disputesDb).WaitFor(disputesDb)               // індекси й seed створює сам сервіс
                      .WithReference(rabbit).WaitFor(rabbit)                       // Лаб. 08: СПОЖИВАЧ подій Білінгу (merchantName)
                      .WithHttpHealthCheck("/health/ready", endpointName: "http");

// ---- Aggregator: паралельно опитує сервіси ----
var aggregator = builder.AddProject<Projects.Platform_Aggregator>("aggregator")
                        .WithReference(payments).WithReference(billing).WithReference(disputes)
                        .WithReference(redis).WaitFor(redis)                       // Лаб. 06: кеш агрегованих відповідей
                        .WaitFor(payments).WaitFor(billing).WaitFor(disputes)      // Лаб. 07: чекає на Healthy (readiness), а не лише на запуск процесу
                        .WithHttpHealthCheck("/health/ready", endpointName: "http");

// ---- Gateway: єдина точка входу (фіксований порт 5000) ----
builder.AddProject<Projects.Platform_ApiGateway>("gateway")
       .WithHttpEndpoint(port: 5000, name: "public")
       .WithReference(payments).WithReference(billing).WithReference(disputes)
       .WithReference(aggregator)
       .WaitFor(aggregator)
       .WithHttpHealthCheck("/health/ready", endpointName: "public");

builder.Build().Run();
