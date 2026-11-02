# Master prompt — EncodeLabs conversational inbox challenge

Act as a pragmatic senior software architect and hands-on full-stack engineer. Implement the EncodeLabs take-home challenge specified in `chat_inbox_tech_test.md` (attached or placed at repository root). That file is the **source of truth**. Read it in full before proceeding. Follow `CLAUDE.md` throughout the job. Do not merely propose an implementation: create the working source code, configuration, and documentation in this repository.

## Goal and evaluation priorities
Deliver the **simplest complete, maintainable, demonstrably working** Telegram conversational inbox. Prioritize every mandatory requirement, end-to-end reliability, readable code, sound architecture decisions, and strong documentation over sophistication. This is a take-home exercise, not an enterprise platform. Explicitly distinguish MUST-HAVE from OPTIONAL.

## Non-negotiable constraints
- **One monorepo**. One ASP.NET Core host for the API, Telegram webhook, SignalR hub, and RabbitMQ `BackgroundService` consumer; separate .NET class-library projects are allowed and encouraged for Clean Architecture. Do not introduce a separate deployable worker, microservices, CQRS/MediatR frameworks, event sourcing, Redis, Kubernetes, complicated abstractions, or auth.
- Backend: latest **GA stable** .NET at implementation time (as of 2026-11-02: .NET 10); Angular: latest GA stable at implementation time (Angular 22). Verify actual installed/package version availability before pinning; **never use preview/RC releases**. Keep compatible EF Core and supporting packages.
- PostgreSQL with EF Core migrations; RabbitMQ for incoming Telegram message processing; SignalR for backend-to-frontend realtime updates; ngrok for externally accessible Telegram webhook; Docker Compose for all services. Angular should be served from a Docker service and reachable via a documented localhost URL.
- A single command `docker compose up` after populating `.env` must bring everything up, including ngrok and the **automatic webhook registration** with Telegram using the ngrok public HTTPS URL. No manual webhooks setup and no host-side `npm`/`dotnet` installation required for the documented Docker workflow.
- No authentication, no rich media, no responsive requirement, no history pagination. No artificial complexity.
- All frontend and backend variable names, parameters, properties, fields, methods, classes and observables must be descriptive. Ban names such as `c`, `x`, `tmp`, `data1`, `res`, `msg`, `obj`, `arr`, ambiguous abbreviations, and unnecessary single-letter lambdas. Standard loop indices may be accepted only if scope is tiny and clear; prefer expressive names everywhere. Use consistent terminology: Contact, Conversation, Message, TelegramUserId, TelegramChatId, UnreadCount.
- Keep components/functions small and single-purpose. Prefer straightforward idiomatic C# and Angular over generic patterns. Avoid comments that restate code; comment WHY when necessary. No dead code, magic strings, placeholder implementations, mocked production flows, or fake screenshots.
- Use actual Telegram integration. No hardcoded sample conversations as proof of functioning.

## Scope and acceptance criteria (source challenge takes precedence)
1. Receive Telegram text messages to the bot in near realtime via webhook.
2. Show contacts with name or username, unread message count, and latest message preview, ordered by last activity.
3. Open a contact to see persistent message history in chronological order, differentiating incoming and outgoing.
4. Mark existing inbound messages as read when opening a conversation. Update unread counts consistently, including on the server; handle messages arriving during the read operation without accidentally marking them read.
5. Send text replies from Angular through the backend using Telegram Bot API; only claim a message is sent after Telegram acknowledges it, or clearly show pending/error states. Store successful outbound messages; display errors without inventing success.
6. Publish persisted new incoming messages / contact summaries and read-state changes through SignalR; the client must update without refreshing, including sensible reconnection and initial fetch behavior.
7. Desktop-oriented two-panel interface. Use EncodeLabs visual identity **as inspiration**, not a copied website; inspect the provided URL when accessible, otherwise use a restrained professional palette and document the limitation. Include loading, empty, and basic error states.
8. All services start with Compose and a populated `.env`: Angular, API/worker, PostgreSQL, RabbitMQ, ngrok. Make startup robust to dependency readiness and transient failure.

## Proposed simple architecture
`src/backend/EncodeLabs.Domain`, `EncodeLabs.Application`, `EncodeLabs.Infrastructure`, `EncodeLabs.Api`, `src/frontend/`, `docs/`, `tests/` (only if implementing domain unit tests). Domain should not depend on infrastructure, Telegram SDK, SignalR, EF Core, RabbitMQ or ASP.NET. Application defines use cases and ports; Infrastructure implements persistence and external integrations; API defines HTTP endpoints/webhook, SignalR Hub, DI composition and hosted processing. Keep dependencies flowing inward. Use lightweight handlers/services rather than frameworks solely for layering.

Minimal data model: Contact (TelegramUserId, TelegramChatId, display name/username, last activity), Message (unique TelegramMessageId scoped to chat for inbound; incoming/outgoing direction, text, timestamp, read-state for incoming). Add explicit indexes and constraints, particularly uniqueness of Telegram update ID or `(chatId,messageId)` to protect against Telegram retries. Document which identifiers correspond to user/chat/message/update. Preserve exact Telegram message and update identity where needed.

Inbound: Telegram webhook endpoint validates a secret header (`X-Telegram-Bot-Api-Secret-Token`) with configured secret; validate payload; accept text messages only; publish accepted update to a **durable** RabbitMQ queue; return 2xx only after RabbitMQ publisher confirmation, otherwise return non-2xx so Telegram can retry. Avoid holding webhook open while DB processing. Consumer processes idempotently, persists message and contact, publishes SignalR notification after commit, ACKs broker delivery only on success. On processing failures use bounded retry/dead-letter strategy that avoids poison-message infinite loops; document queue/exchange/DLQ names. Assume at-least-once delivery, not exactly-once. If transient broker unavailability occurs, never return a success status for an unqueued message. If consumer fails after commit but before ACK, deduplication must prevent duplicate message rows. Explain the recovery gap between DB commit and SignalR notification and why refresh/reconnect reconciles state. Keep any retry topology minimal and correct.

Startup: ngrok container exposes the **backend API port**, not the Angular frontend. API startup automation waits for ngrok local agent API tunnel inspection endpoint (typically port 4040 inside the Compose network), discovers the actual HTTPS public URL, then registers `https://.../<webhook-path>` using Telegram `setWebhook` with secret token, with bounded retries and non-secret logs. Webhook registration MUST run without requiring user intervention. Avoid registering a webhook before the API is ready to serve it. Handle changing tunnel URLs on each restart. If ngrok tunnel setup or Telegram registration fails, surface clear actionable logs and retry instead of silently pretending success. Never log the bot token, ngrok token or webhook secret. Carefully handle any local ngrok free-tier constraints. Document how to confirm via Telegram `getWebhookInfo` without displaying credentials publicly.

Frontend: Angular standalone components, typed models, a small API client and SignalR connection service, reactive signals or RxJS where appropriate. Avoid global state libraries unless justified. Do not duplicate UI state sources unnecessarily. Protect against listener leaks, duplicate incoming event handling, race conditions when switching selected contacts, and repeated submissions while sending. Desktop only. Use accessible labels and readable typography.

## Documentation and deliverables
Put **all supplemental documentation** inside `docs/` (except mandatory root-level `README.md`, `CLAUDE.md`, `docker-compose.yml`, `.env.example`, and operational files). Generate:
- `README.md`: prerequisites (Docker, Telegram bot created with BotFather, ngrok account/token), `.env` instructions, ONE command to run, app URL, step-by-step Telegram end-to-end test, expected UI behavior, troubleshooting, assumptions, and links to docs. Do not claim tests you have not run.
- `docs/architecture/c4-context.svg` and `docs/architecture/c4-containers.svg`: valid **actual SVG diagram files** (not just Mermaid). Keep C4 context/container levels correct and aligned with deployment/code. Clearly label the human operator, Telegram user, Telegram platform, frontend, backend, PostgreSQL, RabbitMQ and ngrok as appropriate; avoid treating an internal class as a container.
- `docs/adr/ADR-001-database.md`, `ADR-002-realtime.md`, `ADR-003-telegram-rabbitmq.md`: exact challenge ADR template (State Accepted; Context; Alternatives; Decision; Consequences), **maximum one page each** (keep concise).
- `docs/ai-usage.md`: **maximum one page** explaining tools and actual agent roles used, 2–3 **real** representative prompts (including this master prompt if applicable), and real manual/code validation performed. Do not fabricate human reviews, tests or agents. Leave any user-only claims as explicitly marked checkboxes/to-be-confirmed.
- `docs/code-review.md`: maintained review log with concrete findings, severity, file references, status, fixes, commands actually executed and remaining risks. Never pretend it was human-reviewed if it was not.
- `docs/validation-checklist.md`: requirement-by-requirement PASS/FAIL/NOT VERIFIED with observable evidence; include bonus questions answered briefly (one paragraph each) and a runnable test matrix.
- `.gitignore` excludes `.env`, secrets, build artifacts and dependencies; `.env.example` includes names/placeholders but not real values or disguised valid secrets.

## Execution process and quality gates
1. Read source challenge, `CLAUDE.md`, and inspect repository. Make a compact requirement traceability table (requirement -> implementation -> evidence). Identify ambiguities and make pragmatic assumptions, recording them in README. Do not spend time eliciting answers to minor uncertainties.
2. Create short task plan and proposed structure; implement in vertical slices: infrastructure startup -> inbound webhook/queue/persistence -> API/read-state -> SignalR -> send reply -> Angular UI -> diagrams/ADRs/README. Prioritize a working full pipeline over polishing isolated components.
3. Write `.env.example` and Compose early. Ensure `docker compose up` is sufficient once the `.env` is populated; add health checks/retries/migrations and ngrok auto-registration as required. Do not bake real credentials into images or source.
4. Run actual build/lint/tests/Compose smoke checks if tools available. Include exact command and output summary. Verify via real Telegram bot ONLY if credentials are supplied; otherwise clearly mark Telegram end-to-end as NOT VERIFIED and provide instructions. Do not invent passing results.
5. Perform focused self-code review emphasizing requirements, layering, retry/idempotency, broker ACK/publisher confirms, webhook security, lifecycle of SignalR connections, UI synchronization, env leakage, Docker-only setup, descriptive names and recovery cases. Record findings and FIX them, then repeat verification.
6. Final response: summary of delivered files, startup instructions, what was truly verified, any blockers and remaining assumptions. No unexplained unfinished TODOs. Prefer simpler compliant implementation if time-constrained.

## Explicit edge cases to verify
- Repeated Telegram webhook update -> one stored message.
- Telegram contact without `username` / missing names -> sensible display fallback.
- Multiple text messages arrive rapidly / arrival while conversation is open -> correct unread counts and realtime UI.
- Conversation opened while new messages arrive -> no stale unread counter.
- Database or RabbitMQ temporary restart -> recover without silent data loss; errors visible.
- Telegram send failure -> no false successful outgoing message.
- Angular refresh/reconnect -> correct history, list and read state from API.
- Unsupported Telegram updates (media, edits, bot/service messages) -> ignored safely and acknowledged as out of scope, no infinite retries.
- Empty contact list/conversation -> understandable UI.

**Keep moving from planning to implementation. Treat operational success and evidence as more valuable than generated code volume.**
