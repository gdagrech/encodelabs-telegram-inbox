# CLAUDE.md — EncodeLabs conversational inbox

## Mission
Implement the attached EncodeLabs Telegram inbox challenge with the smallest honest, reliable solution that meets all mandatory requirements. The source challenge is the authority. Optimize for interview explainability, clarity, and demonstrable end-to-end behavior.

## Read first
1. `chat_inbox_tech_test.md` (the actual challenge; if absent, ask for it rather than invent requirements).
2. `PROMPT_FOR_CLAUDE.md`.
3. Current `README.md`, `docs/validation-checklist.md`, and existing source before changing behavior.

## Working rules
- One monorepo; use one deployable ASP.NET Core API host with background RabbitMQ consumer. Angular is a separate frontend service, not another repository.
- Clean Architecture dependency direction: Domain <- Application <- Infrastructure/API. Domain is framework-free; Application expresses use cases and boundaries; Infrastructure implements technical details; API is composition/web transport.
- Use latest **GA stable**, not RC, .NET and Angular versions appropriate at implementation time; pin coherent supported packages.
- Avoid overengineering: no MediatR unless truly necessary, no extra microservices, Kafka, Redis, event sourcing, user auth, pagination, multimedia, excessive generic abstractions, or elaborate frontend state management.
- Descriptive English names for **every identifier**, server and client. No meaningless one-letter variable, ambiguous shortening, `foo`, `bar`, `tmp`, `res`, `msg`, `obj`, or magic constants. Prefer `incomingTelegramMessage`, `selectedConversationId`, `unreadMessageCount`, `rabbitMqChannel`, etc.
- Idiomatic consistent C#/TypeScript, small cohesive functions, nullable-aware C#, typed Angular models, meaningful exception handling. Never swallow errors or log tokens.
- No code generation artifacts masquerading as final source. No placeholders for required behavior. Changes should be focused, understandable and easy to defend during a technical interview.

## Required architecture / behavior
- ASP.NET Core HTTP webhook with Telegram secret-token header validation.
- RabbitMQ durable incoming queue, explicit publisher confirms and consumer manual ACK; deduplicate Telegram deliveries; bounded retries and poison-message handling.
- PostgreSQL + EF Core to persist contacts/messages. Correct unread/read consistency.
- SignalR push after persistence and frontend reconnect/refetch.
- Telegram Bot API for replies; don't show success when send fails.
- ngrok tunnel plus automatic webhook registration from ngrok public HTTPS URL. Automatic setup with retry on `docker compose up` after `.env` is filled.
- Desktop Angular two-column inbox matching EncodeLabs styling in spirit.

## Repository and documentation layout
- `src/backend/` => .NET solution/project layering.
- `src/frontend/` => Angular app.
- `docs/architecture/` => C4 level 1/2 **SVG** diagrams.
- `docs/adr/` => exactly the 3 mandatory ADRs, each <= one page.
- `docs/ai-usage.md` => <= one page of truthful AI-use disclosure.
- `docs/code-review.md` => active review log.
- `docs/validation-checklist.md` => traced acceptance criteria, evidence, bonus answers.
- Root `README.md` is REQUIRED by challenge; root `docker-compose.yml`, `.env.example`, `.gitignore`, `CLAUDE.md` are operational files and exceptions to docs-only rule.

## How to work
1. Understand requirements and inspect current code. Write/update short checkpoints in `docs/validation-checklist.md`.
2. Implement one working vertical slice at a time. Never build a feature that isn't required unless it materially helps correctness/security.
3. After meaningful changes run the relevant commands: formatter/build, tests if implemented, Compose config, Compose boot, endpoint checks, and frontend build. Don't label anything passing without actually running it.
4. Document issues and fixes in `docs/code-review.md` with concrete file locations; fix high-severity items before cosmetic work.
5. Track assumptions in README and ensure diagram/code/ADR consistency.
6. Before finishing, inspect `git status`, confirm `.env` is ignored, check no hardcoded credentials, and provide honest verified/unverified status.

## Review gate (repeat until clear)
- Functionality: every requirement maps to a verified behavior or documented blocker.
- Delivery: `docker compose up` does the full startup without manual webhook registration.
- Reliability: idempotency, publish confirm, consumer ACK, DB transaction, retry/DLQ, reconnection and error UI.
- Architecture: no inward layer depends on outer implementation; diagrams match actual processes.
- Security: Telegram secret header checked; `.env` never committed; no secret values in output/logs.
- Quality: meaningful naming, readable code, no unnecessary packages or patterns, tests only when valuable.
- Honesty: only claim a run, review or human validation if it actually happened.

## Final communication
Summarize what exists, how to run/test it, actual validation evidence, unresolved risks and explicit reasons for any unverified integration. Never replace working code with a long generic explanation.
