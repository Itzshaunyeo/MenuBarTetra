# Tetra leaderboard server

Run locally with Node 18+:

```powershell
cd server
npm start
```

It listens at `http://localhost:8787`. For public multiplayer scores, deploy this `server` folder to a Node.js host with persistent disk storage and set `PORT` if the host provides one. Then replace the `endpoint` value in `Assets/Resources/OnlineLeaderboardConfig.json` with the public HTTPS URL and rebuild the Unity player.

Endpoints:

- `GET /api/leaderboard` returns the top 20 scores.
- `POST /api/scores` accepts `{ "name": "Player", "score": 100 }`.

The service validates names/scores and includes a basic per-IP submission cooldown. For a competitive public release, add authentication and server-side game verification.
