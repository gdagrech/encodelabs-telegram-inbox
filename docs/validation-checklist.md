# Checklist de validación

Estado real de cada requisito, con la evidencia que lo respalda. Se distingue explícitamente entre
**lo que fue ejecutado** y **lo que está implementado pero todavía no se corrió de punta a punta**.

**Leyenda**

| Estado | Significado |
|---|---|
| **PASS** | Ejecutado y observado. La evidencia dice con qué comando. |
| **PEND. E2E** | Implementado y compilando, pero no verificado contra servicios reales todavía. |
| **N/A** | Fuera de alcance por el enunciado. |

> **Por qué hay ítems en PEND. E2E:** el daemon de Docker no estaba disponible durante el desarrollo
> y la prueba con bot real necesita credenciales en `.env`. Ninguno de esos ítems se reporta como
> aprobado. La sección 4 es el guion exacto para cerrarlos.

---

## 1. Requisitos funcionales (enunciado §3.1)

| # | Requisito | Implementación | Estado | Evidencia |
|---|---|---|---|---|
| 1 | Recibir en tiempo real los mensajes de texto enviados al bot | `TelegramWebhookEndpoints` → RabbitMQ (durable + confirms) → `IncomingTelegramMessageConsumerService` → `ProcessIncomingTelegramMessageHandler` → SignalR | PEND. E2E | Compila; lógica cubierta por el diseño de ADR-003. Falta corrida real. |
| 2 | Listado de contactos con nombre o username | `Contact.DisplayName` con cadena de respaldo nombre → `@username` → `Telegram user <id>` | **PASS** (dominio) | 5 tests en `ContactTests` cubren las cuatro ramas, incluida la de solo espacios en blanco. |
| 2 | Cantidad de mensajes no leídos | Sub-consulta correlacionada en `ContactRepository.BuildSummaryQuery` | PEND. E2E | Traducción a SQL no ejercitada contra PostgreSQL (riesgo 2 de `code-review.md`). |
| 2 | Vista previa del último mensaje | `ContactSummary.BuildPreview` (corta a 160 caracteres) + `lastMessageDirection` para el prefijo "Vos:" | PEND. E2E | — |
| 2 | Ordenado por última actividad | `Contact.LastActivityAtUtc` + índice descendente; el cliente reordena al recibir eventos | PEND. E2E | `Contact.RegisterActivity` sólo avanza: test `RegisterActivity_IgnoresAnOlderTimestamp...`. |
| 3 | Abrir conversación y ver historial persistente, en orden y diferenciando entrantes de salientes | `GetConversationHistoryHandler` ordena por `SequenceNumber`; `MessageDirection` decide el lado y el color de la burbuja | PEND. E2E | — |
| 4 | Marcar como leídos al abrir la conversación | `MarkConversationAsReadHandler` con corte `readUpToSequenceNumber` | PEND. E2E | Semántica del dominio cubierta: `MarkAsRead` devuelve si hubo cambio y rechaza mensajes salientes (`MessageTests`). |
| 4 | Contadores consistentes, incluso con mensajes llegando durante la operación | El corte por número de secuencia excluye lo que llegó después; los no leídos se recalculan tras el commit | PEND. E2E | Caso E-04 de la sección 4. |
| 5 | Responder al contacto desde la bandeja y que llegue a Telegram | `SendOperatorReplyHandler` → `TelegramBotApiClient.SendTextMessageAsync`; persiste **sólo** tras el ACK de Telegram | PEND. E2E | Caso E-06. |

## 2. Requisitos técnicos (enunciado §3.2)

| Área | Requisito | Estado | Evidencia |
|---|---|---|---|
| Backend | .NET última versión estable | **PASS** | .NET 10 (`net10.0`), SDK 10.0.300. Sin paquetes preview/RC. |
| Backend | Clean Architecture | **PASS** | `Domain` sin `PackageReference` alguno; `Application` sólo con paquetes de abstracciones; los puertos se definen en `Application` y se implementan en `Infrastructure`/`Api`. Verificado leyendo los cuatro `.csproj`. |
| Frontend | Angular última versión estable | **PASS** | Angular 22.2 (`npm view @angular/core version` → 22.2.4). Standalone + signals + zoneless. `npx ng build` OK. |
| Base de datos | A elección | **PASS** (decisión) | PostgreSQL 18 + EF Core 10, justificado en ADR-001. Migración `InitialInboxSchema` generada e inspeccionada. |
| Mensajería | RabbitMQ para mensajes entrantes | **PASS** (código) / PEND. E2E (corrida) | Cola durable, publisher confirms, ACK manual, DLQ. Topología en ADR-003. |
| Tiempo real | Backend ↔ frontend | **PASS** (código) / PEND. E2E | SignalR; ADR-002. |
| Telegram | Bot propio y webhook registrado automáticamente al levantar, con la URL de ngrok | **PASS** (código) / PEND. E2E | `TelegramWebhookRegistrationService` espera `ApplicationStarted`, descubre la URL por el agente de ngrok y reintenta hasta 30 veces. |
| Exposición | ngrok | **PASS** (código) / PEND. E2E | Servicio `ngrok` en Compose con `web_addr: 0.0.0.0:4040`. |
| Ejecución | `docker compose up` tras completar `.env` | PEND. E2E | `docker compose config` **PASS**; `docker compose up --build` **no ejecutado**. |

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

Guion para cerrar los ítems PEND. E2E. Requiere `.env` completo y Docker corriendo.

| # | Caso | Pasos | Resultado esperado | Estado |
|---|---|---|---|---|
| E-01 | Arranque completo | `docker compose up --build` | Los 5 servicios arrancan; en el log aparece `Telegram webhook registered for https://....ngrok-free.app/...` sin intervención manual. | ☐ |
| E-02 | Mensaje entrante en tiempo real | Escribirle `Hola` al bot con la bandeja abierta | El contacto aparece sin recargar, con vista previa y badge `1`. | ☐ |
| E-03 | Apertura y marcado de leídos | Clic en el contacto | Se ve el historial; el badge pasa a `0`; `GET /api/contacts` confirma `unreadMessageCount: 0`. | ☐ |
| E-04 | Mensaje llegando con la conversación abierta | Enviar 3 mensajes seguidos desde Telegram | Se agregan al final en orden; el contador no queda "pegado". | ☐ |
| E-05 | Entrega duplicada | Reenviar el mismo update al webhook con el header correcto (copiar un request desde http://localhost:4040) | Se almacena **un solo** mensaje; en el log: `Discarded duplicate delivery of Telegram message ...`. | ☐ |
| E-06 | Respuesta saliente | Responder desde la bandeja | Llega a Telegram; la burbuja aparece a la derecha **después** del ACK. | ☐ |
| E-07 | Fallo de envío | `docker compose stop` + bloquear el bot desde Telegram, o cortar la red del contenedor `api`, y responder | Banner de error con el texto devuelto por Telegram; **no** aparece ninguna burbuja nueva. | ☐ |
| E-08 | Contacto sin nombre ni username | Escribir desde una cuenta sin nombre visible | Se muestra `Telegram user <id>`. | ☐ |
| E-09 | Update fuera de alcance | Enviar una foto, un sticker o editar un mensaje | Se ignora; en el log: `Ignoring Telegram update ...: the message has no text content ...`; Telegram no reintenta. | ☐ |
| E-10 | Webhook sin secreto | `curl -X POST http://localhost:8080/api/telegram/webhook -H 'Content-Type: application/json' -d '{}'` | `401`, nada encolado, warning en el log. | ☐ |
| E-11 | Broker caído | `docker compose stop rabbitmq`, escribir al bot | El webhook devuelve `503`; Telegram reintenta; al levantar RabbitMQ el mensaje llega. | ☐ |
| E-12 | Base caída durante el consumo | `docker compose stop postgres`, escribir al bot, esperar los reintentos, `docker compose start postgres` | Reintentos en el log; el mensaje no se pierde (o queda en la DLQ con el motivo visible). | ☐ |
| E-13 | Reconexión del cliente | `docker compose restart api` con la bandeja abierta | El indicador pasa a "Reconectando…" y vuelve a "En línea"; contactos e historial se recargan solos. | ☐ |
| E-14 | Refresh del navegador | F5 con una conversación abierta | Historial, orden y contadores se recuperan desde la API. | ☐ |
| E-15 | URL de ngrok nueva | `docker compose restart ngrok api` | Se registra la nueva URL automáticamente; los mensajes siguen llegando. | ☐ |
| E-16 | Estados vacíos | Levantar con la base recién creada | "Todavía no hay conversaciones" y "Ninguna conversación seleccionada". | ☐ |

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
