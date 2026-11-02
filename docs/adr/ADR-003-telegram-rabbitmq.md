# ADR-003: Flujo del webhook y topología de colas en RabbitMQ

**Estado:** Aceptado

## Contexto

Telegram entrega cada update con un `POST` al webhook y **reintenta si no recibe un 2xx**. El
endpoint es público: cualquiera que descubra la URL de ngrok puede golpearlo. Y el procesamiento
real (resolver el contacto, deduplicar, persistir, notificar) toca la base de datos, que puede
estar lenta o caída.

Eso deja tres problemas: autenticar la entrega, no sostener la petición de Telegram mientras se
escribe en la base, y no perder mensajes si algo falla en el medio. El enunciado además exige
RabbitMQ para el procesamiento de los mensajes entrantes.

## Alternativas consideradas

| Opción | Ventajas | Desventajas |
|---|---|---|
| **Procesar dentro del webhook, sin cola** | Menos piezas; el mensaje aparece antes. | Contradice el requisito de usar RabbitMQ; ata el timeout de Telegram a la latencia de la base; un pico de mensajes golpea la base sin amortiguación. |
| **Cola + reintentos infinitos con `requeue: true`** | No se pierde nada. | Un *poison message* (payload inválido, invariante de dominio violada) vuelve a la cabeza de la cola para siempre y bloquea el consumo. |
| **Cola + retry queue con TTL y reintento diferido** | Back-off sin ocupar el consumidor. | Topología de tres o cuatro colas para un caso donde la falla esperable es una indisponibilidad de segundos: complejidad que no se paga. |
| **Cola + reintentos acotados en proceso + dead-letter** *(elegida)* | Topología mínima; el poison message termina en un lugar inspeccionable en vez de girar. | Durante los reintentos el delivery queda sin confirmar y ocupa prefetch. |

## Decisión

**El webhook autentica, valida, encola y contesta; el consumidor persiste.**

1. **Autenticación.** Se valida el header `X-Telegram-Bot-Api-Secret-Token` contra el secreto
   configurado, con comparación de tiempo fijo. Sin header válido: `401`, sin tocar la cola.
2. **Validación de alcance.** Solo se aceptan mensajes de texto, de un humano (`is_bot: false`), en
   chat `private`. Todo lo demás (multimedia, ediciones, mensajes de servicio, grupos) devuelve
   `200`: es la forma de decirle a Telegram "recibido, no insistas" sobre algo que este sistema
   nunca va a procesar.
3. **Encolado con confirmación.** Se publica en una topología *direct* y **durable**, con
   `publisher confirms` y mensajes persistentes. El `202` se devuelve recién cuando el broker
   confirmó. Si no confirma, se responde `503` para que Telegram reintente: nunca se acusa recibo
   de un mensaje que no quedó encolado.
4. **Consumo con ACK manual.** El consumidor corre como `BackgroundService` dentro del mismo host
   (requisito de un solo desplegable), con `prefetch 10` y despacho secuencial. Confirma el
   delivery **solo después** del commit en la base.

**Topología:**

| Elemento | Nombre |
|---|---|
| Exchange (direct, durable) | `encodelabs.inbox.telegram` |
| Routing key | `telegram.incoming-message` |
| Cola (durable, con DLX) | `encodelabs.inbox.telegram.incoming-messages` |
| Dead-letter exchange | `encodelabs.inbox.telegram.dead-letter` |
| Dead-letter queue | `encodelabs.inbox.telegram.incoming-messages.dead-letter` |

**Manejo de fallas en el consumidor:** hasta 3 intentos con back-off lineal para fallas transitorias
(base caída, timeout). Las fallas permanentes —JSON ilegible o violación de invariante de dominio—
no se reintentan: van directo a la dead-letter queue. Agotados los intentos, `basic.nack` con
`requeue: false` manda el mensaje a la DLQ, donde queda visible en la consola de RabbitMQ
(`http://localhost:15672`) para inspección manual.

**Idempotencia.** La entrega es *at-least-once*, no *exactly-once*. La deduplicación es por
`(contacto, message_id de Telegram)`: primero una consulta barata y, como red definitiva, el índice
único de PostgreSQL, que cubre el caso de dos entregas procesadas concurrentemente.

## Consecuencias

**Ganamos:** el webhook responde en milisegundos; un pico de mensajes se amortigua en la cola; nada
se pierde ante un reinicio del consumidor (sin ACK → redelivery); un mensaje venenoso termina
aislado en vez de bloquear la cola; el `503` ante broker caído aprovecha el reintento de Telegram.

**Resignamos:** latencia extra (un salto por el broker), una pieza más que operar, y la
posibilidad de que el mismo mensaje se procese dos veces —que es exactamente lo que la
deduplicación absorbe.

**Riesgos que quedan:** si el consumidor cae *después* del commit y *antes* del ACK, el mensaje se
redelivera y se descarta por duplicado, pero la notificación SignalR de esa primera pasada puede
haberse emitido o no; el cliente reconcilia al reconectar (ver ADR-002). Los mensajes en la DLQ
requieren intervención manual: no hay reproceso automático, decisión consciente para no esconder
fallas. Y si Telegram reintenta mientras el broker está caído, los updates quedan pendientes del
lado de Telegram hasta que el `503` deje de devolverse.
