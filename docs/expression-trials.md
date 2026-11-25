# Expression trials (Part 17.8)

Your step. Calibrate the face (checklist area "Part 17: Expression", step 3), then match five photos (step 9). After each match the tab shows the model's score and has five buttons to rate how recognisable the result is. Copy both into the table.

The score is the face model's own number (100 x (1 - weighted mean error / 0.5)). It says how close the blendshapes are. It is not a judgement of likeness, which is what the 1 to 5 rating is for.

Ratings: 1 not at all, 2 a bit, 3 recognisable, 4 good, 5 spot on.

**Rule:** if the median of the five ratings is under 3, the Expression tab labels itself EXPERIMENTAL and hides its controls behind a tick box. The tab works this out from the ratings it records itself; this file is the written record.

| # | Date | Photo (describe it, no filename or person) | Score before | Score after | Rating (1 to 5) | Notes |
|---|------|--------------------------------------------|--------------|-------------|-----------------|-------|
| 1 |      |                                            |              |             |                 |       |
| 2 |      |                                            |              |             |                 |       |
| 3 |      |                                            |              |             |                 |       |
| 4 |      |                                            |              |             |                 |       |
| 5 |      |                                            |              |             |                 |       |

**Median rating:** not yet measured.

**Calibration:** face key, time taken, bone moves measured, any bone that distorted the face: not yet measured.

**Verdict:** not yet measured. (Experimental or not, per the rule above.)

---

# Body pose trials (Part 18.6)

Your step: checklist area "Part 18: Body pose", step 11. Match five full-length photos on the Expression tab's body section. The tab shows the outline score and five buttons for the rating.

The score is how well the outline of the game's picture overlaps the photo's, after both are cut to their own bounding box and compared on a 64 by 64 grid, times how alike the boxes' proportions are. A pose that only moves arms inside the torso's outline barely changes it, so the average limb direction error (also shown) is recorded too. Neither is a judgement of likeness: the 1 to 5 rating is.

**Rule:** same as the face. If the median of the five ratings is under 3, the body section labels itself EXPERIMENTAL and hides its controls behind a tick box.

| # | Date | Photo (describe it, no filename or person) | Outline score before | Outline score after | Limb error before / after (deg) | Rating (1 to 5) | Notes |
|---|------|--------------------------------------------|----------------------|---------------------|----------------------------------|-----------------|-------|
| 1 |      |                                            |                      |                     |                                  |                 |       |
| 2 |      |                                            |                      |                     |                                  |                 |       |
| 3 |      |                                            |                      |                     |                                  |                 |       |
| 4 |      |                                            |                      |                     |                                  |                 |       |
| 5 |      |                                            |                      |                     |                                  |                 |       |

**Median rating:** not yet measured.

**Body map check:** missing bones, segments that did not fit, rest lines from the log: not yet measured.

**Verdict:** not yet measured. (Experimental or not, per the rule above.)
