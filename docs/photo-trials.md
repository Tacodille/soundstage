# Photo-to-scene trials

Your step (plan 13.7). Pick ten photos you are allowed to use (your own, or ones you took), run each through the Photo
tab, apply the result and rate it 1 to 5 against the photo:

- 5: the light direction, softness and colours are what the photo shows.
- 3: the idea is right but something obvious is off (wrong side, wrong colour).
- 1: it does not look like the photo.

The photos stay out of the repository and out of this file. Only the rating and a line on what went wrong go here. The
ratings say how these ten went. They are not an accuracy figure for photos in general.

| # | Rating (1 to 5) | Provider and model | Closest Scene it picked | What went wrong |
|---|---|---|---|---|
| 1 | | | | |
| 2 | | | | |
| 3 | | | | |
| 4 | | | | |
| 5 | | | | |
| 6 | | | | |
| 7 | | | | |
| 8 | | | | |
| 9 | | | | |
| 10 | | | | |

## Tuning (plan 13.8)

After the ten are rated, look for a pattern (a side that is always flipped, a softness that reads too hard). The tables are
at the top of `src/Soundstage/Ai/PhotoToScene/Mapper.cs` (`PhotoTables`: direction to degrees, elevation, softness to cone)
and the wording is in `Prompt.cs`. Change them, then rerun the three worst photos and record before and after here.

| Photo # | Rating before | What was changed | Rating after |
|---|---|---|---|
| | | | |
| | | | |
| | | | |
