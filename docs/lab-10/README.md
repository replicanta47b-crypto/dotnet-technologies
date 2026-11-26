# Лабораторна 10 — Розгортання мікросервісів у Kubernetes (Aspir8 + ручний деплой)

Гілка `lab-10`. Кластер — Kubernetes у Docker Desktop (контекст `docker-desktop`).

## Відповідність термінам завдання

| У завданні | У нашій платформі |
|---|---|
| orders-api | `payments-api` (Лаб. 02) |
| catalog-api | `billing-api` (Лаб. 03) |
| reviews-api | `disputes-api` (Лаб. 04, MongoDB) |
| `/api/products` | `/api/merchants` (billing-api через Gateway; після Лаб. 09 потрібен JWT) |
| ключ підпису JWT `Jwt__SecretKey` | `Auth__AuthService__SecretKey` (Лаб. 09) |

IdentityServer і Keycloak (Лаб. 09, ч. 2–3) у кластер не переносяться: у `AppHost.cs` вони обгорнуті в `if (!builder.ExecutionContext.IsPublishMode)`, тож `aspirate generate` їх не бачить. В кластері працює Auth Service.

## Частина 1. Aspir8 (aspirate)

```powershell
kubectl version --client
kubectl config use-context docker-desktop
dotnet tool install -g aspirate

cd Platform.AppHost
aspirate init                 # один раз: реєстр і тег образів
aspirate generate             # збирає образи і створює ./aspirate-output
aspirate apply                # або: kubectl apply -k ./aspirate-output
kubectl get pods -w
kubectl get services
kubectl logs deploy/gateway
kubectl port-forward service/gateway 8080:<порт-сервісу>
```

Aspirate шифрує секрети й просить пароль (його потрібно пам'ятати для `apply`). Перед комітом перегляньте `aspirate-output` — у репозиторій не повинно потрапити нешифрованих паролів. Після перевірки: `aspirate destroy`.

Якщо aspirate під час `generate` намагається запушити образи у віддалений реєстр, використайте `aspirate generate --image-pull-policy IfNotPresent` та реєстр-заглушку (образи залишаються в локальному Docker Desktop).

## Фактичний хід Частини 1 (нюанси, які виявились)

1. `aspirate init` створює `Platform.AppHost/aspirate.json`; стан і секрети — в `aspirate-state.json` (не комітиться, у `.gitignore`).
2. Власний виклик AppHost з боку aspirate у нашому середовищі завершувався помилкою, тому маніфест згенеровано напряму:
   `dotnet run --project . -- --publisher manifest --output-path aspire-manifest.json`, далі
   `aspirate generate --skip-build --image-pull-policy IfNotPresent -m aspire-manifest.json`.
3. Образи зібрано через `docker/Dockerfile.service` (скрипт `scripts/k8s-aspirate-images.ps1`), бо `dotnet publish /t:PublishContainer` образів не створив. Теги `platform/<service>:latest`, `imagePullPolicy: IfNotPresent`.
4. У publish-режимі блок IdentityServer/Keycloak вимкнено (`IsPublishMode`), для gateway задано `targetPort: 8080`.
5. Aspirate **не генерує liveness/readiness probes** і створює Service на порту **8080** (не 80), на відміну від ручних маніфестів.
6. Payments API у Kubernetes сам створює БД `PaymentsDb` (`EnsureDatabaseExistsAsync`): локально БД створює Aspire, у кластері цього ніхто не робить.
7. Kubelet кешує образ `latest`; після перебудови використовуйте унікальний тег + `kubectl set image` (або `rollout restart`, якщо образ нового тегу).
8. Service discovery: у Gateway адреси кластерів `http://auth-api` без порту, а Service від aspirate слухає 8080 → 504 (ConnectTimeout). Виправлення після `aspirate apply`:
   `kubectl set env deploy/gateway ReverseProxy__Clusters__<cluster>__Destinations__d1__Address=http://<service>:8080` для кластерів auth, payments, billing, disputes, settlements, aggregator.
9. Результат: усі 12 pod у `Running`; `POST /auth/login` і `GET /api/merchants` через `kubectl port-forward service/gateway 8081:8080` повертають 200.
10. Секрети: у `aspirate-output` паролі Mongo/Redis та ключ JWT замінено на `<redacted>` перед комітом; `aspirate-state.json` не комітиться.

## Частина 2–3. Ручний деплой

Файли:

```
Services/Billing/Billing.Api/Dockerfile   # billing-api (catalog)
Platform.ApiGateway/Dockerfile            # gateway
Services/Auth/Auth.Api/Dockerfile         # auth-api (потрібен, щоб отримати JWT)
k8s/manual/infra/{sql-server,redis,rabbitmq}.yaml
k8s/manual/{billing-api,auth-api,gateway}/{configmap,deployment,service}.yaml
scripts/k8s-manual.ps1                    # build + Secrets + apply
```

Запуск однією командою з кореня репозиторію:

```powershell
.\scripts\k8s-manual.ps1 -Action up
kubectl port-forward service/gateway 8080:80
```

Що робить скрипт: `docker build` трьох образів (`:v1`, `imagePullPolicy: IfNotPresent`), створює Secrets через `kubectl create secret` (рядки підключення, пароль SQL, ключ підпису JWT — у YAML їх немає), застосовує інфраструктуру (SQL Server, Redis, RabbitMQ) і сервіси, чекає на `rollout status`.

Особливості маніфестів:
- Deployment з 2 репліками; `livenessProbe` → `/health/live`, `readinessProbe` → `/health/ready`, `startupProbe` → `/health/startup` (endpoints Лаб. 07 зареєстровані в усіх середовищах через `MapDefaultEndpoints`).
- Service `ClusterIP`, `port: 80 → targetPort: 8080`; ім'я Service = DNS-ім'я (`http://billing-api`).
- ConfigMap `gateway-config`: `services__billing-api__http__0=http://billing-api` (формат service discovery з Лаб. 05, код Gateway не змінюється); Secrets підключені через `envFrom.secretRef`.

### Перевірка (запит через port-forward)

```powershell
$t = (Invoke-RestMethod -Method Post http://localhost:8080/auth/login -ContentType application/json `
      -Body '{"userNameOrEmail":"admin","password":"Admin#12345"}').accessToken
Invoke-RestMethod http://localhost:8080/api/merchants -Headers @{Authorization="Bearer $t"}   # Gateway → billing-api
kubectl logs deploy/gateway | Select-String billing
```

### Контрольні точки

```powershell
# 1. Відновлення пода
kubectl delete pod -l app=billing-api --wait=false ; kubectl get pods -l app=billing-api -w
# 2. Readiness при недоступній базі: под лишається Running, але 0/1 Ready (не перезапускається)
kubectl scale deploy/sql --replicas=0 ; kubectl get pods -l app=billing-api -w
kubectl describe pod -l app=billing-api        # Readiness probe failed ... /health/ready
kubectl scale deploy/sql --replicas=1          # поди повертаються в Ready
# 3. ConfigMap потребує rollout restart
kubectl rollout restart deploy/gateway
# 4. Secret лише в Base64
kubectl get secret gateway-secrets -o yaml ; # значення: [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('...'))
```

Прибирання: `.\scripts\k8s-manual.ps1 -Action down`.

## Порівняння з aspirate-output (додатково)

| | Aspirate | Ручні маніфести |
|---|---|---|
| Джерело істини | модель AppHost | YAML у репозиторії |
| Швидкість | усі ресурси автоматично | потрібно писати кожен файл |
| Probes | не генеруються з `WithHttpHealthCheck` | задано явно (live/ready/startup) |
| Репліки, ресурси, pull policy | типові значення / прапорці | повний контроль |
| Секрети | шифруються aspirate (пароль) | `kubectl create secret`, значень у YAML немає |
| Інфраструктура | контейнери з AppHost (SQL, Mongo, Redis, RabbitMQ) | окремі мінімальні маніфести |

## Контрольні питання

1. **Aspir8** перетворює маніфест AppHost на Kubernetes YAML (Deployment, Service, ConfigMap, Secret, kustomize) і збирає образи. Плюси: швидко, узгоджено з AppHost. Мінуси: менше контролю (probes, ресурси, репліки), залежність від версії Aspire та інструмента.
2. **Deployment** підтримує задану кількість реплік пода і керує оновленнями; **Service** — стабільна адреса (DNS + балансування) для набору подів, сам нічого не запускає.
3. Service знаходить поди за **label selector**. Типи: ClusterIP, NodePort, LoadBalancer, ExternalName (+ headless).
4. **Liveness** — «процес живий?»; збій → kubelet перезапускає контейнер. **Readiness** — «готовий приймати трафік?»; збій → под виключається з Service, без перезапуску.
5. **ConfigMap** — відкриті налаштування, **Secret** — чутливі дані (окремі права доступу RBAC, можливе шифрування etcd). Паролі в ConfigMap видно будь-кому з доступом на читання і в `kubectl describe`.
6. Вбудований DNS (CoreDNS): `http://billing-api` → ClusterIP Service у тому самому namespace (`billing-api.default.svc.cluster.local`).
7. Gateway читає адреси з конфігурації `services:<name>:http:0`; Aspire і ConfigMap задають їх однаково (`__` ↔ `:`).
8. `IfNotPresent` — не завантажувати образ із реєстру, якщо він уже є на вузлі; локально зібраний образ (`docker build`) інакше не знайшовся б.
9. Браузер/curl → `localhost:8080` → `kubectl port-forward` → Service `gateway` (:80) → под Gateway (:8080) → YARP, перевірка JWT → Service `billing-api` → под billing-api → SQL/Redis/RabbitMQ за DNS-іменами.
