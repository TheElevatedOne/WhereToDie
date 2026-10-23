# Where To Die

![Static Badge](https://img.shields.io/badge/CLAUDE-VIBECODED?style=for-the-badge&logo=claude&label=VIBECODED&labelColor=%23333333&color=%23de7356)

![Icon](https://raw.githubusercontent.com/TheElevatedOne/WhereToDie/refs/heads/main/icon.png?v=2)

Quality of life for the **Page of Reincarnation** (the N'tormat page from Hallowed Concepts that sacrifices you
to take you somewhere else). It shows where the page works and whether it will kill you, fixes two annoyances
with getting and keeping the page, and adds two new places to use it.

## Die or Not

- **Page icon highlight.** While you stand where Reincarnation works, its icon above the N'tormat changes colour, fading from green at the bottom to:
    - **yellow** at the top: the ritual works and nobody has to die (the Veil of Memory in the Soulbound Threshold)
    - **red** at the top: the ritual works by killing you (everywhere else)
- **Hallowed aurora.** The aurora of a hallowed shrine hangs above every place where Reincarnation works, so you can
  find them from across the map. Yellow if the ritual doesn't kill, red if it does. It only shows while you hold an
  unused Page of Reincarnation. During the first 30 seconds of a normal stage (when the page rerolls the stage) a
  sparser aurora covers the whole map, and it disappears when that window closes. Auroras fade in and out.

| Kills you | Nobody dies |
| --- | --- |
| ![Red page icon](https://raw.githubusercontent.com/TheElevatedOne/WhereToDie/refs/heads/main/docs/screenshots/hud_icon_red.png) | ![Yellow page icon](https://raw.githubusercontent.com/TheElevatedOne/WhereToDie/refs/heads/main/docs/screenshots/hud_icon_yellow_row.png) |
| ![Red aurora over a stage in its first 30 seconds](https://raw.githubusercontent.com/TheElevatedOne/WhereToDie/refs/heads/main/docs/screenshots/01_stage_aurora.jpg) | ![Yellow aurora at the Veil of Memory](https://raw.githubusercontent.com/TheElevatedOne/WhereToDie/refs/heads/main/docs/screenshots/05_threshold_veil_aurora.jpg) |
| First 30 seconds of a stage | The Veil of Memory |

## Quality

- **Reincarnation with the N'tormat.** Picking up the N'tormat also gives you the Page of Reincarnation, if you
  don't have it yet.
- **Recharged in the Soulbound Threshold.** A page that changes the stage stays spent for the next stage too, so the page
  you used to reach the Soulbound Threshold couldn't open the way to Terminal Lucidity, and you needed two.
  Now it recharges when you arrive, and one page is enough.

## Space and Time

Two new places where dying by Reincarnation takes you to the **Soulbound Threshold**:

| Place | When | Chat message |
| --- | --- | --- |
| On top of the rocketship on the Moon (Commencement) | after Mithrix is defeated | **Found a way to escape the Moon** |
| The final arena of the Planetarium | after Voidling is defeated | **Found peace in the Void** |

| Rocketship roof | The ritual on the roof |
| --- | --- |
| ![Red aurora over the rocketship](https://raw.githubusercontent.com/TheElevatedOne/WhereToDie/refs/heads/main/docs/screenshots/02_moon_roof_aurora.jpg) | ![Ritual circle on the rocketship roof](https://raw.githubusercontent.com/TheElevatedOne/WhereToDie/refs/heads/main/docs/screenshots/04_moon_ritual.jpg) |
| **Planetarium final arena** | **Chat** |
| ![Red aurora over the Planetarium arena](https://raw.githubusercontent.com/TheElevatedOne/WhereToDie/refs/heads/main/docs/screenshots/07_planetarium_aurora.jpg) | ![Found a way to escape the Moon](https://raw.githubusercontent.com/TheElevatedOne/WhereToDie/refs/heads/main/docs/screenshots/hud_chat_moon.png) |

They only work while nobody has entered Terminal Lucidity this run. (Prime Meridian needs nothing new: the game already
has a zone at the altar in the False Son's arena, which leads to Terminal Lucidity; it gets its aurora like any other.)

![Aurora over the Prime Meridian altar](https://raw.githubusercontent.com/TheElevatedOne/WhereToDie/refs/heads/main/docs/screenshots/08_meridian_altar_aurora.jpg)

**No way back after the finale.** Taking either of these places means Mithrix or Voidling is beaten, so the run doesn't
return to normal stages: the portal after the Anima Harvester and the teleporter after Providence in Terminal Lucidity
are replaced by a portal to **Obliteration** (A Moment, Fractured), and nothing leads out of Obliteration except the Obelisk.
Obliterating always ends the run there: Beads of Fealty don't open A Moment, Whole (whose Reincarnation zone leads back to
the Moon).

| Soulbound Threshold | Terminal Lucidity |
| --- | --- |
| ![Obliteration portal in the Soulbound Threshold](https://raw.githubusercontent.com/TheElevatedOne/WhereToDie/refs/heads/main/docs/screenshots/06_threshold_obliteration_portal.jpg) | ![Obliteration portal in Terminal Lucidity](https://raw.githubusercontent.com/TheElevatedOne/WhereToDie/refs/heads/main/docs/screenshots/09_lucidity_obliteration_portal.jpg) |

**Re-entering the Soulbound Threshold.** Defeating the Anima Harvester normally ends the soul-binding trial for the
rest of the run. If you haven't entered Terminal Lucidity yet, the Shrine of Descent and the trial come back, so you can
try again (this also applies to the game's own way in, not only to the new places).

## Multiplayer

| Who has the mod | What happens |
| --- | --- |
| Host only | Everything gameplay-related works for everyone: the page with the N'tormat, the recharge, the new places, re-entry. Nobody sees the highlight or aurora except the host. |
| Host and some players | Those players also see the aurora and the icon highlight. |
| A player but not the host | That player gets the icon highlight for the game's own places (where dying is assumed for zones it can't identify). No aurora, no gameplay changes. |

It adds no items, rules or networked objects, so vanilla players can join a modded host.

## Configuration

`BepInEx/config/com.elevated.WhereToDie.cfg`, or in game through Risk Of Options (Mod Options → Where To Die).

| Section | Setting | Default | Description |
| --- | --- | --- | --- |
| Die or Not | `Page icon highlight` | `true` | Colour the Reincarnation icon while you stand where it works. |
| Die or Not | `Hallowed aurora` | `true` | Aurora above places where Reincarnation works. Needs the host to have the mod. |
| Quality | `Give Reincarnation with the N’tormat` | `true` | Host only. |
| Quality | `Refresh Reincarnation in the Soulbound Threshold` | `true` | Host only. |
| Space and Time | `Rocketship roof` | `true` | Host only. |
| Space and Time | `Planetarium final arena` | `true` | Host only. |
| Space and Time | `Re-enter the Soulbound Threshold` | `true` | Host only. |
| Space and Time | `No way back after the finale` | `true` | Host only. After the rocketship roof or the Planetarium, the run ends in Obliteration. |

Console command (open the console with `Ctrl + Alt + ~`):

| Command | Description |
| --- | --- |
| `wheretodie_status` | Print the places where Reincarnation works right now. |

## How it works

- The game already marks the holder of the page with a "ready for sacrifice" buff while they stand in a working zone.
  The highlight reads that buff and the zone's behaviour (kill and travel, or only trigger an event).
- The host works out which zones are usable (without rolling the run's stage RNG) and sends their positions only to
  players who have the mod.
- The new places are ordinary sacrifice zones that exist only on the host, which is where the ritual decides where to go.
- The recharge removes the page's one-stage cooldown and turns the spent page back into a usable one, the same way the
  game does at the start of a stage.

## Compatibility

- Requires the Hallowed Concepts DLC to be enabled for the run.
- The new places lead to the Soulbound Threshold; from there the run continues to Terminal Lucidity and/or Obliteration.
