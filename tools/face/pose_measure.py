"""Part 18 step 18.2: measure the local body pose model on photos. Dev tool, prints numbers only.

    pose_measure.py --model <pose_landmarker_full.task> <photo> [<photo> ...]

For each photo: time per call (after one warm-up), how many of the 33 landmarks are visible (>= 0.5), the world-frame axis directions
(so the solver's convention is measured, not assumed), and left/right limb-length ratios (a pose with no ground truth can still be
checked for anatomical consistency: the two upper arms of one person should be about as long as each other).
"""
import argparse
import math
import sys
import time

import cv2
import numpy as np

sys.path.insert(0, __import__("os").path.dirname(__import__("os").path.abspath(__file__)))
import face_core  # noqa: E402

N = {n: i for i, n in enumerate(face_core.POSE_NAMES)}


def d(w, a, b):
    return math.dist(w[N[a]][:3], w[N[b]][:3])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True)
    ap.add_argument("photos", nargs="+")
    a = ap.parse_args()
    t0 = time.perf_counter()
    lm = face_core.make_pose_landmarker(a.model)
    print(f"model load {1000*(time.perf_counter()-t0):.0f} ms")
    img0 = cv2.imdecode(np.fromfile(a.photos[0], dtype=np.uint8), cv2.IMREAD_COLOR)
    face_core.analyse_pose(lm, img0)  # warm-up
    for p in a.photos:
        img = cv2.imdecode(np.fromfile(p, dtype=np.uint8), cv2.IMREAD_COLOR)
        ts = []
        for _ in range(5):
            t = time.perf_counter()
            r = face_core.analyse_pose(lm, img)
            ts.append(1000 * (time.perf_counter() - t))
        if not r["ok"]:
            print(p, "no body", r["reason"]); continue
        w = r["world"]; im = r["image"]
        vis = sum(1 for q in w if q[3] >= 0.5)
        sh = [(w[N["left_shoulder"]][i] + w[N["right_shoulder"]][i]) / 2 for i in range(3)]
        hp = [(w[N["left_hip"]][i] + w[N["right_hip"]][i]) / 2 for i in range(3)]
        print(f"{p.split(chr(92))[-1].split('/')[-1]}: {img.shape[1]}x{img.shape[0]}  {np.median(ts):.0f} ms  visible {vis}/33")
        print(f"   shoulder-hip y {sh[1]-hp[1]:+.3f} (negative = y points down)  left-right shoulder x {w[N['left_shoulder']][0]-w[N['right_shoulder']][0]:+.3f}  image x left-right {im[N['left_shoulder']][0]-im[N['right_shoulder']][0]:+.3f}")
        print(f"   upper arm L/R {d(w,'left_shoulder','left_elbow'):.3f}/{d(w,'right_shoulder','right_elbow'):.3f}  forearm {d(w,'left_elbow','left_wrist'):.3f}/{d(w,'right_elbow','right_wrist'):.3f}  thigh {d(w,'left_hip','left_knee'):.3f}/{d(w,'right_hip','right_knee'):.3f}  shin {d(w,'left_knee','left_ankle'):.3f}/{d(w,'right_knee','right_ankle'):.3f}")
        sil = r.get("silhouette")
        print(f"   silhouette {'none' if not sil else str(sil['aspect']) + ' aspect, ' + str(sil['cover']) + ' of the picture'}")
        print(f"   torso length {math.dist(sh,hp):.3f} m, nose z {w[N['nose']][2]:+.3f} shoulder z {sh[2]:+.3f}")


if __name__ == "__main__":
    main()
