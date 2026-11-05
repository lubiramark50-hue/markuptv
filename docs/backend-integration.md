# MarkUpTV backend/frontend integration

## Current architecture

The repository contains the **.NET MAUI client only**. The ASP.NET backend source, hosting configuration, database migrations, and payment webhook code are **not** in this repository. The current client expects a MarkUpTvServer API. End-to-end certification needs access to its source or deployed environment.

In `MauiProgram.cs`, **Debug** connects to the local development machine (`localhost`, Android emulator `10.0.2.2`, or LAN `192.168.1.75` on physical Android) and can discover a new local endpoint. **Release** connects to the configured `ProductionApiUrl` using HTTPS; it does not probe a developer computer or trust a saved LAN override.

The existing service clients reference `https://markuptvnew-fjhjd6bkb6bahqeh.southafricanorth-01.azurewebsites.net/`. On **2026-11-04**, read-only requests to `/health`, `/api/search?query=test`, and `/api/news?category=all&page=1&pageSize=1` all returned **403, Web App - Unavailable**. This address is **not validated as healthy** and the production release must not be approved until its service is restored or an updated HTTPS base URL is configured and verified.

## Client routes to verify against server controllers

| Feature | App request path | Expected behavior |
|---|---|---|
| Readiness | `GET /health` | HTTP 2xx; no authentication |
| TV categories | `GET /api/tv/{category}` | JSON category and channels |
| EPG | `GET /api/epg/channels/{channelId}/programmes` | JSON programmes |
| Football | `GET /api/football`, `GET /api/football/live` | Fixtures/channels JSON |
| Search | `GET /api/search?query=...` | Search JSON |
| News | `/api/news/...` | News JSON |
| Movies | `/api/movies/latest`, `/api/movies/search` | Movie catalogue JSON |
| Social | `/api/social/feed` and related mutating actions | Identity-bound feed, posts, likes and chat |
| Device/payment | `/api/device/register`, `/api/device/status` and checkout paths | Server-owned pricing and entitlements |
| Donation | `POST /api/donation/initiate` | HTTPS `redirectUrl`; **never** send real payments during a smoke test |

Some routes may be absent or differ on the deployed server: check actual route definitions rather than changing the client from guesses.

## Read-only live smoke test

Once the ASP.NET server is deployed and made reachable:

```sh
python3 tools/backend_contract_smoke.py --base-url 'https://YOUR-BACKEND-HOST/'
```

The checker exits nonzero if required public GET endpoints return errors, non-JSON payloads, or cannot be reached. It does not submit a payment, register a device, create a post, or alter stored data. The API host can be supplied to the manual GitHub Actions workflow.

## Release verification checklist

1. Confirm server is running, HTTPS certificate is valid, and ingress permits normal mobile clients; update `ProductionApiUrl` if the host changes.
2. Compare all client routes and JSON DTOs with deployed ASP.NET controller signatures and database migrations.
3. Run the read-only smoke checker, then build an Android **Release** APK and test on a real Android phone using mobile data, not the developer's Wi-Fi.
4. Test TV/EPG/search/news/movies and distinguish a truly empty result from 403/5xx or network failures.
5. In a sandbox payment environment, verify checkout initiation, signed webhook validation, device entitlement updates, expiry, cancellations, and retries. Never trust client-reported `isEmulator`, device IDs, prices, or permissions without server verification.
6. Only exact backend-origin payment callback paths or the app's own custom callback scheme may signal checkout completion. The app must verify entitlement with the server before granting access; callback URLs and virtual-device flags are not proof of payment.
7. Exercise social post/comment/like/chat calls with separate identities; verify authorization and per-user data isolation.
7. Deploy only after backend and client checks pass. Merging into `main` triggers Android OTA release in the existing GitHub workflow, so do not merge a failing integration branch.

## Automated PR checks

The backend integration PR compiles the Android Release client, runs payment-callback origin regression tests and exercises the production live-stream catalogue/resolver harness. The latter uses public internet sample streams and can fail independently if a third-party sample stream is down or unavailable to the runner. Treat that as a playback availability finding; check the logged source before changing application code.

## Additional checkout safeguards (2026-11-05)

- Payment is disabled until fresh backend status returns an explicit positive unlock price and three-letter currency, with a second live check before checkout creation.
- Payment completion checks use the live backend rather than a cached offline lease. A payment-success deep link alone is not proof of payment.
- Release signing credentials are not stored in the project; protected GitHub Actions secrets are required to sign a distribution APK.
