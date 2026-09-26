# TestJob

REST API that processes a base64-encoded HTML page: extracts selected element attributes
with [AngleSharp](https://anglesharp.github.io/), saves them to PostgreSQL with
[Dapper](https://github.com/DapperLib/Dapper), counts emails found in the page, and decrypts a
text encrypted with AES-256 (ECB mode).

Built with **.NET 10**, **ASP.NET Core**, **PostgreSQL 18**, **pgAdmin**, and **docker compose**.

## Stack

| Component  | Version / Image                    |
|------------|------------------------------------|
| Runtime    | .NET 10 (SDK image `10.0`)         |
| Web API    | ASP.NET Core + Swashbuckle         |
| Parsing    | AngleSharp                         |
| Data access| Dapper + Npgsql                    |
| Validation | FluentValidation                   |
| Database   | `postgres:18`                      |
| Admin UI   | `dpage/pgadmin4`                   |

## Run with docker compose

```bash
docker compose up -d --build
```

Services:

| Service | Address                                  | Credentials                 |
|---------|------------------------------------------|-----------------------------|
| API     | http://localhost:8090/api/swagger/index.html | —                       |
| PGAdmin | http://localhost:8080                    | no password required        |
| Postgres| `db:5432` inside the compose network     | `testjob` / `testjob`       |

PostgreSQL is deliberately **not** published to the host: the API and pgAdmin both reach it as
`db:5432` over the compose network, so the project does not occupy port 5432 on the host and cannot
clash with a locally installed PostgreSQL.

Swagger JSON is served at `http://localhost:8090/api/swagger/v1/swagger.json`.

On startup the API creates the `elements` table if it does not exist:

```sql
CREATE TABLE IF NOT EXISTS elements (
    id              BIGSERIAL PRIMARY KEY,
    attribute_value TEXT NOT NULL,
    element_html    TEXT NOT NULL
);
```

To inspect the data from the host, run psql inside the container:

```bash
docker compose exec db psql -U testjob -d testjob -c "SELECT * FROM elements ORDER BY id DESC LIMIT 20;"
```

## Run locally (no Docker)

Requires the .NET 10 SDK and a PostgreSQL instance. The connection string in
`src/TestJob.Api/appsettings.json` points at `localhost:5433` (user `postgres`, password `testjob`)
and is meant for a throwaway local instance:

```bash
# create the database once
psql -h localhost -p 5433 -U postgres -c "CREATE DATABASE testjob;"

# run the API
dotnet run --project src/TestJob.Api
```

Override the connection string with an environment variable if your setup differs:

```powershell
$env:ConnectionStrings__Default = "Host=localhost;Port=5432;Database=testjob;Username=testjob;Password=testjob"
```

Note that `appsettings.json` is only the fallback: under docker compose the connection string is
always supplied by the `ConnectionStrings__Default` environment variable in `compose.yml`, so the
local value never affects the containerised run.

## Endpoint

### `POST /api/elements`

Request body (`application/json`):

| Field                     | Type   | Description                                        |
|---------------------------|--------|----------------------------------------------------|
| `key_bytes_b64`           | string | AES-256 key, base64-encoded bytes (32 bytes)       |
| `encrypted_text_bytes_b64`| string | AES-256-ECB ciphertext, base64-encoded bytes       |
| `page_b64`                | string | HTML page, base64-encoded UTF-8                    |
| `url_b64`                 | string | Page URL, base64-encoded UTF-8                     |
| `selector`                | string | CSS selector to find elements in the page          |
| `attribute`               | string | Attribute whose value is extracted from each match |

Response body field order:

| Field                   | Type         | Description                                    |
|-------------------------|--------------|------------------------------------------------|
| `is_error`              | int          | `1` on error, `0` otherwise                    |
| `error_code`            | string       | machine-readable error code                    |
| `error_message`         | string       | human-readable error detail                    |
| `elements_count`        | int          | number of matched elements                     |
| `emails_count`          | int          | number of email matches in the page            |
| `url`                   | string       | decoded page URL                               |
| `decrypted_plain_text`  | string       | decrypted text from the ciphertext             |
| `elements_attr_list`    | string[]     | extracted attribute values                     |
| `emails_list`           | string[]     | found emails                                   |

### Error codes

| Code                      | When                                                |
|---------------------------|-----------------------------------------------------|
| `INVALID_JSON`            | body is not valid JSON                              |
| `VALIDATION_ERROR`        | required fields are missing/empty (`error_message` lists them) |
| `URL_BASE64_DECODE_ERROR` | `url_b64` cannot be decoded                         |
| `PAGE_BASE64_DECODE_ERROR`| `page_b64` cannot be decoded                        |
| `PARSE_ERROR`             | page parsing / selector evaluation failed           |
| `DB_ERROR`                | database write failed                               |
| `EMAIL_REGEX_ERROR`       | email extraction failed                             |
| `DECRYPTION_ERROR`        | AES decryption failed                               |

The endpoint always replies `200 OK`, including for errors — the outcome is reported in
`is_error`. A selector that simply matches nothing, or an attribute that is absent on the
matched elements, is **not** an error: `is_error` stays `0` and the affected lists come back
empty (or filled with empty strings for a missing attribute).

Example response:

```json
{
  "is_error": 0,
  "error_code": "",
  "error_message": "",
  "elements_count": 238,
  "emails_count": 5,
  "url": "https://test.com/page1",
  "decrypted_plain_text": "AES Error: Object reference not set to an instance of an object.",
  "elements_attr_list": ["https://adv.rbc.ru/", "..."],
  "emails_list": ["webmaster@rbc.ru", "letters@rbc.ru"]
}
```

## Implementation notes

- **AES-256-ECB** with `PaddingMode.None` and the provided 256-bit key — the algorithm is
  used exactly as specified by the task (ECB is not recommended for general use, but is
  required here).
- **Email regex**: `[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}` with
  `Compiled | IgnoreCase | CultureInvariant`.
- **Validation** with FluentValidation before any processing. The failure messages are
  returned in `error_message` so the caller can see which field was rejected.
- Each selected element is stored with its extracted attribute value and full outer HTML.
  All inserts for one request run inside a single transaction, so a mid-way failure cannot
  leave a half-written batch behind.
- Errors never change the HTTP status code: the endpoint always answers `200 OK` and reports
  the outcome in `is_error` / `error_code` / `error_message`, as the task specifies. Results
  that were already computed before a failure are still returned, so a single response is
  enough to diagnose what happened.
- The `elements` table is append-only: every request adds rows and nothing is deleted, so
  the row count grows with the number of requests.

## About `async`

The request path is asynchronous end to end, and the choice of *where* is deliberate.

**Why async helps a REST API.** A thread inside a .NET thread pool is a scarce, shared
resource. A synchronous call that waits for I/O — a database round trip, reading the request
body — parks that thread for the whole duration of the wait while doing no work. Under
concurrency, requests queue up behind each other and throughput collapses even though the CPU
is idle. `await` releases the thread instead: the method returns to the pool immediately and
resumes on a continuation when the result arrives, so a few hundred slow requests can be in
flight on a small thread pool. It also removes the risk of thread starvation and of
sync-over-async deadlocks, and it is what lets the server scale by adding replicas rather than
threads. On top of that, everything in this pipeline is I/O-shaped — an HTTP-ish page load, a
base64 body, a database write — so the win is direct rather than theoretical.

**Where it is used here, and why.**

| Operation | How | Reason |
|---|---|---|
| Reading the request body | `JsonSerializer.DeserializeAsync` | Genuine I/O: the body arrives over the socket in chunks. |
| HTML parsing | AngleSharp `OpenAsync` | Asynchronous loading API; awaited so nothing blocks while the document is being resolved. |
| Database connect + insert | `await connection.OpenAsync`, Dapper `ExecuteAsync` with a `CancellationToken` | The classic case: ~ms of network wait per request, and the write must be cancellable when the client disconnects. |
| `CancellationToken` plumbed through | `HttpContext.RequestAborted` | Abandons work for requests the client already gave up on instead of finishing them. |

**Where it is deliberately *not* used, and why that is the right call.** Wrapping CPU-bound
work in `Task.Run` does not make it parallel or faster — it just moves the same computation to
another thread pool thread and adds scheduling overhead. Worse, it burns one of the same
threads `await` exists to protect, so it actively reduces the capacity available to genuinely
asynchronous work. So the following are intentionally synchronous:

- `EmailRegex.Matches(page)` — pure CPU, microseconds-to-milliseconds, and the pattern is
  pre-compiled (`RegexOptions.Compiled`) so the match itself is cheap. An `await` here would
  buy nothing.
- `DecryptAes` — a 64-byte ECB decrypt. There is no asynchronous overload of
  `Aes.CreateDecryptor`/`TransformFinalBlock` because there is nothing to wait for.
- `Encoding.UTF8.GetString` and `Convert.FromBase64String` — synchronous by design; base64
  decoding over ~150 KB of memory finishes in about a tenth of a millisecond, far below the
  threshold where offloading would pay for the context switch.
- AngleSharp's document is built from an in-memory string, so no network fetch is involved;
  the await is kept only because it is the library's documented entry point.

The rule of thumb: make something `async` when it *waits*; leave it synchronous when it
*computes*. If a CPU-bound stage ever grows large enough to matter (a multi-megabyte page with
a pathological regex, say), the right answer is not `Task.Run` but a bounded background
service or `Parallel.For` over independent chunks — and even then, only for the stage that
actually needs it.

## Project structure

```
├── compose.yml                  # API + Postgres 18 + pgAdmin
├── Dockerfile                   # build-and-run image for the API
├── pgadmin/
│   └── servers.json             # pgAdmin server registration
├── json_result_1.txt            # API response for json_payload_1.txt
├── json_result_2.txt            # API response for json_payload_2.txt
├── TestJob.Api.sln
└── src/TestJob.Api/
    ├── Program.cs               # host, swagger, DB bootstrap
    ├── Controllers/TestJobController.cs
    ├── Models/Models.cs         # request/response DTOs
    ├── Services/TestJobService.cs
    ├── Validation/TestJobRequestValidator.cs
    └── appsettings.json
```

`json_result_1.txt` and `json_result_2.txt` were produced by posting `json_payload_1.txt` and
`json_payload_2.txt` from the original assignment to a freshly started `docker compose` stack,
against an empty `elements` table.

## Notes on the container setup

- The API image is `mcr.microsoft.com/dotnet/sdk:10.0` and the entrypoint runs
  `dotnet build && dotnet run` on every container start. Because `compose.yml` bind-mounts
  `./src` into `/app/src`, editing the source and restarting the container is enough to
  rebuild — no image rebuild required.
- `PGADMIN_CONFIG_SERVER_MODE: "False"` puts pgAdmin into desktop mode, which is what makes
  it open at <http://localhost:8080> with no login form. The pre-registered connection comes
  from `pgadmin/servers.json`, so no credentials have to be typed either.
- `PGADMIN_DEFAULT_EMAIL` must use a real, non-reserved TLD. Values ending in `.local`,
  `.test`, `.example` or `.invalid` are rejected by pgAdmin's validator and the container
  exits with code 1.
- PostgreSQL data lives in the named volume `pgdata`, mounted at `/var/lib/postgresql`, which
  is the correct mount point for the `postgres:18` image (its `PGDATA` is
  `/var/lib/postgresql/18/docker`). The data survives `docker compose down` / `up`.
- The API creates its own table on startup and retries for about 40 s while the database
  finishes initialising, which is backed up by `depends_on: condition: service_healthy`.

## License

Unlicensed — test assignment artifact.
