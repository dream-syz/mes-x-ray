# Security and governance

Design §10 applied to this code base. The POC is a **read-only analysis service**; it never writes to MES.

## Data hygiene

- Real hosts, passwords, JWTs and personal data never enter documents, source, fixtures, prompts or logs. The fixture under `fixtures/pick-order-details` is synthetic: placeholder pick order, materials, users and locations.
- Connection strings and API keys come from environment variables / a secret manager only. The AI key is read from the variable named by `XRay:AI:ApiKeyEnvironmentVariable` (default `MESXRAY_AI_API_KEY`) and is never logged or echoed. `.env*` files are git-ignored; `.env.example` lists variable *names* only.
- `Redactor` (`MesXray.Runtime`) masks sensitive JSON keys (user/login/operator, password/secret/token/jwt/authorization/apiKey, host/server/dataSource/connectionString, email/phone/ip …) and sensitive patterns in free text (JWTs, `Bearer …`, `Password=…`/`Server=…` key-value pairs, IPv4 addresses, internal host names, e-mail addresses). It is applied to runtime responses before they are stored, to tool-call arguments before they are audited, and to every string the evidence binder emits.

## Runtime access

- `RuntimeToolGateway` is the only path from the UI/AI to runtime data. Whitelist: `trace_pick_order`, `trace_material`, `read_system_parameter`, `read_pick_order_response`. Explicitly forbidden and rejected with a policy message: `execute_arbitrary_sql`, `execute_sql`, `update_system_parameter`, `update_pick_order`, `delete_pick_order`, `write_sql`. Unlisted tools are rejected too.
- Arguments are validated before anything runs: required arguments, maximum length (`Runtime:MaxArgumentLength`), a conservative character set (no `;`, quotes, path separators or `..`), and a SQL-fragment detector.
- Every call — allowed or denied — is recorded in the audit log with redacted arguments (`GET /api/xray/runtime/audit`).
- Runtime data must come from an allowed environment (`Runtime:AllowedEnvironments`, default `FIXTURE, TEST, UAT`); anything else is refused with 403. Row count (`Runtime:MaxRows`) and timeout (`Runtime:Timeout`) are enforced. Production adapters must use a read-only account.
- The first version implements no UPDATE/DELETE, no arbitrary SQL and no auto-fix; the AI cannot modify SQL, system parameters or business data — there is simply no tool for it.

## AI

- The AI provider must comply with the company data-classification policy. The default provider (`rules`) is fully offline. The OpenAI-compatible client sends only the evidence bundle (node/edge/lineage descriptions and redacted runtime values), never source files, connection strings or raw responses.
- Every conclusion is evidence-bound: `EvidenceBindingValidator` rejects facts whose evidence ids are not in the bundle (they become unverified hypotheses), forces the verdict to *Need More Evidence*/*Unknown* when gaps exist, and caps confidence.
- Each explanation carries an audit record: provider, model, prompt version, timestamp, cited evidence ids and a note (e.g. fallback reason).
- Hidden chains of thought are never shown; the response exposes auditable steps, facts, hypotheses with status and unknowns only.

## Web / API

- CORS is restricted to the configured UI origins. Errors are problem details without stack traces or internal paths beyond the fixture-relative source path.
- The UI never calls a database or an AI provider directly; all requests go through the API.

## Checklist before committing a fixture

1. No hostnames, IPs, connection strings, tokens or credentials in `source/**`, `runtime/**`, `expected-graph/**`.
2. Users, operators, e-mails and phone numbers replaced with placeholders.
3. `environment` in runtime fixtures is `FIXTURE` or `TEST`.
4. Run `dotnet test` — `RuntimeGatewayTests` and `AcceptanceCriteriaTests` assert that redaction and the deny paths still hold.
