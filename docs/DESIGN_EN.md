# One Way Tickets — Keyboard & QoL Mod Detailed Design

> **[Back to Quick Start (../README_EN.md)](../README_EN.md)** | **[中文详细设计文档 (./DESIGN.md)](./DESIGN.md)**

This document describes the internal mechanics, multi-action weapon bindings, stage-aware toolbox progression, and developer build instructions for `OneWayTickets.KeyboardMod`.

---

## 1. Fixed 5-Slot Architecture (`Q W E R T` / `A S D F G`)

Decompiling `World.dll`, `AutoCodes.dll`, and scene assets confirmed that:
- Every caravan in the game has **at most 5 weapon/equipment slots** from left to right:
  1. `slot_back` (Slot 1: `Q` / `A`)
  2. `slot_back_top` (Slot 2: `W` / `S`)
  3. `slot_top` (Slot 3: `E` / `D`)
  4. `slot_front_top` (Slot 4: `R` / `F`)
  5. `slot_front` (Slot 5: `T` / `G`)
- The 6th entry in `DeviceMgr.slots_device` is **`device_slot_wheel`** (the dashboard wheel slot on the far left).
- `DeviceSlotResolver` filters out `device_slot_wheel` and binds `Q W E R T` and `A S D F G` strictly to **Fixed Weapon Slots 1–5** so keys never shift even when intermediate slots are empty.

---

## 2. Stage-Aware Toolbox Progression

| Shortcut | Tool Upgrade Path | Stage-Aware Behavior |
| :--- | :--- | :--- |
| **`Space`** | **Global Smart Maintenance** | Automatically prioritizes: Wake stunned characters → Extinguish fire → Clear acid → Wipe/Blow-dry water → Cut top-layer vines → Kick sleeping devices → Wrench repair 0-HP devices → Caravan Repair Kit |
| **`A~G`** | **Fixed-Slot Maintenance (Slots 1–5)** | Clears hazards (acid/fire/vines/water/sleep) and repairs the fixed weapon slot directly above (`Q~T`) |
| **`Z`** | **Single Wrench → Ratchet → Auto-Wrench + Caravan Repair Kit** | Scales with Wrench upgrade speed on 0-HP broken devices; automatically triggers the **Caravan Repair Kit (`Widget_Fix_Context`)** on damaged devices/hull when no device is at 0 HP |
| **`X`** | **Extinguisher (All Stages)** | Uses current extinguisher efficiency to put out burning devices |
| **`C`** | **Hand Saw (`Lv0`) → Scissors (`Lv1`) → Chainsaw (`Lv2`) → Circular Saw (`Lv3`)** | Always cuts top-most vines first (`upper_vines.Count == 0`, zero wrong-order HP penalty) and scales cutting speed with saw stage |
| **`V`** | **Rag (`Lv0`) → Big Rag (`Lv1`) → Mop (`Lv2`) → Drier (`Lv3`)** | **Early stages**: Wipes droplets/acid on one device without aiming;<br>**Drier (`Lv3`)**: Pressing `V` **blows dry all devices across the entire caravan simultaneously**! |
| **`B`** | **Hammer (`Lv0`) → Chicken (`Lv1`) → Clock (`Lv2`) → AED (`Lv3`)** | **Early stages**: Wakes one comatose character at a time (or `B + 1~6` for a specific character);<br>**AED (`Lv3`)**: Pressing `B` **wakes all comatose characters simultaneously**! |

---

## 3. Multi-Action Weapons (1–3 Actions) Mapping

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
| **Backup Ammo / Accelerator / Shield** | Load ammo / Add fuel / Charge energy | Manual Load | — |

---

## 4. Building from Source (Developers Only)

End users only need to double-click `双击安装Mod (Install).bat` (which uses the prebuilt binaries in `dist/`). To rebuild from source after editing `src/*.cs`:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Mod.ps1 -BuildFromSource
```
