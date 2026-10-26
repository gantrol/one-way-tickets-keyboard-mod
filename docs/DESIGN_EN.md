# One Way Tickets — Keyboard & QoL Mod Detailed Design

> **[Back to Quick Start (../README.md)](../README.md)** | **[中文详细设计文档 (./DESIGN.md)](./DESIGN.md)**

This document describes the internal mechanics, ergonomic two-handed layout, multi-action weapon bindings, stage-aware toolbox progression with pull-out animation, character auto-assign algorithm, and developer build instructions for `OneWayTickets.KeyboardMod`.

---

## 1. Ergonomic Two-Handed Layout & Fixed 5-Slot Architecture

### 1.1 Two-Handed Home Row (`ASDFG` + `HJKL;`) Design
- **Most frequent combat actions sit directly under your home-row fingers**:
  - **Left Hand Home Row (`A S D F G`)**: Fixed Weapon Slots 1–5 (Primary Fire/Strike, Smart Auto-Reload, and `Ctrl + A~G` `[AUTO]` hosting).
  - **Right Hand Home Row (`H J K L ;`)**: The 5 core Toolbox tools (Wrench, Extinguisher, Saw, Rag/Drier, WakeUp), paired with `Space` for Global Smart Maintenance.
- **Left Hand Upper Row (`Q W E R T`)**: Aligned vertically above `A S D F G` for **Per-Slot Quick Maintenance** on Slots 1–5.
- **Left Hand Lower Row (`Z X C V`)**: Caravan driving controls — **`Z` Brake**, **`X` Psionic Acceleration (Sprint Charge)**, **`C` Furnace Bellows (+ Push Car)**, and **`V` Reserved Wheel Slot Key**.
- **Right Hand Upper Row (`Y U I O P [` + `]`/`'`)**: Character selection (1–6, with number keys `1–6` also active as aliases) and **One-Key Optimal Character Auto-Assign (`'` or `]`)**.

### 1.2 Fixed 5 Weapon Slots (Wheel Slot Excluded)
Decompiling `World.dll`, `AutoCodes.dll`, and scene assets confirmed that every caravan has **at most 5 weapon/equipment slots** from left to right (`slot_back`, `slot_back_top`, `slot_top`, `slot_front_top`, `slot_front`). The 6th entry in `DeviceMgr.slots_device` is **`device_slot_wheel`** (the dashboard wheel slot), which is filtered out of the 5 weapon slots and bound to the reserved **`C`** key.

---

## 2. Stage-Aware Toolbox Progression & Pull-Out Animation

### 2.1 Exception-Safe Tool Pull-Out & Put-Back Animation
- Pressing `H J K L ;`, `Q W E R T`, or `Space` visually opens the Toolbox lid (`PlayerToolMgrView.box_image`), pulls out the corresponding tool sprite (`ToolLogic`), animates it working over the target slot/character, and returns it to the Toolbox.
- Every animation step is wrapped in defensive `try-catch` blocks so even if a future game update changes UI prefabs, the underlying repair/extinguish/cut/wipe/wake mechanics remain 100% functional.

### 2.2 Stage-Aware Behavior Table

| Shortcut | Tool Upgrade Path | Stage-Aware Behavior |
| :--- | :--- | :--- |
| **`Space`** | **Global Smart Maintenance** | Prioritizes: Wake stunned characters → Extinguish fire → Clear acid → Wipe/Blow-dry water → Cut top-layer vines → Kick sleeping devices → Wrench repair 0-HP devices → Caravan Repair Kit |
| **`Q~T`** | **Fixed-Slot Maintenance (Slots 1–5)** | Clears hazards (acid/fire/vines/water/sleep) and repairs the fixed weapon slot directly below (`A~G`) |
| **`H`** | **Single Wrench → Ratchet → Auto-Wrench + Caravan Repair Kit** | Scales with Wrench upgrade speed on 0-HP broken devices; automatically triggers the **Caravan Repair Kit (`Widget_Fix_Context`)** on damaged devices/hull when no device is at 0 HP |
| **`J`** | **Extinguisher (All Stages)** | Uses current extinguisher efficiency to put out burning devices |
| **`K`** | **Hand Saw (`Lv0`) → Scissors (`Lv1`) → Chainsaw (`Lv2`) → Circular Saw (`Lv3`)** | Always cuts top-most vines first (`upper_vines.Count == 0`, zero wrong-order HP penalty) and scales cutting speed with saw stage |
| **`L`** | **Rag (`Lv0`) → Big Rag (`Lv1`) → Mop (`Lv2`) → Drier (`Lv3`)** | **Early stages**: Wipes droplets/acid on one device without aiming;<br>**Drier (`Lv3`)**: Pressing `L` **blows dry all devices across the entire caravan simultaneously**! |
| **`;`** | **Hammer (`Lv0`) → Chicken (`Lv1`) → Clock (`Lv2`) → AED (`Lv3`)** | **Early stages**: Wakes one comatose character at a time (or `; + Y~[` for a specific character);<br>**AED (`Lv3`)**: Pressing `;` **wakes all comatose characters simultaneously**! |

---

## 3. Caravan Driving: Brake (`Z`), Psionic Boost (`X`), Bellows (`C`) & Wheel Slot (`V`)

| Shortcut | Action | Internal Mechanism |
| :--- | :--- | :--- |
| **`Z` (Hold)** | **Caravan Brake** | Pulls `CaravanLever2.lever_slider` to maximum and sets `WorldContext.brake_lever_input = 1f`; releasing `Z` releases the brake lever |
| **`X` (Tap)** | **Psionic Boost** | Strictly triggers `SpeedChargeView.@do()` (Psionic Sprint Charge) without touching the bellows |
| **`C` (Hold/Tap)** | **Furnace Bellows** | Pumps the furnace bellows (`Widget_Blower_Context` / `BellowView`) while held, and pushes the caravan if stopped |
| **`V` (Tap)** | **Wheel Slot Key** | Activates wheel-slot abilities (`JetWheel.Accelerate()`, `BasicWheelUiView.Push_Car_btn`) |

---

## 4. Character Dispatch (`Y U I O P [`) & One-Key Auto-Assign (`'` / `]`)

- **Manual Dispatch**: Tap `Y U I O P [` (or `1 ~ 6`) to select Character 1–6, then tap `A ~ G` (or `Q ~ T`) to assign them to Slot 1–5 (tap again to cycle cubicles). Double-tap the character key or press `Backspace` / `0` to recall them to rest.
- **One-Key Optimal Auto-Assign (`'` or `]`)**: Evaluates `Character.GetAbility(device)` and `CanWork(device)` across all slots and assigns idle/available characters to the highest-priority cubicles.
- **Continuous Auto-Assign (`Ctrl + '` or `Ctrl + ]`)**: Periodically assigns idle characters and automatically shifts solo workers on a weapon between Attack and Load/Carry/Sharp cubicles when ammo runs dry.

---

## 5. Multi-Action Weapons (1–3 Actions) Mapping

| Weapon / Device Type | Action 1 (`A~G` or `[AUTO]`) | Action 2 (`Shift + A~G`) | Action 3 (`Alt + A~G`) |
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

## 6. Building from Source (Developers Only)

End users only need to run the standalone installer `双击一键安装或卸载Mod.exe`. To rebuild from source after editing `src/*.cs`:

```powershell
dotnet build .\OneWayTickets.KeyboardMod.csproj -c Release
```

The Release build automatically updates `dist\OneWayTickets.KeyboardMod.dll` and regenerates the standalone installer `双击一键安装或卸载Mod.exe`, which does not require `.ps1` or `.bat` scripts.
