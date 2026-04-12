# STS2 card UI (`NCard` / `card.tscn`)

Use the STS2 Modding MCP (`get_entity_source("NCard")`, `search_game_code`) after game updates.

## One scene for all cards

Character (Ironclad, Silent, Defect, …), rarity (common / uncommon / rare), and card type (attack / skill / power) do **not** use different scene trees. The same `res://scenes/cards/card.tscn` is pooled; **`NCard.UpdateVisuals`** / model data swap **textures and materials** (e.g. `_frame.Texture`, `Model.FrameMaterial`, banners) on the **same nodes**.

## Important nodes on `NCard`

| Unique name | Role |
|-------------|------|
| `%Frame` | `TextureRect` — **full card frame** art (includes the outer colored border / “blue” bottom band). Best parent for UI that must align with the **drawn** card edge. |
| *(root)* `NCard` | Logical card root; size usually matches `defaultSize` (300×422) × scale; use if `%Frame` is unavailable. |
| `%CardContainer` | `NCard.Body` — inner chrome, glow VFX, etc. |
| `%OverlayContainer` | Portrait-area slot for `ReloadOverlay()` — **not** full-card coordinates. |
| `%Portrait`, `%DescriptionLabel`, `%TypePlaque`, … | See `NCard._Ready` in decompiled source. |

## PathPlanner win-rate strip

The strip is parented to **`%Frame`**: a bottom `Control` (`BottomWide`) holds a **`CenterContainer`** so the `PanelContainer` **shrink-wraps** the label and stays **horizontally centered** on the frame, not full-width.
