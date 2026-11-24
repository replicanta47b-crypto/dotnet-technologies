var builder = DistributedApplication.CreateBuilder(args);

// База даних: ім'я ресурсу = ім'я рядка підключення, яке читає API (ConnectionStrings:PaymentsDb).
// Значення береться з appsettings.json цього AppHost (локальний SQL Server із Лаб. 01).
var paymentsDb = builder.AddConnectionString("PaymentsDb");

// API отримує рядок підключення через WithReference → змінна ConnectionStrings__PaymentsDb
builder.AddProject<Projects.Payments_Api>("payments-api")
       .WithReference(paymentsDb)
       .WithExternalHttpEndpoints();

// Billing (EF Core): окремий мікросервіс зі своєю БД BillingDb (рядок підключення ConnectionStrings:BillingDb)
var billingDb = builder.AddConnectionString("BillingDb");

builder.AddProject<Projects.Billing_Api>("billing-api")
       .WithReference(billingDb)
       .WithExternalHttpEndpoints();

builder.Build().Run();
