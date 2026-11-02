# Prueba técnica **encodelabs**: Bandeja conversacional

---

## 1. Contexto

Una empresa atiende consultas de sus clientes por Telegram. Hoy los operadores responden desde la app de Telegram, sin una vista unificada de las conversaciones ni de los mensajes pendientes.

## 2. Objetivo

Vas a construir una bandeja web donde un operador recibe, visualiza y responde en tiempo real los mensajes de texto que los usuarios envían a un bot de Telegram.

---

## 3. Alcance

### 3.1 Requisitos funcionales

La bandeja debe permitir:

1. Recibir en tiempo real los mensajes de texto enviados al bot.
2. Ver el listado de contactos, cada uno con:
   - Nombre o username de Telegram.
   - Cantidad de mensajes no leídos.
   - Vista previa del último mensaje.
3. Abrir una conversación y ver su historial.
4. Marcar los mensajes como leídos al abrir la conversación.
5. Responder al contacto desde la bandeja. La respuesta debe llegar al usuario en Telegram.

### 3.2 Requisitos técnicos

| Área | Requisito |
|---|---|
| Backend | .NET, última versión estable, con Clean Architecture. |
| Frontend | Angular, última versión estable. |
| Base de datos | A elección (SQL o NoSQL). |
| Mensajería | RabbitMQ para el procesamiento de los mensajes entrantes. |
| Tiempo real | Comunicación en tiempo real entre backend y frontend. Tecnología a elección. |
| Telegram | Bot propio (creado con BotFather) y webhook registrado automáticamente en Telegram al levantar la solución, usando la URL pública de ngrok. |
| Exposición pública | ngrok para exponer el webhook. |
| Ejecución | Docker Compose. La solución debe levantarse con `docker compose up`, después de completar el `.env`. |

### 3.3 Fuera de alcance

No debés implementar:

- Autenticación ni usuarios.
- Mensajes que no sean de texto (imágenes, audio, archivos, stickers).
- Diseño responsive.
- Paginación del historial.

---

## 4. Diseño de la interfaz

**Layout:**

- **Panel lateral:** listado de contactos.
- **Panel central:** conversación seleccionada, con el campo para responder.

**Estilo:** debe ser similar en colores y estilo general a [encodelabs.com.ar](https://encodelabs.com.ar/es/). No tiene que ser idéntico y no se esperan animaciones ni efectos. Se busca una interfaz sencilla, prolija y agradable.

---

## 5. Entregables

### 5.1 Repositorio y README

Un repositorio Git público (GitHub o GitLab) que incluya:
 
- `docker-compose.yml` con todos los servicios necesarios para levantar la solución.
- `.env.example` con todas las variables requeridas, sin valores reales.
- `README.md` con el contenido detallado abajo.

El `README.md` debe incluir:
 
1. Requisitos previos.
2. Cómo completar el `.env` (token del bot, token de ngrok y lo que necesites).
3. Cómo levantar la solución.
4. Cómo probarla de punta a punta.
5. **Supuestos:** decisiones de alcance que tomaste ante lo que no está definido en este documento.

### 5.2 Diagramas C4

- **Nivel 1:** contexto del sistema.
- **Nivel 2:** contenedores.
- **Formato:** SVG o XML (draw.io).
- Alto nivel, sin detalle de componentes.

### 5.3 ADRs

Debés entregar estos 3 ADRs:

| # | Decisión |
|---|---|
| ADR-001 | Base de datos elegida. |
| ADR-002 | Tecnología de comunicación en tiempo real. |
| ADR-003 | Flujo de recepción del webhook y topología de colas en RabbitMQ. |

Usá esta plantilla, con un máximo de una página por ADR:

```markdown
# ADR-00X: Título

**Estado:** Aceptado

## Contexto
Qué problema o necesidad motiva la decisión.

## Alternativas consideradas
Opciones evaluadas, con ventajas y desventajas.

## Decisión
Qué elegiste y por qué.

## Consecuencias
Qué ganás, qué resignás y qué riesgos quedan.
```

Podés sumar otros ADRs si lo considerás necesario.

### 5.4 Trabajo con IA

Un documento de máximo una página que explique:

1. Qué herramientas de IA usaste.
2. Si usaste agentes, con qué rol cada uno.
3. Dos o tres prompts representativos.
4. Cómo revisaste, corregiste y validaste el código generado.

### 5.5 Preguntas bonus (opcional)

Respondé brevemente, en un máximo de un párrafo cada una:

1. ¿Para qué usarías un sistema de caché en una bandeja conversacional?
2. ¿Cómo gestionarías el tráfico de archivos multimedia?

---

## 6. Condiciones

- **Credenciales:** el `.env` no debe subirse al repositorio. 
- **Tests:** son opcionales. Si los incluís, que sean unitarios sobre el dominio.
- **Dudas:** si algo no está definido, tomá una decisión y registrala en la sección de supuestos del README.

---

## 7. Evaluación

| Criterio | Qué se evalúa | Peso |
|---|---|---|
| Visión de negocio | Comprensión del problema, priorización y supuestos. | 15 % |
| Arquitectura | Coherencia entre los diagramas C4 y el código, separación de responsabilidades y desacople. | 20 % |
| Toma de decisiones | Calidad de los ADRs: alternativas, trade-offs y consecuencias. | 20 % |
| Trabajo con IA | Uso de prompts y agentes, iteración y criterio al validar. | 15 % |
| Calidad del código | Clean Architecture, principios SOLID, patrones de diseño, legibilidad y consistencia. | 20 % |
| Funcionamiento e interfaz | Flujo completo operativo y estilo visual cuidado. | 10 % |

**Puntos extra:** decisiones de diseño que vayan más allá de lo pedido y estén bien justificadas, tests unitarios y respuestas a las preguntas bonus.

---