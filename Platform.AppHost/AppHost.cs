// AppHost: оркестрація всієї платформи однією командою (бізнес-логіки тут немає).
// БД → сервіси → Aggregator → Gateway; усе адресується логічними іменами (service discovery).
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

// ---- мікросервіси: ім'я ресурсу (payments-api) = логічне ім'я для service discovery ----
var payments = builder.AddProject<Projects.Payments_Api>("payments-api")
                      .WithReference(paymentsDb).WaitFor(paymentsDb)
                      .WithEnvironment("Database__InitializeOnStartup", "true");   // порожня БД у контейнері → db/p1 з Лаб. 01

var billing = builder.AddProject<Projects.Billing_Api>("billing-api")
                     .WithReference(billingDb).WaitFor(billingDb);                 // міграції й seed EF Core виконує сам сервіс

var disputes = builder.AddProject<Projects.Disputes_WebApi>("disputes-api")
                      .WithReference(disputesDb).WaitFor(disputesDb);              // індекси й seed створює сам сервіс

// ---- Aggregator: паралельно опитує сервіси ----
var aggregator = builder.AddProject<Projects.Platform_Aggregator>("aggregator")
                        .WithReference(payments).WithReference(billing).WithReference(disputes)
                        .WaitFor(payments).WaitFor(billing).WaitFor(disputes);

// ---- Gateway: єдина точка входу (фіксований порт 5000) ----
builder.AddProject<Projects.Platform_ApiGateway>("gateway")
       .WithHttpEndpoint(port: 5000, name: "public")
       .WithReference(payments).WithReference(billing).WithReference(disputes)
       .WithReference(aggregator)
       .WaitFor(aggregator);

builder.Build().Run();
