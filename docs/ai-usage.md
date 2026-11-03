# Trabajo con IA

## 1. Herramientas

**Claude Code** (Anthropic, modelo Opus 5) en la terminal, con acceso al sistema de archivos y a la
shell. Fue la única herramienta de IA usada: ni Copilot, ni ChatGPT, ni generadores de scaffolding
más allá del `ng new` oficial.

La decisión de fondo fue usar un asistente **con acceso a la shell** en lugar de uno de chat: lo que
importaba no era generar código, sino que cada afirmación quedara respaldada por un comando
ejecutado. Casi todos los hallazgos de `code-review.md` salieron de compilar y correr, no de leer.

## 2. Agentes y roles

**No se usaron múltiples agentes.** Un único agente ejecutó todo, con tres modos separados en el
prompt maestro: **arquitectura** (modelo de dominio, límites de capas, topología de RabbitMQ,
mecanismo de corte del estado de leído; produjo los ADRs *antes* del código, no después para
justificarlo), **implementación** (rebanadas verticales: infraestructura → webhook/cola/persistencia
→ API → SignalR → envío → UI → documentación) y **revisión**.

No repartí esto en subagentes: el problema es chico y acoplado —el estado de leído toca dominio,
SQL, API y cliente a la vez— y un agente con todo el contexto encuentra inconsistencias entre capas
que agentes aislados se pierden.

## 3. Prompts representativos

**a) Prompt maestro** (completo en `PROMPT_FOR_CLAUDE.md`). Lo que más rindió fueron las
restricciones negativas y los criterios de honestidad:

> "Entregá la solución **más simple, completa, mantenible y demostrablemente funcional**. (…) No
> introduzcas un worker desplegable aparte, microservicios, CQRS/MediatR, event sourcing, Redis,
> Kubernetes ni autenticación. (…) Prohibidos nombres como `c`, `x`, `tmp`, `res`, `msg`. (…)
> Verificá con bot real SOLO si se proveen credenciales; en caso contrario marcá el E2E de Telegram
> claramente como NO VERIFICADO. No inventes resultados aprobados."

**b) Sobre el punto más difícil del enunciado**, la carrera del estado de leído:

> "El requisito 4 dice marcar como leídos los mensajes al abrir la conversación, sin marcar por
> error los que llegan *durante* esa operación. Los timestamps de Telegram tienen resolución de un
> segundo y pueden empatar. Proponé un mecanismo determinista y explicá qué garantiza."

De ahí salió la columna identidad `SequenceNumber` y el corte `readUpToSequenceNumber`, en vez del
`UPDATE … WHERE is_read = false` ingenuo.

**c) De revisión**, ya con el código escrito: *"…para cada hallazgo: dónde, por qué importa, y el
escenario concreto que lo dispara. **No listes hallazgos que no puedas disparar con un escenario.**"*
Esa última frase es la que importa: sin ella la revisión devuelve observaciones de estilo.

## 4. Cómo se validó

Lo generado **no se aceptó por inspección visual**. El filtro, en orden de rendimiento:

1. **Levantar el stack y ejercitarlo**, no sólo compilarlo. Encontró CR-013 (PostgreSQL 18 cambió el
   punto de montaje y `docker compose up` no arrancaba) y, con bot real, CR-016 y CR-017.
2. **Compilar siempre**, con `TreatWarningsAsErrors` y `strict` + `strictTemplates`. Así cayó CR-001:
   una vulnerabilidad conocida (`NU1903`) que entraba de forma transitiva.
3. **Verificar las APIs de terceros contra el compilador, no contra la memoria del modelo.** Las
   superficies de RabbitMQ.Client 7, Telegram.Bot 22 y EF Core 10 cambiaron; compilar Infrastructure
   temprano y aislada hizo aparecer tres métodos inventados.
4. **Verificar versiones contra el registro real** (`api.nuget.org`, `npm view`), no asumirlas.
5. **Tests sobre las ramas que un modelo tiende a simplificar**: contacto sin nombre ni username,
   `MarkAsRead` repetido, marcado sobre un saliente, y el aislamiento entre contactos cuando dos
   remitentes traen el mismo `message_id`. **80 tests, 0 fallos**, corridos por `scripts/verify.sh`,
   que es la compuerta: ningún cambio se da por revisado si no termina en verde. Cada test de
   regresión se validó revirtiendo su corrección y mirándolo fallar.
6. **Mirar los artefactos**: la migración se leyó línea por línea; los SVG de C4 se renderizaron y
   el nivel 2 se rehízo porque tenía etiquetas superpuestas.
7. **Paleta tomada del sitio real**: se extrajeron los tokens del CSS de encodelabs.com.ar en vez de
   inventar "colores tipo EncodeLabs".

## 5. Estado de la validación humana

- ☑ Prueba con **bot y túnel reales**, con credenciales del autor, operando la interfaz en el
  navegador. Los 23 casos de la matriz quedaron cerrados.
- ☑ **Segunda revisión completa de `src/`** ya con el sistema funcionando: seis defectos más (CR-018
  a CR-023). Tres sólo aparecen con varios contactos o con una falla a mitad de camino, que una
  corrida feliz nunca expone. Verificar de punta a punta no reemplaza revisar.
- ☐ **Revisión humana del código** por el autor antes de publicar el repositorio.

Esa corrida destapó tres defectos en cadena. CR-010 (mezcla de idiomas en los errores) se había
aceptado como riesgo *sobre el papel*; ver el banner real con un bot bloqueado lo volvió
indefendible. Al corregirlo apareció CR-016: un rechazo de Telegram y una Bot API inalcanzable
daban el mismo código de estado, siendo accionables al revés. Y traducir el mensaje volvió legible a
CR-017, que llevaba todo el tiempo ahí: al reconectar, el cliente pedía una conversación que ya no
existía. En inglés se leía como ruido del servidor; en español, como un absurdo.

**Los defectos que importaron aparecieron ejecutando, no leyendo** — y uno sólo apareció después de
un cambio aparentemente cosmético. El estado exacto de cada requisito está en
`validation-checklist.md`; ningún ítem no ejecutado se reporta como aprobado.
