# Redrawing the 2D car diagram from a dimensioned blueprint

## Why

`Pages/Shared/_CarDiagram.cshtml` is hand-drawn: five SVG views, ~90 clickable
`data-part` regions, all coordinates placed by eye. It works, but the shapes are
measurably wrong, and the errors are the kind a panel beater notices:

- The side view is drawn at **2.51:1** where a real sedan is **3.35:1** — the car is
  about 25% too stubby.
- The wheelbase is **227 units** where the same drawing's own overhangs imply **266** —
  ~15% short, which is why the wheels sit visibly too far inboard.
- Each view was drawn independently, so panel boundaries (where the front door ends and
  the rear door begins, where the wing meets the door) are guesses rather than shutlines.

The fix is to stop eyeballing and derive every coordinate from published dimensions.

## Source

A dimensioned BMW M5 (F10, 2012) general-arrangement blueprint, chosen over the other
three references supplied (Audi S6 Avant, Mercedes CLS, Ferrari FF) because it is the
only one carrying all four elevations at a single scale *and* printing its own numbers.

**Shape is traced, styling is not.** The diagram stays brand-neutral: plain rounded
grille openings rather than kidneys, generic lamp shapes, no badges. This is a workshop
that services every make; the drawing should read as "a car", not "a BMW".

| Dimension | mm |
|---|---|
| Overall length | 4910 |
| Front overhang | 836 |
| Wheelbase | 2964 |
| Rear overhang | 1110 |
| Overall height | 1467 |
| Body width | 1891 |
| Width across mirrors | 2119 |
| Front track | 1627 |
| Rear track | 1582 |

Consistency check: 836 + 2964 + 1110 = 4910. ✓

## A decision that got reversed by thinking it through

The obvious move is to give all four orbit views one shared units-per-mm scale, so the
car doesn't change size as you rotate front → left → rear → right.

**Rejected.** `.car-view` is `width: 100%`, so every view renders at the same on-screen
width regardless of its viewBox. Sharing a scale would mean the front view (1891mm wide)
occupies ~37% of the frame that the side view (4910mm long) fills — visually honest, and
actively worse to use. The front view now carries 19 hotspots including the nested
grille/spotlamp cluster; shrinking it to a third of the frame makes the smallest of those
genuinely hard to hit.

This is a tagging tool before it is an illustration. **Each view fills its own frame**
(current behaviour, retained); the blueprint's contribution is correct proportions and
correct panel boundaries *within* each view.

## Derived geometry

Two scales, both chosen so the car nearly fills its frame.

### Side views — `viewBox="0 0 460 165"`, scale **0.09 units/mm**

Car spans x = 9 (nose) → 451 (tail); ground line y = 150; roof y = 18.

| Landmark | mm from nose | x |
|---|---|---|
| Nose / front bumper leading edge | 0 | 9 |
| Front bumper ↔ bonnet shut | 700 | 72 |
| **Front axle** | 836 | 84 |
| Front door leading edge (A-pillar base) | 1450 | 140 |
| Cowl / windscreen base | 1900 | 180 |
| A-pillar top / roof front | 2500 | 234 |
| B-pillar (front ↔ rear door) | 2550 | 238 |
| Roof rear / C-pillar top | 3450 | 320 |
| Rear door trailing edge | 3400 | 315 |
| **Rear axle** | 3800 | 351 |
| Rear screen base / boot leading edge | 3950 | 364 |
| Boot ↔ rear bumper shut | 4700 | 432 |
| Tail | 4910 | 451 |

| Landmark | mm from ground | y |
|---|---|---|
| Ground | 0 | 150 |
| Sill underside | 250 | 128 |
| Wheel centre | 335 | 120 |
| Bumper mid-height | 500 | 105 |
| Headlight centre | 800 | 78 |
| Bonnet top | 1000 | 60 |
| Beltline (glass base) | 1150 | 47 |
| Roof | 1467 | 18 |

Wheel arch radius 34 (380mm); tyre radius 30 (≈670mm overall diameter); rim radius 13.

### Front / rear views — `viewBox="0 0 290 215"`, scale **0.1321 units/mm**

Centreline x = 145; ground y = 205; roof y = 11.

| Landmark | mm | units | position |
|---|---|---|---|
| Body width | 1891 | 250 | x 20 → 270 |
| Across mirrors | 2119 | 280 | x 5 → 285 |
| Overall height | 1467 | 194 | y 11 → 205 |
| Front track | 1627 | 215 | wheel centres x 37.5 / 252.5 |
| Rear track | 1582 | 209 | wheel centres x 40.5 / 249.5 |

### Top view — `viewBox="0 0 200 460"`, scale **0.09 units/mm**

Portrait, nose up — retained as-is, and deliberately exempt from the "fills its frame"
rule because CSS already sizes it by `max-height`, not width. Sharing the side view's
0.09 scale is a free consistency win.

| Landmark | mm | units |
|---|---|---|
| Length | 4910 | 442 (y 9 → 451) |
| Body width | 1891 | 170 (x 15 → 185) |
| Across mirrors | 2119 | 191 (x 4.5 → 195.5) |

## What changes, and what explicitly does not

**Changes:** the SVG path/rect/circle geometry inside `_CarDiagram.cshtml`. That is all.

**Unchanged, by design:**

- Every `data-part` value — same 90 hotspots, same names, same count. This is a redraw,
  not a re-scope.
- Every CSS class (`.car-body`, `.cp`, `.car-detail`, `.car-glass`, `.car-handle`,
  `.car-tyre`, `.car-rim`, `.car-arch`). Coverage shading, selection, and the damage-mark
  `mark` mode therefore keep working with zero JS or CSS edits.
- `Models/Part.cs`, `wwwroot/js/cardiagram.js`, `wwwroot/js/damagemarks.js`,
  `wwwroot/models/car.glb`, `tools/car-model/build_car.py`. The 3D model and the
  damage-marking feature are finished and deployed; nothing here touches them.
- The right-side view stays a mirrored transform of the left
  (`translate(460,0) scale(-1,1)`) with L/R part names swapped — half the work, and the
  two sides cannot drift apart.

## Front-end structure

The one place the blueprint earns its keep beyond proportions. The M5's front already has
the exact nesting the 16 new parts needed, so these stop being invented rectangles:

```
FrontBumper
├── MainGrill              upper opening
├── CentreGrill            wide lower centre opening
├── BumperGrillFrontLeft   outer lower opening
│   └── SpotlampGrillLeft
│       └── SpotlampLeft
├── BumperGrillFrontRight  outer lower opening
│   └── SpotlampGrillRight
│       └── SpotlampRight
└── FrontSpoiler           lower lip
```

Nesting resolves by SVG document order (later sibling wins the click), which is already
how the current front view works and was verified by real hit-testing.

## Build order

Each step leaves the diagram fully working:

1. Left side + right side (mirrored)
2. Front
3. Rear
4. Top

## Verification

The standard applied to the rest of this system — not "it rendered".

- **No lost hotspots.** Diff the full sorted list of `data-part` values against the
  current file; count must stay 90. A typo that silently drops a region is the main
  regression risk here, and it is invisible to the eye.
- **Real hit-testing**, not synthetic dispatch: `document.elementFromPoint()` at computed
  screen coordinates for each region, so paint order and occlusion are genuinely
  exercised. Dispatching a click straight at an element would pass regardless of whether
  the layering actually resolves — the trap already documented for the 3D picker.
- **Nested front-end cluster specifically**: confirm Spotlamp is reachable inside
  SpotlampGrill inside BumperGrillFront, and CentreGrill inside MainGrill.
- **Cross-view consistency**: a part drawn in several views (roof appears in four) must
  still light up in all of them at once — the built-in drift detector.
- **Render and look**, iterating on proportions rather than accepting the first pass.
