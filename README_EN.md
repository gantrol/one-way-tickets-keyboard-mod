# One Way Tickets — Full Keyboard & QoL Mod

**[中文文档 (README.md)](./README.md)** | **[Detailed Design & Weapon Table (docs/DESIGN_EN.md)](./docs/DESIGN_EN.md)**

Preserves 100% of vanilla mouse controls while mapping **character dispatch, fixed 5-slot weapon control (`[AUTO]` hosting), and drag-free toolbox maintenance** onto a clean 4-row left-hand keyboard layout.

---

## One-Click Install / Uninstall (No Command Line Needed)

1. **Close the game** first.
2. Double-click **`双击安装Mod (Install).bat`** to auto-detect your Steam game folder and install the mod (if your game is in a custom folder, a folder picker window will pop up automatically).
3. To uninstall and restore vanilla files, double-click **`双击卸载Mod (Uninstall).bat`**.

---

## Keyboard Layout at a Glance

```text
┌─────────────────────────────────────────────────────────────────────────┐
│ [ 1 ]   [ 2 ]   [ 3 ]   [ 4 ]   [ 5 ]   [ 6 ]       <- Row 1: Characters│
│ Char 1  Char 2  Char 3  Char 4  Char 5  Char 6         (Tap 1~6 -> Q~T) │
├─────────────────────────────────────────────────────────────────────────┤
│ [Tab]   [ Q ]   [ W ]   [ E ]   [ R ]   [ T ]       <- Row 2: Weapons   │
│AllAuto  Slot 1  Slot 2  Slot 3  Slot 4  Slot 5         (Fixed Slots 1-5)│
├─────────────────────────────────────────────────────────────────────────┤
│         [ A ]   [ S ]   [ D ]   [ F ]   [ G ]       <- Row 3: Slot Fix  │
│         Fix 1   Fix 2   Fix 3   Fix 4   Fix 5          (Fix Slot 1-5)   │
├─────────────────────────────────────────────────────────────────────────┤
│         [ Z ]   [ X ]   [ C ]   [ V ]   [ B ]       <- Row 4: Toolbox   │
│         Wrench  FireEx  CutVine Rag/Dry WakeUp         (Stage-Aware)    │
├─────────────────────────────────────────────────────────────────────────┤
│               [       Space (Hold/Tap)     ]        <- Global Smart Fix!│
└─────────────────────────────────────────────────────────────────────────┘
```

### Quick Modifiers

- **Weapon Controls (`Q ~ T`)**:
  - **Tap/Hold `Q ~ T`**: Primary weapon action + smart auto-reload
  - **`Ctrl + Q ~ T`** (or **`Tab`** for all): Toggle **`[AUTO]` mode** (you can still manually override via keyboard/mouse at any time; click the green badge or top banner to cancel)
  - **`Shift + Q ~ T` / `Alt + Q ~ T`**: Secondary Action 2 / Tertiary Action 3 (sharpen, manual reload, throw spear, crank elevation, etc. — see [docs/DESIGN_EN.md](./docs/DESIGN_EN.md))
- **Character Dispatch (`1 ~ 6`)**:
  - **Tap `1 ~ 6` then `Q ~ T`**: Dispatch character to weapon slot (press again to switch cubicles)
  - **Double-tap `1 ~ 6`** (or `0` / `Backspace`): Recall character to rest
- **HUD & Config**:
  - **`F9`**: Show / hide HUD overlay (auto-hides in camp & settlement screens)
  - **`F10`**: Hot-reload keybindings from `OneWayTickets.KeyboardMod.ini` in the game folder
