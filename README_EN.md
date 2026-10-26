# One Way Tickets — Ergonomic Two-Handed Keyboard & QoL Mod

**[中文文档 (README.md)](./README.md)** | **[Detailed Design & Weapon Table (docs/DESIGN_EN.md)](./docs/DESIGN_EN.md)**

Preserves 100% of vanilla mouse controls while splitting high-frequency combat actions across an **ergonomic two-handed home-row layout (`ASDFG` + `HJKL;`)**: Left hand handles **fixed 5-slot weapons, per-slot quick fix, brake, and psionic boost**; Right hand handles **animated toolbox tools, character selection, and one-key optimal character auto-assignment**. Status feedback is anchored cleanly above the Toolbox on the right.

---

## One-Click Install / Uninstall (No Command Line Needed)

1. **Close the game** first.
2. Double-click **`双击安装Mod (Install).bat`** to auto-detect your Steam game folder and install the mod (if your game is in a custom folder, a folder picker window will pop up automatically).
3. To uninstall and restore vanilla files, double-click **`双击卸载Mod (Uninstall).bat`**.

---

## Two-Handed Keyboard Layout at a Glance

```text
┌───────────────────────────────────────┬─────────────────────────────────────────────┐
│             [ LEFT HAND ]             │                [ RIGHT HAND ]               │
├───────────────────────────────────────┤─────────────────────────────────────────────┤
│       [Q]   [W]   [E]   [R]   [T]     │ [Y]   [U]   [I]   [O]   [P]   [ [ ]   [ ] ] │
│       Fix1  Fix2  Fix3  Fix4  Fix5    │ Chr1  Chr2  Chr3  Chr4  Chr5  Chr6   AutoAsn│
├───────────────────────────────────────┤─────────────────────────────────────────────┤
│ [Tab] [A]   [S]   [D]   [F]   [G]     │ [H]   [J]   [K]   [L]   [ ; ]   [ ' ]       │
│AllAuto Wpn1  Wpn2  Wpn3  Wpn4  Wpn5   │Wrnch  Fire  Saw   Rag   Wake   AutoAsn      │
├───────────────────────────────────────┤─────────────────────────────────────────────┤
│       [Z]     [X]       [C]           │                                             │
│      Brake  PsiBoost  WheelKey        │                                             │
├───────────────────────────────────────┴─────────────────────────────────────────────┤
│                        [          Space (Hold / Tap)      ]                         │
│                    Global Smart Maintenance (with Tool Animation)                   │
└─────────────────────────────────────────────────────────────────────────────────────┘
```

### Quick Guide

- **Left Hand Home Row: Fixed 5-Slot Weapons (`A S D F G`)**
  - **Tap/Hold `A ~ G`**: Primary weapon action + smart auto-reload
  - **`Ctrl + A ~ G`** (or **`Tab`** for all): Toggle **`[AUTO]` mode** (override anytime with keyboard/mouse; click the green badge or status panel above the Toolbox to cancel)
  - **`Shift + A ~ G` / `Alt + A ~ G`**: Secondary Action 2 / Tertiary Action 3 (sharpen, manual reload, throw spear, crank elevation, etc. — see [docs/DESIGN_EN.md](./docs/DESIGN_EN.md))
- **Left Hand Upper & Lower Rows: Slot Fix (`Q ~ T`) & Driving (`Z X C`)**
  - **`Q W E R T`**: Quick-fix the weapon slot directly below (`1 ~ 5`)
  - **`Z` (Hold)**: Pull the caravan brake lever; release to let go
  - **`X` (Tap/Hold)**: Trigger **Psionic Acceleration Sprint** + pump furnace bellows + push stopped caravan
  - **`C`**: Reserved Wheel Slot key (JetWheel boost / Push Car)
- **Right Hand Home & Upper Rows: Toolbox (`H J K L ;`) & Characters (`Y U I O P [` + `'`/`]`)**
  - **`H J K L ;`**: Visually pulls **Wrench / Extinguisher / Saw / Rag(Drier) / WakeUp** out of the Toolbox, animates it on the target device/character, and puts it back (automatically scales with tool upgrades)
  - **`'` or `]`**: **One-Key Character Auto-Assign** (assigns idle/fainted characters to optimal weapon slots based on ability scores; **`Ctrl + '` / `Ctrl + ]`** toggles continuous `[AUTO-ASSIGN]`)
  - **`Y U I O P [`** (`1 ~ 6` also supported): Select character 1~6, then press `A ~ G` (or `Q ~ T`) to dispatch; double-tap or press `Backspace` to recall
- **HUD & Config**: **`F9`** toggles HUD overlay; **`F10`** hot-reloads `OneWayTickets.KeyboardMod.ini`
