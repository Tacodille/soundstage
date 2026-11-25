"""Read-only: bounding box of housing furniture models from the installed game. Prints numbers only.

    measure_mdl.py <model key> [<model key> ...] [--json out.json]

Model path pattern: bgcommon/hou/indoor/general/NNNN/bgparts/fun_b0_mNNNN.mdl (LOD0, every mesh).
Prints per model: width (X extent), height (Y extent), depth (Z extent) in metres at scale 1, and the min/max corner,
so the pivot offset is visible: pivot_x = -(min_x+max_x)/2 says how far the middle of the model sits from its origin.
Uses the existing read-only readers (sqpack.py, mdl6.py). Never writes under the game folder.

Needs the readers sqpack.py and mdl6.py on the folders named in SOUNDSTAGE_PYLIBS (separated like PATH).
"""
import json
import os
import sys

for _p in os.environ.get("SOUNDSTAGE_PYLIBS", "").split(os.pathsep):
    if _p:
        sys.path.insert(0, _p)
import sqpack  # noqa: E402
import mdl6  # noqa: E402
import numpy as np  # noqa: E402

sq = sqpack.SqPack(sqpack.DEFAULT_GAME)


def model_path(key):
    return f"bgcommon/hou/indoor/general/{key:04d}/bgparts/fun_b0_m{key:04d}.mdl"


def measure(key):
    data, _ = sq.read(model_path(key))
    m = mdl6.parse(data)
    lod0 = m["lods"][0]
    first, count = lod0[0], lod0[1]  # mesh_index, mesh_count of LOD0
    lo = [1e9, 1e9, 1e9]
    hi = [-1e9, -1e9, -1e9]
    verts = 0
    per_mesh = []
    all_pos = []
    for k in range(first, first + count):
        pos = mdl6.read_attr(data, m, k, 0)
        if pos is None or not len(pos):
            continue
        verts += len(pos)
        all_pos.append(pos)
        per_mesh.append({"verts": len(pos), "min": [round(float(pos[:, a].min()), 4) for a in range(3)],
                         "max": [round(float(pos[:, a].max()), 4) for a in range(3)]})
        for a in range(3):
            lo[a] = min(lo[a], float(pos[:, a].min()))
            hi[a] = max(hi[a], float(pos[:, a].max()))
    # Which way the model faces: the mean Z of the top third of the vertices (a chair or couch back) against the
    # middle of the box. Negative means the tall part sits at -Z, so the open front is at +Z. Only meaningful for
    # things with a back; for symmetrical models it is near 0.
    cat = np.concatenate(all_pos) if all_pos else np.zeros((0, 3))
    top_bias = 0.0
    if len(cat) and hi[1] > lo[1]:
        top = cat[cat[:, 1] > lo[1] + (hi[1] - lo[1]) * 0.66]
        if len(top):
            top_bias = float(top[:, 2].mean()) - (lo[2] + hi[2]) / 2
    return {
        "per_mesh": per_mesh,
        "top_bias_z": round(top_bias, 4),
        "key": key,
        "meshes": count,
        "verts": verts,
        "min": [round(v, 4) for v in lo],
        "max": [round(v, 4) for v in hi],
        "width": round(hi[0] - lo[0], 4),
        "height": round(hi[1] - lo[1], 4),
        "depth": round(hi[2] - lo[2], 4),
        "centre": [round((lo[a] + hi[a]) / 2, 4) for a in range(3)],
    }


def main(argv):
    out_path = None
    argv = [a for a in argv if a != "--meshes"]
    if "--json" in argv:
        i = argv.index("--json")
        out_path = argv[i + 1]
        argv = argv[:i] + argv[i + 2:]
    results = []
    for a in argv:
        try:
            r = measure(int(a))
        except Exception as e:  # noqa: BLE001
            r = {"key": int(a), "error": type(e).__name__}
        results.append(r)
        if "error" in r:
            print(f"{r['key']:04d} ERROR {r['error']}")
        else:
            print(f"{r['key']:04d} width {r['width']} height {r['height']} depth {r['depth']} "
                  f"min {r['min']} max {r['max']} centre {r['centre']} verts {r['verts']} meshes {r['meshes']} "
                  f"top_bias_z {r['top_bias_z']}")
            if "--meshes" in sys.argv:
                for i, pm in enumerate(r["per_mesh"]):
                    print(f"      mesh {i}: verts {pm['verts']} min {pm['min']} max {pm['max']}")
    if out_path:
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(results, f, indent=1)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
