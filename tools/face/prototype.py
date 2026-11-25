"""16.1 prototype: run the MediaPipe Face Landmarker on one image and print what the plugin will use.

    tools/face/.venv/Scripts/python.exe tools/face/prototype.py --model <face_landmarker.task> --image <picture>

Dev tool only. It is not part of the plugin and the plugin never calls it.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import cv2  # noqa: E402

import face_core  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True)
    ap.add_argument("--image", required=True)
    ap.add_argument("--landmarks", action="store_true", help="also print all 478 landmarks")
    a = ap.parse_args()

    t0 = time.perf_counter()
    lm = face_core.make_landmarker(a.model)
    t1 = time.perf_counter()
    img = cv2.imread(a.image)
    if img is None:
        print("could not read", a.image)
        return 2
    t2 = time.perf_counter()
    res = face_core.analyse(lm, img)
    t3 = time.perf_counter()
    if not res["ok"]:
        print("NO FACE:", res["reason"])
        return 1
    print(f"yaw {res['yawDeg']:.1f}  pitch {res['pitchDeg']:.1f}  roll {res['rollDeg']:.1f}  confidence {res['confidence']}")
    top = sorted(res["blendshapes"].items(), key=lambda kv: -kv[1])[:8]
    print("blendshapes:", len(res["blendshapes"]), "top:", ", ".join(f"{k}={v:.2f}" for k, v in top))
    print("landmarks:", len(res["landmarks"]))
    print(f"load model {1000*(t1-t0):.0f} ms, analyse {1000*(t3-t2):.0f} ms")
    if a.landmarks:
        print(json.dumps(res["landmarks"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
