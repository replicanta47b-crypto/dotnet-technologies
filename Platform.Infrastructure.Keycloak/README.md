# Platform.Infrastructure.Keycloak — Keycloak як код (Pulumi, C#)

Realm `DevRealmDrivers`, 4 resource servers (scopes, resources, role policies, scope permissions), 4 realm-ролі,
клієнти (фабрика) і тестові користувачі створюються **кодом**, а не вручну в Admin Console.

## Запуск (Keycloak уже працює в AppHost: http://localhost:8080, admin / admin)

```powershell
cd Platform.Infrastructure.Keycloak
pulumi login --local                       # стан стеку — у файловому сховищі (без Pulumi Cloud)
$env:PULUMI_CONFIG_PASSPHRASE = ""         # порожня passphrase: лише локальна розробка
pulumi stack init dev
pulumi up --yes
pulumi stack output                         # RealmUrl, AdminConsoleUrl, ClientIds, TestUsers
pulumi stack output ClientSecrets --show-secrets
```

Конфіг (необов'язково; за замовчуванням — значення локальної розробки):
`pulumi config set keycloakUrl http://localhost:8080`, `keycloakAdminUser`, `keycloakAdminPassword`, `testUserPassword`.

## Що створюється

| Ресурс | Pulumi-тип | Файл |
|---|---|---|
| Realm `DevRealmDrivers` | `Keycloak.Realm` | Program.cs |
| Ролі admin / manager / user / guest | `Keycloak.Role` | Program.cs |
| Resource servers `payments-api`, `billing-api`, `disputes-api`, `settlements-api` | `Keycloak.OpenId.Client` (+ Authorization Services) | Program.cs |
| Scopes (`payments:read`, …) і resource кожного сервісу | `ClientAuthorizationScope`, `ClientAuthorizationResource` | Program.cs |
| Permission policies (по одній на роль) | `ClientRolePolicy` | Program.cs |
| Permissions (scope → ролі) | `ClientAuthorizationPermission` | PlatformModel.cs (матриця) |
| Клієнти `postman` (Authorization Code + PKCE), `aggregator-service` (Client Credentials) | `ClientFactory` + extension methods | ClientFactory.cs |
| Audience mappers, default scopes, роль service account | `WithAudience`, `WithDefaultScopes`, `WithServiceAccountRole` | ClientFactory.cs |
| Тестові користувачі `test-admin/-manager/-user/-guest` з **тимчасовим** паролем | `Keycloak.User`, `Keycloak.UserRoles` | Program.cs |

Матриця «роль → permissions» задана в одному місці — `PlatformModel.RolePermissions`.
Скинути все: `pulumi destroy`.
