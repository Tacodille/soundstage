"""Face analysis shared by the prototype, the symmetry test and the helper the plugin runs.

One function does the work: analyse(landmarker, bgr_image) -> dict. The dict is the same shape the plugin parses
(yawDeg, pitchDeg, rollDeg, confidence, blendshapes{name: score}, landmarks [[x, y, z]...] in 0..1 image units).

Angle convention (fixed here, checked by the symmetry test and by Face/FaceMath.cs):
  yaw   > 0  the face turns toward the RIGHT side of the picture (the subject's left cheek leads).
  pitch > 0  the face tilts UP (chin lifts, looking above the lens).
  roll  > 0  the head tips toward the right side of the picture (the top of the head moves right).
"""
import math

import numpy as np

MODEL_NAME = "face_landmarker.task"


def make_landmarker(model_path):
    from mediapipe.tasks import python as mp_python
    from mediapipe.tasks.python import vision

    opts = vision.FaceLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path=model_path),
        running_mode=vision.RunningMode.IMAGE,
        num_faces=1,
        output_face_blendshapes=True,
        output_facial_transformation_matrixes=True,
    )
    return vision.FaceLandmarker.create_from_options(opts)


def angles_from_matrix(m):
    """yaw, pitch, roll in degrees from the 4x4 facial transformation matrix, in the convention above."""
    r = np.asarray(m, dtype=float).reshape(4, 4)[:3, :3]
    # MediaPipe's canonical face is y up, z toward the viewer, in a right-handed frame whose x points to the
    # image right. Column 2 of r is where the face's front points; column 1 is where the top of the head points.
    fwd = r[:, 2]
    up = r[:, 1]
    # In camera space +x is the image right, +y is up, +z is toward the viewer.
    yaw = math.degrees(math.atan2(fwd[0], fwd[2]))
    pitch = math.degrees(math.asin(max(-1.0, min(1.0, fwd[1]))))
    roll = math.degrees(math.atan2(up[0], up[1]))
    return yaw, pitch, roll


def analyse(landmarker, bgr):
    """The first face's result as a dict, or {"ok": false, "reason": ...}."""
    import mediapipe as mp

    rgb = bgr[:, :, ::-1].copy()
    image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
    res = landmarker.detect(image)
    if not res.face_landmarks:
        return {"ok": False, "reason": "No face found in the picture."}
    lm = res.face_landmarks[0]
    out = {"ok": True, "landmarks": [[round(p.x, 5), round(p.y, 5), round(p.z, 5)] for p in lm]}
    if res.facial_transformation_matrixes:
        yaw, pitch, roll = angles_from_matrix(res.facial_transformation_matrixes[0])
        out.update(yawDeg=round(yaw, 3), pitchDeg=round(pitch, 3), rollDeg=round(roll, 3))
    else:
        return {"ok": False, "reason": "The model gave no head pose for this picture."}
    bs = {}
    if res.face_blendshapes:
        for c in res.face_blendshapes[0]:
            bs[c.category_name] = round(float(c.score), 5)
    out["blendshapes"] = bs
    # The landmarker gives no face score. Confidence here is a framing figure, 0 to 1: how much of the picture the face
    # fills (square root of the landmark box area over 0.09, capped). A small face in a big picture reads low.
    xs = [p.x for p in lm]
    ys = [p.y for p in lm]
    fill = (max(xs) - min(xs)) * (max(ys) - min(ys))
    out["confidence"] = round(min(1.0, math.sqrt(max(fill, 0.0) / 0.09)), 3)
    return out


# ---- Part 18: body pose ----------------------------------------------------------------------------------------
POSE_MODEL_NAME = "pose_landmarker_full.task"
POSE_NAMES = [
    "nose", "left_eye_inner", "left_eye", "left_eye_outer", "right_eye_inner", "right_eye", "right_eye_outer", "left_ear", "right_ear",
    "mouth_left", "mouth_right", "left_shoulder", "right_shoulder", "left_elbow", "right_elbow", "left_wrist", "right_wrist",
    "left_pinky", "right_pinky", "left_index", "right_index", "left_thumb", "right_thumb", "left_hip", "right_hip", "left_knee",
    "right_knee", "left_ankle", "right_ankle", "left_heel", "right_heel", "left_foot_index", "right_foot_index",
]


def make_pose_landmarker(model_path):
    from mediapipe.tasks import python as mp_python
    from mediapipe.tasks.python import vision

    opts = vision.PoseLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path=model_path),
        running_mode=vision.RunningMode.IMAGE,
        num_poses=1,
        output_segmentation_masks=True,
    )
    return vision.PoseLandmarker.create_from_options(opts)


MASK_SIZE = 64


def silhouette(mask):
    """A person mask (float, 0..1, any size) as a MASK_SIZE x MASK_SIZE bit grid of its own bounding box, base64 of packed bits, plus the box's
    width / height ratio and the share of the picture the person covers. None when nothing is there. Comparing two of these ignores where the
    person stands and how big they are in the frame, which differ between a photo and a game screenshot."""
    import base64

    m = np.squeeze(np.asarray(mask, dtype=np.float32)) > 0.5
    if not m.any():
        return None
    ys, xs = np.where(m)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    crop = m[y0:y1, x0:x1].astype(np.uint8) * 255
    import cv2

    small = cv2.resize(crop, (MASK_SIZE, MASK_SIZE), interpolation=cv2.INTER_AREA) > 127
    return {
        "bits": base64.b64encode(np.packbits(small.reshape(-1)).tobytes()).decode("ascii"),
        "size": MASK_SIZE,
        "aspect": round(float((x1 - x0) / (y1 - y0)), 4),
        "cover": round(float(m.mean()), 4),
    }


def analyse_pose(landmarker, bgr):
    """The first body's 33 landmarks as a dict, or {"ok": false, "reason": ...}.

    "image" is x, y (0..1 across the picture, y down), z (relative depth), visibility. "world" is metres with the origin at the hip centre
    (MediaPipe's own frame: x toward the picture's right, y DOWN, z away from the camera is positive), plus visibility.
    """
    import mediapipe as mp

    rgb = bgr[:, :, ::-1].copy()
    image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
    res = landmarker.detect(image)
    if not res.pose_landmarks or not res.pose_world_landmarks:
        return {"ok": False, "reason": "No body found in the picture."}
    img = [[round(p.x, 5), round(p.y, 5), round(p.z, 5), round(p.visibility or 0.0, 4)] for p in res.pose_landmarks[0]]
    world = [[round(p.x, 5), round(p.y, 5), round(p.z, 5), round(p.visibility or 0.0, 4)] for p in res.pose_world_landmarks[0]]
    out = {"ok": True, "image": img, "world": world}
    if res.segmentation_masks:
        sil = silhouette(res.segmentation_masks[0].numpy_view())
        if sil is not None:
            out["silhouette"] = sil
    return out
