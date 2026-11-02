# How to use this pack

1. Put the challenge file `chat_inbox_tech_test.md` in the repository root next to `CLAUDE.md` and `PROMPT_FOR_CLAUDE.md`.
2. Copy `docs-code-review.md` to `docs/code-review.md` at the start of implementation.
3. Open Claude Code from the monorepo root and give it the content of `PROMPT_FOR_CLAUDE.md` (or ask it to read that file and implement it).
4. Provide a Telegram bot token and ngrok authtoken **only through your local `.env`**. Never paste real tokens into a shared prompt or commit them.
5. After implementation, inspect `docs/code-review.md`, `docs/validation-checklist.md`, and `docs/ai-usage.md`, then test with Telegram yourself before publishing.

Note: this pack is planning/instruction material, **not a completed application**. Its review document is explicitly a template until code exists.
