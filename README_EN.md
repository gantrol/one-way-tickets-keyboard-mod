# One Way Tickets — Full Keyboard & QoL Mod (`OneWayTickets.KeyboardMod`)

**[中文文档 (README.md)](./README.md)**

A non-destructive keyboard control and Quality-of-Life (QoL) mod for **One Way Tickets (再再启程)**. While preserving 100% of the vanilla mouse interactions, this mod introduces a **4-row spatial keyboard matrix (Characters / Weapons / Slot Maintenance / Toolbox)**, **1–3 multi-action weapon bindings with smart loops**, and **one-key emergency maintenance**, eliminating tedious toolbox dragging and multi-weapon micromanagement.

---

## Key Features

1. **Instant Toolbox Actions Without Opening or Dragging (Core QoL)**
   - **Hold `Space` for Global Smart Maintenance**: Automatically prioritizes waking stunned characters, extinguishing fires, neutralizing acid, wiping water, cutting vines, kicking sleeping devices, and repairing the lowest-HP device with the wrench.
   - **Hold `A / S / D / F / G / H` for Per-Slot Maintenance**: Directly clears emergencies and repairs the device in Slot 1–6 (vertically aligned with `Q / W / E / R / T / Y` above).
   - **Tap or Hold `Z / X / C / V / B` for Dedicated Tools**: Wrench & Caravan Repair Kit (`Z`), Extinguisher for fire (`X`), Saw/Scissors for vines (`C`), Rag/Mop for water/acid (`V`), and Wake-up tool (`B`, or `B + 1~6` to wake a specific character).

2. **Full Keyboard Weapon Control (1–3 Actions + Manual Override During `[AUTO]`)**
   - **Primary Action & Smart Loop (Tap/Hold `Q / W / E / R / T / Y`)**:
     - Tapping or holding immediately triggers the weapon's direct manual action (melee strike, ranged shot, reload step, wheel boost, war drum beat, etc.) while running the smart loop (auto-reload when empty, auto-charge, turret auto-aim).
   - **Auto-Hosting Mode (`Ctrl + Q~Y` per slot / `Tab` global toggle)**:
     - Automates firing and reloading in the background.
     - **Seamless Manual Override**: Even while `[AUTO]` is active, you can **press `Q~Y` / `Shift+Q~Y` / `Alt+Q~Y` or use the mouse on the weapon UI at any time**. When interacting with a weapon panel via mouse, background `[AUTO]` automatically yields on that slot so your mouse drag/click is never interrupted.
     - **Clear Cancel Prompts & Click-to-Cancel**: When `[AUTO]` is active, a top-center banner and green `[🟢自动]` slot badges clearly display how to cancel (`Tab` or `Ctrl+Q~Y`), and **clicking either the banner or the slot badge with the mouse immediately cancels `[AUTO]`**.
   - **Secondary Action 2 (`Shift + Q~Y`) & Tertiary Action 3 (`Alt + Q~Y`)**:
     - Full support for weapons with 2 or 3 distinct operations (see table below).

3. **Character Selection & Fast Cubicle Dispatch (`1 ~ 6`)**
   - Press `1 ~ 6` to select a front-line character, then press `Q ~ Y` to dispatch them to the corresponding device slot (pressing `Q ~ Y` again cycles between primary/secondary cubicles on multi-station devices).
   - Double-tap `1 ~ 6` or press `0` / `Backspace` to recall a character to rest; press `Alt + 1 ~ 6` to swap with backup characters.

4. **Context-Aware HUD & Hot-Reload**
   - HUD badges and status bars automatically **hide in non-combat scenes (campfire/safe areas and settlement screens)** and are centered at the top so they never overlap the top-left Relic bar.
   - Press `F9` in-game to toggle the HUD overlay, or `F10` to hot-reload `OneWayTickets.KeyboardMod.ini`.

---

## 4-Row Keyboard Matrix

| Keyboard Row | Keys | Target Area | Description |
| :--- | :--- | :--- | :--- |
| **Row 1 (Numbers)** | `1` `2` `3` `4` `5` `6` | **Characters (1–6)** | **Tap `1~6` then `Q~Y`**: Dispatch character to device slot (repeat to cycle cubicles)<br>**Double-tap `1~6` or `0` / `Backspace`**: Recall character to rest<br>**`Alt + 1~6`**: Swap front-line character with backup |
| **Row 2 (QWERTY)** | `Q` `W` `E` `R` `T` `Y`<br>+ `Tab` | **Weapon Bar (Slots 1–6, L to R)** | **Tap/Hold `Q~Y`**: Primary action + Smart Loop (attack/fire/auto-reload/charge)<br>**`Ctrl + Q~Y` / Click Badge**: Toggle slot **`[AUTO]` mode**<br>**`Tab` / Click Top Banner**: Toggle all weapon slots `[AUTO]`<br>**`Shift + Q~Y`**: Weapon Action 2<br>**`Alt + Q~Y`**: Weapon Action 3 |
| **Row 3 (ASDFGH)** | `A` `S` `D` `F` `G` `H` | **Slot Maintenance (Slots 1–6)** | **Hold `A~H`**: Perform **Emergency Clear + Wrench Repair** on the corresponding device slot (`Q~Y`) directly above |
| **Row 4 (ZXCVB)** | `Z` `X` `C` `V` `B`<br>+ `Space` | **Toolbox & Global Maintenance** | **Tap/Hold `Space`**: **Global Smart Maintenance** (auto-prioritizes all emergencies & repairs)<br>**`Z`**: Wrench / Repair Kit \| **`X`**: Extinguisher (Fire) \| **`C`**: Saw/Scissors (Vines)<br>**`V`**: Rag (Water/Acid) \| **`B`**: Wake-up (`B + 1~6` for specific character) |

---

## Multi-Action Weapons (1–3 Actions) Mapping

| Weapon / Device Type | Action 1 (`Q~Y` or `[AUTO]`) | Action 2 (`Shift + Q~Y`) | Action 3 (`Alt + Q~Y`) |
| :--- | :--- | :--- | :--- |
| **Standard Melee (Baton, Blade, Cactus, Flower, etc.)** | Immediate melee strike / swing | Manual sharpen (for resharpenable melee) | — |
| **Claymore** | Charge + full-charge slash (auto-sharpens when dull) | **Manual Sharpen** | — |
| **Charge Melee (Flail, Saw, Blackhole, Fan, Rocket)** | Charge + release at full charge | **Immediate Release Attack** | — |
| **Throwing Spear** | **Melee Thrust** | **Ranged Spear Throw** (auto-aims) | — |
| **Standard Shooters (Rifle, Pistol, Shotgun, Crossbow, Revolver)** | Aim & Shoot + Auto-reload when empty | **Manual Early Reload / Chamber Bullet** | **Unload Barrel / Cylinder** |
| **Gatling Gun** | Crank fire + auto-carry & reload when empty | **Manual Reload Ammo Box** | **Manual Carry Ammo Box** |
| **Cannon / Windlass** | Shoot + Reload + Auto-rotate | **Manual Reload** | **Crank Elevation / Draw String** |
| **Special Quiver** | Load special arrows | **Toggle Quiver Active State** | — |
| **Unique War Drum** | Beat Left Drumstick | **Beat Right Drumstick** | — |
| **Backup Ammo / Accelerator / Wheel / Shield** | Load ammo / Add fuel / Accelerate / Charge energy | Manual Load | — |

---

## Installation & Uninstallation

### 1. Automated Installation (Recommended)

> **Important**: Please **close the game** before running the installer so `World.dll` and `OneWayTickets.KeyboardMod.dll` are not locked by the running process.

Run the PowerShell installer from the repository root (it **automatically detects your game directory across all Steam libraries** via the Windows Registry and `libraryfolders.vdf`, and passes the resolved `Managed` path to `dotnet build`):

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Mod.ps1
```

If your game is located outside standard Steam libraries, you can also pass `-GameRoot` explicitly:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Mod.ps1 -GameRoot "E:\SteamLibrary\steamapps\common\再再启程 One Way Tickets"
```

### 2. Uninstallation

To restore the original `World.dll.bak` and remove the mod files:

```powershell
powershell -ExecutionPolicy Bypass -File .\Uninstall-Mod.ps1
```

---

## Configuration (`OneWayTickets.KeyboardMod.ini`)

After installation, `OneWayTickets.KeyboardMod.ini` is created in the game root directory. You can customize any keybinding or speed multiplier and press **`F10`** in-game to hot-reload without restarting.
