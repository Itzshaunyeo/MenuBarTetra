# MenuBar Tetra (Windows / Unity)

A compact, keyboard-first 3D Tetris game designed to sit at the upper-right of a Windows desktop. Launching the executable opens a fixed, always-on-top playfield immediately; no title screen or mouse interaction is required.

## Controls

| Key | Action |
| --- | --- |
| Left / Right | Move |
| Down | Soft drop |
| Space | Hard drop |
| Z / X or Up | Rotate |
| R | Restart at any time |
| Esc | Quit |

## Open / build in Unity

1. Open this folder in **Unity 2022.3 LTS** (or newer).
2. Open `Assets/Scenes/Main.unity`, or press Play from any scene: the game bootstraps itself.
3. Select **MenuBar Tetra > Build Windows Player** (or use **File > Build Settings > Windows**). The player opens directly into the game.

The project has no packages or external assets. All geometry is generated at runtime, which makes it easy to import into another Unity project: copy `Assets/Scripts` and add `TetraGame` to any empty GameObject.

## Windows presentation

The player requests a 430x820 borderless, always-on-top window at the top-right of the primary display. This keeps the complete 10x20 field visible while behaving like a compact menu-bar utility. Windows users may pin the built executable to the taskbar / startup; launching it starts a new round instantly.

For an actual notification-area (system-tray) icon, build the player, then run `build-tray-launcher.cmd`. It places `MenuBarTetraTray.exe` alongside `Builds/Windows/MenuBarTetra.exe`. Double-clicking the tray icon starts a game instantly; right-click provides **Start game** and **Exit**.
