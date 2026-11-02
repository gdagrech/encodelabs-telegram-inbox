# ADR-002: Tiempo real con SignalR (WebSocket con degradación automática)

**Estado:** Aceptado

## Contexto

Cuando un mensaje entrante se persiste, la bandeja abierta en el navegador tiene que reflejarlo sin
recargar: el contacto sube al tope del listado, aparece la vista previa, cambia el contador de no
leídos y, si la conversación está abierta, el mensaje se agrega al historial. Lo mismo vale para
los cambios de estado de leído, que pueden originarse en otra pestaña.

El backend es ASP.NET Core y el frontend Angular. No hay autenticación ni usuarios: todos los
clientes conectados miran la misma bandeja compartida. El tráfico es bajo y esporádico, pero la
conexión puede durar horas y debe sobrevivir a un reinicio de la API.

## Alternativas consideradas

| Opción | Ventajas | Desventajas |
|---|---|---|
| **SignalR** | Primera parte en ASP.NET Core: hub tipado, cliente oficial `@microsoft/signalr`, reconexión automática con back-off y negociación de transporte (WebSocket y, si no está disponible, Server-Sent Events o long polling) sin código propio. | Acopla el cliente a una librería de Microsoft; agrega una dependencia npm. |
| **WebSocket "pelado"** (`System.Net.WebSockets`) | Sin dependencias; control total del protocolo. | Habría que escribir a mano el framing de mensajes, el heartbeat, la reconexión con back-off y el manejo de desconexiones: justamente lo que SignalR ya resuelve y probó en producción. |
| **Server-Sent Events** | Simple, sobre HTTP, reconexión nativa del navegador. | Unidireccional (alcanza para este caso), pero sin librería hay que implementar el formato de eventos y el reenvío; y deja la puerta cerrada si en el futuro el cliente necesita invocar algo. |
| **Polling cada N segundos** | Trivial de implementar y de depurar. | No es tiempo real: o se nota la latencia o se castiga la base con consultas constantes. Contradice el requisito explícito del enunciado. |

## Decisión

**SignalR**, con un único hub `InboxHub` en `/hubs/inbox` y dos eventos de salida:

- `MessageAdded`: lleva el mensaje recién persistido **y** la fila actualizada del listado de
  contactos, para que el cliente no tenga que volver a pedir el listado.
- `ConversationReadStateChanged`: lleva el contacto, el nuevo contador de no leídos y el corte de
  lectura aplicado.

El hub es **solo de empuje**: no expone métodos invocables. Todas las acciones del operador viajan
por HTTP, de modo que hay un único camino de comandos y el canal de tiempo real se limita a
notificar. Como no hay autenticación, se emite a todos los clientes (`Clients.All`); introducir
grupos por operador recién tiene sentido junto con autenticación.

Las notificaciones se publican **siempre después del commit** en la base. Un fallo al emitir se
registra como warning pero no propaga: el dato ya está persistido y fallar ahí convertiría una
entrega confirmada en una redelivery innecesaria.

## Consecuencias

**Ganamos:** latencia baja real sin escribir infraestructura de transporte; reconexión con back-off
exponencial del lado del cliente sin código propio; degradación automática de transporte si el
WebSocket no está disponible.

**Resignamos:** una dependencia npm en el frontend y el acoplamiento del cliente al protocolo de
SignalR.

**Riesgos que quedan:** entre el commit en PostgreSQL y el envío de la notificación no hay
atomicidad. Si la API cae en esa ventana, o el socket está caído, el cliente **no** recibe ese
evento. Se mitiga del lado del cliente: cada vez que la conexión se establece o se restablece, el
store vuelve a pedir el listado de contactos y la conversación abierta, de modo que el estado se
reconcilia contra la base. Un refresh del navegador tiene el mismo efecto. Es decir: el tiempo real
es una optimización de latencia, no la fuente de verdad.
