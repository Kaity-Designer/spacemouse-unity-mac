# SpaceMouse for Unity (Mac)

Fly around the Unity Scene view with a 3Dconnexion SpaceMouse on macOS.
3Dconnexion only ships its Unity plugin for Windows, so this package talks to
the installed Mac driver directly.

Editor only: it moves the Scene view camera, not cameras in your game.

## Requirements

- macOS 11 or newer, Apple Silicon or Intel
- Unity 6 (6000.x)
- The 3Dconnexion driver for macOS (3DxWare / "3Dconnexion Home") installed and running

## Install

1. In Unity open **Window > Package Manager**.
2. Click **+ > Install package from git URL…**
3. Paste `https://github.com/Kaity-Designer/spacemouse-unity-mac.git` and press **Install**.

Or add this line to `Packages/manifest.json` under `dependencies`:

```json
"com.kah.spacemouse-mac": "https://github.com/Kaity-Designer/spacemouse-unity-mac.git"
```

To hack on it locally instead, clone the repo and use **+ > Install package
from disk…** with its `package.json`.

## Status

Early version (0.1.0). Tested on macOS with Unity 6000.6 and a SpaceMouse
Wireless: the package compiles, loads its native bridge and connects to the
3Dconnexion driver. Default axis directions may need flipping on your device
(see below). Issues and pull requests are welcome.

## Use

- Hover the mouse over a Scene view and move the cap.
- A light in the Scene view (and at the top of the panel) shows the status:
  green = connected, amber = no SpaceMouse found or navigation off,
  red = 3Dconnexion driver missing. Hide or show it from the Scene view's
  Overlays menu.
- **Window > SpaceMouse** opens the panel with speeds, mode and live bars.
  The same settings are under **Unity > Settings > Preferences > SpaceMouse**.
- Left button: frame the selected object. Right button: cycle Walk / Fly / Orbit.
  Both can be changed in the panel. **Tools > SpaceMouse** also has the three modes.
- Lost in space? **Tools > SpaceMouse > Recenter Scene View** (or the
  **Recenter Scene view** button in the panel) puts the Scene view where the
  scene's Main Camera is, or back to Unity's starting view if there is none.
  You can also assign it to a puck button.
- **Back to original settings** at the bottom of the panel resets everything
  to the defaults. You can also assign it to a puck button.

The harder you push, the faster you go: a light touch is slow and precise,
a full push flies across a whole level. **Push boost** in the panel sets how
much faster a full push is (default 3, 1 = off). Movement eases in over a
fraction of a second, so a sudden hard push doesn't fling the camera.

**Walk** (default): like a game controller. Push or tilt the cap forward/back to
walk, left/right to step sideways, twist to turn, lift/press to go up/down.
You always move level, so you never fly off at an angle.
**Fly**: you are the camera. Push forward to move where you look, twist to turn.
In an orthographic view, pushing forward zooms instead.
With **Lock horizon** on (the default), tilting the cap sideways turns left/right,
and tilting forward/back stops short of looking straight up or down.
**Orbit**: you hold the scene. Push and pull to zoom toward the pivot, twist and tilt to turn it.

Settings live in EditorPrefs, so they apply to every project.

## If a direction is wrong

Open **Window > SpaceMouse > Axis mapping**. Move the cap and watch the bars:
each camera movement shows which puck axis drives it. Tick **Invert** to flip
it, or pick another axis from the dropdown.

## How it works

- `Plugins/macOS/SpaceMouseBridge.bundle` is a small native library (source in
  `Native~/`). It opens `/Library/Frameworks/3DconnexionClient.framework` at
  runtime and registers as the Unity Editor (`UNED`), so the driver sends
  input to Unity only while Unity is the app in front. Blender, Cinema 4D and
  the rest keep their own behaviour.
- No 3Dconnexion code is included; without the driver, Unity simply shows a
  warning in the SpaceMouse panel.
- `Editor/` holds the C# that turns axes into camera movement.

## Rebuild the native bundle

```bash
cd Native~ && ./build.sh
```

`./test_harness 20` in `Native~` prints live device input for 20 seconds
(move the cap while it runs).

## Licence

MIT, see [LICENSE.md](LICENSE.md). Not affiliated with or endorsed by
3Dconnexion. SpaceMouse and 3Dconnexion are trademarks of their owners.
