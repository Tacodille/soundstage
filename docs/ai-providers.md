# AI providers

Soundstage's AI layer is optional. You bring your own key, and with no key nothing else in the plugin changes. This page
records what each provider's own documentation said when the code was written (**21/11/2026**), so the next person can
tell a code bug from a provider change. Every figure carries its date. Re-read the linked page before restating one.

Code: `src/Soundstage/Ai/`. Tab: `Tabs/AiTab.cs`. Offline checks: `tests/Soundstage.SelfCheck/Checks/AiChecks.cs`.

## What the code does

| Piece | File | Notes |
|---|---|---|
| Provider interface | `IAiProvider.cs` | `AnalyseImage(image, mime, schema, instruction)` and `TestKey()`. Result is a parsed JSON object or an `AiFailure`. |
| Error classes | `AiError.cs`, `AiMessages.cs` | One enum, one plain-language message each. The self-check asserts every class has its own message. |
| Gemini | `GeminiProvider.cs` | Image call to the Interactions endpoint; Test key lists models. |
| OpenRouter and any OpenAI-compatible endpoint | `OpenAiCompatProvider.cs` | OpenRouter is the preset; "custom" is a typed web address. |
| Key store | `KeyStore.cs` | Windows DPAPI, current user, with an app-specific extra secret. Config holds only the encrypted text. |
| Log wrapper | `AiLog.cs` | Every AI log line and exception text is scrubbed. The HTTP client has no logging handler. |
| Consent | `Consent.cs` | Versioned, stored per provider (per host for a custom endpoint). |
| Door for the rest of the plugin | `AiService.cs` (`Studio.Ai`) | Refuses to send without a key and a recorded consent. |

The key goes in a request header, never a URL. Scene, share-code, pose-tag and Preset types have no member that can hold
a key (a self-check scans for it). A key is only sent over https, or over http to this machine.

## Google Gemini

Key from Google AI Studio: <https://aistudio.google.com/apikey> (the Get a key button).

| What | Source read 21/11/2026 | What it said |
|---|---|---|
| Image input | <https://aistudio.google.com/docs/image-understanding> (the old `ai.google.dev/gemini-api/docs/image-understanding` redirects here) | `POST https://generativelanguage.googleapis.com/v1beta/interactions`, header `x-goog-api-key`, header `Api-Revision: 2026-05-20`, body `{"model", "input":[{"type":"text","text"},{"type":"image","data":<base64>,"mime_type"}]}`. Inline data counts toward a 20 MB request limit. |
| Structured output | <https://aistudio.google.com/docs/structured-output> | `response_format: {"type":"text","mime_type":"application/json","schema":{...}}`. Schema types are lowercase JSON Schema (`string`, `number`, `integer`, `boolean`, `object`, `array`, `null`). |
| Reply shape | <https://aistudio.google.com/api-reference/interactions> | `steps[]` holds `{"type":"model_output","content":[{"type":"text","text":...}]}`. That page called Interactions "the recommended standard API for all new projects" and said nothing about `generateContent`. |
| Re-read for part 13 (21/11/2026) | <https://aistudio.google.com/docs/image-understanding> and <https://aistudio.google.com/api-reference/generate-content> | The image-understanding page's REST example (inline base64 image plus text) is `POST https://generativelanguage.googleapis.com/v1beta/interactions` with `x-goog-api-key`, `Content-Type: application/json`, `Api-Revision: 2026-05-20` and body `{"model", "input":[{"type":"text",...},{"type":"image","data","mime_type"}]}`, which is exactly what `GeminiProvider` sends; the page does not mention `generateContent`. The generate-content reference still lists `POST /v1beta/models/{model}:generateContent` (parts with `inlineData`, `responseMimeType`, `responseJsonSchema`) with no deprecation note in what was read, so both endpoints exist. The client was left on Interactions. |
| Model list | <https://aistudio.google.com/api-reference/models/list-models> | `GET /v1beta/models?pageSize=...&pageToken=...`, reply `models[]` (each with `name` as `models/{id}`) and `nextPageToken`. The reference shows `?key=` in its example; we send the header instead. The page also noted the methods field might be named `supportedGenerationMethods` or `supported_actions`, so the code does not filter on it. |
| Errors | <https://aistudio.google.com/docs/api-errors>, <https://aistudio.google.com/docs/troubleshooting> | Statuses 400, 403, 404, 429, 500, 503, 504. The error body is `{"error":{"code","message"}}`; the older API used a numeric code plus a status string. The mapper keys on the HTTP status and the message text, so either shape works. 403 `PERMISSION_DENIED` covers a denied project, a leaked key or a verification requirement. |
| Data use on the free tier | <https://aistudio.google.com/docs/terms> | Paraphrase: on the unpaid services Google may use submitted prompts and replies to improve its products and human reviewers may read them (disconnected from account, key and project first). The terms say not to submit sensitive, confidential or personal information. In the EEA, UK and Switzerland the paid-service data terms apply to all services, including free AI Studio use. This is the basis of the consent wording. |
| Free-tier limits | <https://aistudio.google.com/docs/rate-limits> | Limits apply per project, not per key; daily quotas reset at midnight Pacific time; actual figures are shown at <https://aistudio.google.com/rate-limit>. The page's table named models that did not match this plugin's defaults, so **no Gemini figure is quoted here or in the UI**. The tab shows only the count Soundstage itself has sent today. |

Model choice: the key's model list comes from the API. `GeminiProvider.PickDefaultModel` picks a Gemini "flash" model
that is not lite, image, audio, live or embedding, prefers a stable name over a preview, then the highest version. If the
list is empty or fails, the fallback name is `gemini-flash-latest` (**an alias from memory, not confirmed in the docs read
on 21/11/2026**; the Model box on the AI tab overrides it).

Things the code could not confirm without a key, and the first things to check if Test key or the test picture fails:

1. That the Interactions endpoint accepts the key in `x-goog-api-key` for the image call (the docs show it for `interactions`; the model list call is separate).
2. The `Api-Revision` value (a constant in `GeminiProvider`, change it there if Google moves it).
3. That the picked or fallback model accepts images.
4. Whether `response_format.schema` accepts every keyword in the schema the caller passes (only the lowercase subset in `SchemaValidator` is used by this plugin's own schemas).

The reply is validated against the schema by `SchemaValidator` whatever the provider enforces. An older `candidates[0].content.parts[].text` reply is also read, in case the classic endpoint answers.

## OpenRouter

Key: <https://openrouter.ai/keys> (the Get a key button).

| What | Source read 21/11/2026 | What it said |
|---|---|---|
| Base address, auth, endpoint | <https://openrouter.ai/docs/api-reference/overview> | `https://openrouter.ai/api/v1`; `Authorization: Bearer <key>`; `POST /api/v1/chat/completions`. |
| Images | same page | `content` is a list of parts: `{"type":"text","text"}` and `{"type":"image_url","image_url":{"url":<URL or base64 data>}}`. We send a `data:<mime>;base64,...` URL. |
| Structured output | same page | `response_format: {"type":"json_schema","json_schema":{"name","strict","schema"}}`. "Check the models page for which models support it", so the code retries once without it when the endpoint refuses. |
| Errors | <https://openrouter.ai/docs/api-reference/errors> | Body `{"error":{"code","message","metadata"}}`; 400 bad request, 401 invalid credentials, 402 insufficient credits, 403 forbidden (guardrail or moderation), 408 timeout, 429 rate limited, 502 model down, 503 no provider. 402 is mapped to the rate-limit message (allowance used up). |
| Free-model limits | <https://openrouter.ai/docs/api-reference/limits> | Models whose id ends `:free`: 20 requests a minute; 50 a day with under 10 credits ever bought, 1,000 a day with at least 10 (figures as read 21/11/2026; re-read before quoting). |
| Daily count | same page | `GET https://openrouter.ai/api/v1/key` returns `free_model_daily_requests` with `used`, `limit`, `remaining` (UTC day). Test key reads it and shows "N free requests left today". |
| Model list | <https://openrouter.ai/docs/guides/overview/models> | Each model lists `architecture.input_modalities` (for example `["text","image"]`); free ones end `:free`. Test key suggests the first `:free` model that accepts images. |
| Data use | not found in the pages read | The consent wording therefore says "check the model's page" and states no rule. |

## Any OpenAI-compatible endpoint

The same code path as OpenRouter, with a typed base address (ending in `/v1`) and model. The chat-completions shape
above is OpenAI's own; the OpenAI reference page (`platform.openai.com/docs/api-reference/chat/create`) returned 403 to
the fetch tool on 21/11/2026, so the shape is taken from OpenRouter's documentation of the same format. Test key calls
`GET {base}/models`; an endpoint that answers 404 or 405 is reported as connected but unchecked.

## Where a key can and cannot end up

- Stored: `pluginConfigs\Soundstage.json`, section `ai`, `ProtectedKey` only, DPAPI-encrypted for the current Windows user. Another Windows user or another PC reads it as "no key" and the user pastes it again.
- Sent: one request header (`x-goog-api-key` or `Authorization`), over https.
- Never: a URL, a log line, an exception text, a Scene, a share code, a pose tag. The self-check feeds a sentinel key through 14 hostile replies on all three providers (image call and Test key), where the fake server echoes the key back in bodies, headers and exception messages, and asserts it appears in no log line, failure detail, message or saved setting. A control run with redaction switched off must find the key, or the check is not measuring anything.
- In game: the checklist's AI area step 10 counts the saved key in `dalamud.log` and expects 0.
