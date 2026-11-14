# SkillSwap API (Phase 1: foundation)

Skill-for-skill exchange platform. .NET 8 minimal API + EF Core (SQLite) + Identity + JWT.

## Run it

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
cd SkillSwap.Api
dotnet restore
dotnet run
```

Open the URL printed in the console (e.g. http://localhost:5xxx). It redirects to Swagger.
The database (`skillswap.db`) is created and seeded on first launch. Delete it to reset.

## Demo accounts (all use `Password123!`)

| Email | Story |
|---|---|
| ada@skillswap.demo | Excel + Data Analysis, wants Graphic Design + Figma (has discover history) |
| tunde@skillswap.demo | Graphic Design + Photoshop, wants Excel + Bookkeeping (Ada's mutual match) |
| chioma@skillswap.demo / emeka@skillswap.demo | French + Public Speaking <-> Web Dev |
| fatima@skillswap.demo / seyi@skillswap.demo | Cooking <-> Digital Marketing (completed swap, rated) |

## Try it in Swagger

1. `POST /api/auth/login` with `ada@skillswap.demo` / `Password123!`
2. Copy the `token`, click **Authorize**, paste it
3. `GET /api/users/me/skills`, `GET /api/users/me/interests`, `GET /api/skills?query=design`

## What's built

Auth (register/login/me), profiles, skill catalog, my skills, onboarding, interests, event tracking, reviews list.
The full data model (swaps, messages, sessions, reviews, events) already exists in the DbContext.

## Next phases

2. Matching engine + swaps (propose / accept / decline / complete)
3. Discover page (shelves)
4. Chat (SignalR), sessions, reviews, impact stats
