"""The face helper the plugin starts as a separate process (Option B in docs/face-model-decision.md).

Protocol, one JSON object per line on stdin and stdout, nothing else on stdout:
  helper -> plugin   {"ready": true, "version": "1"}                         once the model is loaded
  plugin -> helper   {"id": 7, "image": "C:\\path\\to\\picture.png"}          analyse one picture
  helper -> plugin   {"id": 7, "ok": true, "yawDeg": ..., ... }              the face_core.analyse dict, plus the id
  plugin -> helper   {"id": 8, "image": "...", "mode": "pose"}               body pose instead of the face (needs --pose-model)
  helper -> plugin   {"id": 8, "ok": true, "image": [[x,y,z,vis]...33], "world": [[x,y,z,vis]...33]}
  plugin -> helper   {"quit": true}  (or close stdin)                        exit
The picture is read from disk and never sent anywhere. The helper opens no sockets.

    python helper.py --model <face_landmarker.task>
"""
import argparse
import json
import os
import sys

HELPER_VERSION = "1"
POSE_LANDMARKER = None   # built on first body request


def out(obj):
    sys.stdout.write(json.dumps(obj, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True)
    ap.add_argument("--pose-model", default="")   # Part 18, optional: the body pose model, downloaded on its own
    a = ap.parse_args()

    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    try:
        import cv2
        import numpy as np

        import face_core

        landmarker = face_core.make_landmarker(a.model)
    except Exception as e:  # the plugin shows this sentence; it never sees a traceback
        out({"ready": False, "reason": f"The face helper could not start: {type(e).__name__}: {e}"})
        return 3
    out({"ready": True, "version": HELPER_VERSION})

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
        except ValueError:
            out({"id": None, "ok": False, "reason": "The helper was sent something that is not JSON."})
            continue
        if req.get("quit"):
            return 0
        rid = req.get("id")
        if req.get("mode") == "pose":
            try:
                if not a.pose_model or not os.path.exists(a.pose_model):
                    out({"id": rid, "ok": False, "reason": "The body pose model is not downloaded yet."})
                    continue
                global POSE_LANDMARKER
                if POSE_LANDMARKER is None:
                    POSE_LANDMARKER = face_core.make_pose_landmarker(a.pose_model)
                data = np.fromfile(req["image"], dtype=np.uint8)
                img = cv2.imdecode(data, cv2.IMREAD_COLOR) if data.size else None
                if img is None:
                    out({"id": rid, "ok": False, "reason": "The helper could not read that picture."})
                    continue
                res = face_core.analyse_pose(POSE_LANDMARKER, img)
                res["id"] = rid
                out(res)
            except Exception as e:
                out({"id": rid, "ok": False, "reason": f"{type(e).__name__}: {e}"})
            continue
        try:
            data = np.fromfile(req["image"], dtype=np.uint8)
            img = cv2.imdecode(data, cv2.IMREAD_COLOR) if data.size else None
            if img is None:
                out({"id": rid, "ok": False, "reason": "The helper could not read that picture."})
                continue
            res = face_core.analyse(landmarker, img)
            res["id"] = rid
            out(res)
        except Exception as e:
            out({"id": rid, "ok": False, "reason": f"{type(e).__name__}: {e}"})
    return 0


if __name__ == "__main__":
    sys.exit(main())
