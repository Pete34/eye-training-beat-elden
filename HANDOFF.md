# Handoff: VR Spell Duel, Slice 1

## Current status
The design is captured in [SPEC.md](SPEC.md). A first runtime prototype exists in `Assets/Scenes/SpellDuelPrototype.unity`, based on the original XR rig but cleaned of the sample environment/interactables. The original `SampleScene` remains untouched. The prototype scene is saved.

Project root: `/home/pete/eye-training-beat-elden`

Existing Unity scenes include `Assets/Scenes/BasicScene/BasicScene.unity`, `Assets/Scenes/SampleScene/SampleScene.unity`, and `Assets/Scenes/SpellDuelPrototype.unity`. The project manifest currently lists XR Interaction Toolkit 3.6.0 and XR Hands 1.9.0. The prototype scene contains only the XR Origin, Lighting, EventSystem, Spell Duel Gameplay, and a simple Prototype Floor. The copied sample environment, Interactables, sample UI, Teleport Area Setup, and Hands Permissions Manager were removed from the prototype scene.

## Prototype implementation status
- `Assets/Scripts/SpellProjectile.cs`: projectile movement, eye-axis depth, constant apparent-size scaling, miss-boundary event, and predicted boundary arrival time. Projectiles steer each frame toward a point 0.65 m below the player's eye midpoint, so they visibly home toward the torso as the player moves. Desktop speed multiplier is configurable; XR uses authored speed. The projectile mesh rotates to face its current flight direction. On first volley miss, remaining projectiles enter retaliation at the configured 7 m/s, turn red, and keep homing toward the torso. A retaliation impact is removed without restarting retaliation. Correctly countered projectiles disable renderers/colliders immediately and are destroyed shortly afterward.
- `Assets/Scripts/SpellVolleySpawner.cs`: one opening volley of exactly 3 primitive spells by default, speed/depth randomization, shared size per volley, shape glyphs, and validated depth-band bounds. Desktop uses a 3.8 m minimum depth, wider separated spawn lanes, and a slightly lowered row to keep three glyphs visible apart from the boss; XR uses its authored depth/layout. The spawner's `bossAnchor` references `Ember Adept Placeholder`. Desktop refills automatically through `DesktopSpellPlaytest`.
- `Assets/Scripts/SpellCounterJudge.cs`: shared acquisition range, nearest current-depth target, configurable slash-vs-block glyph mapping (currently two slash glyphs and one shield glyph), wrong-action/order results, and correct counter resolution.
- `Assets/Scripts/SpellCounterInput.cs`: XR device position/rotation/velocity/trigger reads; fast trigger press attempts slash, slow trigger press attempts block; sword sweep and padded shield volume checks. Editor keyboard fallback: `F` animates slash, `J` animates block, `Space` requests another volley. F/J no longer call the judge or clear spells. Keyboard input uses the Input System package. Trigger actions are rising-edge only to avoid repeat penalties while held.
- `Assets/Scripts/DesktopSpellPlaytest.cs`: automatically enables only when XR is inactive; starts in a 25-degree top-down view, toggled by `T` or middle-click; left-click animates a slash and right-click animates a shield block. Spells resolve only if the corresponding animated sword sweep or shield volume actually overlaps one. Desktop mode refreshes a three-spell volley after a short configurable delay when the current volley clears. Desktop projectiles receive short trails using a supported URP particle-unlit material. The desktop HUD/instruction Canvas was deliberately removed; the player Main Camera is the only camera and frames the playfield in a centered 16:9 viewport. No reticle or depth-stabilizing overlay is used.
- `Assets/Scripts/DesktopPlayerControls.cs`: desktop-only WASD/arrow movement translates the XR Origin, with Shift sprint and a bounded arena radius. Runtime desktop sword and shield placeholders are camera-attached, have a subtle idle sway, and animate a pronounced slash arc/block thrust on input; they are not targeting aids. XR controller/hand behavior remains separate.
- `Assets/Scripts/BossPlaceholderMotion.cs`: small boss hover and yaw idle animation. The boss placeholder is now a compact humanoid silhouette assembled from primitive torso, head, shoulder, and ember-core shapes with orange/red/yellow renderer colors.
- The prototype has visible placeholder spheres and shape glyphs, and is attached to a `Spell Duel Gameplay` object in `SpellDuelPrototype.unity`.
- Runtime checks confirmed exactly three active, rendered desktop spell bodies within the single Main Camera's centered 16:9 viewport and zero active Canvas objects; the final frame has distinct glyphs, boss, sword, and shield with no HUD. A runtime direction check measured the projectile mesh facing its chest-height target at 0-degree error. Sword and shield transforms change immediately when their slash/block triggers fire. A non-contact F-key slash test leaves all three spells active; a confirmed swept-sword overlap clears the matching spell. A normal miss starts one retaliation phase at 7 m/s; a retaliation impact does not restart the phase. Desktop refill is automatic; the spawner's general automatic-repeat setting remains off. Individual Unity script validation reports zero errors. The local .NET SDK is unavailable, so `dotnet build` could not run.
- The inherited XR sample scene emits NullReferenceExceptions from XRI affordance receivers and `Assets/VRTemplateAssets/Scripts/AnchorVisuals.cs`; these are outside the new gameplay scripts. The local editor also reports `DllNotFoundException: UnityOpenXR` when attempting desktop Play mode. Treat actual headset validation as pending.
- A generic script-validator advisory about string concatenation in `SpellVolleySpawner.Update()` persisted even after cleanup; Unity script validation reports zero errors and the advisory did not appear in the editor console as a compile error.

## Agreed gameplay decisions
- The player repeatedly targets the nearest active, uncountered spell in a shared gesture-independent acquisition range, measured by current depth along the viewing direction from the eye midpoint. Spells may pass one another, so target order changes during flight.
- After selecting the nearest target, the player reads its glyph and performs the mapped action. Two shape-distinct glyphs require a slash; one requires a palm-block. Glyphs must not rely on color alone.
- Validate the performed action against its own hit volume. The shared acquisition range only selects targets and is not itself a hit. A farther spell hit while a nearer uncountered spell is also in acquisition range is an order error.
- Palm-block projects a temporary shield volume. Padding modestly enlarges its hit area without multiplying projection distance. Slash follows the sword's swept path and can reach at most 10% beyond the physical sword tip; collider thickness is separate from reach.
- Wrong-action and order-error counters do not consume the spell. A spell crossing the player-side miss boundary without a correct counter damages the player and is removed.
- All spells in a volley share one world-space size-at-reference-distance value, randomized between volleys. Per-frame world size scales with distance to keep apparent angular size constant. Spell speed varies independently of depth and does not determine target order.
- `Δ` is normalized difficulty in `[0, 1]`: zero is easiest and maps to a 30% eye-to-boss depth span; one is hardest and maps to 5%. A clean volley raises `Δ` by `2 * staircaseStep`; a failed volley lowers it by `4 * staircaseStep`, with one update per completed volley and clamp to `[0, 1]`.
- A comfort break is prompted after about 10 minutes of play, lasts at least 10 seconds, resets `Δ` to zero, and resumes only after player confirmation.
- Keep assets simple until the core loop is fun. Lab mode is separate from normal play, uses bounded named variables and saveable presets, and logs the preset and effective settings. Normal mode uses curated defaults.

## Depth-band rule
Use Unity units as meters. Measure `eyeToBossDistance` along the viewing direction from the eye midpoint. Compute:

```text
depthSpan = lerp(0.30, 0.05, Δ) * eyeToBossDistance
nearDepth = minSpellDistanceMeters
farDepth = min(nearDepth + depthSpan, eyeToBossDistance - bossClearanceMeters)
```

If `eyeToBossDistance - bossClearanceMeters <= nearDepth`, do not spawn the volley; show a Lab/configuration error. If the valid interval is positive but narrower than the requested span, use the available interval and log the effective span.

## Tuning values, not design blockers
Expose and tune in Lab/headset tests: `staircaseStep`, `minSpellDistanceMeters`, `bossClearanceMeters`, player-side miss boundary, spell speed ranges, arrival gaps, shared acquisition volume, shield projection/padding, volley apparent size, and Lab slider ranges. The exact slash/block glyph assignment is configurable. Avoid inventing clinical claims or assuming that initial placeholder values are validated for comfort.

The shared acquisition volume's concrete geometry is intentionally unspecified. Start with a simple configurable volume covering the intended slash and shield interaction space; ensure its shape does not itself reveal depth ordering, then assess in headset.

## Suggested implementation sequence
1. Resolve the editor's `UnityOpenXR` desktop-run limitation if a clean desktop launch is required. The cleaned prototype can render and spawn spells in Game view, but console reports `DllNotFoundException: UnityOpenXR`; do not change project XR settings without checking intended headset deployment.
2. Refine `SpellCounterInput`: use a deliberate gesture signal instead of classifying slash/block only from velocity at trigger-down; verify physical sword orientation/reach and shield geometry in headset.
3. Validate shared-acquisition ordering against real slash/block contacts, including farther-contact order errors and moving spells crossing in depth.
4. Add boss tell/stagger/punish loop, health/damage feedback, and a HUD.
5. Add clean/failed volley staircase updates and comfort break state handling.
6. Add CSV session logging, then Lab UI and bounded preset persistence with effective values recorded per run.
7. Test on headset with one eye closed/head still, tune comfort-sensitive distances, hit volumes and speeds, and keep split-eye glyph/tell work and polished VFX out of scope unless the spec is deliberately revised.

## Code and workflow notes
- Prefer OpenXR/XR Interaction Toolkit patterns already used in the project.
- Add concise comments to non-obvious code blocks explaining intent; do not narrate self-evident statements.
- Keep each edit small and compiling. After modifying Unity scripts, check the Unity console for compilation errors before wiring new components.
- When Unity MCP is connected, inspect project/editor state before scene mutations and use the editor for scene/prefab work. Without it, write scripts under `Assets/Scripts/` and leave concise scene/prefab wiring notes.
- Do not commit changes unless explicitly requested.
