# REDZONE — Project Plan (Entertainment track)

Cinematic redzone "pure touchdown experience" for Meta Quest. Broadcast-style
presentation → wipe transition → first-person passing moment. The hands-first
throw is the interactive beat. No popup notes, no companion data, no game
systems (no playbook, downs, score, or drive logic) — every moment is a
touchdown waiting to happen.

## Design language
- **Blue** = a run (RB) is the probable play → broadcast-style cinematic moment.
- **Red** = passing → YOUR throw, first-person.
- Flow per scenario: broadcast card (color wash) → A/Space takes the field →
  wipe → pre-snap → snap → moment → TOUCHDOWN → next scenario.

## P0 — Experience scaffold
- [x] Unity project structure, package manifest, gitignore
- [x] Procedural regulation field (FieldBuilder)
- [x] QB throw mechanics from hand velocity: grab + throw, spiral, trail (Football)
- [x] 4 route-runners + man-coverage defenders + pass rusher (RouteRunner, DefenderAI)
- [x] ScenarioSet: 4 redzone moments, color-coded (3 red / 1 blue)
- [x] ExperienceDirector: Broadcast → Wipe → PreSnap → Live → Celebration
- [x] BroadcastPresenter: jumbotron scenario card, signature wipe, touchdown splash
- [x] Blue-scenario handoff cinematic (Football.HandTo — the run is destiny)
- [x] One-click scene builder + Quest build-settings menu (SetupNFLSim)
- [ ] Compile in Unity 6000.3.25f1 + Meta XR All-in-One SDK v81 on the dev machine, fix any API drift
- [ ] First on-device playtest (Quest 3): tune throw feel + wipe timing

## P1 — Atmosphere (this IS the product)
- Spatial crowd audio: murmur under broadcast, swell on the snap, eruption on touchdown
- Commentary lines per scenario (`broadcastLine`) — TTS or recorded
- Stadium shell: stands, jumbotron (the broadcast card lives there), tunnel, night lighting
- Haptics: snap thump, throw release tick, touchdown rumble
- Broadcast graphics polish: lower-third cards, color stingers on the wipe

## P2 — Submission
- Playable Quest build (APK) with one-tap sideload instructions — judges can't
  score what they can't launch
- Short video: capture the wipe → throw → touchdown beat
- Submission form

## Decisions (locked)
1. **Entertainment track: redzone pure-touchdown experience** (2026-10-06) —
   replaces the QB passing game. Coach sim / 11v11 are gone, not deferred.
2. **Game systems stripped** (2026-10-06): no playbook, downs, score, turnovers.
   A failed moment just restages ("run it back").
3. **Color language** (2026-10-06): blue = run (RB probable), red = pass (your throw).
4. **Signature flow** (2026-10-06): broadcast presentation → wipe → first-person
   red passing moment. The world re-dresses behind the wipe.
5. **Meta XR All-in-One SDK v81+ (v207)** (2026-10-06).
6. **Small-sided: 5 defenders** (2026-10-04, carried over) — keep it tight.

## Open decisions (need your call)
1. **Team**: default colors are black & gold — keep, or pick?
2. **Scale**: `UnitsPerYard = 0.6` arcade scale vs true 1:1 field (throwing gets much harder).
