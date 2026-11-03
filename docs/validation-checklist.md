# Checklist de validación

Estado real de cada requisito, con la evidencia que lo respalda. Se distingue explícitamente entre
**lo que fue ejecutado** y **lo que está implementado pero todavía no se corrió de punta a punta**.

**Leyenda**

| Estado | Significado |
|---|---|
| **PASS** | Ejecutado y observado. La evidencia dice con qué comando y qué se vio. |
| **NO VERIFICADO** | Implementado pero no ejercitado. Se dice explícitamente por qué. |
| **N/A** | Fuera de alcance por el enunciado. |

> **Cómo se verificó.** En dos pasadas. La primera, sin credenciales, levantó el stack completo con
> `docker compose up` e **inyectó al webhook las mismas cargas JSON que envía Telegram**, con el
> header del secreto correcto: eso ejercita validación del secreto, filtrado de alcance, publicación
> con confirmación, consumo con ACK, deduplicación, persistencia, historial y estado de leído.
>
> La segunda, el **2 de noviembre de 2026**, se hizo con un bot propio (`@encodelabs_inbox_gd_bot`)
> y una cuenta de ngrok real, operando la interfaz en el navegador. Cerró lo que no se puede
> simular: apertura del túnel, `setWebhook`, entregas reales de Telegram, envío saliente,
> reconexión del cliente y el comportamiento ante caídas de RabbitMQ y PostgreSQL. La evidencia
> concreta de esa corrida está en `code-review.md`, sección "Resultados con bot y túnel reales".

---

## 1. Requisitos funcionales (enunciado §3.1)

| # | Requisito | Implementación | Estado | Evidencia |
|---|---|---|---|---|
| 1 | Recibir en tiempo real los mensajes de texto enviados al bot | `TelegramWebhookEndpoints` → RabbitMQ (durable + confirms) → `IncomingTelegramMessageConsumerService` → `ProcessIncomingTelegramMessageHandler` → SignalR | **PASS** | Con bot real: `POST /api/telegram/webhook → 202` en el inspector de ngrok (43 ms y 104 ms), `Queued Telegram update 571282812` → `Stored incoming message …`, y el contacto **apareció en la bandeja sin recargar** (push de SignalR observado en el navegador). |
| 2 | Listado de contactos con nombre o username | `Contact.DisplayName` con cadena de respaldo nombre → `@username` → `Telegram user <id>` | **PASS** | 5 tests de dominio cubren las cuatro ramas; además, en la corrida real un contacto sin nombre ni username devolvió `"displayName": "Telegram user 55502"`. |
| 2 | Cantidad de mensajes no leídos | Sub-consulta correlacionada en `ContactRepository.BuildSummaryQuery` | **PASS** | Traducción a SQL confirmada contra PostgreSQL 18 real. Tres entregas (una duplicada) produjeron `unreadMessageCount: 2`. |
| 2 | Vista previa del último mensaje | `ContactSummary.BuildPreview` (corta a 160 caracteres) + `lastMessageDirection` para el prefijo "Vos:" | **PASS** | `"lastMessagePreview": "Es sobre el pedido 4471"`, `"lastMessageDirection": "Incoming"`. |
| 2 | Ordenado por última actividad | `Contact.LastActivityAtUtc` + índice descendente; el cliente reordena al recibir eventos | **PASS** | El contacto más reciente encabeza la respuesta de `/api/contacts`. `RegisterActivity` sólo avanza (test `RegisterActivity_IgnoresAnOlderTimestamp…`). |
| 3 | Abrir conversación y ver historial persistente, en orden y diferenciando entrantes de salientes | `GetConversationHistoryHandler` ordena por `SequenceNumber`; `MessageDirection` decide el lado y el color de la burbuja | **PASS** | Verificado en el navegador: entrantes a la izquierda en claro, salientes a la derecha en teal con la etiqueta "Vos", en orden de secuencia. Tras un `F5` el historial completo se reconstruyó desde la API. |
| 4 | Marcar como leídos al abrir la conversación | `MarkConversationAsReadHandler` con corte `readUpToSequenceNumber` | **PASS** | `POST …/read-state` con corte 2 → `unreadMessageCount: 0`. |
| 4 | Contadores consistentes, incluso con mensajes llegando durante la operación | El corte por número de secuencia excluye lo que llegó después; los no leídos se recalculan tras el commit | **PASS** | Demostrado explícitamente: con corte **1** quedó `unreadMessageCount: 1` (el mensaje de secuencia 2 **no** se marcó); con corte **2**, `0`. |
| 5 | Responder al contacto desde la bandeja y que llegue a Telegram | `SendOperatorReplyHandler` → `TelegramBotApiClient.SendTextMessageAsync`; persiste **sólo** tras el ACK de Telegram | **PASS** | La respuesta escrita en la bandeja llegó al chat de Telegram (verificado en ambas pantallas). Se persistió con `TelegramMessageId = 3`, identificador que **asigna Telegram**: su presencia prueba que `sendMessage` fue aceptado. |
| 5 | No reportar éxito cuando el envío falla | La fila se escribe después del ACK; `InboxExceptionHandler` distingue rechazo (`502`) de API inalcanzable (`504`) | **PASS** | Con el bot bloqueado por el usuario: banner `Telegram rechazó la respuesta: Forbidden: bot was blocked by the user`, **sin burbuja**, sin fila nueva en la base, y el texto conservado en el campo para no perder el trabajo del operador. |
| — | Updates fuera de alcance ignorados sin reintentos | Filtro en `TelegramWebhookEndpoints.TryAcceptTextMessage` | **PASS** | Foto, mensaje de bot y mensaje de grupo → `200` con el motivo en el log, en los tres casos. |
| — | Deduplicación de entregas repetidas | Índice único `(ContactId, TelegramMessageId)` + chequeo previo | **PASS** | La misma entrega dos veces → un solo mensaje; log: `Discarded duplicate delivery of Telegram message 11`. |
| — | El webhook rechaza requests sin el secreto | `CryptographicOperations.FixedTimeEquals` | **PASS** | `POST` sin header → `401`, nada encolado. |

## 2. Requisitos técnicos (enunciado §3.2)

| Área | Requisito | Estado | Evidencia |
|---|---|---|---|
| Backend | .NET última versión estable | **PASS** | .NET 10 (`net10.0`), SDK 10.0.300. Sin paquetes preview/RC. |
| Backend | Clean Architecture | **PASS** | `Domain` sin `PackageReference` alguno; `Application` sólo con paquetes de abstracciones; los puertos se definen en `Application` y se implementan en `Infrastructure`/`Api`. Verificado leyendo los cuatro `.csproj`. |
| Frontend | Angular última versión estable | **PASS** | Angular 22.2 (`npm view @angular/core version` → 22.2.4). Standalone + signals + zoneless. `npx ng build` OK. |
| Base de datos | A elección | **PASS** (decisión) | PostgreSQL 18 + EF Core 10, justificado en ADR-001. Migración `InitialInboxSchema` generada e inspeccionada. |
| Mensajería | RabbitMQ para mensajes entrantes | **PASS** | En el arranque: `Connected to RabbitMQ at rabbitmq:5672` y `Declared durable queue encodelabs.inbox.telegram.incoming-messages … with dead-letter queue …`, luego `Consuming … with prefetch 10`. Los mensajes publicados se consumieron y persistieron. |
| Tiempo real | Backend ↔ frontend | **PASS** | Un mensaje real apareció en la bandeja **sin recargar**. Ante `docker compose restart api` el indicador pasó de "En línea" a "Reconectando…" y volvió solo, con la conversación intacta. |
| Telegram | Bot propio y webhook registrado automáticamente al levantar, con la URL de ngrok | **PASS** | `Telegram webhook registered for https://3ed5-….ngrok-free.app/api/telegram/webhook` sin intervención manual, y `getWebhookInfo` lo confirma desde el lado de Telegram. Al reiniciar el túnel, la API arrancó antes que él: `Attempt 1/30: no public HTTPS tunnel available yet` y se re-registró con la URL nueva. |
| Exposición | ngrok | **PASS** | Túnel abierto contra el puerto de la API (no el del frontend). `web_addr: 0.0.0.0:4040` verificado: la API consulta la API de agente del contenedor para descubrir la URL pública. |
| Ejecución | `docker compose up` tras completar `.env` | **PASS** | Arranque en frío real desde cero (`down --volumes` + `up --build`): **21 segundos** hasta el webhook registrado, sin un solo paso manual. Requirió corregir CR-013 (punto de montaje del volumen en PostgreSQL 18). |

## 3. Entregables (enunciado §5)

| Entregable | Estado | Ubicación |
|---|---|---|
| `docker-compose.yml` con todos los servicios | **PASS** | Raíz. 5 servicios: `postgres`, `rabbitmq`, `api`, `ngrok`, `frontend`. |
| `.env.example` sin valores reales | **PASS** | Raíz. Sólo nombres, placeholders e instrucciones. |
| `README.md` con requisitos, `.env`, ejecución, prueba E2E y supuestos | **PASS** | Raíz. 14 supuestos documentados. |
| C4 nivel 1 y nivel 2 en SVG | **PASS** | `docs/architecture/`. SVG válidos (`xml.etree` parsea ambos) y renderizados para revisar la maquetación. |
| 3 ADRs con la plantilla del enunciado, máx. 1 página | **PASS** | `docs/adr/`. Estado / Contexto / Alternativas / Decisión / Consecuencias. Se sumó un cuarto, ADR-004, sobre el host único: el enunciado habilita ADRs adicionales y era la decisión propia de mayor alcance sin registrar. |
| Documento de trabajo con IA, máx. 1 página | **PASS** | `docs/ai-usage.md`. |
| Preguntas bonus | **PASS** | Sección 5 de este documento. |
| `.env` fuera del repositorio | **PASS** | `.gitignore` incluye `.env` y `.env.*` con excepción para `.env.example`. No existe ningún `.env` en el árbol. |
| Tests unitarios sobre el dominio (opcional) | **PASS** | 25 tests, 0 fallos: `dotnet run --project tests/EncodeLabs.Domain.UnitTests -c Release`. El alcance elegido —dominio sí, integración no— se justifica en *Estrategia de tests* de [architecture/overview.md](architecture/overview.md). |

---

## 4. Matriz de pruebas end-to-end

**☑ = ejecutado y observado. ☐ = pendiente.** La columna "Cómo" distingue si el caso se cerró
inyectando cargas al webhook (**inyectado**) o con el bot, el túnel y el navegador reales
(**real**).

| # | Caso | Resultado observado | Cómo | Estado |
|---|---|---|---|---|
| E-01 | Arranque completo | Los 5 servicios arrancan; `Telegram webhook registered for https://3ed5-….ngrok-free.app/api/telegram/webhook` sin intervención manual. | real | **☑** |
| E-02 | Mensaje entrante | `202` en el inspector de ngrok; contacto creado con vista previa y contador. | real | **☑** |
| E-03 | Historial | Mensajes en orden por `sequenceNumber`, entrantes y salientes diferenciadas en pantalla. | real | **☑** |
| E-04 | Corte del marcado como leído | Con corte 1 queda 1 sin leer; con corte 2, `0`. Con bot real: `Marked 2 … up to sequence 2; 0 still unread`. | inyectado + real | **☑** |
| E-05 | Entrega duplicada | Un solo mensaje almacenado; `Discarded duplicate delivery of Telegram message 11`. | inyectado | **☑** |
| E-06 | Respuesta saliente | Llegó al chat de Telegram; persistida con el `message_id` que devolvió Telegram. | real | **☑** |
| E-07 | Fallo de envío | Bot bloqueado → `Telegram rechazó la respuesta: Forbidden: bot was blocked by the user`, sin burbuja y sin fila en la base. | real | **☑** |
| E-08 | Contacto sin nombre ni username | `displayName: "Telegram user 55502"`. | inyectado | **☑** |
| E-09 | Updates fuera de alcance | Foto, mensaje de bot y mensaje de grupo → `200` con el motivo en el log. | inyectado | **☑** |
| E-10 | Webhook sin secreto | `401`, nada encolado. | inyectado | **☑** |
| E-11 | Broker caído | 7 × `503` con el **mismo `update_id`** (Telegram reintentando) y luego `202` al levantar RabbitMQ. Mensaje persistido, DLQ en 0. | real | **☑** |
| E-12 | Base caída durante el consumo | `Attempt 1/3`…`3/3 failed` → `Delivery 2 was routed to …dead-letter`. Payload íntegro en la DLQ con `x-death: rejected`. **El mensaje no llega a la bandeja**: el reproceso es manual (riesgo 1 de `code-review.md`). | real | **☑** |
| E-13 | Reconexión del cliente | `docker compose restart api` (15 s de caída): el indicador pasó a "Reconectando…" y volvió solo a "En línea", con la conversación cargada. | real | **☑** |
| E-14 | Refresh del navegador | `F5` con la conversación abierta: historial completo, orden y selección recuperados desde la API. | real | **☑** |
| E-15 | URL de ngrok nueva | `restart ngrok api` → `Attempt 1/30: no public HTTPS tunnel available yet` y registro con la URL nueva (`4ebf-…`). La entrega siguiente llegó por ella. | real | **☑** |
| E-16 | Estados vacíos | Con la base recién creada: `Todavía no hay conversaciones` en la columna izquierda y `Ninguna conversación seleccionada` en el panel, ambos con una indicación de qué hacer. | real | **☑** |
| E-17 | Contacto abierto que desaparece | Con una conversación abierta y el navegador sin tocar, `down --volumes` + `up`: la bandeja vuelve sola a los estados vacíos, el indicador regresa a "En línea" y **no** aparece ningún banner de error. Regresión de CR-017. | real | **☑** |
| E-18 | Arranque en frío desde cero | `down --volumes` + `up --build`: **21 segundos** hasta `Telegram webhook registered`. Migración aplicada en el intento 1 sobre base nueva, cola y DLQ declaradas, consumidor activo, `GET /api/contacts` → `[]`, nginx `200`. Es el recorrido exacto de quien evalúe: clonar, completar `.env`, levantar. | real | **☑** |

**Comando usado para los casos "inyectado"** (el secreto es el del `.env` de prueba, fuera del
repositorio). Los casos "real" se ejercitaron escribiendo en Telegram y operando la bandeja en el
navegador; la evidencia por log y por base está en `code-review.md`:

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:8080/api/telegram/webhook \
  -H 'Content-Type: application/json' \
  -H "X-Telegram-Bot-Api-Secret-Token: $TELEGRAM_WEBHOOK_SECRET_TOKEN" \
  -d '{"update_id":100001,"message":{"message_id":11,"date":1762100000,
       "text":"Hola, necesito ayuda con mi pedido",
       "from":{"id":55501,"is_bot":false,"first_name":"Ana","last_name":"Gomez","username":"anagomez"},
       "chat":{"id":55501,"type":"private"}}}'
```

---

## 5. Preguntas bonus

### ¿Para qué usarías un sistema de caché en una bandeja conversacional?

Para lo que se lee muchas veces y cambia poco, no para el historial en sí. El candidato más claro es
el **listado de contactos con sus contadores**: es lo primero que pide cada cliente al conectarse y
en cada reconexión, y hoy se resuelve con una sub-consulta correlacionada por contacto; con cientos
de conversaciones abiertas eso se vuelve el punto caliente, y una entrada en caché invalidada al
persistir un mensaje o al cambiar el estado de leído lo resolvería sin tocar el modelo. El segundo
caso es **la deduplicación**: hoy cada mensaje entrante hace un `SELECT` por `(contacto, message_id)`
antes de insertar; un set de IDs recientes en memoria o en Redis evitaría ese viaje en el camino
caliente, manteniendo el índice único como red de seguridad. Un tercero, si hubiera autenticación,
sería cachear el *backplane* de SignalR para escalar a varias instancias. Lo que **no** cachearía es
el historial ni los contadores como fuente de verdad: en una bandeja, mostrar un número de no leídos
desactualizado erosiona la confianza del operador mucho más rápido de lo que una caché mejora la
latencia. Por eso, para el volumen de esta prueba, la decisión consciente fue **no** agregar caché:
sería complejidad sin un problema de rendimiento que justificarla.

### ¿Cómo gestionarías el tráfico de archivos multimedia?

No haría pasar los bytes por la API. El flujo que usaría es: el webhook recibe el update con
`photo`/`document`/`voice`, guarda el `file_id` de Telegram junto con el mensaje (tipo MIME, tamaño,
`file_unique_id`) y encola el trabajo de descarga en una cola aparte de la de texto, para que un
archivo de 20 MB no demore un mensaje de texto detrás suyo. Un consumidor resuelve `getFile`,
descarga por streaming y sube el contenido a un almacenamiento de objetos (S3, MinIO o Blob Storage),
registrando la clave en la base. El frontend nunca pide el archivo a la API: recibe una **URL
prefirmada de corta duración** y lo baja directo del object storage, de modo que el ancho de banda
no atraviesa el backend. Para imágenes generaría además una miniatura en el mismo consumidor, que es
lo que se muestra en la conversación, dejando el original detrás de un clic. En sentido saliente,
`sendPhoto`/`sendDocument` admiten una URL pública o un `multipart`, así que el archivo viaja del
storage a Telegram sin materializarse en memoria de la API. Hay que acotar explícitamente tamaño
máximo y tipos aceptados (Telegram ya limita a 20 MB la descarga vía Bot API), fijar una política de
retención, y tener presente que los `file_id` son estables por bot pero no eternos: la copia propia
en el object storage es la que garantiza que el historial siga siendo legible dentro de un año.
