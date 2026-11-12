# UI layout: keeping things from overlapping

The UI is drawn with Unity's immediate-mode GUI: every element computes its own rectangle. That is quick to write
and has caused most of the reported UI bugs (cards on top of each other, buttons over text, texts leaving their
box). These rules and tools keep that in check.

## Rules for new UI code

1. **Stacks, not fixed positions.** Cards that can appear together share a column and take the next free place.
   On the ride screen the left column is `TakeLeft(width, height, out rect)` in `Hud.cs`; a card that does not fit
   is left out for now. Never give two optional cards the same fixed position.
2. **Sizes from content.** Button widths come from `Ui.ButtonWidth`; rows that must fit a width are scaled to it
   (see the summary screen's button row). Heights of pop-up cards are the sum of their parts.
3. **Text never leaves its box.** Use `Clip(style)` for single lines that may be long (names, titles), and shrink
   or wrap where every character matters (see the "next block" line of the workout box).
4. **Tall content scrolls.** If the content can exceed the screen (lists, summaries with climbs), put the middle
   in `Ui.BeginScroll` / `Ui.EndScroll` and pin headers and buttons outside it.
5. **Both languages, both shapes.** German texts are longer than English ones; phones are wide (about 2.2:1),
   tablets narrow (4:3, at least 1152 units wide). Lay out for the longer text and the narrower screen.

## The overlap check (`UiCheck.cs`)

In the editor and in development builds every card and button drawn in a frame is recorded. Afterwards:

- two cards may not intersect unless one contains the other;
- two buttons may not intersect;
- a button may not stick out of a card it touches.

Collisions are outlined in red on the screen and logged once as `UI overlap on <screen>: …` (Unity console /
device log; `UiCheck.Problems` holds them for tooling). Things inside scroll views are skipped, and whatever is
drawn after `UiCheck.Modal()` (dialogs, toasts) is on its own layer. A card that sticks out of another on purpose
is drawn with `Ui.Card(..., overlaps: true)`.

Not covered: text that is too long for its label (rule 3), and elements drawn with plain `Ui.Rect` or `GUI.Label`.

## Possible next steps

- A walk through the UI states (ride, workout, group ride, climb, junction, summary; German and English; phone and
  tablet shape) in the editor with screenshots and the list of `UiCheck.Problems`, using the render-shot tooling.
- A label helper that reports text wider than its rectangle.
- Moving screens to UI Toolkit (flexbox layout) would remove hand-computed rectangles altogether, but means
  rewriting the whole UI; not planned.
