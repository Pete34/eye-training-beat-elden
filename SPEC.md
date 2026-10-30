# SPEC — VR Spell Duel, Slice 1 (Ember Adept)

## Pitch
Elden Ring bosses meet Beat Saber. A mage boss fires spell volleys. You read glyphs, continually judge which in-range spell is nearest, and counter it with controllers. Clean counters fill a stagger meter; a full meter opens a short punish window. Depth perception and eye teaming are trained under the hood. Frame as a game, not a treatment. No medical claims.

## Slice 1 scope (ship this only)
- One arena
- Ember Adept, phase 1 only
- 3 glyph types
- Per-volley apparent-size randomization + depth ordering
- Vergence staircase (`Δ` 0–1) controls the spell depth-band width
- Session logging (hits, order errors, Δ, breaks, recoveries, arrival gaps)
- Comfort break prompt after about 10 minutes of play; pause gameplay for at least 10 seconds, reset `Δ` to 0 during the break, and resume only when the player indicates they are ready
- Keep assets simple and functional while validating gameplay; prioritize readable spell silhouettes, glyphs, and responsive counters over visual polish
- Selection screen with a separate Lab mode for tuning gameplay variables

Out of scope for slice 1: Frost Seer, Trickster, multi-phase bosses, polished VFX beyond a simple shatter.

## Lab mode
- Access Lab mode from the selection screen; keep it separate from the normal game flow.
- Provide editable, bounded, named settings for spell speed, volley size, depth-band range/`Δ`, spell apparent size, slash reach allowance, shield projection/padding, and staircase step. Show units or valid ranges where applicable.
- Support saving/loading named presets. Apply settings before a run and record the selected preset and effective values in that session's log so playtests are reproducible.
- Lab adjustments may be made by the user or an AI helper, but only through these named settings and their bounds; do not allow arbitrary scene or code changes from the Lab UI.
- Treat the initial Lab as a developer/playtest tool; normal game mode uses curated defaults.

## Core loop (20–40s)
1. Boss tell (wind-up)
2. Volley of 3–6 spells
3. Player identifies the currently nearest uncountered spell in range, then uses its glyph to choose the counter (slash or palm-block), reassessing depth before each counter
4. Correct action and order with physical slash/shield contact → shatter + meter; wrong action/order or no contact → no clear, spell remains active; miss → hit and trigger retaliation
5. Full meter → ~3s stagger / punish window
6. Repeat

## Counter hit geometry
- A palm-block gesture projects a temporary shield volume forward from the controller. Give the shield a modest configurable padding around its projected shape; padding enlarges the hit volume but does not multiply the shield's projection distance. Tune padding in-headset.
- Slash detection follows the sword's swept path. Determine reach from the sword's physical length, allowing up to 10% extra beyond the tip (`effectiveReach = swordLength * 1.1`). Keep collider thickness separate from reach.
- Target selection uses a shared, gesture-independent acquisition range. At each counter, select the nearest active uncountered spell in that range by current viewing depth, then read its glyph and validate the performed action against that action's hit volume. The acquisition range must cover the intended slash and shield interaction areas; it does not itself count as a hit.
- A wrong action or out-of-order counter does not consume the spell; it remains active and can be targeted again after the order is corrected. A spell that crosses a configurable player-side miss boundary without a correct counter counts as a miss, damages the player, and is removed.
- A slash/block input or animation alone never clears a spell. The sword's swept volume or the projected shield volume must overlap the spell, then `CounterJudge` validates depth order and glyph action.
- The first miss in a volley triggers one retaliation phase: all remaining unresolved spells turn visibly hostile and accelerate sharply toward the player. A retaliation impact damages the player and removes that spell; retaliation impacts do not recursively trigger another retaliation phase. Retaliation speed is configurable and should be tuned for a readable but urgent rush.

## Depth must be the only cue
- Treat 1 Unity unit as 1 meter throughout the scene. Measure `eyeToBossDistance` from the player's eye midpoint to the boss along the viewing direction.
- `Δ` is a dimensionless difficulty level clamped to 0–1 (`0` easiest, `1` hardest), not a distance. Set the volley depth-band width to `depthSpan = lerp(0.30, 0.05, Δ) * eyeToBossDistance`: 30% of the player-to-boss distance at easiest, narrowing to 5% at hardest.
- Place the depth band between `nearDepth = minSpellDistanceMeters` and `farDepth = min(nearDepth + depthSpan, eyeToBossDistance - bossClearanceMeters)`, measured from the player's eye midpoint along the viewing direction. Keep `minSpellDistanceMeters` and `bossClearanceMeters` configurable and tune them in-headset; sample spell depths within this band.
- Validate that the available interval is positive before spawning: if `eyeToBossDistance - bossClearanceMeters <= nearDepth`, do not start the volley and show a Lab/configuration error. If the interval is positive but narrower than `depthSpan`, use the available interval and record the effective span.
- Every frame, scale each spell as `scale = volleyWorldSizeAtRefDistance * currentDistance / refDistance` so angular size stays constant (no looming). All spells in a volley share the same `volleyWorldSizeAtRefDistance`, randomized between volleys, not between spells within a volley, so size does not reveal depth order.
- At each counter, determine order from the spells' current depth along the player's viewing direction, not distance from the controller. Among active, uncountered spells within the shared acquisition range, the nearest is the required target. Re-evaluate this for every counter; spells may pass one another and change order while in flight.
- Vary spell speeds independently of depth; speed and arrival time do not define the required target. If a farther in-range spell is countered while a nearer uncountered spell is also in range, count an order error.
- Playtest with one eye closed and head still; ordering should get hard without stereo.
- Log arrival-time gaps per volley as timing/difficulty data only. Tight gaps (150–300ms) may increase challenge, but the required target is always the currently nearest eligible spell and must not be derivable from arrival time. The staircase may tighten gaps as well as raise Δ.

## Eye checks (build into fun)
- Three visually distinct glyphs: two require a slash, one requires a palm-block. Distinguish glyphs by shape, not color alone. First select the nearest eligible spell by depth; then read its glyph to choose the action. Exact glyph-to-action assignments are configurable. Optional later: split glyph across LeftEye/RightEye layers.
- Split tell later: left eye sees wind-up pose, right eye sees rune color; log per-eye misses.
- Difficulty = prism demand `Δ`. A clean volley means every spell in that volley is correctly countered in the required order, with no wrong actions, order errors, or misses. After a clean volley, increase `Δ` by `2 * staircaseStep`; after any failed volley, decrease it by `4 * staircaseStep`; apply one update per completed volley and clamp to 0–1. `staircaseStep` is a tunable dimensionless value. A comfort break pauses gameplay for at least 10 seconds and resets `Δ` to 0; wait for player confirmation before resuming. Separate break/recovery logs for convergence vs divergence. Ember Adept = near/clustered (convergence).

## Scripts to implement
- `SpellProjectile` — flight, per-frame angular scale, ArrivalTime, GlyphType, Depth
- `VolleySpawner` — `Δ` → depth-band width as a fraction of eye-to-boss distance; spawn 3–6 spells in the band with mixed speeds and one shared apparent size per volley
- `CounterJudge` — on verified sword/shield contact only, find the nearest active uncountered spell in the shared acquisition range by current viewing depth; grade slash vs palm-block against its glyph action and order
- `SaberHand` — detect slash vs palm-block via trigger + velocity; evaluate sword swept reach and projected shield hit volume; haptics
- `BossController` + `StaggerMeter` — tell / volley / stagger state machine
- `GlyphDisplay` — three shape-distinct whole glyphs with configured slash/block actions (split-eye later)
- `SessionLogger` — CSV per volley
- `ComfortTimer` — prompt after about 10 minutes of play; pause gameplay for a minimum 10-second break, reset `Δ` to 0, and wait for player confirmation to resume
- Reuse / create dichoptic eye rig: EyeCam_L / EyeCam_R, LeftEye & RightEye layers

## Prefabs
XR Rig (dichoptic), Spell_Ember, EmberAdept, Arena, HUD_Meter

## Build order
1. Spells fly, hits judged by depth order (placeholder geometry and glyphs); validate that the core counter loop is fun before visual polish
2. Per-frame scale + one-eye playtest
3. Staircase + logger
4. Glyph types (+ split tell if time)
5. Audio pitch by hit order + haptics polish

## Feel
Physical Beat Saber hits, shatter SFX rising in pitch on correct order, haptic pulses, learnable patterns.

## Agent notes
- Prefer small, compiling C# slices. Do not expand scope past Ember Adept phase 1.
- Keep code human-readable and add concise comments to non-obvious code blocks explaining their intent; avoid comments that merely restate the code.
- Prefer OpenXR / XR Interaction Toolkit patterns already in the project.
- When Unity MCP is connected, create scene objects/prefabs in-editor; otherwise write scripts under `Assets/Scripts/` and leave wiring notes.
