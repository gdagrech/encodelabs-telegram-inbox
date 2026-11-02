# Checklist de validación

Estado real de cada requisito, con la evidencia que lo respalda. Se distingue explícitamente entre
**lo que fue ejecutado** y **lo que está implementado pero todavía no se corrió de punta a punta**.

**Leyenda**

| Estado | Significado |
|---|---|
| **PASS** | Ejecutado y observado. La evidencia dice con qué comando y qué se vio. |
| **PEND. TELEGRAM** | Implementado y con el stack verificado, pero falta el tramo que exige credenciales reales (túnel ngrok y Bot API). |
| **N/A** | Fuera de alcance por el enunciado. |

> **Cómo se verificó sin bot real.** El stack completo se levantó con `docker compose up` y se le
> inyectaron al webhook **las mismas cargas JSON que envía Telegram**, con el header del secreto
> correcto. Eso ejercita de punta a punta todo lo que vive dentro del sistema: validación del
> secreto, filtrado de alcance, publicación con confirmación, consumo con ACK, deduplicación,
> persistencia, historial y estado de leído. Lo que **no** se puede simular —abrir el túnel,
> `setWebhook` y el envío saliente— queda marcado como **PEND. TELEGRAM** y no se reporta aprobado.
> La sección 4 es el guion para cerrarlo.

---

## 1. Requisitos funcionales (enunciado §3.1)

| # | Requisito | Implementación | Estado | Evidencia |
|---|---|---|---|---|
| 1 | Recibir en tiempo real los mensajes de texto enviados al bot | `TelegramWebhookEndpoints` → RabbitMQ (durable + confirms) → `IncomingTelegramMessageConsumerService` → `ProcessIncomingTelegramMessageHandler` → SignalR | **PASS** (dentro del stack) / PEND. TELEGRAM (entrega real) | Webhook con secreto válido → `202`; el mensaje aparece persistido en `GET /api/contacts`. Log: `Queued Telegram update …` → `Stored incoming message …`. `POST /hubs/inbox/negotiate` responde `200` con WebSockets disponible. |
| 2 | Listado de contactos con nombre o username | `Contact.DisplayName` con cadena de respaldo nombre → `@username` → `Telegram user <id>` | **PASS** | 5 tests de dominio cubren las cuatro ramas; además, en la corrida real un contacto sin nombre ni username devolvió `"displayName": "Telegram user 55502"`. |
| 2 | Cantidad de mensajes no leídos | Sub-consulta correlacionada en `ContactRepository.BuildSummaryQuery` | **PASS** | Traducción a SQL confirmada contra PostgreSQL 18 real. Tres entregas (una duplicada) produjeron `unreadMessageCount: 2`. |
| 2 | Vista previa del último mensaje | `ContactSummary.BuildPreview` (corta a 160 caracteres) + `lastMessageDirection` para el prefijo "Vos:" | **PASS** | `"lastMessagePreview": "Es sobre el pedido 4471"`, `"lastMessageDirection": "Incoming"`. |
| 2 | Ordenado por última actividad | `Contact.LastActivityAtUtc` + índice descendente; el cliente reordena al recibir eventos | **PASS** | El contacto más reciente encabeza la respuesta de `/api/contacts`. `RegisterActivity` sólo avanza (test `RegisterActivity_IgnoresAnOlderTimestamp…`). |
| 3 | Abrir conversación y ver historial persistente, en orden y diferenciando entrantes de salientes | `GetConversationHistoryHandler` ordena por `SequenceNumber`; `MessageDirection` decide el lado y el color de la burbuja | **PASS** (entrantes) / PEND. TELEGRAM (salientes) | `GET /api/contacts/{id}/messages` devolvió los dos mensajes en orden, con `direction: "Incoming"` y `sequenceNumber` 1 y 2. |
| 4 | Marcar como leídos al abrir la conversación | `MarkConversationAsReadHandler` con corte `readUpToSequenceNumber` | **PASS** | `POST …/read-state` con corte 2 → `unreadMessageCount: 0`. |
| 4 | Contadores consistentes, incluso con mensajes llegando durante la operación | El corte por número de secuencia excluye lo que llegó después; los no leídos se recalculan tras el commit | **PASS** | Demostrado explícitamente: con corte **1** quedó `unreadMessageCount: 1` (el mensaje de secuencia 2 **no** se marcó); con corte **2**, `0`. |
| 5 | Responder al contacto desde la bandeja y que llegue a Telegram | `SendOperatorReplyHandler` → `TelegramBotApiClient.SendTextMessageAsync`; persiste **sólo** tras el ACK de Telegram | PEND. TELEGRAM | No simulable: exige un bot real. Caso E-06. |
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
| Tiempo real | Backend ↔ frontend | **PASS** (transporte) | `POST /hubs/inbox/negotiate` a través de nginx → `200` ofreciendo WebSockets, SSE y long polling. La recepción de eventos en el navegador no se inspeccionó visualmente. |
| Telegram | Bot propio y webhook registrado automáticamente al levantar, con la URL de ngrok | PEND. TELEGRAM | El servicio de registro corre, espera `ApplicationStarted`, consulta al agente y reintenta de forma acotada con logs legibles (`Attempt N/30`). Con un authtoken real debería completar; no verificado. |
| Exposición | ngrok | PEND. TELEGRAM | Servicio `ngrok` definido con `web_addr: 0.0.0.0:4040`; con authtoken ficticio el contenedor no levanta (esperado). |
| Ejecución | `docker compose up` tras completar `.env` | **PASS** | Los 5 servicios arrancan; `postgres` y `rabbitmq` quedan `healthy`; migraciones aplicadas en el intento 1. Requirió corregir CR-013 (punto de montaje del volumen en PostgreSQL 18). |

## 3. Entregables (enunciado §5)

| Entregable | Estado | Ubicación |
|---|---|---|
| `docker-compose.yml` con todos los servicios | **PASS** | Raíz. 5 servicios: `postgres`, `rabbitmq`, `api`, `ngrok`, `frontend`. |
| `.env.example` sin valores reales | **PASS** | Raíz. Sólo nombres, placeholders e instrucciones. |
| `README.md` con requisitos, `.env`, ejecución, prueba E2E y supuestos | **PASS** | Raíz. 14 supuestos documentados. |
| C4 nivel 1 y nivel 2 en SVG | **PASS** | `docs/architecture/`. SVG válidos (`xml.etree` parsea ambos) y renderizados para revisar la maquetación. |
| 3 ADRs con la plantilla del enunciado, máx. 1 página | **PASS** | `docs/adr/`. Estado / Contexto / Alternativas / Decisión / Consecuencias. |
| Documento de trabajo con IA, máx. 1 página | **PASS** | `docs/ai-usage.md`. |
| Preguntas bonus | **PASS** | Sección 5 de este documento. |
| `.env` fuera del repositorio | **PASS** | `.gitignore` incluye `.env` y `.env.*` con excepción para `.env.example`. No existe ningún `.env` en el árbol. |
| Tests unitarios sobre el dominio (opcional) | **PASS** | 25 tests, 0 fallos. |

---

## 4. Matriz de pruebas end-to-end

**☑ = ejecutado y observado en esta sesión. ☐ = pendiente.** Los ☑ se ejecutaron inyectando al
webhook las cargas de Telegram; los ☐ necesitan `.env` con credenciales reales o un navegador.

| # | Caso | Pasos | Resultado esperado | Estado |
|---|---|---|---|---|
| E-01 | Arranque completo | `docker compose up --build` | Los 5 servicios arrancan; en el log aparece `Telegram webhook registered for https://....ngrok-free.app/...` sin intervención manual. | **◐** stack ☑ (`postgres`/`rabbitmq` healthy, migraciones, cola, consumidor); registro del webhook ☐ |
| E-02 | Mensaje entrante | Inyectar un update de texto válido con el header del secreto | `202`; el contacto aparece en `/api/contacts` con vista previa y `unreadMessageCount: 1`. | **☑** |
| E-03 | Historial | `GET /api/contacts/{id}/messages` | Mensajes en orden, con dirección y `sequenceNumber`. | **☑** |
| E-04 | Corte del marcado como leído | `POST …/read-state` con un corte menor al máximo y luego con el máximo | Con el corte menor queda 1 sin leer; con el máximo, 0. | **☑** |
| E-05 | Entrega duplicada | Enviar dos veces el mismo `message_id` | Un solo mensaje; log `Discarded duplicate delivery…`. | **☑** |
| E-06 | Respuesta saliente | Responder desde la bandeja | Llega a Telegram; la burbuja aparece a la derecha **después** del ACK. | ☐ requiere bot real |
| E-07 | Fallo de envío | Bloquear el bot desde Telegram y responder | Banner de error con el texto de Telegram; **no** aparece burbuja nueva. | ☐ requiere bot real |
| E-08 | Contacto sin nombre ni username | Update con `from` sin `first_name` ni `username` | `displayName: "Telegram user <id>"`. | **☑** |
| E-09 | Updates fuera de alcance | Foto, mensaje de bot y mensaje de grupo | `200` en los tres, con el motivo en el log; Telegram no reintenta. | **☑** |
| E-10 | Webhook sin secreto | `POST` sin el header | `401`, nada encolado, warning en el log. | **☑** |
| E-11 | Broker caído | `docker compose stop rabbitmq` y enviar un update | El webhook devuelve `503`; al levantar RabbitMQ el reintento llega. | ☐ |
| E-12 | Base caída durante el consumo | `docker compose stop postgres`, enviar un update, esperar los reintentos, `start postgres` | Reintentos en el log; el mensaje no se pierde (o queda en la DLQ con el motivo visible). | ☐ |
| E-13 | Reconexión del cliente | `docker compose restart api` con la bandeja abierta | El indicador pasa a "Reconectando…" y vuelve a "En línea"; contactos e historial se recargan solos. | ☐ requiere navegador |
| E-14 | Refresh del navegador | F5 con una conversación abierta | Historial, orden y contadores se recuperan desde la API. | ☐ requiere navegador |
| E-15 | URL de ngrok nueva | `docker compose restart ngrok api` | Se registra la nueva URL automáticamente; los mensajes siguen llegando. | ☐ requiere ngrok real |
| E-16 | Estados vacíos | Levantar con la base recién creada | "Todavía no hay conversaciones" y "Ninguna conversación seleccionada". | ☐ requiere navegador (la API sí devolvió `[]`) |

**Comando usado para los casos ☑** (el secreto es el del `.env` de prueba, fuera del repositorio):

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
