# Body retargeting: where the pose comes from

Plan part 18. Measured 21/11/2026 on the development PC (Windows 11, Python 3.14.5, mediapipe 1.2.3). Where something was not measured it says so.

## 18.1 Go or no-go

Go. The run decision of 21/11/2026 says to build every part and not to skip this one as optional. Part 17's trial has not been run (its ratings in `docs/expression-trials.md` are empty), so the plan's own bar ("the bar is the expression result") is not measured. The body section ships with its own trial in the same file and the same rule: if the median of five ratings is under 3, it is labelled experimental.

## 18.2 Pose source: local host or cloud

| | Local (part 16's helper, Google Pose Landmarker) | Cloud vision model asked for joint positions |
|---|---|---|
| Extra download | one file, `pose_landmarker_full.task`, 9,398,198 bytes (9.0 MB), on top of the face helper | none |
| Output | 33 landmarks in the picture and 33 in metres (hip-centred), a visibility score each, and a person mask | a list of joints in words or numbers, whatever the prompt asks |
| Per picture | 28 to 30 ms in a script with landmarks only; 47 to 55 ms with the mask; 51 to 57 ms through the helper's own protocol after the first call; first call 170 ms (the model is made on first use) | one network call. Not measured: it needs the stored AI key and uploads the picture, which this run did not do |
| Picture leaves the machine | no | yes |
| Gives a score from the same loop | yes: the same model reads the game's screenshot | no, a second paid call per pass |

Five public-domain NASA portraits (Wikimedia Commons, full length, 960 pixels wide): all 33 landmarks reported visible on all five. Visibility is the model's own figure and it also says "visible" for joints it has guessed, so it is not evidence of accuracy.

There is no ground truth, so the consistency checks are limb lengths, which one person keeps equal left to right (MediaPipe's world landmarks are not forced to be symmetric):

| Photo | Upper arm L / R (m) | Forearm L / R | Thigh L / R | Shin L / R |
|---|---|---|---|---|
| 1 | 0.246 / 0.254 | 0.205 / 0.206 | 0.391 / 0.418 | 0.373 / 0.428 |
| 2 | 0.246 / 0.248 | 0.199 / 0.200 | 0.392 / 0.395 | 0.373 / 0.432 |
| 3 | 0.242 / 0.242 | 0.211 / 0.207 | 0.390 / 0.409 | 0.377 / 0.428 |
| 4 | 0.238 / 0.226 | 0.245 / 0.225 | 0.417 / 0.395 | 0.356 / 0.369 |
| 5 | 0.234 / 0.220 | 0.232 / 0.211 | 0.413 / 0.395 | 0.355 / 0.370 |

Arms agree within 6 percent. Shins in photos 1 to 3 differ by up to 15 percent. Only directions are used (not lengths), so this limits how much to trust one leg, not the method.

Frame, measured from the replies (shoulder to hip y was -0.45 to -0.47 m on all five; left shoulder minus right shoulder x was +0.33 to +0.35 m and the picture x difference was +0.20 to +0.25; the nose z was 0.20 to 0.21 m nearer the camera than the shoulder midpoint): x toward the picture's right, y down, z away from the camera, origin at the hip centre. The person's own left is at +x when they face the camera.

Outline score (same code as the plugin: bounding-box-normalised 64 by 64 grid, overlap times box aspect agreement), between the five photos:

| Pair | Score (of 100) |
|---|---|
| Same person, same pose, three shots (1, 2, 3) | 91.4, 91.4, 94.7 |
| Same person, two shots (4, 5) | 98.1 |
| Different person and pose (1 to 4, 1 to 5, 2 to 4 ...) | 49.2 to 49.5 |
| Each photo against its own mirror image | 59.3 to 69.7 |
| Each photo against itself | 100.0 |

So the outline score separates a matching pose from a different one by about 40 points, and a mirror image by about 30. It is a coarse measure: a pose that only moves arms inside the torso's outline will barely change it, which is why the loop also reports the joint direction error.

Choice: local. It is the only option that was measured, it keeps the photo on the computer, and it gives the game-side reading the loop needs. The cloud option is recorded as not measured, not as worse.

## Measured how

`tools/face/pose_measure.py --model <pose_landmarker_full.task> <photos>` (landmarks, timings, frame, limb lengths). The scores were made with `face_core.analyse_pose` on the same five photos. Both run in a throwaway venv (mediapipe 1.2.3, numpy, opencv-python-headless) in a scratch folder, deleted afterwards. The photos were downloaded from Wikimedia Commons into the same folder and deleted with it; none is kept in the repository. `tests/Soundstage.SelfCheck/TestData/body-recorded.json` holds the helper's replies (landmark numbers and outline bits, no pictures) for photos 1 and 4.
