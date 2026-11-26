# Лабораторна 9 — Автентифікація та авторизація в мікросервісній архітектурі

Гілка: `lab-09`. Три підходи в одній платформі (єдина бібліотека `Platform.Security` приймає токени всіх трьох емітентів за `iss`).

## 1. Адаптація до домену платформи

| Форма лабораторної | У нашій платформі | Область (scopes) |
|---|---|---|
| orders-api | `payments-api` (Лаб. 02): customers, payments, invoices | payments:read/create/update/delete/approve |
| products-api | `billing-api` (Лаб. 03): merchants, billing invoices | billing:read/publish/update/delete/... |
| payments-api (проводки) | **новий** `settlements-api` (тільки admin) | settlements:read/process/refund |
| додатково | `disputes-api` (Лаб. 07) теж захищений | disputes:read/create/update/delete |

## 2. Частина 1 — Auth Service (ASP.NET Identity + JWT)
`Services/Auth/Auth.Api`: окрема `IdentityDb`, JWT 15 хв (HS256, iss `platform-auth`, aud `platform-api`), refresh-токен 7 днів у БД (HttpOnly cookie, ротація/відкликання), mock-email (`/auth/dev/outbox`), ролі User/Admin. Усі ендпоінти таблиці 2 (`/auth/*`, `/users/*`). Gateway маршрутизує `/auth/**` (анонімно) і `/users/**`, перевіряє JWT до проксіювання; сервіси перевіряють JWT самостійно.

## 3. Частина 2 — Duende IdentityServer
`Platform.IdentityServer`: окремий SQL Server `identityserver-sql`, БД `IdentityServerDb`, три EF-контексти з міграціями (Configuration, PersistedGrant, Application/Identity), Quickstart UI. Клієнти: `postman` (Authorization Code), `aggregator_service` (Client Credentials), `swagger` (публічний, PKCE). Політики на scope.

Контрольні точки (виконано):
- публічний клієнт `swagger` **без PKCE** → `invalid_request: code challenge required`;
- Authorization Code + PKCE для alice → access-токен зі `sub`, `scope: openid profile payments.read`, `aud: payments_api`;
- Client Credentials (`aggregator_service`) → токен **без `sub`**.

## 4. Частина 3 — Keycloak + Pulumi (C#)
`Platform.Infrastructure.Keycloak`: realm `DevRealmDrivers`, resource servers з scopes, ролі admin/manager/user/guest + permission-політики, клієнти через фабрику з extension-методами (scopes, audience mapper), тестові користувачі (тимчасовий пароль), outputs `RealmUrl`, `AdminConsoleUrl`, `ClientIds`, `ClientSecrets`. Сервіси використовують `PermissionRequirement` + `[RequirePermission]` на кожному ендпоінті (RPT через UMA ticket).

## 5. Запуск
```powershell
dotnet build Platform.slnx
.\scripts\generate-migrations.ps1      # міграції (один раз)
dotnet run --project Platform.AppHost
cd Platform.Infrastructure.Keycloak; pulumi login --local; pulumi stack init dev; pulumi up --yes
```
Postman: `postman/Platform-Lab09.postman_collection.json`.

## 6. Матриця фактичних відповідей (через Gateway, Keycloak RPT)
Коди: 200/204 — доступ є; 400/404/409 — авторизацію пройдено, відхилено бізнес-валідацією (порожнє тіло/неіснуючий id/конфлікт); **403 — заборонено**.

| Ендпоінт | Маршрут | admin | manager | user | guest |
|---|---|---|---|---|---|
| customers GET list | `GET /api/customers` | 200 | 200 | 200 | 403 |
| customers GET id | `GET /api/customers/{id}` | 200 | 404 | 404 | 403 |
| customers POST | `POST /api/customers` | 400 | 403 | 400 | 403 |
| customers PUT | `PUT /api/customers/{id}` | 400 | 400 | 403 | 403 |
| customers DELETE | `DELETE /api/customers/{id}` | 204 | 403 | 403 | 403 |
| payments GET id | `GET /api/payments/{id}` | 200 | 200 | 200 | 403 |
| customer payments | `GET /api/customers/{id}/payments` | 404 | 404 | 404 | 403 |
| payments POST | `POST /api/payments` | 400 | 403 | 400 | 403 |
| payments allocations | `POST /api/payments/{id}/allocations` | 400 | 400 | 403 | 403 |
| payments status | `POST /api/payments/{id}/status` | 400 | 400 | 403 | 403 |
| payments authorize | `POST /api/payments/{id}/authorize` | 400 | 400 | 403 | 403 |
| payments DELETE | `DELETE /api/payments/{id}` | 409 | 403 | 403 | 403 |
| invoices(payments) GET list | `GET /api/invoices` | 200 | 200 | 200 | 403 |
| invoices(payments) GET id | `GET /api/invoices/{id}` | 200 | 200 | 200 | 403 |
| invoices(payments) POST | `POST /api/invoices` | 400 | 403 | 400 | 403 |
| invoices(payments) PUT | `PUT /api/invoices/{id}` | 400 | 400 | 403 | 403 |
| merchants GET list | `GET /api/merchants` | 200 | 200 | 200 | 200 |
| merchants GET id | `GET /api/merchants/{id}` | 200 | 200 | 200 | 200 |
| merchants POST | `POST /api/merchants` | 400 | 403 | 403 | 403 |
| merchants PUT name | `PUT /api/merchants/{id}/name` | 400 | 400 | 403 | 403 |
| billing GET list | `GET /api/billing/invoices` | 200 | 200 | 200 | 200 |
| billing overdue | `GET /api/billing/invoices/overdue` | 200 | 200 | 200 | 200 |
| billing by category | `GET /api/categories/{slug}/invoices` | 200 | 200 | 200 | 200 |
| billing GET id | `GET /api/billing/invoices/{id}` | 200 | 200 | 200 | 200 |
| billing POST | `POST /api/billing/invoices` | 400 | 403 | 403 | 403 |
| billing pay | `POST /api/billing/invoices/{id}/pay` | 409 | 409 | 403 | 403 |
| billing void | `POST /api/billing/invoices/{id}/void` | 409 | 409 | 403 | 403 |
| billing due-date | `PUT /api/billing/invoices/{id}/due-date` | 400 | 400 | 403 | 403 |
| billing surcharges | `POST /api/billing/invoices/{id}/surcharges` | 400 | 400 | 403 | 403 |
| billing categories | `POST /api/billing/invoices/{id}/categories` | 400 | 400 | 403 | 403 |
| billing DELETE | `DELETE /api/billing/invoices/{id}` | 409 | 403 | 403 | 403 |
| disputes GET list | `GET /api/disputes` | 200 | 200 | 200 | 403 |
| disputes GET id | `GET /api/disputes/{id}` | 400 | 400 | 400 | 403 |
| disputes POST | `POST /api/disputes` | 400 | 403 | 400 | 403 |
| disputes PUT | `PUT /api/disputes/{id}` | 400 | 400 | 403 | 403 |
| disputes comments | `POST /api/disputes/{id}/comments` | 400 | 403 | 400 | 403 |
| disputes DELETE | `DELETE /api/disputes/{id}` | 400 | 403 | 403 | 403 |
| settlements GET list | `GET /api/settlements` | 200 | 403 | 403 | 403 |
| settlements process | `POST /api/settlements/process` | 400 | 403 | 403 | 403 |
| settlements refund | `POST /api/settlements/refund` | 404 | 403 | 403 | 403 |

Контрольні точки Keycloak: manager `PUT /api/customers/{id}` → 400 (дозволено), `DELETE` → **403** (не 500); user на всіх ендпоінтах settlements → **403**.

## 7. Розшифровані JWT
**Auth Service (admin)** — iss `platform-auth`, aud `platform-api`, ttl 15 хв (exp−nbf=900):
```json
{"sub":"97c9a21b-647e-4126-be58-fef05f7a2557","userId":"97c9a21b-...","unique_name":"admin","email":"admin@platform.local","role":["User","Admin"],"iss":"platform-auth","aud":"platform-api"}
```
**IdentityServer Client Credentials** (немає `sub`):
```json
{"iss":"http://localhost:5300","aud":["billing_api","disputes_api","payments_api"],"client_id":"aggregator_service","scope":["billing.read","disputes.read","payments.read"]}
```
**Keycloak manager (RPT для payments-api)**:
```json
{"iss":"http://localhost:8080/realms/DevRealmDrivers","aud":"payments-api","azp":"postman","preferred_username":"test-manager","realm_access":{"roles":["manager"]},"authorization":{"permissions":[{"rsname":"payments","scopes":["payments:approve","payments:update","payments:read"]}]}}
```
