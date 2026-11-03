# Log de revisión de código

**Alcance:** revisión propia (asistida por Claude Code) sobre todo el código de `src/` y la
configuración de Compose, previa a la entrega.
**Fecha:** 2026-11-02, con una segunda pasada completa el 2026-11-03 —ya con el sistema verificado de
punta a punta— que produjo CR-018 a CR-023 y las verificaciones del reproceso de la dead-letter queue
y del reintento de migraciones.
**Revisor humano:** ☐ pendiente — este documento registra únicamente la revisión automatizada y los
comandos realmente ejecutados. No se reporta ninguna revisión humana que no haya ocurrido.

## Qué se revisó

| Área | Resultado |
|---|---|
| Dirección de dependencias entre capas | Sin hallazgos. `Domain` no referencia paquetes; `Application` solo abstracciones; `Infrastructure`/`Api` implementan los puertos. |
| Seguridad del webhook | Sin hallazgos. Secreto validado en tiempo fijo; `401` sin tocar la cola. |
| Confirmaciones de publicación y ACK manual | Sin hallazgos. |
| Idempotencia / deduplicación | Sin hallazgos. Verificada en vivo dos veces: con los reintentos reales de Telegram (E-11) y republicando a mano el mismo payload desde la DLQ (E-19), que el consumidor descartó como `Discarded duplicate delivery` sin crear fila. |
| Arranque y resiliencia | **CR-003** (alto). |
| Enrutamiento HTTP | **CR-004**. |
| Sincronización de estado en el cliente | **CR-005**, **CR-006**. |
| Ciclo de vida del consumidor | **CR-007**; en la segunda pasada, **CR-018** (alto): el apagado descartaba el mensaje en vuelo. |
| Aislamiento entre contactos | **CR-019** (alto): el borrador de respuesta se cruzaba de conversación. Cubierto ahora por tests de varios remitentes. |
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
- **Estado:** CORREGIDO y **verificado forzando el escenario**: con `postgres` detenido y
  `restart api`, el log muestra `PostgreSQL is not ready yet (attempt 1..10): Name or service not
  known` —la `SocketException` anidada que antes no entraba al filtro— y, al volver la base,
  `Database schema is up to date (attempt 11)`. La API no murió ni entró en *restart loop*.

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
- **Decisión inicial:** se aceptó, razonando que el `detail` transporta el texto que devolvió
  Telegram (`Forbidden: bot was blocked by the user`) y que traducir solo el prefijo daría una falsa
  sensación de localización.
- **Revisión tras verlo en ejecución:** el juicio cambió al observar el banner real con un bot
  bloqueado. `Telegram rejected the reply: Forbidden: bot was blocked by the user` conviviendo con
  `Reintentar`, `Cerrar` y `RESPONDER A …` no se lee como una cita, se lee como un descuido — y
  aparece justo en la pantalla que demuestra el requisito 5. El error de la decisión original fue
  plantearlo como "traducir o no el diagnóstico": la línea correcta es **que el servidor no redacte
  prosa para el operador**.
- **Corrección:** `InboxExceptionHandler` devuelve en `detail` el motivo técnico desnudo (`Forbidden:
  bot was blocked by the user`) y `describeRequestFailure` compone la frase en español a partir del
  código de estado. Las palabras de Telegram se conservan textuales, porque son su diagnóstico.
- **Estado:** CORREGIDO y verificado contra la API (`502` → `detail: "Forbidden: bot was blocked by
  the user"`; `400` → la regla de longitud).

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

### CR-013 — PostgreSQL 18 no arrancaba por el punto de montaje del volumen
- **Severidad:** CRÍTICA (rompía `docker compose up`)
- **Dónde:** `docker-compose.yml`, servicio `postgres`
- **Evidencia:** en la primera corrida real, Compose abortó con
  `container for service "postgres" is unhealthy`. El log del contenedor explicaba la causa:
  `in 18+, these Docker images are configured to store database data in a format which is
  compatible with "pg_ctlcluster" (…) Counter to that, there appears to be PostgreSQL data in:
  /var/lib/postgresql/data (unused mount/volume)`.
- **Por qué importa:** era el montaje habitual (`postgres-data:/var/lib/postgresql/data`), correcto
  hasta PostgreSQL 17 y **rechazado** por la imagen 18, que guarda los datos en un subdirectorio por
  versión mayor. La solución completa no levantaba. Es exactamente el tipo de defecto que sólo
  aparece corriendo y no revisando.
- **Corrección:** el volumen se monta un nivel más arriba, en `/var/lib/postgresql`, con un
  comentario que explica por qué.
- **Estado:** CORREGIDO y **verificado**: tras recrear los volúmenes, `postgres` queda `healthy` y
  la API aplica las migraciones en el primer intento.

### CR-014 — Un token de bot mal formado tiraba un stack trace en vez de un mensaje accionable
- **Severidad:** MEDIA
- **Dónde:** `src/backend/EncodeLabs.Infrastructure/Telegram/TelegramBotOptions.cs`
- **Evidencia:** con un `TELEGRAM_BOT_TOKEN` con formato inválido, la API muere al arrancar con
  `Unhandled exception. System.ArgumentException: Bot token invalid (Parameter 'token')` y ~25
  líneas de stack del contenedor de inyección de dependencias.
- **Por qué importa:** fallar rápido está bien; fallar de forma ilegible no. Quien pega mal el token
  recibe una traza del SDK en lugar de "revisá esta variable".
- **Corrección:** el formato del token (`^\d{5,}:[A-Za-z0-9_-]{30,}$`) y el alfabeto del secreto del
  webhook (`^[A-Za-z0-9_-]{16,256}$`, el que Telegram exige en `setWebhook`) se validan ahora como
  opciones con `ValidateOnStart`. El mensaje de error nombra la variable y el formato esperado, y
  **nunca** el valor.
- **Estado:** CORREGIDO.

### CR-015 — Ruido de logs en el reintento del túnel
- **Severidad:** BAJA
- **Dónde:** `src/backend/EncodeLabs.Api/appsettings.json`
- **Evidencia:** mientras ngrok no está disponible, el logging de `IHttpClientFactory` emitía un
  stack trace completo de `HttpRequestException` por cada intento, enterrando el mensaje propio de
  una línea.
- **Por qué importa:** el escenario de arranque sin túnel es justo el que alguien va a estar
  diagnosticando; llenarlo de trazas esconde la línea útil.
- **Corrección:** `System.Net.Http.HttpClient` baja a `Warning`. El log queda en dos líneas legibles
  por intento (`The ngrok agent API … is not answering yet` + `Attempt N/30 …`).
- **Estado:** CORREGIDO y verificado en la corrida.

---

### CR-016 — Un rechazo de Telegram y una API inalcanzable daban el mismo código
- **Severidad:** MEDIA
- **Dónde:** `src/backend/EncodeLabs.Api/ErrorHandling/InboxExceptionHandler.cs`
- **Evidencia:** `TelegramReplyFailedException` cubría dos fallas distintas —Telegram respondió y
  rechazó, o Telegram no respondió— y ambas se traducían a `502 Bad Gateway` con el texto de la
  excepción. El cliente no tenía forma de distinguirlas. Apareció al corregir CR-010.
- **Por qué importa:** son accionables al revés. Si el bot está bloqueado, reintentar **nunca** va a
  funcionar y el operador necesita saberlo; si la Bot API no respondió, reintentar es exactamente lo
  que corresponde. Ofrecer el mismo botón "Reintentar" para las dos induce al error.
- **Corrección:** la excepción lleva ahora `TelegramDescription`, nulo cuando Telegram no contestó.
  El manejador mapea rechazo a `502` y falla de transporte a `504 Gateway Timeout`, y el cliente
  redacta cada caso por separado.
- **Estado:** CORREGIDO. Rechazo verificado en vivo con el bot bloqueado (`502` + descripción de
  Telegram). El camino `504` no se forzó: exigiría cortar la salida a `api.telegram.org`.

### CR-017 — Al reconectar se pedía una conversación que ya no existía
- **Severidad:** MEDIA
- **Dónde:** `src/frontend/src/app/core/services/inbox-store.ts`, `reloadInboxState`
- **Evidencia:** con una conversación abierta y el navegador sin tocar, bajar y volver a levantar el
  stack con `docker compose down --volumes` hacía aparecer **por sí solo** el banner `La conversación
  ya no existe. Actualizá la bandeja.` junto a `Todavía no hay conversaciones`: dos afirmaciones que
  se contradicen en la misma pantalla. Se dispara en la reconexión de SignalR, sin intervención del
  operador. Un `F5` lo limpiaba, porque la selección sólo vive en memoria.
- **Causa:** `reloadInboxState` recarga el listado y después pide el historial de la conversación
  seleccionada **sin comprobar que ese contacto siga existiendo**. Con la base reseteada, el
  contacto ya no está, la API responde `404` y el cliente lo trata como un error que mostrar.
- **Por qué importa:** el banner no es accionable. "Actualizá la bandeja" no arregla nada, porque
  la bandeja ya está actualizada: lo que sobra es la selección en memoria.
- **Corrección:** si el contacto abierto no aparece en el listado recién traído, se limpia la
  selección y se vacía la conversación en vez de pedir su historial.
- **Cómo apareció:** es preexistente, pero estaba oculto. Antes de corregir CR-010 el mismo caso
  mostraba `Contact … was not found.` en inglés, y se leía como ruido del servidor. Localizar el
  mensaje lo volvió legible, y recién ahí se notó que la frase era absurda. **Una corrección
  cosmética destapó un defecto de comportamiento.**
- **Estado:** CORREGIDO y verificado reproduciendo el escenario.

---

> Los seis hallazgos siguientes salieron de una **segunda revisión completa de `src/`** el 2026-11-03,
> ya con el sistema verificado de punta a punta. Ninguno se había visto en la primera pasada: tres de
> ellos sólo se manifiestan con más de un contacto o con una falla a mitad de camino, que es
> exactamente lo que una corrida feliz no ejercita.

### CR-018 — Reiniciar la API descartaba el mensaje que estaba procesando
- **Severidad:** ALTA
- **Dónde:** `src/backend/EncodeLabs.Infrastructure/Messaging/RabbitMq/IncomingTelegramMessageConsumerService.cs`
- **Evidencia:** el `catch` de cancelación comentaba *"leave the delivery unacknowledged so it is
  redelivered"* y devolvía `false`; el llamador trataba `false` como veneno y hacía
  `BasicNackAsync(requeue: false)`. **El comentario declaraba una intención y el código hacía la
  contraria.**
- **Por qué importa:** un `docker compose restart api` con un mensaje en vuelo lo mandaba a la DLQ
  para siempre. Telegram no lo reintenta porque ya había respondido `202`. Y el propio README
  recomienda ese `restart api` en Troubleshooting, así que el defecto se disparaba siguiendo nuestras
  instrucciones.
- **Causa de fondo:** un booleano no alcanzaba para tres situaciones distintas. "No confirmes" y
  "rechazá" son decisiones opuestas y estaban colapsadas en el mismo `false`.
- **Corrección:** un enum `DeliveryDisposition` con `Acknowledge`, `DeadLetter` y
  `LeaveUnacknowledged`. El apagado devuelve el tercero y el handler no hace *nada*: sin ack ni nack,
  RabbitMQ reencola al cerrarse el canal.
- **Estado:** CORREGIDO y **verificado en vivo** (E-20): con la base detenida y un mensaje
  reintentándose, `docker compose restart api` dejó
  `Shutting down while processing Telegram update 999000001; leaving the delivery unconfirmed so
  RabbitMQ redelivers it.`, la cola principal en **1** y la DLQ en **0**.

### CR-019 — El borrador de respuesta se cruzaba de contacto
- **Severidad:** ALTA
- **Dónde:** `src/frontend/src/app/features/inbox/message-composer.component.ts`
- **Evidencia:** `replyText` era un `signal('')` que nunca se limpiaba al cambiar de conversación, y
  el componente **no se recrea**: vive dentro de la misma rama `@else` de
  `conversation-panel.component.html`, así que Angular reutiliza la instancia.
- **Por qué importa:** escribir una respuesta para Ana, cambiar a Bruno y presionar Enter **le manda
  a Bruno el texto escrito para Ana**. En una bandeja de mensajería, entregarle un mensaje a la
  persona equivocada es el peor defecto posible, y es irreversible: el enunciado excluye editar y
  borrar.
- **Por qué no se había visto:** la verificación de punta a punta se hizo con **un solo contacto**.
  Ninguna corrida feliz lo expone.
- **Corrección:** `linkedSignal` atado al contacto seleccionado, de modo que el borrador se descarta
  cuando cambia la conversación.
- **Estado:** CORREGIDO. Cubierto además por los tests de varios remitentes.

### CR-020 — Una respuesta entregada podía reportarse como fallida
- **Severidad:** ALTA
- **Dónde:** `src/backend/EncodeLabs.Application/Inbox/UseCases/SendOperatorReplyHandler.cs`
- **Evidencia:** el envío a Telegram ocurre antes de `SaveChangesAsync`. Si la base fallaba después
  del envío, la excepción caía en el `_ =>` del manejador y salía un `500` genérico; la interfaz
  mostraba "No se pudo enviar" y **dejaba el texto para reintentar**.
- **Por qué importa:** el contacto **ya había recibido** el mensaje. El operador reintentaba de buena
  fe y le llegaba duplicado. La interfaz inducía exactamente la acción incorrecta.
- **Corrección:** `ReplyDeliveredButNotRecordedException`, mapeada a un `ProblemDetails` con un
  `type` estable —el código de estado no alcanza, es un `500` como cualquier otro—. El cliente lo
  reconoce, **limpia el campo** para que nadie reenvíe y explica que la respuesta sí llegó.
- **Estado:** CORREGIDO. Cubierto por
  `AReplyTelegramDeliveredButTheDatabaseRejected_IsReportedAsDeliveredNotFailed`.

### CR-021 — Un cuerpo malformado devolvía 500 y se logueaba como error
- **Severidad:** MEDIA
- **Dónde:** `src/backend/EncodeLabs.Api/ErrorHandling/InboxExceptionHandler.cs`
- **Evidencia:** `BadHttpRequestException` no tenía caso propio y caía en el `_ =>`.
- **Por qué importa:** un cuerpo ilegible es culpa del cliente, no del servidor. Además, en el
  webhook público esto ocurre **antes** de validar el secreto, así que cualquiera puede llenar el log
  de errores mandando basura.
- **Corrección:** caso explícito que devuelve `400`, que además lo baja a nivel informativo.
- **Estado:** CORREGIDO.

### CR-022 — El estado vacío tranquilizaba cuando en realidad había fallado la carga
- **Severidad:** MEDIA
- **Dónde:** `contact-list.component.html`, `conversation-panel.component.html`
- **Evidencia:** si `GET /api/contacts` fallaba, el panel mostraba `Todavía no hay conversaciones`
  —con su sugerencia amable de escribirle al bot— debajo del banner rojo que decía que algo había
  fallado. Mismo caso en el historial.
- **Por qué importa:** es la **misma clase de contradicción que CR-017**, que creíamos cerrada:
  la pantalla afirma dos cosas incompatibles y el operador no sabe cuál creer. Peor aún, el estado
  vacío es el más tranquilizador de todos.
- **Corrección:** `hasLoadedConversation` en el store junto al `hasLoadedContacts` que ya existía, y
  una rama explícita de "no se pudo cargar" en ambas plantillas.
- **Estado:** CORREGIDO.

### CR-023 — La vista previa podía cortar un emoji al medio
- **Severidad:** BAJA
- **Dónde:** `src/backend/EncodeLabs.Application/Inbox/ReadModels/ContactSummary.cs`
- **Evidencia:** `AsSpan(0, 160)` corta por unidades `char`. Un emoji ocupa dos, así que un mensaje
  largo con un emoji sobre el límite dejaba un sustituto huérfano, que se dibuja como `�`.
- **Por qué importa:** en el listado de contactos, un `�` se lee como dato corrupto en la base, no
  como un texto recortado.
- **Corrección:** el corte retrocede una posición cuando caería entre las dos mitades de un par
  suplente.
- **Estado:** CORREGIDO. Cubierto por `AnEmojiStraddlingTheCutIsDroppedRatherThanSplit`.

### CR-024 — nginx cacheaba la dirección de la API y quedaba hablando sola
- **Severidad:** ALTA
- **Dónde:** `src/frontend/nginx.conf`
- **Evidencia:** la API respondía `200` en `localhost:8080` y **sana** según `docker compose ps`,
  pero todo fallaba con `502` a través de `localhost:4200`. En el log de nginx:
  `connect() failed (111: Connection refused) ... upstream: "http://172.26.0.4:8080/..."`, mientras
  el contenedor `api` estaba en `172.26.0.2`.
- **Causa:** `proxy_pass http://api:8080` con un nombre literal se resuelve **una sola vez, al cargar
  la configuración**, y nginx cachea esa dirección para siempre. Cada vez que el contenedor `api` se
  recrea recibe una dirección nueva, y nginx sigue marcando la vieja.
- **Por qué importa:** la bandeja queda completamente rota —sin listado y sin tiempo real— con todos
  los contenedores sanos, que es el diagnóstico más confuso posible. Y se dispara siguiendo
  **nuestras propias instrucciones**: el README recomienda `docker compose restart api` en
  Troubleshooting. El arranque limpio nunca lo expone; sólo aparece al reiniciar.
- **Corrección:** `resolver 127.0.0.11` (el DNS interno de Docker) y el destino nombrado a través de
  una variable, `proxy_pass http://$inbox_api_host:8080`, que fuerza a nginx a resolver en cada
  petición en vez de cachear.
- **Estado:** CORREGIDO y **verificado forzando el escenario** (E-22): se recreó `api` con otro
  contenedor ocupando su dirección anterior, de modo que pasó de `172.26.0.2` a `172.26.0.7`. Sin
  reiniciar nginx, `/api/contacts` y `/hubs/inbox/negotiate` siguieron devolviendo `200`.

### CR-025 — El `502` de nginx se mostraba como un rechazo de Telegram
- **Severidad:** MEDIA
- **Dónde:** `src/frontend/src/app/core/services/inbox-api.service.ts`, `describeRequestFailure`
- **Evidencia:** con nginx sin poder llegar a la API (CR-024), la bandeja mostró
  `Telegram rechazó la respuesta.` — Telegram nunca estuvo involucrado.
- **Causa:** la corrección de CR-016 asumió que **todo** `502` proviene de nuestro manejador de
  excepciones. Pero hay un proxy inverso delante que emite sus propios `502` y `504`, con cuerpo
  HTML en vez de `ProblemDetails`.
- **Por qué importa:** manda al operador a investigar el sistema equivocado. Es además una lección
  sobre la corrección anterior: **un código de estado no identifica quién produjo la respuesta**
  cuando hay intermediarios en el camino.
- **Corrección:** antes de interpretar `502`/`504` se comprueba que el cuerpo sea realmente nuestro
  `ProblemDetails`; si no lo es, el mensaje señala a la API inalcanzable.
- **Estado:** CORREGIDO.

## La compuerta

Ocho defectos en dos pasadas de revisión dejaron clara una asimetría: **encontrarlos no sirve si
nada impide que vuelvan**. Desde CR-025, la regla es que ningún cambio está revisado hasta que
[`scripts/verify.sh`](../scripts/verify.sh) termina en 0, y
[`.github/workflows/ci.yml`](../.github/workflows/ci.yml) lo repite en cada push y cada pull request.

Cada hallazgo corregido tiene ahora un test que falla si alguien revierte la corrección, ubicado
donde estuvo el error:

| Hallazgo | Dónde quedó cubierto |
|---|---|
| CR-016, CR-020, CR-021 | `EncodeLabs.Api.UnitTests` — el mapeo de fallas a códigos de estado |
| CR-018 | `EncodeLabs.Infrastructure.UnitTests` — la política de reintentos, extraída del consumidor para poder probarla sin broker |
| CR-019, CR-020 | `message-composer.component.spec.ts` — el borrador y qué lo limpia |
| CR-022 | `contact-list.component.spec.ts` — el estado vacío contra la carga fallida |
| CR-010, CR-025 | `inbox-api.service.spec.ts` — la redacción de errores y quién produjo la respuesta |
| CR-021, y el secreto del webhook | `scripts/smoke-test.sh` — contra la API real |
| **CR-024** | `scripts/smoke-test.sh` — **no tiene test unitario posible**: es cableado entre contenedores |

Los tests se verificaron revirtiendo la corrección y mirándolos fallar. CR-018 derriba
`ShutdownLeavesTheDeliveryUnacknowledgedInsteadOfDeadLetteringIt`; CR-019 derriba los dos tests del
borrador. **Un test que nunca falló no es evidencia, es una afirmación.**

La prueba de humo de CR-024 además dejó al descubierto un matiz que no habíamos medido: la
recuperación de nginx **no es instantánea**, porque mantiene la dirección anterior hasta que vence el
TTL del resolver. La primera versión del guion falló por exigir un `200` inmediato. Está documentado
en vez de disimulado bajando el TTL.

## Comandos efectivamente ejecutados

| Comando | Resultado |
|---|---|
| `dotnet build` (solución completa, `src/backend`) | **OK** — `Build succeeded.` sin advertencias, con `TreatWarningsAsErrors`. |
| `dotnet run --project tests/EncodeLabs.Domain.UnitTests -c Release` | **OK** — `Total: 25, Errors: 0, Failed: 0, Skipped: 0`. |
| `dotnet run --project tests/EncodeLabs.Application.UnitTests -c Release` | **OK** — `Total: 13, Errors: 0, Failed: 0`. Varios remitentes, respuestas del operador y recorte de vistas previas. |
| `dotnet run --project tests/EncodeLabs.Infrastructure.UnitTests -c Release` | **OK** — `Total: 7, Errors: 0, Failed: 0`. La política de reintentos y las tres disposiciones de una entrega. |
| `dotnet run --project tests/EncodeLabs.Api.UnitTests -c Release` | **OK** — `Total: 8, Errors: 0, Failed: 0`. El mapeo de fallas a códigos de estado. |
| `ng test` (en Docker, Node fijado) | **OK** — 27 tests en 3 archivos, 0 fallos. |
| `scripts/verify.sh` | **OK** — build y los 80 tests en verde. |
| `scripts/smoke-test.sh` | **OK** — contra el stack levantado, incluida la recreación de la API en otra dirección. |
| `dotnet ef migrations add InitialInboxSchema` | **OK** — migración generada e inspeccionada (identidad, `timestamptz`, índice único de deduplicación). |
| `npx ng build` (producción) | **OK** — 245,55 kB iniciales / 65,43 kB transferidos, sin errores de plantilla ni de tipos. |
| `docker compose config` | **OK** — configuración válida, interpolación correcta. |
| `docker compose build` | **OK** — ambas imágenes construidas (frontend 61,8 MB). |
| `docker compose up -d` | **OK** tras corregir CR-013 — los 5 servicios arrancan; `postgres` y `rabbitmq` quedan `healthy`; la API aplica migraciones en el intento 1 y declara la cola y su DLQ. |
| Pipeline entrante simulando a Telegram | **OK** — ver la tabla de abajo. Se ejecutó con el secreto correcto contra el webhook local, con un `.env` de prueba de valores ficticios guardado **fuera** del repositorio. |
| Prueba end-to-end con bot y túnel reales | **OK** — ejecutada con un bot propio y ngrok. Resultados en la tabla "Resultados con bot y túnel reales". |

### Resultados del pipeline entrante (ejecutado)

| Comprobación | Resultado observado |
|---|---|
| `GET /health` | `200` |
| `GET /api/contacts` con base vacía | `200` + `[]` — **confirma que las sub-consultas correlacionadas de EF traducen a SQL** contra PostgreSQL real (cerraba el riesgo abierto de proyecciones). |
| `POST` al webhook **sin** el header del secreto | `401`, nada encolado. |
| `POST` al webhook con mensaje de texto válido | `202` (tras la confirmación del broker) → persistido → visible en `/api/contacts`. |
| Misma entrega repetida (mismo `message_id`) | `202` ambas veces, **un solo mensaje almacenado**; en el log: `Discarded duplicate delivery of Telegram message 11 (update 100001)`. |
| Mensaje sin texto (foto) | `200` + `Ignoring Telegram update …: the message has no text content`. |
| Mensaje de un bot | `200` + `… the message was sent by a bot`. |
| Mensaje de grupo | `200` + `… does not belong to a private chat`. |
| Contacto sin nombre ni username | `displayName: "Telegram user 55502"`. |
| Vista previa, dirección y orden por actividad | Correctos en la respuesta de `/api/contacts`. |
| `GET /api/contacts/{id}/messages` | Historial en orden, con `sequenceNumber` 1 y 2 y `highestSequenceNumber: 2`. |
| Marcado como leído con corte en secuencia **1** | `unreadMessageCount: 1` — el mensaje de secuencia 2 **no** se marcó. Es la demostración del requisito 4. |
| Marcado como leído con corte en secuencia **2** | `unreadMessageCount: 0`. |
| `POST /hubs/inbox/negotiate` **a través de nginx** | `200` con `WebSockets`, `ServerSentEvents` y `LongPolling` disponibles — valida el proxy de `/hubs`. |
| `GET /` y `GET /api/contacts` **a través de nginx** (`:4200`) | `200` en ambos — valida el bundle servido y el proxy de `/api` (confirma la corrección CR-004). |
| Reintento de registro del webhook sin túnel | Dos líneas legibles por intento y cuenta `Attempt N/30`, sin stack traces (CR-015). |

### Resultados con bot y túnel reales (ejecutado)

Corrida del 2 de noviembre de 2026 con un bot propio (`@encodelabs_inbox_gd_bot`) y una cuenta de
ngrok real. Cierra los riesgos 1, 2, 3 y 6 de la lista anterior.

| Comprobación | Resultado observado |
|---|---|
| Registro automático del webhook | `Telegram webhook registered for https://3ed5-….ngrok-free.app/api/telegram/webhook`, sin intervención manual. |
| Confirmación desde el lado de Telegram | `getWebhookInfo` → la URL esperada, `pending_update_count: 0`, `last_error_message: none`, `allowed_updates: ['message']`. |
| Mensaje entrante real | `POST /api/telegram/webhook → 202` visto en el inspector de ngrok; contacto `Gabriel Agrech` creado; mensaje persistido. |
| Latencia del webhook | 43 ms y 104 ms, incluyendo publicar y esperar la confirmación del broker. |
| Marcado como leído desde la UI real | `Marked 2 incoming message(s) as read … up to sequence 2; 0 still unread`. |
| Respuesta saliente real | `Stored outgoing message … (Telegram message 3)`. El `message_id` lo asigna Telegram, así que su presencia prueba que `sendMessage` fue aceptado. |
| URL de ngrok nueva (`restart ngrok api`) | La API arrancó antes que el túnel: `Attempt 1/30: no public HTTPS tunnel available yet; retrying in 5s` y luego registro con la URL nueva (`4ebf-…`). **El reintento acotado quedó ejercitado, no solo escrito.** |
| Broker caído, mensaje real (E-11) | 7 × `503` en el inspector de ngrok con el **mismo `update_id` 571282815** (es Telegram reintentando la misma entrega) y luego `202` al levantar RabbitMQ. Mensaje persistido, DLQ en 0: no se perdió nada. |
| Base caída, mensaje real (E-12) | `Attempt 1/3`, `2/3`, `3/3 to process Telegram update 571282816 failed` → `Delivery 2 was routed to …dead-letter`. Payload íntegro y legible en la DLQ, con la cabecera `x-death: rejected`. |
| Envío fallido (E-07) | Bot bloqueado por el usuario → banner con el texto real de Telegram, **sin burbuja** y sin fila nueva en la base (5 mensajes, 1 saliente). El texto escrito quedó en el campo. |

## Riesgos abiertos

1. **Un mensaje que agota los reintentos no llega a la bandeja.** Demostrado en E-12: con la base
   caída, el mensaje terminó en la dead-letter queue y **no** aparece en la interfaz. Telegram
   tampoco lo reintenta, porque el webhook ya había respondido `202` al encolarlo. El payload queda
   íntegro y recuperable, pero **el reproceso es manual**. Es una decisión consciente —reencolar
   haría un bucle infinito con un mensaje venenoso— y en producción se acompañaría de una alerta
   sobre la profundidad de esa cola; no se agregó observabilidad porque el enunciado no la pide.
   El procedimiento de recuperación está documentado en el README (sección 9) y **ejecutado de
   punta a punta** en E-19: el mensaje descartado volvió a la bandeja y republicarlo dos veces no
   duplicó nada. El riesgo sigue abierto porque la recuperación requiere un operador, no porque el
   mensaje sea irrecuperable.
2. **El camino `504` de CR-016 no se forzó.** Verificar "la Bot API no responde" exigiría cortar la
   salida del contenedor hacia `api.telegram.org`. El rechazo (`502`) sí se verificó en vivo.
3. **Entrega *at-least-once*, no *exactly-once*.** Propiedad del diseño; la deduplicación la
   absorbe, y quedó demostrada.
4. **Entre el commit y la notificación de SignalR no hay atomicidad.** El cliente reconcilia al
   reconectar y al refrescar (ADR-002).
5. **La conversación abierta no sobrevive al refresco.** El historial sí se recupera entero desde la
   API, pero la selección es estado en memoria y vuelve a cero (supuesto 15 del README). No es un
   defecto de recuperación sino la consecuencia de no tener ruta por conversación.
