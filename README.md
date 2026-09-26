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
| Postgres| localhost:5432 (db `testjob`)            | `testjob` / `testjob`       |

Swagger JSON is served at `http://localhost:8090/api/swagger/v1/swagger.json`.

On startup the API creates the `elements` table if it does not exist:

```sql
CREATE TABLE IF NOT EXISTS elements (
    id              BIGSERIAL PRIMARY KEY,
    attribute_value TEXT NOT NULL,
    element_html    TEXT NOT NULL
);
```

## Run locally (no Docker)

Requires a PostgreSQL instance (the default connection string targets `localhost:5433`)
and the .NET 10 SDK.

```bash
# create the database once
psql -h localhost -p 5433 -U postgres -c "CREATE DATABASE testjob;"

# run the API
dotnet run --project src/TestJob.Api
```

Override the connection string with an environment variable if needed:

```powershell
$env:ConnectionStrings__Default = "Host=localhost;Port=5433;Database=testjob;Username=postgres;Password=postgres"
```

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
| `VALIDATION_ERROR`        | required fields are missing/empty                   |
| `URL_BASE64_DECODE_ERROR` | `url_b64` cannot be decoded                         |
| `PAGE_BASE64_DECODE_ERROR`| `page_b64` cannot be decoded                        |
| `PARSE_ERROR`             | page parsing / selector evaluation failed           |
| `DB_ERROR`                | database write failed                               |
| `EMAIL_REGEX_ERROR`       | email extraction failed                             |
| `DECRYPTION_ERROR`        | AES decryption failed                               |

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

- **Fully async** end-to-end: `JsonSerializer.DeserializeAsync`, AngleSharp `OpenAsync`,
  Npgsql `OpenAsync`/`ExecuteAsync` (via Dapper `CommandDefinition`). No blocking IO on the
  request path, so the API scales under concurrent load without thread-pool starvation.
- **AES-256-ECB** with `PaddingMode.None` and the provided 256-bit key — the algorithm is
  used exactly as specified by the task (ECB is not recommended for general use, but is
  required here).
- **Email regex**: `[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}` with
  `Compiled | IgnoreCase | CultureInvariant`.
- **Validation** with FluentValidation before any processing.
- Each selected element is stored with its extracted attribute value and full outer HTML.

## Project structure

```
├── compose.yml                  # API + Postgres 18 + pgAdmin
├── Dockerfile                   # build-and-run image for the API
├── pgadmin/
│   └── servers.json             # pgAdmin server registration
├── json_result_1.txt            # sample API response (payload 1)
├── json_result_2.txt            # sample API response (payload 2)
└── src/TestJob.Api/
    ├── Program.cs               # host, swagger, DB bootstrap
    ├── Controllers/TestJobController.cs
    ├── Models/Models.cs         # request/response DTOs
    ├── Services/TestJobService.cs
    ├── Validation/TestJobRequestValidator.cs
    └── appsettings.json
```

## License

Unlicensed — test assignment artifact.