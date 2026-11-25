# Capture route notes (Part 12)

Which way the plugin gets a PNG of the game view, in the order it tries them. The plan asks for each route to be measured for two
things: does the file contain the Dalamud overlay, and how long it takes to reach disk. Both need the game running, and this build
was made without it (run decision, 21/11/2026), so those cells read **NOT RUN** until you run the checklist area "Part 12: Workflow"
(`/soundstage test`, steps 1 to 3). The expectations in the table are reasoning, not measurements, and are marked as such.

## Route table

| # | Route (id) | How it works | Contains the Dalamud overlay? | Time to file | Measured |
|---|---|---|---|---|---|
| 1 | ReShade add-on `shot` (`addon`) | Plugin sends `shot <path>` over the pipe; the add-on reads the back buffer with `capture_screenshot` and writes the PNG itself. | Expected: depends on whether ReShade's present hook runs before Dalamud draws its overlay. If Dalamud draws first the overlay is in the file. The plugin hides its own overlay and (optionally) the game UI either way. | Expected under about 200 ms (one frame plus a PNG write). | NOT RUN (checklist step 1) |
| 2 | Game screenshot key (`key`) | Plugin taps the screenshot key (default Print Screen), watches the screenshot folder for a new file, converts it to PNG. | Expected: no overlay for the game's own screenshot (it reads the game's render target); ReShade's own screenshot on the same key follows the back buffer rule above. | Expected 300 to 1000 ms (poll interval 120 ms plus a settle wait of 150 ms). | NOT RUN (checklist step 2) |
| 3 | Window capture (`window`) | Copies the game window's client area off the screen with `Graphics.CopyFromScreen`. | Yes: it copies what is on screen, so the overlay, the game UI and anything on top of the window are in it. Last resort. | Expected under 100 ms. | NOT RUN (checklist step 3) |

The default order is 1, 2, 3, and the Workflow tab can reorder or switch routes off. The verdict goes here once you have run the steps: if
route 1 shows the overlay in the file, the booth already hides every Soundstage overlay and the Scroll Lock hide-UI covers Dalamud's own
windows, so the shot is still clean; the table row just records which hiding the route needs.

## What was measured in this build

Offline only (self-check area "Workflow (Part 12)", synthetic images, fake routes):

- Fallback order: failing route, then a working route, then a third that never runs. Calls recorded as `addon,key`.
- A route that returns a file which is not an image is rejected and the next route runs.
- A route that ignores its cancellation token is abandoned after the timeout (300 ms in the check) and the next route runs.
- A route that throws is reported in the result, not thrown, and nothing is left at the final file name.
- The finished file is written under a `.part.png` name and renamed, so a half-written file never has the final name.

## Why the "key" route is a fallback, not the main path

Run decision (21/11/2026): the add-on `shot` route is the main path. The key route and window route stay as runtime fallbacks, so if the
pipe does not answer (add-on not installed, or the game not restarted since it was copied in) a capture still works and the Workflow tab
status line says which route took the shot.

## Overlay hiding, route by route

Whatever the route, a capture hides in this order and restores in reverse, even if the capture throws:

1. The Soundstage windows (they skip drawing while `WorkflowState.UiHidden` is set).
2. Light markers (the existing "show light markers" setting is cleared and put back; it is never saved while hidden).
3. The composition overlay (thirds, golden ratio, centre cross, safe margin: the four switches are cleared and put back).
4. The onion skin (it does not draw while a capture is hiding things).
5. The game UI, by sending Scroll Lock, only if it was not already hidden and only if "Hide the game UI" is ticked. Scroll Lock is sent
   again afterwards only if the plugin sent it the first time.

## Not done

- No game, so no capture was taken and no timing was measured. Do not read the "Expected" cells as results.
- The add-on `shot` command was built in Part 3 and has not been loaded in the game yet (Part 3 checklist, steps 1 and 5).
