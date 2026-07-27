# Tetra (Windows / Unity)

A compact, keyboard-first Tetris game designed for the upper-right of a Windows desktop. It opens to a start screen with player-name entry, live global scores, and a click/keyboard Start Game action.

## Features

- Hold queue, landing ghost, upcoming-piece previews, score/stage UI, and live global leaderboard.
- Timed **Stage Flip**: every three minutes, the established stack vertically inverts and gravity reverses.
- Generated retro background music plus move, rotate, hold, drop, line-clear, and game-over effects.
- Runtime-generated visuals and audio: no external Unity packages or media assets are required.

## Controls

| Key | Action |
| --- | --- |
| Left / Right | Move |
| Down | Soft drop in the active gravity direction |
| Up / X | Rotate clockwise |
| Z | Rotate counter-clockwise |
| Shift | Hold or swap the active piece |
| Space | Hard drop in the active gravity direction |
| R | Restart at any time |
| P | Pause |
| L | Refresh the online leaderboard |
| Esc | Quit |

## Open / build in Unity

1. Open this folder in **Unity 2022.3 LTS** (or newer).
2. Open `Assets/Scenes/Main.unity`, or press Play from any scene: the game bootstraps itself.
3. Select **MenuBar Tetra > Build Windows Player** (or use **File > Build Settings > Windows**).

The project has no packages or external assets. All geometry is generated at runtime, which makes it easy to import into another Unity project: copy `Assets/Scripts` and add `TetraGame` to any empty GameObject.

## Windows presentation

The player requests a 430x820 borderless, always-on-top window at the top-right of the primary display. This keeps the complete 10x20 field visible while behaving like a compact menu-bar utility. Windows users may pin the built executable to the taskbar / startup.

For an actual notification-area (system-tray) icon, build the player, then run `build-tray-launcher.cmd`. It places `MenuBarTetraTray.exe` alongside `Builds/Windows/MenuBarTetra.exe`. Double-clicking the tray icon starts a game instantly; right-click provides **Start game** and **Exit**.

## Online leaderboard

Tetra includes a shared leaderboard client and a dependency-free Node.js server in [`server/`](server). The checked-in build configuration points to the live service at `https://menubartetra-leaderboards.onrender.com`.

Players enter their name on the start screen. Scores submit at game over, leaderboard listings refresh automatically every 15 seconds, and **L** or the Refresh button refreshes immediately. To run your own server, use `cd server` then `npm start`, deploy it to a Node.js host with persistent storage, update `Assets/Resources/OnlineLeaderboardConfig.json`, and rebuild the player.
