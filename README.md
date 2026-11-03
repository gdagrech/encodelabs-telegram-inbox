# Bandeja conversacional de Telegram — EncodeLabs

Bandeja web donde un operador recibe, lee y responde en tiempo real los mensajes de texto que los
usuarios le envían a un bot de Telegram.

Todo el stack levanta con **un solo comando** después de completar el `.env`: API, consumidor,
PostgreSQL, RabbitMQ, ngrok y el frontend. El webhook de Telegram se registra **solo**, usando la
URL pública que ngrok asigna en cada arranque.

```
Telegram ──HTTPS──► ngrok ──► Webhook (valida secreto) ──► RabbitMQ (durable, confirms)
                                                                │
                                                        Consumidor (ACK manual)
                                                                │
                                                    PostgreSQL ─┴─► SignalR ──► Angular
```

| | |
|---|---|
| **Backend** | .NET 10 · ASP.NET Core · Clean Architecture (Domain → Application → Infrastructure/Api) |
| **Frontend** | Angular 22 (standalone, signals, zoneless) servido por nginx |
| **Base de datos** | PostgreSQL 18 + EF Core 10 con migraciones |
| **Mensajería** | RabbitMQ 4 (cola durable + dead-letter queue) |
| **Tiempo real** | SignalR |
| **Exposición pública** | ngrok (agente en contenedor) |

---

## Evaluación en 10 minutos

Si tenés poco tiempo, este es el recorrido más corto que muestra todo lo que importa.

**Minutos 1 a 4 — que funcione.** Completá el `.env` (sección 2) y corré `docker compose up --build`.
Desde cero tarda unos 20 segundos en llegar a la línea que prueba que el registro automático
funcionó:

```
Telegram webhook registered for https://....ngrok-free.app/api/telegram/webhook
```

Escribile al bot desde Telegram y abrí http://localhost:4200. El contacto aparece **sin recargar**.
Respondé desde la bandeja y el mensaje llega al chat.

**Minutos 5 a 7 — que esté bien resuelto.** Tres archivos, en este orden:

| Archivo | Qué muestra |
|---|---|
| [docs/architecture/overview.md](docs/architecture/overview.md) | Cada caja del C4 mapeada a su archivo, el esquema de datos y los modos de falla |
| [docs/adr/](docs/adr/) | Las cuatro decisiones, con las alternativas que se descartaron y por qué |
| [src/backend/EncodeLabs.Domain/Messages/Message.cs](src/backend/EncodeLabs.Domain/Messages/Message.cs) | El mecanismo que resuelve el requisito 4 sin condiciones de carrera |

**Minutos 8 a 10 — que sea honesto.** [docs/validation-checklist.md](docs/validation-checklist.md)
traza cada requisito a la evidencia que lo respalda, y
[docs/code-review.md](docs/code-review.md) registra 25 hallazgos propios, incluido **un juicio que
se revirtió al verlo en ejecución** (CR-010), el defecto que esa corrección destapó (CR-017), un
comentario que declaraba lo contrario de lo que hacía el código (CR-018) y **un defecto que
introdujo una corrección anterior** (CR-025).

**Si querés romperlo:** `docker compose stop rabbitmq` y escribile al bot. El webhook devuelve `503`,
Telegram reintenta y el mensaje entra solo cuando levantás el broker. Nada se pierde.

---

## 1. Requisitos previos

| Requisito | Detalle |
|---|---|
| **Docker + Docker Compose** | Docker Desktop reciente, o Docker Engine con el plugin `compose` v2. Es lo único que hace falta: **no** necesitás .NET ni Node instalados. |
| **Bot de Telegram** | Creado con [@BotFather](https://t.me/BotFather) → `/newbot`. Guardá el token que devuelve. |
| **Cuenta de ngrok** | Gratuita alcanza. Necesitás el authtoken de [dashboard.ngrok.com](https://dashboard.ngrok.com/get-started/your-authtoken). |
| **Una cuenta de Telegram** | Para escribirle al bot y probar el circuito completo. |
| **Puertos libres** | `4200` (bandeja), `8080` (API), `15672` (consola RabbitMQ), `4040` (inspector de ngrok). |

> **ngrok gratuito:** solo admite **una sesión de agente simultánea**. Si ya tenés un `ngrok`
> corriendo en tu máquina, cerralo antes de levantar el stack o el túnel va a fallar con
> `ERR_NGROK_108`.

---

## 2. Completar el `.env`

```bash
cp .env.example .env
```

Abrí `.env` y completá:

| Variable | De dónde sale |
|---|---|
| `TELEGRAM_BOT_TOKEN` | El token que te dio BotFather. |
| `TELEGRAM_WEBHOOK_SECRET_TOKEN` | **Lo generás vos.** Es el secreto que Telegram devuelve en el header `X-Telegram-Bot-Api-Secret-Token` y es lo único que distingue una entrega legítima de cualquier request a la URL pública. Generalo con `openssl rand -hex 32`. Entre 1 y 256 caracteres, solo `A-Z a-z 0-9 _ -`. |
| `NGROK_AUTHTOKEN` | Tu authtoken de ngrok. |
| `POSTGRES_PASSWORD` | La elegís vos. Usá solo alfanuméricos: va embebida en una connection string. |
| `RABBITMQ_DEFAULT_PASS` | La elegís vos. Solo alfanuméricos: va embebida en una URI `amqp://`. |

`POSTGRES_DB`, `POSTGRES_USER` y `RABBITMQ_DEFAULT_USER` ya vienen con valores por defecto
razonables.

> `.env` está en `.gitignore` y **no debe commitearse**. `.env.example` solo tiene nombres y
> placeholders, nunca valores reales.

---

## 3. Levantar la solución

```bash
docker compose up --build
```

Eso es todo. Corre en primer plano a propósito, para que se vea el arranque; si preferís la
terminal libre, `docker compose up -d --build` y después `docker compose logs -f api`.

El arranque hace, en orden:

1. Levanta PostgreSQL y RabbitMQ y **espera a que estén sanos** (healthchecks).
2. La API aplica las migraciones de EF Core (con reintentos mientras la base termina de arrancar).
3. La API empieza a escuchar y el consumidor de RabbitMQ declara la cola durable y su dead-letter.
4. ngrok abre el túnel contra el puerto de la API.
5. La API **descubre la URL pública** consultando el agente de ngrok y **registra el webhook** en
   Telegram con el secreto configurado, reintentando si el túnel todavía no está listo.

Sabés que quedó listo cuando aparece en el log:

```
info: EncodeLabs.Infrastructure.Telegram.TelegramBotApiClient[0]
      Telegram webhook registered for https://xxxx-xx-xx-xx-xx.ngrok-free.app/api/telegram/webhook
info: EncodeLabs.Api.BackgroundServices.TelegramWebhookRegistrationService[0]
      Telegram reports webhook https://xxxx-....ngrok-free.app/api/telegram/webhook with 0 pending update(s). Last error: none.
```

### URLs

| Servicio | URL |
|---|---|
| **Bandeja (lo que se usa)** | **http://localhost:4200** |
| API | http://localhost:8080 |
| Estado del webhook (sin secretos) | http://localhost:8080/api/telegram/webhook-status |
| Health de la API | http://localhost:8080/health |
| Consola de RabbitMQ | http://localhost:15672 (usuario/clave del `.env`) |
| Inspector de ngrok | http://localhost:4040 |

### Bajar y volver a levantar

| Comando | Qué hace |
|---|---|
| `docker compose stop` | Apaga los servicios conservando contenedores y datos. Se retoma con `docker compose start`. |
| `docker compose down` | Elimina los contenedores y **conserva** base y broker (volúmenes). |
| `docker compose down --volumes` | Elimina también los volúmenes: arranque limpio, sin contactos ni mensajes. |

En los tres casos, al volver a levantar ngrok abre un túnel **con una URL nueva** y la API registra
el webhook otra vez sin intervención manual.

---

## 4. Probar de punta a punta

1. Abrí **http://localhost:4200**. Como todavía no hay conversaciones, vas a ver el estado vacío
   ("Todavía no hay conversaciones") y el indicador **En línea** arriba a la derecha: eso confirma
   que el canal SignalR está conectado.
2. Desde tu cuenta de Telegram, buscá tu bot y mandale un mensaje de texto, por ejemplo `Hola`.
3. **Sin recargar**, el contacto aparece en el panel izquierdo con su nombre, la vista previa del
   mensaje y un badge con `1` sin leer.
4. Hacé clic en el contacto. Se abre el historial y el badge pasa a `0`: al abrir la conversación
   la API marca como leídos los mensajes entrantes que el cliente efectivamente recibió.
5. Escribí una respuesta en el campo inferior y presioná **Enter** (o **Enviar**). El mensaje
   aparece alineado a la derecha **solo después** de que Telegram confirmó el envío, y te llega a
   Telegram.
6. Mandá dos o tres mensajes más desde Telegram con la conversación abierta: se agregan al final en
   tiempo real.
7. Recargá el navegador (F5): el historial, el orden del listado y los contadores se recuperan
   igual desde la API.

### Verificar el webhook sin exponer credenciales

```bash
curl -s http://localhost:8080/api/telegram/webhook-status | python3 -m json.tool
```

```json
{
    "registeredUrl": "https://xxxx.ngrok-free.app/api/telegram/webhook",
    "pendingUpdateCount": 0,
    "lastErrorMessage": null,
    "lastErrorAtUtc": null
}
```

Este endpoint consulta `getWebhookInfo` de Telegram y devuelve **solo** campos no sensibles: nunca
el token del bot ni el secreto del webhook. Usalo en lugar de llamar a
`https://api.telegram.org/bot<TOKEN>/getWebhookInfo`, que te obligaría a escribir el token en la
terminal.

### Probar la API directamente

```bash
# Listado de contactos
curl -s http://localhost:8080/api/contacts | python3 -m json.tool

# Historial de una conversación
curl -s http://localhost:8080/api/contacts/<CONTACT_ID>/messages | python3 -m json.tool

# Responder
curl -s -X POST http://localhost:8080/api/contacts/<CONTACT_ID>/messages \
  -H 'Content-Type: application/json' \
  -d '{"text":"Respuesta desde curl"}' | python3 -m json.tool

# Marcar como leído hasta un número de secuencia
curl -s -X POST http://localhost:8080/api/contacts/<CONTACT_ID>/read-state \
  -H 'Content-Type: application/json' \
  -d '{"readUpToSequenceNumber":12}' | python3 -m json.tool
```

El webhook (`POST /api/telegram/webhook`) **rechaza con 401** cualquier request sin el header
`X-Telegram-Bot-Api-Secret-Token` correcto; podés comprobarlo mandándole un POST vacío.

### Inspeccionar la base

PostgreSQL **no publica su puerto al host**: solo es accesible desde la red de Compose, porque nada
fuera del stack necesita llegarle. Por eso un cliente gráfico apuntado a `localhost:5432` no conecta.
Para mirar los datos se entra por el contenedor, que ya tiene las credenciales en su entorno:

```bash
docker compose exec postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
```

Dentro: `\dt` lista las tablas (`contacts`, `messages`), `\d messages` describe una. Las columnas
las creó EF Core en PascalCase, así que **hay que citarlas**, porque sin comillas PostgreSQL las pasa
a minúsculas:

```sql
select "Id", "TelegramUserId", "FirstName" from contacts;
select "SequenceNumber", "Direction", "Text", "ReadAtUtc" from messages order by "SequenceNumber";
```

`Direction` es `1` entrante y `2` saliente; `ReadAtUtc` en `null` es un mensaje sin leer.

---

## 5. Comportamiento esperado ante errores

| Situación | Qué pasa |
|---|---|
| **El mismo update llega dos veces** (reintento de Telegram) | Se guarda un solo mensaje. La deduplicación es por `(contacto, message_id)` con índice único en PostgreSQL. |
| **RabbitMQ caído al llegar un mensaje** | El webhook responde `503`, Telegram reintenta. Nunca se acusa recibo de un mensaje que no quedó encolado. |
| **PostgreSQL caído durante el consumo** | Hasta 3 intentos con back-off; el delivery no se confirma, así que el mensaje no se pierde. Agotados los intentos va a la dead-letter queue. |
| **Mensaje imposible de procesar** (JSON ilegible, invariante violada) | Va directo a la DLQ, sin reintentos, para no bloquear la cola. Queda visible en http://localhost:15672. |
| **Telegram rechaza un envío** | No se guarda nada y la bandeja muestra el error que devolvió Telegram. Nunca aparece un mensaje "enviado" que no se entregó. |
| **Telegram entrega la respuesta pero falla al guardarla** | La bandeja avisa que **sí se entregó** y limpia el campo, para que nadie la reenvíe: el contacto ya la tiene y reintentar se la duplicaría. Es la única falla que no se debe reintentar. |
| **Se reinicia la API con un mensaje a medio procesar** | El delivery queda sin confirmar y RabbitMQ lo reencola. No se descarta: apagarse no es un juicio sobre el mensaje. |
| **Se cambia de conversación con una respuesta a medio escribir** | El borrador se descarta. Arrastrarlo al siguiente contacto podría entregarle el texto a la persona equivocada. |
| **Se cae el canal SignalR** | El indicador pasa a "Reconectando…". Al reconectar, el cliente vuelve a pedir contactos e historial, así que no quedan huecos. |
| **Llega un mensaje mientras se marca como leído** | Queda sin leer. El marcado usa un corte por número de secuencia: solo afecta a lo que el cliente ya había recibido. |
| **Mensaje multimedia, edición, sticker, grupo o bot** | Se ignora con `200` (para que Telegram no reintente) y se registra el motivo en el log. Está fuera de alcance. |
| **Contacto sin nombre ni username** | Se muestra `Telegram user <id>`. Si solo hay username, `@username`. |

---

## 6. Supuestos

Decisiones tomadas ante lo que el enunciado no define:

1. **Bandeja compartida, sin usuarios.** Como la autenticación está fuera de alcance, todos los
   navegadores conectados ven la misma bandeja y reciben las mismas notificaciones. No hay
   asignación de conversaciones a operadores.
2. **Solo chats privados de texto.** Grupos, canales, multimedia, ediciones y mensajes de servicio
   se descartan explícitamente con `200`.
3. **Un contacto = un usuario de Telegram**, identificado por `TelegramUserId` (índice único). Se
   guarda además el `TelegramChatId` por separado: para chats privados coinciden, pero esa
   igualdad no es parte del contrato del Bot API.
4. **Marcado como leído.** Ocurre al abrir la conversación y, si la conversación está abierta,
   cuando llega un mensaje nuevo (el operador lo está mirando). Siempre con corte por
   `sequenceNumber`, nunca "todo lo no leído".
5. **Los no leídos se calculan por consulta**, no se desnormalizan en la tabla de contactos: una
   sola fuente de verdad (ver ADR-001).
6. **Los mensajes salientes se persisten solo después del ACK de Telegram.** No hay estado
   "pendiente" persistido ni reintento automático de envío: si falla, el texto queda en el campo de
   respuesta para reintentar a mano.
7. **Sin paginación del historial** (fuera de alcance): se trae la conversación completa.
8. **Sin edición ni borrado** de mensajes ya enviados.
9. **Escritorio únicamente**, sin diseño responsive (fuera de alcance). El layout tiene un ancho
   mínimo de 960 px.
10. **Timestamps en UTC** en backend y base; el navegador los formatea en la zona local.
11. **La URL de ngrok cambia en cada arranque** (plan gratuito). Por eso se descubre y se
    re-registra automáticamente en lugar de configurarse.
12. **La DLQ no se reprocesa automáticamente.** Un mensaje que llega ahí requiere inspección
    manual: preferimos que una falla se vea antes que esconderla con reintentos infinitos.
13. **Documentación en español, código en inglés.** La documentación apunta al equipo evaluador;
    los identificadores siguen la convención habitual en inglés.
14. **Se publica el puerto 8080 de la API** para poder diagnosticar con `curl`. La bandeja en sí
    solo necesita http://localhost:4200, que proxea `/api` y `/hubs` a la API por nginx (por eso no
    hace falta configurar CORS).
15. **La conversación abierta no sobrevive al refresco** y no tiene URL propia. La selección es
    estado en memoria; la app no usa router ni almacenamiento del navegador. Una ruta
    `/contactos/:id` daría enlace compartible, pero introduce el router de Angular para una única
    pantalla. Se eligió el estado más simple que cumple el layout pedido.

---

## 7. Arquitectura y documentación

| Documento | Contenido |
|---|---|
| [docs/architecture/c4-context.svg](docs/architecture/c4-context.svg) | C4 nivel 1 — contexto del sistema |
| [docs/architecture/c4-containers.svg](docs/architecture/c4-containers.svg) | C4 nivel 2 — contenedores |
| [docs/architecture/overview.md](docs/architecture/overview.md) | Trazabilidad C4 ↔ código, esquema de base de datos, garantías y modos de falla |
| [docs/adr/ADR-001-database.md](docs/adr/ADR-001-database.md) | Base de datos elegida |
| [docs/adr/ADR-002-realtime.md](docs/adr/ADR-002-realtime.md) | Tecnología de tiempo real |
| [docs/adr/ADR-003-telegram-rabbitmq.md](docs/adr/ADR-003-telegram-rabbitmq.md) | Flujo del webhook y topología de colas |
| [docs/adr/ADR-004-single-deployable-host.md](docs/adr/ADR-004-single-deployable-host.md) | Un solo host desplegable (ADR propio, no pedido por el enunciado) |
| [docs/ai-usage.md](docs/ai-usage.md) | Trabajo con IA |
| [docs/validation-checklist.md](docs/validation-checklist.md) | Qué se validó, con evidencia, y preguntas bonus |
| [docs/code-review.md](docs/code-review.md) | Log de revisión de código con hallazgos y correcciones |

### Estructura del repositorio

```
src/backend/
  EncodeLabs.Domain/          Entidades e invariantes. Sin EF, sin ASP.NET, sin SDK de Telegram.
  EncodeLabs.Application/     Casos de uso y puertos (repositorios, cola, Telegram, tiempo real).
  EncodeLabs.Infrastructure/  EF Core + PostgreSQL, RabbitMQ, Bot API de Telegram, agente ngrok.
  EncodeLabs.Api/             Endpoints HTTP, webhook, hub SignalR, composición, arranque.
src/frontend/                 Angular 22 + nginx.
tests/                        Tests unitarios de dominio y de casos de uso (xUnit v3).
docs/                         Diagramas, ADRs, validación y revisión.
docker/                       Configuración del agente de ngrok.
```

Las dependencias apuntan **hacia adentro**: `Domain` no referencia nada, `Application` solo
`Domain`, `Infrastructure` implementa los puertos de `Application`, y `Api` compone todo. El hub de
SignalR vive en `Api` porque es transporte web, e implementa ahí el puerto
`IInboxRealtimeNotifier` definido en `Application`.

El consumidor de RabbitMQ es un `BackgroundService` **dentro del mismo host** de la API: un solo
desplegable, como pide el enunciado.

---

## 8. Desarrollo sin Docker (opcional)

El flujo documentado es Docker. Para trabajar sobre el código:

```bash
# Backend (requiere .NET 10 SDK)
cd src/backend
dotnet build
dotnet run --project EncodeLabs.Api     # necesita las variables de entorno de configuración

# Tests (desde la raíz del repo, no desde src/backend)
cd /ruta/al/repo
dotnet run --project tests/EncodeLabs.Domain.UnitTests -c Release       # 25 tests
dotnet run --project tests/EncodeLabs.Application.UnitTests -c Release  # 13 tests

# Frontend (requiere Node >= 24.15)
cd src/frontend
npm install
npm start      # http://localhost:4200, proxea /api y /hubs a http://localhost:8080
```

Son **38 tests** en dos proyectos. Los 25 de dominio cubren las reglas donde el dominio decide:
estado de leído, el límite de 4096 de Telegram, el respaldo de `DisplayName` y el orden de la bandeja
ante entregas desordenadas. Los 13 de casos de uso, con dobles en memoria, cubren lo que **ninguna
corrida real puede ejercitar**: que varios contactos no se crucen. Telegram numera los mensajes por
chat, así que dos personas producen el mismo `message_id` de forma rutinaria, y comprobarlo en vivo
exigiría varias cuentas.

No hay tests de integración a propósito: la deduplicación y la no-pérdida las garantizan un índice
único y los *publisher confirms*, que un doble no ejercitaría. El razonamiento completo está en
[docs/architecture/overview.md](docs/architecture/overview.md) → *Estrategia de tests*.

> Se corren con `dotnet run` y no con `dotnet test`: son ejecutables auto-hospedados y el runner de
> `dotnet test` falla en este SDK. Ver CR-002 en [docs/code-review.md](docs/code-review.md).

---

## 9. Troubleshooting

| Síntoma | Causa probable y solución |
|---|---|
| El log repite `no public HTTPS tunnel available yet` | ngrok no levantó el túnel. Mirá `docker compose logs ngrok`. Casi siempre es el authtoken vacío/inválido o `ERR_NGROK_108` (ya hay otra sesión de ngrok activa: cerrala). |
| `Giving up after 30 attempts to register the Telegram webhook` | Pasaron ~2,5 minutos sin túnel. Corregí `NGROK_AUTHTOKEN` o `TELEGRAM_BOT_TOKEN` en `.env` y reiniciá con `docker compose restart api`. |
| La API no arranca y el log menciona `DataAnnotation validation failed` | Falta una variable en `.env`. La validación de opciones falla al arrancar a propósito, para no fallar recién con el primer mensaje. |
| Escribo al bot y no pasa nada | Revisá `curl -s http://localhost:8080/api/telegram/webhook-status`: si `lastErrorMessage` tiene algo, ahí está la causa. Un bot solo puede tener **un** webhook: si registraste el mismo bot desde otro lado, el último gana. |
| En el log de la API: `Rejected a webhook call with a missing or incorrect secret token header` | El webhook registrado en Telegram usa un secreto distinto al del `.env` actual. Reiniciá la API para re-registrarlo. |
| La bandeja muestra "Sin tiempo real" | El hub no conecta. Verificá que el contenedor `api` esté sano (`docker compose ps`). El cliente reintenta solo con back-off. |
| Todo falla con `502` **pero los contenedores están sanos** | Es el proxy, no la API: compará `curl localhost:8080/api/contacts` (directo) con `curl localhost:4200/api/contacts` (por nginx). Si el primero da `200` y el segundo `502`, mirá `docker compose logs frontend` para ver a qué dirección está yendo. |
| `docker compose up` falla al construir el frontend | Normalmente es `node_modules` local filtrándose; el build usa `.dockerignore`. Probá `docker compose build --no-cache frontend`. |
| Mensajes que no aparecen y la cola crece | Mirá http://localhost:15672 → si crecen los mensajes en `...incoming-messages.dead-letter`, el consumidor los descartó; el motivo está en `docker compose logs api`. |

### Inspeccionar y reprocesar la dead-letter queue

Un mensaje que agota los 3 intentos del consumidor termina en
`encodelabs.inbox.telegram.incoming-messages.dead-letter` y **no vuelve solo**: Telegram ya recibió
`202` cuando se encoló, así que tampoco lo reintenta. Recuperarlo es manual y deliberado
(supuesto 12).

En http://localhost:15672 → *Queues* → la cola `...dead-letter` → panel **Get messages**. Dejá
**Ack Mode** en `Nack message requeue true`: con `Automatic ack` **mirar el mensaje lo borra**,
porque la consola consume de verdad. Vas a ver el payload JSON y la cabecera `x-death` con el motivo
del descarte, la cola de origen y cuántas veces murió.

Para reprocesarlo: copiá el payload y publicalo en *Exchanges* → `encodelabs.inbox.telegram` →
**Publish message**, con routing key `telegram.incoming-message`. Después volvé a la cola y sacalo
de la DLQ con **Ack Mode** en `Automatic ack`, que ahí sí es lo que querés.

**Reprocesar es seguro aunque no sepas si el mensaje alcanzó a persistirse**: el índice único
`(ContactId, TelegramMessageId)` rechaza el duplicado y el consumidor lo registra como
`Discarded duplicate delivery`. Verificado publicando el mismo payload dos veces: la segunda no
creó una fila nueva.

---

## 10. Estado de la validación

Lo que fue efectivamente ejecutado y lo que no está detallado —sin adornos— en
[docs/validation-checklist.md](docs/validation-checklist.md).
