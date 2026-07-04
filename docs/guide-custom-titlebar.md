# Guide: Custom title bar (Avalonia 12 drawn decorations)

**Status:** implementation guide — not yet implemented
**Goals:**
1. Extend the acrylic effect from the sidebar up into the title bar (one continuous glass column/strip).
2. Add a **Settings** button to the title bar.

**Acceptance:** both goals fulfilled **and** all native title-bar features keep working:
- drag to move; double-click to maximize/restore
- Win+arrow snapping; **Win11 snap-layout flyout** when hovering the maximize button
- right-click title bar / Alt+Space → system menu
- minimize/maximize/close buttons behave natively; taskbar interactions unchanged
- acrylic falls back gracefully when Windows transparency is off (same as today)

## Verified API surface (Avalonia 12.0.5 — confirmed against the installed package)

- `Window.ExtendClientAreaToDecorationsHint` (bool) — **still exists in v12** (only
  `ExtendClientAreaChromeHints` was removed; we hit that AVLN2000 earlier — do not use it).
- `Window.ExtendClientAreaTitleBarHeightHint` (double).
- `WindowDecorationProperties.SetElementRole(Visual, WindowDecorationsElementRole)` —
  attached property (XAML: `chrome:WindowDecorationProperties.ElementRole="..."`,
  `xmlns:chrome="using:Avalonia.Controls.Chrome"`). Per its XML docs: *"only has an effect
  if `ExtendClientAreaToDecorationsHint` is true."* Roles drive **cross-platform non-client
  hit testing** — this is what makes drag/snap/system-menu native again.
  Roles: `TitleBar`, `MinimizeButton`, `MaximizeButton`, `CloseButton`, `FullScreenButton`,
  `ResizeN/S/E/W/NE/NW/SE/SW`, `User`, `None`, `DecorationsElement`.
- `Window.IsExtendedIntoWindowDecorations`, `Window.WindowDecorationMargin` — read-only
  state for layout adjustments (e.g. maximized inset).
- `WindowDrawnDecorations` (Chrome namespace) + `Window.WindowDecorationsTheme` — the
  fully app-drawn chrome path (template with Underlay/Overlay/FullscreenPopover layers and
  `PART_MinimizeButton` / `PART_MaximizeButton` / `PART_CloseButton` parts). See
  https://docs.avaloniaui.net/controls/primitives/windowdrawndecorations

## Recommended approach: Route 1 — extend client area + element roles

Route 2 (override `WindowDecorationsTheme` and re-template `WindowDrawnDecorations`) is the
right tool when you want fully app-drawn chrome on platforms without system decorations.
On Windows, Route 1 is the path that best preserves native behaviors, is less code, and is
the documented purpose of the roles system. Use Route 2 only if Route 1's verification
checklist fails (see below).

### Design

- `MainWindow`: set `ExtendClientAreaToDecorationsHint="True"` and
  `ExtendClientAreaTitleBarHeightHint="40"` (tune to taste).
- Window layout becomes a two-row grid: **row 0 = custom title bar (40px)**, row 1 = the
  existing sidebar/content grid.
- Move the `ExperimentalAcrylicBorder` so it spans **both rows** (it already fills the root
  Grid — it will naturally extend under the title bar once the client area extends).
  - Title-bar strip background: `Transparent` → acrylic shows through, continuous with the
    transparent sidebar below. Visual result: one glass region covering title bar + sidebar.
  - Content pane keeps its solid `PageBrush` (unchanged from current design).
  - Optional: hairline bottom border on the title bar only over the content-pane span, to
    keep the content visually anchored.
- Title bar contents, left→right:
  1. App icon (16px, from `Assets/syncmaid.ico` artwork — use a PNG/bitmap or the existing
     icon rendered small) + `SyncMaid` title text (13px, `TextSecondaryBrush`).
  2. Star `*`-width spacer — this whole strip (including icon/title area) is the drag
     region: put `ElementRole="TitleBar"` on the strip's root Border.
  3. **Settings button** — reuse `Button.caption` style (already in
     `Styles/Theme.axaml`, left over from the earlier custom-chrome attempt) with a
     `Cog`/`CogOutline` Material icon. Mark it `ElementRole="User"` so it is clickable, not
     draggable. Command: `OpenSettingsCommand` on `MainWindowViewModel` (stub until the
     settings dialog exists — see guide-settings-autostart.md).
  4. Caption buttons: three `Button.caption` buttons (minimize `WindowMinimize` icon /
     maximize-restore / close `Close` icon; `close` also gets the existing
     `Button.caption.close` hover style). Assign roles `MinimizeButton`, `MaximizeButton`
     (this is what should light up the Win11 snap-layout flyout), `CloseButton`. Wire
     their clicks to `Window.WindowState` changes and `Close()` in code-behind — keep it
     in the view; it is pure window plumbing, not view-model logic.
     - Maximize button icon should swap between `WindowMaximize` and `WindowRestore`
       based on `WindowState` (subscribe in code-behind or a style on `:maximized`).

### Known pitfalls to handle

- **Maximized inset:** when maximized, Windows extends the window frame slightly
  off-screen. Bind extra padding on the root when maximized (use
  `Window.WindowDecorationMargin` / `OffScreenMargin` if exposed, or a `:maximized`-driven
  padding). Verify visually: title-bar buttons must not be clipped at the top when
  maximized.
- **Modal overlay:** the `DialogHost` backdrop currently fills the root Grid; after the
  change it will also dim the title bar. That is acceptable (standard modal behavior), but
  confirm the window can still be dragged while a dialog is open **only if desired** — if
  the backdrop covers the `TitleBar`-role element, drag is blocked during modals, which is
  fine; note it in the PR.
- **Icon in title bar:** `Window.Icon` keeps working for taskbar/Alt-Tab; the in-titlebar
  icon is a separate visual you draw.
- **Do not** reference `ExtendClientAreaChromeHints` (removed in v12; AVLN2000).

### Verification checklist (manual, on Windows 11)

- [ ] Drag by title bar; double-click toggles maximize.
- [ ] Hover maximize button ≥1s → snap-layout flyout appears; choosing a zone works.
- [ ] Win+Left/Right snapping; Aero shake if enabled.
- [ ] Right-click title bar and Alt+Space → system menu at correct position.
- [ ] Minimize/close buttons work; close still exits cleanly (triggers Dispose path).
- [ ] Acrylic continuous across title bar + sidebar; content pane still solid.
- [ ] Transparency disabled in Windows settings → solid fallback, everything readable.
- [ ] Maximized: no clipped buttons, no 7px dead zone at screen top edge
      (buttons clickable when mouse slammed to y=0 — Fitts's law check).
- [ ] Headless UI tests still pass (they host views in plain Windows; extend-client-area
      is a no-op on the headless platform, but verify no binding errors).

### Suggested sequencing

1. Extend client area + empty transparent title-bar row + roles on the strip; verify drag
   and system menu before adding any buttons.
2. Add caption buttons with roles; verify snap-layout flyout. **This is the riskiest
   acceptance item — if the flyout does not appear with `MaximizeButton` role, stop and
   evaluate Route 2 before building more.**
3. Add settings button (stubbed command) + icon/title.
4. Acrylic/spacing polish; maximized-inset handling.
5. Update `MainWindow`-related headless tests if any assert on layout.
