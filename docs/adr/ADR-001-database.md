# ADR-001: Base de datos relacional (PostgreSQL + EF Core)

**Estado:** Aceptado

## Contexto

La bandeja necesita persistir contactos y mensajes y, sobre esos datos, responder tres preguntas en
cada pantalla: el listado de contactos ordenado por última actividad, la cantidad de mensajes no
leídos por contacto y el historial cronológico de una conversación. Además hay dos requisitos de
consistencia que pesan más que el volumen:

- Telegram y RabbitMQ entregan *al menos una vez*: el mismo mensaje puede llegar dos veces y debe
  almacenarse una sola.
- Marcar como leído y contar no leídos tienen que coincidir siempre, incluso si llega un mensaje
  nuevo durante la operación.

El volumen esperado es bajo (una empresa atendiendo consultas, un operador), y el modelo es
pequeño y estable: dos entidades con una relación uno a muchos.

## Alternativas consideradas

| Opción | Ventajas | Desventajas |
|---|---|---|
| **PostgreSQL + EF Core** | Unicidad declarativa `(contacto, message_id)` que convierte la deduplicación en una garantía del motor, no del código; transacciones ACID para "guardar mensaje + actualizar contacto"; columna identidad para un orden de llegada monótono; migraciones versionadas; índices parciales/compuestos para los contadores. | Requiere migraciones y un esquema explícito; un ORM agrega una capa de traducción. |
| **MongoDB** | Esquema flexible; natural para documentos tipo "conversación con mensajes embebidos". | La deduplicación exige índices únicos igual, pero las actualizaciones multi-documento (mensaje + contacto) complican la atomicidad; embeber mensajes en el contacto hace crecer el documento sin límite; no aporta nada frente a un modelo fijo de dos entidades. |
| **SQLite** | Cero operación, ideal para una prueba técnica. | Un archivo dentro de un contenedor desentona con un stack Compose con servicios reales; escrituras concurrentes del consumidor y de la API compiten por el lock de escritura. |
| **Redis como almacén principal** | Muy rápido para contadores de no leídos. | No es un almacén durable adecuado para el historial; perderíamos el requisito de persistencia real. |

## Decisión

**PostgreSQL 18 con EF Core 10 y migraciones versionadas**, aplicadas automáticamente al arrancar
la API (con reintentos mientras la base termina de levantar).

El modelo son dos tablas. `contacts` tiene índice único sobre `TelegramUserId`, lo que hace
idempotente la creación de contactos. `messages` tiene índice único sobre
`(ContactId, TelegramMessageId)`: como el `message_id` de Telegram es único dentro de un chat en
ambas direcciones, esa es la clave natural de deduplicación y el motor rechaza la segunda inserción
aunque dos entregas se procesen en paralelo. `messages.SequenceNumber` es una columna identidad que
da un orden de llegada estrictamente creciente; es lo que permite marcar como leído "hasta acá" de
forma determinista, algo que los timestamps de Telegram (resolución de un segundo) no garantizan.

Los contadores de no leídos y la vista previa del último mensaje se **calculan por consulta**, no se
desnormalizan en `contacts`: con este volumen el costo es despreciable y evita la clase entera de
bugs en que el contador y los mensajes dejan de coincidir.

## Consecuencias

**Ganamos:** deduplicación garantizada por el motor en lugar de por código; una transacción por
mensaje entrante que cubre contacto y mensaje a la vez; un orden de lectura exacto para el estado
de leído; esquema versionado y reproducible con `docker compose up`.

**Resignamos:** flexibilidad de esquema (cualquier cambio futuro necesita migración) y una
dependencia más en el stack. Calcular los no leídos por consulta tiene un costo que crecería con el
volumen.

**Riesgos que quedan:** si la bandeja creciera a miles de conversaciones con historiales largos,
habría que paginar el historial (hoy explícitamente fuera de alcance) y probablemente materializar
el contador de no leídos con un índice parcial o una columna mantenida transaccionalmente. El
índice `ix_messages_contact_id_direction_is_read` ya existe para ese camino.
