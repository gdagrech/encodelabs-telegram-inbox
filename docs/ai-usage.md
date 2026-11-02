# Trabajo con IA

## 1. Herramientas usadas

**Claude Code** (Anthropic, modelo Opus 5) corriendo en la terminal, con acceso al sistema de
archivos y a la shell del proyecto. Fue la única herramienta de IA usada: no hubo Copilot, ni
ChatGPT, ni generadores de scaffolding por IA además del `ng new` oficial de Angular.

La decisión de fondo fue usar un asistente **con acceso a la shell** en lugar de uno de chat: lo que
importaba no era generar código, sino que cada afirmación quedara respaldada por un comando
ejecutado. Casi todos los hallazgos reales de `code-review.md` salieron de compilar y correr, no de
leer.

## 2. Agentes y roles

**No se usaron múltiples agentes.** Un único agente ejecutó todo el trabajo, pero con tres modos
separados explícitamente en el prompt maestro, porque mezclarlos degrada los tres:

| Modo | Qué hizo |
|---|---|
| **Arquitectura** | Modelo de dominio, límites de capas, topología de RabbitMQ, mecanismo de corte para el estado de leído. Produjo los ADRs *antes* de escribir el código, no después para justificarlo. |
| **Implementación** | Código en rebanadas verticales: infraestructura → webhook/cola/persistencia → API → SignalR → envío → UI → documentación. |
| **Revisión** | Pasada de auto-revisión sobre código ya escrito, buscando condiciones de carrera, orden de fallos y fugas de secretos. Produjo CR-003 a CR-009. |

Elegí no repartir esto en subagentes: el problema es chico y acoplado (el estado de leído toca
dominio, SQL, API y cliente a la vez), y un agente con todo el contexto encuentra inconsistencias
entre capas que agentes aislados se pierden.

## 3. Prompts representativos

**a) Prompt maestro** (completo en `PROMPT_FOR_CLAUDE.md`, ~200 líneas). Lo que más rindió fueron
las restricciones negativas y los criterios de honestidad:

> "Entregá la solución **más simple, completa, mantenible y demostrablemente funcional**. (…) No
> introduzcas un worker desplegable aparte, microservicios, CQRS/MediatR, event sourcing, Redis,
> Kubernetes ni autenticación. (…) Prohibidos nombres como `c`, `x`, `tmp`, `data1`, `res`, `msg`.
> (…) Verificá con bot real SOLO si se proveen credenciales; en caso contrario marcá el E2E de
> Telegram claramente como NO VERIFICADO. No inventes resultados aprobados."

Acompañado de un `CLAUDE.md` en el repositorio con las reglas permanentes (dirección de
dependencias, convenciones de nombres, qué va en `docs/`), que el asistente relee en cada sesión.

**b) Prompt de diseño sobre el punto más difícil del enunciado** — la carrera del estado de leído:

> "El requisito 4 dice marcar como leídos los mensajes al abrir la conversación, sin marcar por
> error los que llegan *durante* esa operación. Los timestamps de Telegram tienen resolución de un
> segundo y pueden empatar. Proponé un mecanismo determinista y explicá qué garantiza."

De ahí salió la columna identidad `SequenceNumber` y el corte `readUpToSequenceNumber` que viaja
desde el cliente, en vez del `UPDATE ... WHERE is_read = false` ingenuo.

**c) Prompt de revisión, ya con el código escrito:**

> "Revisá el código buscando condiciones de carrera, orden de operaciones ante fallos y fugas de
> secretos. Para cada hallazgo: dónde, por qué importa, y el escenario concreto que lo dispara. No
> listes hallazgos que no puedas disparar con un escenario."

La última frase es la que importa: sin ella la revisión devuelve observaciones de estilo. Con ella
aparecieron CR-003 (el reintento de migraciones no atrapaba las excepciones envueltas por EF Core) y
CR-005 (un mensaje empujado por SignalR durante la carga del historial quedaba pisado).

## 4. Cómo se revisó y validó lo generado

Lo generado por IA **no se aceptó por inspección visual**. El filtro fue, en orden:

1. **Compilar siempre.** `TreatWarningsAsErrors` activo en todo el backend y `strict` +
   `strictTemplates` en TypeScript. Así se cayó CR-001: `dotnet build` falló con `NU1903` por una
   vulnerabilidad conocida en `Microsoft.OpenApi` 2.0.0, que entraba de forma transitiva. Se eliminó
   el paquete en vez de convivir con la advertencia.
2. **Verificar las APIs de terceros contra el compilador, no contra la memoria del modelo.** Las
   superficies de RabbitMQ.Client 7, Telegram.Bot 22 y EF Core 10 cambiaron respecto de versiones
   anteriores. Se compiló la capa de Infrastructure de forma aislada y temprano, precisamente para
   que cualquier método inventado apareciera como error de compilación. Aparecieron tres y se
   corrigieron.
3. **Verificar las versiones contra el registro real.** Las versiones de paquetes se consultaron a
   `api.nuget.org` y a `npm view` antes de fijarlas, no se asumieron. Así se detectó que
   `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 exige EF Core ≥ 10.0.4, y que el CLI de Angular
   requiere Node ≥ 24.15 (CR-012).
4. **Tests unitarios sobre el dominio**, escritos para las ramas que un modelo tiende a simplificar:
   contacto sin nombre ni username, nombre con sólo espacios, `MarkAsRead` repetido, marcado sobre
   un mensaje saliente. 25 tests, 0 fallos.
5. **Mirar los artefactos generados.** La migración de EF se leyó línea por línea para confirmar la
   columna identidad, `timestamp with time zone` y el índice único de deduplicación. Los SVG de C4
   se renderizaron a PNG y se inspeccionaron visualmente: la primera versión del nivel 2 tenía
   etiquetas superpuestas y se rehízo con rutas ortogonales.
6. **Levantar el stack y ejercitarlo, no sólo compilarlo.** Es lo que más defectos encontró. El
   primer `docker compose up` falló con `container for service "postgres" is unhealthy`: la imagen
   de PostgreSQL 18 cambió el punto de montaje del directorio de datos (CR-013). Ningún análisis
   estático lo habría detectado. Con el stack arriba se le inyectaron al webhook las mismas cargas
   JSON que manda Telegram, lo que permitió verificar de verdad la deduplicación, el filtrado de
   updates fuera de alcance y —lo más importante— el corte del marcado como leído.
7. **Paleta tomada del sitio real.** En vez de inventar "colores tipo EncodeLabs", se descargó el CSS
   de encodelabs.com.ar y se extrajeron los tokens que usa (rampa teal en `oklch`, grises neutros,
   Red Hat Display). La bandeja está inspirada en esa identidad, no es una copia del sitio.

## 5. Lo que queda pendiente de validación humana

Marcado como tal, sin adornos:

- ☑ `docker compose up --build`: ejecutado. Los cinco servicios levantan y el pipeline entrante se
  verificó de punta a punta dentro del stack.
- ☐ **Prueba con bot y túnel reales:** falta el tramo que necesita credenciales (apertura del
  túnel, `setWebhook` y envío saliente).
- ☐ **Inspección visual de la interfaz** en un navegador.
- ☐ **Revisión humana del código** por el autor antes de publicar el repositorio.

El estado exacto de cada requisito está en `validation-checklist.md`, que distingue entre lo
ejecutado y lo pendiente. Ningún ítem no ejecutado se reporta como aprobado.
