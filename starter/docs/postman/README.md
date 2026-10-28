# Postman collection — AppBrand API

Every request the platform's API answers, chained: sign-in (with the code fetched from Mailpit), account and sessions,
MFA, the household and its invitations, notifications, billing, the admin console, the public API and webhooks, and the
DELETE-ME Notes sample. It is the platform's collection as of the release this app started from.

| File | In Postman |
|------|-----------|
| `AppBrand.postman_collection.json` | the collection (v2.1), one for every environment |
| `AppBrand.local.postman_environment.json` | environment "AppBrand — local dev" |
| `AppBrand.staging.postman_environment.json` | environment "AppBrand — staging (Render)": set its `baseUrl` to your host |

## Quick start (local)

1. `dotnet platform up` (Postgres + Mailpit), then `dotnet run --project src/Host --launch-profile https`.
2. Import the collection and both environments; select **AppBrand — local dev**.
3. Allow the dev certificate: Postman → Settings → General → SSL certificate verification off.
4. Folder **1 · Sign in**: `OTP — send`, wait a few seconds, `OTP — fetch code from Mailpit`, `OTP — verify`. The
   tokens land in the environment, and every other request uses them.

Some folders need a gate switched on in `.env` (and the host restarted): `Billing__Enabled`, `PublicApi__Enabled`,
`Webhooks__Enabled`, and your email in `Admin__StaffEmails__0` for the admin console. When your app adds a slice, add its
requests here; when you delete the Notes sample, delete its folder and the `noteId` variable too.
