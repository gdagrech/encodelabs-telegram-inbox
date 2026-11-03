# ADR-004: Un solo host desplegable para API, hub y consumidor

**Estado:** Aceptado

## Contexto

La solución tiene cuatro responsabilidades de servidor: atender el webhook de Telegram, servir la
API de la bandeja, empujar eventos en tiempo real y consumir la cola de RabbitMQ. La pregunta es si
viven en un proceso o en varios.

La respuesta de manual es separar el consumidor en su propio desplegable "para escalarlo
independiente". Esa respuesta merece examinarse, porque acá tiene un costo concreto: **después de
persistir un mensaje hay que notificar a los navegadores conectados**, y quien persiste es el
consumidor mientras que quien tiene las conexiones abiertas es el hub.

El enunciado no obliga a un solo desplegable; exige que todo levante con `docker compose up`. La
decisión es propia y conviene dejarla explícita porque condiciona el resto de la arquitectura.

## Alternativas consideradas

| Opción | Ventajas | Desventajas |
|---|---|---|
| **Worker separado + backplane de SignalR (Redis)** | Escala el consumo sin escalar la API; aislamiento de fallas entre ambos. | Introduce Redis como dependencia nueva **sólo** para que dos procesos propios se hablen; dos imágenes, dos despliegues, dos configuraciones; el enunciado no pide escalar nada. |
| **Worker separado notificando por HTTP a la API** | Evita Redis. | Inventa un protocolo interno entre dos componentes del mismo sistema; hay que manejar reintentos y timeouts de esa llamada; acopla igual, pero peor y a mano. |
| **Worker separado escribiendo a una tabla que la API sondea** | Sin dependencias nuevas. | Reintroduce el *polling* que SignalR vino a eliminar; latencia peor que la del diseño actual. |
| **Un solo host con el consumidor como `BackgroundService`** *(elegida)* | El consumidor y el hub comparten proceso: notificar es una llamada en memoria; una imagen, un despliegue, un lugar donde mirar logs. | El consumo se detiene mientras el host se reinicia; no se puede escalar el consumo sin escalar también la API. |

## Decisión

**Un único proceso ASP.NET Core** aloja el webhook, la API, el hub de SignalR y el consumidor,
registrado como `BackgroundService` en el mismo host.

El criterio aplicado fue: *separar procesos se justifica cuando hay un recurso que escala distinto o
una falla que conviene aislar.* Acá no hay ninguno de los dos. El trabajo del consumidor es resolver
un contacto, insertar una fila y emitir un evento —microsegundos de CPU dominados por la latencia de
la base—, así que no existe el cuello de botella que la separación vendría a resolver.

Separarlo habría significado agregar Redis como backplane para que dos componentes propios se
comuniquen: **infraestructura nueva para resolver un problema que la separación misma crea.**

## Consecuencias

**Ganamos:** notificar tras persistir es una llamada en memoria, sin backplane ni protocolo interno;
una sola imagen que construir, desplegar y observar; el flujo completo de un mensaje se sigue en un
solo archivo de logs, que es lo que más acelera el diagnóstico.

**Resignamos:** el consumo de la cola se detiene mientras el host se reinicia, y no se puede dar más
capacidad de consumo sin agregar también capacidad de API. Está mitigado, no eliminado: los mensajes
quedan en la cola durable y el consumidor se resuscribe al volver —verificado reiniciando la API con
mensajes en vuelo—, pero es el costo real de esta decisión.

**Riesgos que quedan:** con más de una instancia de API, SignalR necesitaría un backplane para que un
evento llegue a los clientes conectados a otra instancia. Es la primera frontera de este diseño y el
primer lugar donde Redis se justificaría.

**Qué invalidaría esta decisión:** que el procesamiento por mensaje se vuelva caro —transcripción de
audio, OCR, llamadas a un modelo—. Ahí un pico de trabajo pesado degradaría las respuestas HTTP del
operador, y separar el consumidor dejaría de ser ceremonia para pasar a ser necesario.
