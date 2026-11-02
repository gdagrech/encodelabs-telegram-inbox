# Log de revisión de código

**Alcance:** revisión propia (asistida por Claude Code) sobre todo el código de `src/` y la
configuración de Compose, previa a la entrega.
**Fecha:** 2026-11-02.
**Revisor humano:** ☐ pendiente — este documento registra únicamente la revisión automatizada y los
comandos realmente ejecutados. No se reporta ninguna revisión humana que no haya ocurrido.

## Qué se revisó

| Área | Resultado |
|---|---|
| Dirección de dependencias entre capas | Sin hallazgos. `Domain` no referencia paquetes; `Application` solo abstracciones; `Infrastructure`/`Api` implementan los puertos. |
| Seguridad del webhook | Hallazgos menores (ver CR-008). Secreto validado en tiempo fijo; `401` sin tocar la cola. |
| Confirmaciones de publicación y ACK manual | Sin hallazgos. |
| Idempotencia / deduplicación | Sin hallazgos en el diseño; verificación en vivo pendiente (ver riesgos abiertos). |
| Arranque y resiliencia | **CR-003** (alto). |
| Enrutamiento HTTP | **CR-004**. |
| Sincronización de estado en el cliente | **CR-005**, **CR-006**. |
| Ciclo de vida del consumidor | **CR-007**. |
| Contratos de serialización | **CR-008**. |
| Dependencias de terceros | **CR-001** (alto). |
| Secretos en repo/logs | Sin hallazgos. `.env` ignorado; ningún log imprime token ni secreto. |
| Nombres e identificadores | Sin hallazgos tras la pasada final. |

---

## Hallazgos

### CR-001 — Dependencia transitiva con vulnerabilidad conocida
- **Severidad:** ALTA
- **Dónde:** `src/backend/EncodeLabs.Api/EncodeLabs.Api.csproj`
- **Evidencia:** `dotnet build` falló con
  `error NU1903: Package 'Microsoft.OpenApi' 2.0.0 has a known high severity vulnerability
  (GHSA-v5pm-xwqc-g5wc)`, arrastrado por `Microsoft.AspNetCore.OpenApi` 10.0.4.
- **Por qué importa:** meter una dependencia vulnerable para obtener documentación de endpoints es
  un intercambio malo, más aún cuando la API tiene cinco rutas.
- **Corrección:** se eliminó el paquete y la documentación de endpoints pasó a ejemplos de `curl` en
  el README. El motivo quedó comentado en el `.csproj` para que nadie lo vuelva a agregar sin saberlo.
- **Estado:** CORREGIDO. `dotnet build` vuelve a pasar sin advertencias.

### CR-002 — `dotnet test` no funciona con el runner de este SDK
- **Severidad:** MEDIA (herramientas, no producto)
- **Dónde:** `tests/EncodeLabs.Domain.UnitTests/`
- **Evidencia:** con el SDK 10.0.300, `dotnet test` falla primero con
  `Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK`
  y, tras habilitar el runner nuevo en `global.json`, termina en `Zero tests ran / exit code 5`. El
  log de diagnóstico de Microsoft.Testing.Platform corta justo después de inicializar la
  configuración, en modo `--server dotnettestcli`. Se reprodujo con xunit.v3 4.0.2 y 3.2.2.
- **Por qué importa:** quien clone el repo y escriba `dotnet test` va a ver un error confuso.
- **Corrección:** los tests son ejecutables auto-hospedados; el comando documentado es
  `dotnet run --project tests/EncodeLabs.Domain.UnitTests -c Release`, que **sí** corre y reporta.
  Documentado en el README y en el `.csproj`.
- **Estado:** RIESGO ACEPTADO (limitación de la cadena de herramientas, no del código).

### CR-003 — El reintento de migraciones no detectaba las excepciones envueltas
- **Severidad:** ALTA
- **Dónde:** `src/backend/EncodeLabs.Infrastructure/Persistence/InboxDatabaseInitialiser.cs`
- **Evidencia:** el filtro era `migrationException is NpgsqlException or TimeoutException`. Con
  `EnableRetryOnFailure()` activo, EF Core envuelve el fallo en `RetryLimitExceededException`, y un
  host todavía no resoluble llega como `SocketException` anidada. Ninguno de los dos casos entraba
  al filtro.
- **Por qué importa:** rompía justamente el escenario para el que existe el reintento. Con
  PostgreSQL arrancando un poco lento, la API habría terminado con una excepción en vez de esperar,
  y `docker compose up` habría quedado en un *restart loop*.
- **Corrección:** `IsDatabaseNotReadyYet` ahora recorre toda la cadena de `InnerException` buscando
  `NpgsqlException`, `SocketException` o `TimeoutException`.
- **Estado:** CORREGIDO. Compila; la verificación en vivo depende de la corrida de Compose.

### CR-004 — El listado de contactos quedaba mapeado con barra final
- **Severidad:** MEDIA
- **Dónde:** `src/backend/EncodeLabs.Api/Endpoints/InboxEndpoints.cs`
- **Evidencia:** `MapGroup("/api/contacts").MapGet("/", ...)` produce la plantilla
  `/api/contacts/`, mientras el cliente pide `/api/contacts`.
- **Por qué importa:** depender de la normalización de barra final del enrutador para la pantalla
  principal es innecesariamente frágil.
- **Corrección:** patrón vacío (`MapGet("", ...)`), que mapea exactamente `/api/contacts`.
- **Estado:** CORREGIDO.

### CR-005 — Un mensaje empujado durante la carga del historial desaparecía
- **Severidad:** MEDIA
- **Dónde:** `src/frontend/src/app/core/services/inbox-store.ts`
- **Evidencia:** `loadConversation` hacía `conversationMessagesState.set([...history.messages])`. Si
  llegaba un `MessageAdded` por SignalR **después** de que la consulta leyó la base pero **antes**
  de que la respuesta llegara al cliente, ese mensaje se agregaba y acto seguido era pisado por el
  historial, que no lo contenía.
- **Por qué importa:** el mensaje quedaba invisible hasta el siguiente refresh, con el contador ya
  descontado. Es exactamente el tipo de inconsistencia silenciosa que el requisito de tiempo real
  busca evitar.
- **Corrección:** `mergeMessagesBySequence` une por `messageId` y reordena por `sequenceNumber`. La
  copia traída de la base gana, porque es la que trae el estado de leído autoritativo.
- **Estado:** CORREGIDO.

### CR-006 — Contenido obsoleto al cambiar de conversación
- **Severidad:** BAJA
- **Dónde:** `src/frontend/src/app/core/services/inbox-store.ts`
- **Evidencia:** al seleccionar otro contacto, los mensajes del anterior seguían en pantalla bajo el
  encabezado del nuevo hasta que respondía la API.
- **Corrección:** se limpia el historial al cambiar de selección, de modo que se vea el estado de
  carga. El token de petición (`conversationRequestSequence`) ya descartaba respuestas tardías.
- **Estado:** CORREGIDO.

### CR-007 — Excepción no observada en el callback del consumidor
- **Severidad:** MEDIA
- **Dónde:** `src/backend/EncodeLabs.Infrastructure/Messaging/RabbitMq/IncomingTelegramMessageConsumerService.cs`
- **Evidencia:** `consumer.ReceivedAsync` devolvía directamente la `Task` de `HandleDeliveryAsync`.
  Si fallaba algo fuera del pipeline con reintentos —por ejemplo `BasicAckAsync` sobre un canal ya
  cerrado— la excepción quedaba sin observar y sin registrar.
- **Por qué importa:** una falla de ACK invisible es muy difícil de diagnosticar; el mensaje se
  redelivera sin que nadie sepa por qué.
- **Corrección:** el handler envuelve la llamada en `try/catch` y registra el error. La entrega
  queda sin confirmar y se redelivera, que es el comportamiento deseado.
- **Estado:** CORREGIDO.

### CR-008 — El formato de los payloads de SignalR dependía del valor por defecto del framework
- **Severidad:** BAJA
- **Dónde:** `src/backend/EncodeLabs.Api/Program.cs`
- **Evidencia:** los modelos TypeScript están tipados en camelCase; el protocolo JSON de SignalR lo
  usa por defecto, pero eso no estaba declarado en ninguna parte.
- **Corrección:** se fija explícitamente `PropertyNamingPolicy = CamelCase` en el protocolo del hub,
  junto al `JsonStringEnumConverter` que ya estaba.
- **Estado:** CORREGIDO.

### CR-009 — Filtro aplicado sobre una proyección en vez de sobre la tabla
- **Severidad:** BAJA
- **Dónde:** `src/backend/EncodeLabs.Infrastructure/Persistence/Repositories/ContactRepository.cs`
- **Evidencia:** `GetSummaryAsync` proyectaba primero y filtraba después por
  `candidate.Contact.Id`, lo que obliga a EF a traducir el predicado a través de la proyección.
- **Corrección:** `BuildSummaryQuery` ahora recibe un `IQueryable<Contact>`, y el filtro y el orden
  se aplican sobre la tabla antes de proyectar.
- **Estado:** CORREGIDO.

### CR-010 — Mezcla de idiomas en los mensajes de error de la UI
- **Severidad:** BAJA
- **Dónde:** `src/frontend/src/app/core/services/inbox-api.service.ts`
- **Evidencia:** los mensajes propios del cliente están en español; el campo `detail` de
  ProblemDetails que devuelve la API está en inglés y se muestra tal cual.
- **Decisión:** se acepta. Ese `detail` transporta el texto que devolvió Telegram (por ejemplo
  `Forbidden: bot was blocked by the user`), y traducirlo o reemplazarlo perdería información de
  diagnóstico. Traducir solo el prefijo daría una falsa sensación de localización.
- **Estado:** RIESGO ACEPTADO.

### CR-011 — `topologyDeclared` sin sincronización
- **Severidad:** BAJA
- **Dónde:** `src/backend/EncodeLabs.Infrastructure/Messaging/RabbitMq/RabbitMqConnectionProvider.cs`
- **Evidencia:** el flag lo leen y escriben el publicador y el consumidor sin candado.
- **Decisión:** se acepta. En el peor caso la topología se declara dos veces, y las declaraciones de
  AMQP son idempotentes. Agregar un candado complicaría el código sin corregir nada real.
- **Estado:** RIESGO ACEPTADO.

### CR-012 — El CLI de Angular exige Node ≥ 24.15
- **Severidad:** INFORMATIVO
- **Evidencia:** el primer intento de `ng new` falló con
  `The Angular CLI requires a minimum Node.js version of v22.22.3 or v24.15.0 or v26.0.0`
  sobre Node v24.13.0.
- **Impacto:** ninguno para el flujo documentado — la imagen `node:24-alpine` del Dockerfile cumple
  el requisito. Solo afecta a quien quiera correr `npm start` en el host. Quedó anotado en el README.
- **Estado:** DOCUMENTADO.

---

## Comandos efectivamente ejecutados

| Comando | Resultado |
|---|---|
| `dotnet build` (solución completa, `src/backend`) | **OK** — `Build succeeded.` sin advertencias, con `TreatWarningsAsErrors`. |
| `dotnet run --project tests/EncodeLabs.Domain.UnitTests -c Release` | **OK** — `Total: 25, Errors: 0, Failed: 0, Skipped: 0`. |
| `dotnet ef migrations add InitialInboxSchema` | **OK** — migración generada e inspeccionada (identidad, `timestamptz`, índice único de deduplicación). |
| `npx ng build` (producción) | **OK** — 245,55 kB iniciales / 65,43 kB transferidos, sin errores de plantilla ni de tipos. |
| `docker compose config` | **OK** — configuración válida, interpolación de variables correcta (se usó un `.env` de prueba con valores ficticios, fuera del repo). |
| `docker compose up --build` | **NO EJECUTADO** — el daemon de Docker no estaba disponible en el entorno de desarrollo (`Cannot connect to the Docker daemon`). |
| Prueba end-to-end con bot real | **NO EJECUTADO** — requiere credenciales reales en `.env`. |

## Riesgos abiertos

1. **La corrida de Compose no fue verificada.** Es el riesgo principal. La configuración parsea y
   cada imagen es estándar, pero nada reemplaza a un `docker compose up --build` real. Pendiente de
   ejecución por parte del autor; el detalle está en `validation-checklist.md`.
2. **La traducción de las consultas de EF no fue ejecutada contra PostgreSQL.** Las sub-consultas
   correlacionadas que calculan los no leídos y la vista previa se escribieron para traducir a SQL,
   pero eso se confirma recién al correrlas contra la base.
3. **`web_addr: 0.0.0.0:4040` en la configuración de ngrok no fue verificado en vivo.** Es lo que
   permite que el contenedor de la API consulte al agente; si fallara, el log lo diría explícitamente
   (`The ngrok agent API ... is not answering yet`) y se corregiría ahí.
4. **Entrega *at-least-once*, no *exactly-once*.** Es una propiedad del diseño, no un defecto: la
   deduplicación la absorbe.
5. **Entre el commit y la notificación de SignalR no hay atomicidad.** El cliente reconcilia al
   reconectar y al refrescar (ADR-002).
6. **Los mensajes en la dead-letter queue no se reprocesan solos.** Decisión consciente.
