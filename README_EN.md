# One Way Tickets — Full Keyboard & QoL Mod (`OneWayTickets.KeyboardMod`)

**[中文文档 (README.md)](./README.md)**

A non-destructive keyboard control and Quality-of-Life (QoL) mod for **One Way Tickets (再再启程)**. While preserving 100% of the vanilla mouse interactions, this mod introduces a **4-row spatial keyboard matrix (Characters / Weapons / Slot Maintenance / Toolbox)**, **1–3 multi-action weapon bindings with smart loops**, and **one-key emergency maintenance**, eliminating tedious toolbox dragging and multi-weapon micromanagement.

---

## Key Features

1. **Instant Toolbox Actions Without Opening or Dragging (Stage-Aware QoL)**
   - **Tap or Hold `Space` for Global Smart Maintenance**: Automatically prioritizes waking stunned characters, extinguishing fires, neutralizing acid, wiping water, cutting vines, kicking sleeping devices, and repairing broken/damaged devices.
   - **Tap or Hold `A / S / D / F / G` for Fixed-Slot Maintenance**: Maps strictly to **Fixed Caravan Weapon Slots 1–5** from left to right (keys never shift even if some slots are empty, staying 100% aligned with `Q / W / E / R / T` above).
   - **Tap or Hold `Z / X / C / V / B` for Stage-Aware Dedicated Tools**:
     - `Z` (Wrench & Caravan Repair Kit): Scales with Wrench upgrade stages (single-way, ratchet, auto-wrench) and automatically triggers the Caravan Repair Kit on damaged devices/hull when no device is at 0 HP.
     - `X` (Extinguisher): Uses your current extinguisher efficiency to put out fires.
     - `C` (Hand Saw -> Scissors -> Chainsaw -> Circular Saw): Always cuts top-most vines first (zero wrong-order HP penalty) and scales cutting speed with your saw stage.
     - `V` (Rag -> Big Rag -> Mop -> Hairdryer/Drier): Early stages wipe droplets/acid on one device at a time; once upgraded to the **Drier (`Lv3`)**, pressing `V` **blows dry all devices across the entire caravan simultaneously**!
     - `B` (Inflating Hammer -> Screaming Chicken -> Clock/Tweeter -> AED): Early stages wake one comatose character at a time (or `B + 1~6` for a specific character); once upgraded to the **AED (`Lv3`)**, pressing `B` **wakes all comatose characters simultaneously**!
   - **Instant Visual & Audio Key Feedback**: Every key press highlights the target slot badge in gold (`▶ 1号 抢修[A]!` / `▶ 1号 操控[Q]!`), plays the tool's native SFX, and displays the exact key, tool stage name, and target device in the top banner.

2. **Full Keyboard Weapon Control (1–3 Actions + Manual Override During `[AUTO]`)**
   - **Primary Action & Smart Loop (Tap/Hold `Q / W / E / R / T`)**:
     - Tapping or holding immediately triggers the weapon's direct manual action (melee strike, ranged shot, reload step, wheel boost, war drum beat, etc.) while running the smart loop (auto-reload when empty, auto-charge, turret auto-aim).
   - **Auto-Hosting Mode (`Ctrl + Q~T` per slot / `Tab` global toggle)**:
     - Automates firing and reloading in the background.
     - **Seamless Manual Override**: Even while `[AUTO]` is active, you can **press `Q~T` / `Shift+Q~T` / `Alt+Q~T` or use the mouse on the weapon UI at any time**. When interacting with a weapon panel via mouse, background `[AUTO]` automatically yields on that slot so your mouse drag/click is never interrupted.
     - **Clear Cancel Prompts & Click-to-Cancel**: When `[AUTO]` is active, a top-center banner and green `[🟢自动]` slot badges clearly display how to cancel (`Tab` or `Ctrl+Q~T`), and **clicking either the banner or the slot badge with the mouse immediately cancels `[AUTO]`**.
   - **Secondary Action 2 (`Shift + Q~T`) & Tertiary Action 3 (`Alt + Q~T`)**:
     - Full support for weapons with 2 or 3 distinct operations (see table below).

3. **Character Selection & Fast Cubicle Dispatch (`1 ~ 6`)**
   - Press `1 ~ 6` to select a front-line character, then press `Q ~ T` to dispatch them to the corresponding device slot (pressing `Q ~ T` again cycles between primary/secondary cubicles on multi-station devices).
   - Double-tap `1 ~ 6` or press `0` / `Backspace` to recall a character to rest; press `Alt + 1 ~ 6` to swap with backup characters.

4. **Context-Aware HUD & Hot-Reload**
   - HUD badges and status bars automatically **hide in non-combat scenes (campfire/safe areas and settlement screens)** and are centered at the top so they never overlap the top-left Relic bar.
   - Press `F9` in-game to toggle the HUD overlay, or `F10` to hot-reload `OneWayTickets.KeyboardMod.ini`.

---

## 4-Row Keyboard Matrix

| Keyboard Row | Keys | Target Area | Description |
| :--- | :--- | :--- | :--- |
| **Row 1 (Numbers)** | `1` `2` `3` `4` `5` `6` | **Characters (1–6)** | **Tap `1~6` then `Q~T`**: Dispatch character to device slot (repeat to cycle cubicles)<br>**Double-tap `1~6` or `0` / `Backspace`**: Recall character to rest<br>**`Alt + 1~6`**: Swap front-line character with backup |
| **Row 2 (QWERT)** | `Q` `W` `E` `R` `T`<br>+ `Tab` | **Weapon Bar (Fixed Slots 1–5, L to R)** | **Tap/Hold `Q~T`**: Primary action + Smart Loop (attack/fire/auto-reload/charge)<br>**`Ctrl + Q~T` / Click Badge**: Toggle slot **`[AUTO]` mode**<br>**`Tab` / Click Top Banner**: Toggle all weapon slots `[AUTO]`<br>**`Shift + Q~T`**: Weapon Action 2<br>**`Alt + Q~T`**: Weapon Action 3 |
| **Row 3 (ASDFG)** | `A` `S` `D` `F` `G` | **Slot Maintenance (Fixed Slots 1–5)** | **Tap/Hold `A~G`**: Perform **Emergency Clear + Wrench Repair** on the corresponding fixed weapon slot (`Q~T`) directly above |
| **Row 4 (ZXCVB)** | `Z` `X` `C` `V` `B`<br>+ `Space` | **Toolbox & Global Maintenance** | **Tap/Hold `Space`**: **Global Smart Maintenance** (auto-prioritizes all emergencies & repairs)<br>**`Z`**: Wrench / Repair Kit \| **`X`**: Extinguisher (Fire) \| **`C`**: Saw/Scissors/Chainsaw (Vines)<br>**`V`**: Rag/Mop/Drier (Water/Acid) \| **`B`**: Hammer/AED (`B + 1~6` for specific character) |

---

## Multi-Action Weapons (1–3 Actions) Mapping

| Weapon / Device Type | Action 1 (`Q~T` or `[AUTO]`) | Action 2 (`Shift + Q~T`) | Action 3 (`Alt + Q~T`) |
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
