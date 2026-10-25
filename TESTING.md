# Testing checklist

`make test` (tests/TestDriver) plays all of this as a two-player split-screen session on the host (the second player is a
guest profile that never saves), and round-trips the host -> client marker message through the mod's own writer and reader.
A real remote client (modded or vanilla) needs a second machine and is still untested.

## Singleplayer (host)

- [ ] Pick up the N'tormat without the Page of Reincarnation: the page is added (log: "Gave the Page of Reincarnation").
- [ ] Reincarnation never drops as a page (log at run start: "removed it from the page drops"): not as the random page
      with the first N'tormat, not in the N'tormat's page choice, not from the Ancient Sepulcher or the Shrine of Compulsion.
      Other pages still drop.
- [ ] Normal stage, first 30 s, holding an unused page: red, sparse aurora across the map; gone after 30 s.
      The Reincarnation icon is coloured green-red while it lasts.
- [ ] Shrine of Descent → soul-binding trial → die at the Teleporter → Soulbound Threshold: the page is usable on arrival
      (log: "Recharged the Page of Reincarnation").
- [ ] Soulbound Threshold after the Anima Harvester: yellow aurora at the altar, green-yellow icon; the ritual opens
      the Veil of Memory with that same page.
- [ ] Leave the Soulbound Threshold without entering Terminal Lucidity: on the next stage the log says the Threshold can be
      entered again, and a Shrine of Descent can spawn again.
- [ ] Moon: after Mithrix, red aurora on the rocketship roof; dying there by Reincarnation prints
      **Found a way to escape the Moon** and loads the Soulbound Threshold. Check the log for "roof is not world geometry".
- [ ] Planetarium: after Voidling, red aurora over the final arena; dying there prints **Found peace in the Void**.
- [ ] After entering Terminal Lucidity neither new place appears, and re-entry stops.
- [ ] After the rocketship roof or Planetarium ritual: the Soulbound Threshold's portal after the Anima Harvester and the
      teleporter after Providence are Obliteration portals; leaving goes to Obliteration; Obliteration has no way out;
      obliterating with Beads of Fealty ends the run instead of opening A Moment, Whole.
- [ ] Every option shows in Risk Of Options under "Where To Die" with its readable name.
- [ ] `wheretodie_status` lists the current markers.

## Multiplayer

- [ ] Modded host + vanilla client: the client joins, plays, and gets the gameplay changes; no errors on the client.
- [ ] Modded host + modded client: the client sees the aurora and the icon colours (log on host: "<name> has WhereToDie").
- [ ] Vanilla host + modded client: icon colours for vanilla zones; no aurora; no errors.
- [ ] Two players with pages: `Util.ScalePageRequirement` zones still behave as in vanilla.
