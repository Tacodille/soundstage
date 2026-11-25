"""Generates the shipped Scene catalogue (src/Soundstage/scenes.json) and the dev six-light test Scene.

    python tools/gen_scenes.py                      writes src/Soundstage/scenes.json and src/Soundstage/TestData/six-lights.json
    python tools/gen_scenes.py --stdout             prints scenes.json instead of writing anything
    python tools/gen_scenes.py --legacy --out F     writes the OLD v1 presets.json to F (proof the generator is the source)
    python tools/gen_scenes.py --legacy --stdout    prints the old file

scenes.json is never edited by hand: change a parameter here and regenerate. Every light's range comes from game_range.
"""
import json, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.join(HERE, "..", "src", "Soundstage")

def L(name, shape, color, inten, az, dist, h, aim, cone=30.0, af=10.0, rng=12.0,
      fall="Quadratic", ff=1.0, cs=True, os_=False, spec=True):
    return {"name": name, "shape": shape, "color": color, "intensity": inten, "range": rng,
            "falloff": fall, "falloffFactor": ff, "coneDeg": cone, "angularFalloffDeg": af,
            "azimuthDeg": az, "distance": dist, "height": h, "aimHeight": aim,
            "characterShadows": cs, "objectShadows": os_, "specular": spec}

def B(color, behind, scale, light=None):
    return {"color": color, "distanceBehind": behind, "scale": scale, "light": light}

P = []
P.append(dict(id="neon-split-blue-red", name="Neon split: blue and red", category="Neon",
 description="Hard blue from the right of frame and hard red from the left, meeting down the middle of the face. A faint blue fill keeps the dark side from going black.",
 bestIn="Dark interior or night, close portrait.", references=[2, 11],
 lights=[
  L("Blue key","Spot",[0.1,0.25,1.0],8.0,-65,1.6,1.7,1.55,cone=45,af=12),
  L("Red key","Spot",[1.0,0.05,0.15],7.0,70,1.6,1.6,1.5,cone=40,af=10),
  L("Blue fill","Point",[0.05,0.1,0.6],2.0,0,2.2,1.4,None,rng=8,cs=False,spec=False)],
 backdrop=None))
P.append(dict(id="neon-orange-teal-split", name="Neon split: orange and teal", category="Neon",
 description="Warm orange on one cheek and cold teal on the other, each from a narrow spot at about 60 degrees. The nose shadow is the divide between them.",
 bestIn="Dark interior or night.", references=[15, 4],
 lights=[
  L("Orange key","Spot",[1.0,0.45,0.05],8.0,-55,1.6,1.7,1.55,cone=38,af=8),
  L("Teal key","Spot",[0.0,0.85,0.7],7.0,65,1.6,1.65,1.55,cone=40,af=10),
  L("Teal fill","Point",[0.0,0.5,0.45],1.5,0,2.2,1.3,None,rng=8,cs=False,spec=False)],
 backdrop=None))
P.append(dict(id="neon-magenta-on-blue", name="Neon: magenta side on deep blue", category="Neon",
 description="A magenta spot hits one side of the face from the left while the rest of him and the backdrop sit in deep blue. Face goes half pink, half blue.",
 bestIn="Anywhere, since the backdrop supplies the blue. Best with the camera at chest height.", references=[6, 12],
 lights=[
  L("Magenta key","Spot",[1.0,0.05,0.85],9.0,60,1.5,1.7,1.6,cone=30,af=8),
  L("Blue fill","Point",[0.05,0.1,0.8],3.0,-50,2.0,1.5,None,rng=8,cs=False,spec=False)],
 backdrop=B([0.03,0.05,0.4],2.0,[4.0,3.0,1.0],
   L("Blue wash","Point",[0.1,0.15,1.0],3.0,180,1.2,1.2,None,rng=5,cs=False,spec=False))))
P.append(dict(id="neon-pink-on-lavender", name="Neon: pink key on lavender", category="Neon",
 description="A fairly wide pink key from the front left, a cooler lavender fill from the right and a lavender backdrop. Shadows turn violet rather than black.",
 bestIn="Close portraits. Works in daylight and indoors.", references=[1],
 lights=[
  L("Pink key","Spot",[1.0,0.2,0.55],7.5,20,1.8,2.2,1.55,cone=55,af=25),
  L("Lavender fill","Point",[0.6,0.45,1.0],2.5,-60,2.0,1.5,None,rng=8,cs=False,spec=False)],
 backdrop=B([0.7,0.55,0.95],1.8,[4.0,3.0,1.0],
   L("Lavender wash","Point",[0.75,0.55,1.0],3.5,180,1.0,1.5,None,rng=5,cs=False,spec=False))))
P.append(dict(id="neon-orange-on-cyan", name="Neon: orange key on cyan", category="Neon",
 description="Orange key on the face against a bright cyan backdrop, with a little cyan bounce on the shadow side. The two colours are near opposites, so the face separates from the wall.",
 bestIn="Anywhere. Pairs with orange or red hair and light clothes.", references=[7],
 lights=[
  L("Orange key","Spot",[1.0,0.5,0.1],7.5,-35,1.6,2.2,1.55,cone=50,af=20),
  L("Cyan fill","Point",[0.1,0.7,1.0],2.0,50,2.0,1.4,None,rng=8,cs=False,spec=False)],
 backdrop=B([0.1,0.75,1.0],1.8,[4.0,3.0,1.0],
   L("Cyan wash","Point",[0.1,0.8,1.0],4.0,180,1.0,1.4,None,rng=5,cs=False,spec=False))))
P.append(dict(id="neon-green-rim", name="Neon: green rim light", category="Neon",
 description="Two green lights from behind and above trace the hair and shoulder, with a dim teal-green light from the front side so the profile still reads. Turn him side-on.",
 bestIn="Dark scenes, profile view.", references=[5],
 lights=[
  L("Green rim","Spot",[0.2,1.0,0.1],10.0,150,1.8,2.2,1.65,cone=35,af=10),
  L("Green rim 2","Spot",[0.3,1.0,0.1],7.0,-155,1.8,2.1,1.65,cone=35,af=10),
  L("Teal face light","Spot",[0.05,0.8,0.5],3.5,75,1.7,1.6,1.6,cone=50,af=20,cs=False)],
 backdrop=None))
P.append(dict(id="soft-backlit-hair-glow", name="Backlit hair glow", category="Soft",
 description="Two bright white-cool lights behind and above light up the edges of the hair, and a wide soft light from the front keeps the face gentle.",
 bestIn="Dark or dim scenes. Looks best head-on or three-quarter.", references=[9],
 lights=[
  L("Back light left","Spot",[0.85,0.9,1.0],10.0,165,1.8,2.3,1.7,cone=40,af=15),
  L("Back light right","Spot",[1.0,0.95,0.9],8.0,-165,1.8,2.3,1.7,cone=40,af=15),
  L("Soft front","Spot",[1.0,0.92,0.85],4.0,15,2.0,1.8,1.55,cone=70,af=40,cs=False,spec=False)],
 backdrop=None))
P.append(dict(id="harsh-half-face", name="Hard side light, half face", category="Harsh",
 description="One narrow spot from the side and above with a sharp edge, so half the face goes dark. No fill at all.",
 bestIn="Dark scenes. Face the camera, turn the head slightly to the light.", references=[14],
 lights=[
  L("Hard key","Spot",[1.0,0.95,0.88],11.0,90,1.5,1.9,1.6,cone=22,af=3,os_=True)],
 backdrop=None))
P.append(dict(id="soft-window", name="Soft window light", category="Soft",
 description="A broad soft source from one side, like a window, with a weak warm bounce from the other. The shadow side is gentle and the skin stays even.",
 bestIn="Indoors or anywhere. Turn him towards the window side.", references=[10],
 lights=[
  L("Window","Flat",[1.0,0.95,0.88],6.5,85,1.8,2.1,1.5,cone=60,af=20,rng=8,cs=False),
  L("Bounce","Point",[0.9,0.85,0.8],3.0,-50,2.4,1.3,None,rng=8,cs=False,spec=False)],
 backdrop=None))
P.append(dict(id="natural-golden-hour", name="Golden hour", category="Natural",
 description="A low warm sun from the front side, a hot amber rim from behind and above, and a weak sky-blue fill in the shade.",
 bestIn="Outdoors or any scene with plenty of open space.", references=[16],
 lights=[
  L("Low sun","Spot",[1.0,0.62,0.22],9.5,-50,2.5,2.2,1.55,cone=60,af=25),
  L("Warm rim","Spot",[1.0,0.7,0.3],8.0,170,2.2,2.4,1.7,cone=40,af=15),
  L("Sky fill","Point",[0.5,0.65,1.0],1.5,60,2.2,1.4,None,rng=8,cs=False,spec=False)],
 backdrop=None))
P.append(dict(id="soft-white-high-key", name="White high-key beauty", category="Soft",
 description="A bright white backdrop, a large soft key and a soft fill. A faint blue edge light from low on one side adds a cool tint to the skin.",
 bestIn="Anywhere. The white panel gives a seamless studio look.", references=[8, 13],
 lights=[
  L("Beauty key","Spot",[1.0,0.97,0.93],8.5,10,2.0,2.7,1.55,cone=70,af=35,cs=False),
  L("Fill","Point",[0.95,0.97,1.0],3.5,-40,2.2,1.5,None,rng=8,cs=False,spec=False),
  L("Blue edge","Spot",[0.1,0.35,1.0],3.0,-75,1.6,1.2,1.5,cone=45,af=20,cs=False)],
 backdrop=B([1.0,1.0,1.0],1.8,[4.0,3.0,1.0],
   L("White wash","Point",[1.0,1.0,1.0],6.0,180,1.2,1.5,None,rng=6,cs=False,spec=False))))

# Range by the game's own rule, measured on 18/11/2026 from nine lights of three falloffs (light survey):
# brightest colour channel x intensity / (falloffFactor x range^n) = 0.0196 (about 1/51) for every one of them,
# with n = 1, 2, 3 for Linear, Quadratic, Cubic. So the game sets a light's range to where it falls to ~2%.
GAME_CUTOFF = 0.0196
def game_range(l):
    n = {"Linear": 1, "Quadratic": 2, "Cubic": 3}[l["falloff"]]
    strength = max(l["color"]) * l["intensity"] / (l["falloffFactor"] * GAME_CUTOFF)
    return round(strength ** (1.0 / n) + 0.05, 1)
for p_ in P:
    for l in p_["lights"] + ([p_["backdrop"]["light"]] if p_.get("backdrop") and p_["backdrop"].get("light") else []):
        l["range"] = game_range(l)

# Each Scene is built from a default palette (the colours on its lights and backdrop) plus extra palettes.
# Every range is computed by game_range; none is typed by hand.

def R(role, name, shape, color, inten, az, dist, h, aim, **kw):
    """A light with a role. Same arguments as L, with the role first."""
    d = L(name, shape, color, inten, az, dist, h, aim, **kw)
    out = {"name": d["name"], "role": role}
    out.update({k: v for k, v in d.items() if k != "name"})
    return out

def light_color_map(lights, backdrop):
    m = {}
    for l in lights:
        m[l["role"]] = list(l["color"])
    if backdrop:
        m["backdrop"] = list(backdrop["color"])
        if backdrop.get("light"):
            m[backdrop["light"]["role"]] = list(backdrop["light"]["color"])
    return m

def scene(id, name, category, description, bestIn, rig, tags, lights, default_palette, extra_palettes=None,
          legacy=None, backdrop=None, set_=None, atmosphere=None, camera=None, look=None, pose=None,
          animation=None, references=None):
    palettes = {default_palette: light_color_map(lights, backdrop)}
    for pname, pmap in (extra_palettes or {}).items():
        palettes[pname] = pmap
    s = {"id": id, "name": name, "category": category, "description": description, "bestIn": bestIn,
         "rig": rig, "tags": tags, "legacyIds": legacy or {}, "defaultPalette": default_palette,
         "palettes": palettes, "references": references or [], "lights": lights, "backdrop": backdrop}
    for key, val in (("set", set_), ("atmosphere", atmosphere), ("camera", camera), ("look", look),
                     ("pose", pose), ("animation", animation)):
        if val is not None:
            s[key] = val
    return s

def effect(target, kind, amount, rate, enabled):
    return {"target": target, "kind": kind, "amount": amount, "rateHz": rate, "enabled": enabled}

S = []

# ---- The nine refined Scenes (from the eleven old presets) ----

S.append(scene("neon-split", "Neon split", "Neon",
 "Two hard coloured lights from either side of frame meet down the middle of the face, with a faint fill so the dark side does not go black. Four palettes: blue and red, orange and teal, magenta and cyan, violet and amber.",
 "Dark interior or night, close portrait.", "split neon", ["neon", "portrait", "two-colour"],
 [R("key", "Key", "Spot", [0.1, 0.25, 1.0], 8.0, -65, 1.6, 1.7, 1.55, cone=45, af=12),
  R("key2", "Second key", "Spot", [1.0, 0.05, 0.15], 7.0, 70, 1.6, 1.6, 1.5, cone=40, af=10),
  R("fill", "Fill", "Point", [0.05, 0.1, 0.6], 2.0, 0, 2.2, 1.4, None, rng=8, cs=False, spec=False)],
 "Blue and red",
 {"Orange and teal": {"key": [1.0, 0.45, 0.05], "key2": [0.0, 0.85, 0.7], "fill": [0.0, 0.5, 0.45]},
  "Magenta and cyan": {"key": [1.0, 0.05, 0.85], "key2": [0.05, 0.85, 1.0], "fill": [0.03, 0.4, 0.55]},
  "Violet and amber": {"key": [0.5, 0.15, 1.0], "key2": [1.0, 0.65, 0.1], "fill": [0.3, 0.08, 0.6]}},
 legacy={"neon-split-blue-red": "", "neon-orange-teal-split": "Orange and teal"},
 look={"slot": "neon-night"},
 camera={"framing": "chest-up", "height": "eye", "side": "front", "notes": "Chest-up at eye level."},
 pose={"tags": ["confident", "three-quarter", "arms crossed"]},
 animation={"effects": [effect("key", "pulse", 0.15, 0.25, False), effect("key2", "pulse", 0.15, 0.25, False)]},
 references=[2, 11, 15, 4]))

S.append(scene("neon-wall-hard", "Neon wall, hard", "Neon",
 "A hard magenta spot hits one side of the face from the left while the rest of the frame and the wall behind sit in deep blue. The face goes half pink, half blue.",
 "Anywhere, since the backdrop supplies the blue. Best with the camera at chest height.", "neon wall", ["neon", "backdrop", "hard"],
 [R("key", "Magenta key", "Spot", [1.0, 0.05, 0.85], 9.0, 60, 1.5, 1.7, 1.6, cone=30, af=8),
  R("fill", "Blue fill", "Point", [0.05, 0.1, 0.8], 3.0, -50, 2.0, 1.5, None, rng=8, cs=False, spec=False)],
 "Magenta on blue",
 legacy={"neon-magenta-on-blue": ""},
 backdrop=B([0.03, 0.05, 0.4], 2.0, [4.0, 3.0, 1.0],
   R("bg", "Blue wash", "Point", [0.1, 0.15, 1.0], 3.0, 180, 1.2, 1.2, None, rng=5, cs=False, spec=False)),
 set_={"kind": "sweep", "colorRole": "backdrop", "props": [], "notes": "A dyed sweep once the Set tab lands; the partition panel until then."},
 look={"slot": "neon-night"},
 camera={"framing": "chest-up", "height": "chest", "side": "front", "notes": "Camera at chest height."},
 pose={"tags": ["side profile", "looking away"]},
 animation={"effects": [effect("bg", "flicker", 0.2, 6.0, False)]},
 references=[6, 12]))

S.append(scene("neon-wall-soft", "Neon wall, soft", "Neon",
 "A wide, soft pink key from the front left, a cooler lavender fill and a lavender wall. Shadows turn violet rather than black. A second palette puts an orange key against a bright cyan wall.",
 "Close portraits. Works in daylight and indoors.", "neon wall", ["neon", "backdrop", "soft"],
 [R("key", "Key", "Spot", [1.0, 0.2, 0.55], 7.5, 20, 1.8, 2.2, 1.55, cone=55, af=25),
  R("fill", "Fill", "Point", [0.6, 0.45, 1.0], 2.5, -60, 2.0, 1.5, None, rng=8, cs=False, spec=False)],
 "Pink and lavender",
 {"Orange and cyan": {"key": [1.0, 0.5, 0.1], "fill": [0.1, 0.7, 1.0], "bg": [0.1, 0.8, 1.0], "backdrop": [0.1, 0.75, 1.0]}},
 legacy={"neon-pink-on-lavender": "", "neon-orange-on-cyan": "Orange and cyan"},
 backdrop=B([0.7, 0.55, 0.95], 1.8, [4.0, 3.0, 1.0],
   R("bg", "Wash", "Point", [0.75, 0.55, 1.0], 3.5, 180, 1.0, 1.5, None, rng=5, cs=False, spec=False)),
 set_={"kind": "sweep", "colorRole": "backdrop", "props": [], "notes": "A dyed sweep once the Set tab lands; the partition panel until then."},
 look={"slot": "neon-night"},
 camera={"framing": "close-portrait", "height": "eye", "side": "front", "notes": "Close portrait."},
 pose={"tags": ["head-on", "smiling"]},
 references=[1, 7]))

S.append(scene("rim-only", "Rim only", "Classic",
 "Two lights from behind and above trace the hair and shoulder, and a dim light from the front side keeps the profile readable. Turn side-on. Palettes: green neon, white, amber.",
 "Dark scenes, profile view.", "rim light", ["rim", "profile", "dark"],
 [R("rim", "Rim", "Spot", [0.2, 1.0, 0.1], 10.0, 150, 1.8, 2.2, 1.65, cone=35, af=10),
  R("rim2", "Rim 2", "Spot", [0.3, 1.0, 0.1], 7.0, -155, 1.8, 2.1, 1.65, cone=35, af=10),
  R("fill", "Face light", "Spot", [0.05, 0.8, 0.5], 3.5, 75, 1.7, 1.6, 1.6, cone=50, af=20, cs=False)],
 "Green neon",
 {"White": {"rim": [0.9, 0.95, 1.0], "rim2": [1.0, 0.97, 0.92], "fill": [0.45, 0.5, 0.55]},
  "Amber": {"rim": [1.0, 0.6, 0.15], "rim2": [1.0, 0.7, 0.25], "fill": [0.6, 0.35, 0.1]}},
 legacy={"neon-green-rim": ""},
 look={"slot": "dark-moody"},
 camera={"framing": "side-on", "height": "eye", "side": "right", "notes": "Side-on."},
 pose={"tags": ["profile", "standing"]},
 references=[5]))

S.append(scene("hair-glow", "Hair glow", "Soft",
 "Two bright white-cool lights behind and above light up the edges of the hair, and a wide soft light from the front keeps the face gentle.",
 "Dark or dim scenes. Looks best head-on or three-quarter.", "back light", ["soft", "backlit", "hair"],
 [R("rim", "Back light left", "Spot", [0.85, 0.9, 1.0], 10.0, 165, 1.8, 2.3, 1.7, cone=40, af=15),
  R("rim2", "Back light right", "Spot", [1.0, 0.95, 0.9], 8.0, -165, 1.8, 2.3, 1.7, cone=40, af=15),
  R("key", "Soft front", "Spot", [1.0, 0.92, 0.85], 4.0, 15, 2.0, 1.8, 1.55, cone=70, af=40, cs=False, spec=False)],
 "Cool white",
 legacy={"soft-backlit-hair-glow": ""},
 look={"slot": "soft-bloom"},
 atmosphere={"haze": "light", "dust": False, "sun": False, "notes": "Light haze."},
 pose={"tags": ["looking back over shoulder", "head-on"]},
 references=[9]))

S.append(scene("split-light", "Split light", "Classic",
 "One narrow spot from the side and above with a sharp edge, so half the face goes dark. No fill at all.",
 "Dark scenes. Face the camera, turn the head slightly to the light.", "split lighting", ["classic", "hard", "portrait"],
 [R("key", "Key", "Spot", [1.0, 0.95, 0.88], 11.0, 90, 1.5, 1.9, 1.6, cone=22, af=3, os_=True)],
 "Warm white",
 legacy={"harsh-half-face": ""},
 camera={"framing": "close-portrait", "height": "eye", "side": "front", "notes": "Close portrait."},
 pose={"tags": ["three-quarter turn toward the light"]},
 references=[14]))

S.append(scene("window-light", "Window light", "Soft",
 "A broad soft source from one side, like a window, with a weak warm bounce from the other. The shadow side is gentle and the skin stays even.",
 "Indoors or anywhere. Turn towards the window side.", "window light", ["soft", "natural", "indoor"],
 [R("key", "Window", "Flat", [1.0, 0.95, 0.88], 6.5, 85, 1.8, 2.1, 1.5, cone=60, af=20, rng=8, cs=False),
  R("fill", "Bounce", "Point", [0.9, 0.85, 0.8], 3.0, -50, 2.4, 1.3, None, rng=8, cs=False, spec=False)],
 "Daylight",
 legacy={"soft-window": ""},
 camera={"framing": "three-quarter-waist-up", "height": "eye", "side": "front", "notes": "Three-quarter, waist up."},
 pose={"tags": ["sitting", "looking out"]},
 animation={"effects": [effect("key", "drift", 0.08, 0.05, True)]},
 references=[10]))

S.append(scene("golden-hour", "Golden hour", "Natural",
 "A low warm sun from the front side, a hot amber rim from behind and above, and a weak sky-blue fill in the shade.",
 "Outdoors or any scene with plenty of open space.", "sun and rim", ["natural", "warm", "outdoor"],
 [R("key", "Low sun", "Spot", [1.0, 0.62, 0.22], 9.5, -50, 2.5, 2.2, 1.55, cone=60, af=25),
  R("rim", "Warm rim", "Spot", [1.0, 0.7, 0.3], 8.0, 170, 2.2, 2.4, 1.7, cone=40, af=15),
  R("fill", "Sky fill", "Point", [0.5, 0.65, 1.0], 1.5, 60, 2.2, 1.4, None, rng=8, cs=False, spec=False)],
 "Golden",
 legacy={"natural-golden-hour": ""},
 look={"slot": "warm-grade"},
 atmosphere={"haze": "light", "dust": True, "sun": True, "notes": "Sun haze and floating dust."},
 pose={"tags": ["walking", "looking into the distance"]},
 animation={"effects": [effect("key", "drift", 0.08, 0.05, True)]},
 references=[16]))

S.append(scene("high-key", "High key", "Classic",
 "A bright white backdrop, a large soft key and a soft fill. A faint blue edge light from low on one side adds a cool tint to the skin.",
 "Anywhere. The white backdrop gives a seamless studio look.", "high key", ["classic", "bright", "beauty"],
 [R("key", "Beauty key", "Spot", [1.0, 0.97, 0.93], 8.5, 10, 2.0, 2.7, 1.55, cone=70, af=35, cs=False),
  R("fill", "Fill", "Point", [0.95, 0.97, 1.0], 3.5, -40, 2.2, 1.5, None, rng=8, cs=False, spec=False),
  R("kicker", "Blue edge", "Spot", [0.1, 0.35, 1.0], 3.0, -75, 1.6, 1.2, 1.5, cone=45, af=20, cs=False)],
 "White",
 legacy={"soft-white-high-key": ""},
 backdrop=B([1.0, 1.0, 1.0], 1.8, [4.0, 3.0, 1.0],
   R("bg", "White wash", "Point", [1.0, 1.0, 1.0], 6.0, 180, 1.2, 1.5, None, rng=6, cs=False, spec=False)),
 set_={"kind": "sweep", "color": [1.0, 1.0, 1.0], "props": [], "notes": "A seamless white sweep once the Set tab lands; the white partition until then."},
 look={"slot": "clean-bright"},
 camera={"framing": "three-quarter", "height": "eye", "side": "front", "notes": "Three-quarter."},
 pose={"tags": ["standing", "smiling"]},
 references=[8, 13]))

# ---- Six classic rigs, new. Starting values only: tune them in game (step 4.8). ----

S.append(scene("rembrandt", "Rembrandt", "Classic",
 "A key about 45 degrees to one side and about 45 degrees above eye line, with a small fill on the other side and no backdrop light. The check is a small triangle of light on the cheek in shadow.",
 "Dark or dim scenes. Turn the head a little away from the key.", "rembrandt", ["classic", "portrait", "moody"],
 [R("key", "Key", "Spot", [1.0, 0.93, 0.82], 8.0, 45, 1.4, 2.9, 1.6, cone=35, af=15, os_=True),
  R("fill", "Fill", "Point", [0.85, 0.9, 1.0], 1.2, -50, 2.2, 1.5, None, rng=8, cs=False, spec=False)],
 "Warm",
 {"Cool": {"key": [0.85, 0.92, 1.0], "fill": [1.0, 0.9, 0.8]}},
 look={"slot": "dark-moody"},
 camera={"framing": "three-quarter", "height": "eye", "side": "front", "notes": "Three-quarter."},
 pose={"tags": ["three-quarter", "looking away"]}))

S.append(scene("butterfly", "Butterfly", "Classic",
 "One key straight in front and high, about 2.5 m up, which drops a small shadow under the nose. A low flat fill below the chin lifts the shadow under the jaw.",
 "Anywhere. Face the camera and keep the chin level.", "paramount", ["classic", "beauty", "front"],
 [R("key", "Key", "Spot", [1.0, 0.96, 0.9], 7.5, 0, 1.4, 2.5, 1.6, cone=50, af=25, os_=True),
  R("fill", "Chin fill", "Flat", [0.95, 0.97, 1.0], 2.0, 0, 1.0, 0.95, 1.5, cone=70, af=30, rng=8, cs=False, spec=False)],
 "Warm white",
 {"Cool white": {"key": [0.9, 0.95, 1.0], "fill": [0.85, 0.9, 1.0]}},
 look={"slot": "clean-bright"},
 camera={"framing": "close-portrait", "height": "eye", "side": "front", "notes": "Close portrait."},
 pose={"tags": ["head-on", "chin level"]}))

S.append(scene("loop", "Loop", "Classic",
 "A key about 30 degrees to the side and a little above, with a soft fill. The nose throws a small loop of shadow on the cheek.",
 "Anywhere. A safe, flattering portrait light.", "loop lighting", ["classic", "portrait", "soft"],
 [R("key", "Key", "Spot", [1.0, 0.95, 0.88], 7.0, 30, 1.8, 2.1, 1.55, cone=45, af=20, os_=True),
  R("fill", "Fill", "Point", [0.9, 0.93, 1.0], 2.0, -40, 2.2, 1.5, None, rng=8, cs=False, spec=False)],
 "Warm white",
 {"Cool white": {"key": [0.9, 0.95, 1.0], "fill": [1.0, 0.92, 0.85]}},
 look={"slot": "clean-bright"},
 camera={"framing": "three-quarter", "height": "eye", "side": "front", "notes": "Three-quarter."},
 pose={"tags": ["three-quarter", "smiling"]}))

S.append(scene("clamshell", "Clamshell", "Classic",
 "A key high in front and a fill low in front at about half the strength, so the face is lit from above and below like the two halves of a shell. The beauty rig.",
 "Anywhere. Face the camera.", "clamshell", ["classic", "beauty", "front"],
 [R("key", "Key", "Spot", [1.0, 0.96, 0.9], 7.0, 0, 1.6, 2.3, 1.6, cone=55, af=25, cs=False),
  R("fill", "Low fill", "Flat", [0.95, 0.97, 1.0], 3.5, 0, 1.4, 0.9, 1.5, cone=60, af=25, rng=8, cs=False, spec=False)],
 "Warm white",
 {"Cool white": {"key": [0.9, 0.95, 1.0], "fill": [0.9, 0.95, 1.0]}},
 look={"slot": "clean-bright"},
 camera={"framing": "close-portrait", "height": "eye", "side": "front", "notes": "Close portrait."},
 pose={"tags": ["head-on", "chin level"]}))

S.append(scene("silhouette", "Silhouette", "Classic",
 "A bright wash on the wall behind and a rim from behind, with nothing on the front, so the subject reads as a dark shape against the glow.",
 "Anywhere with room behind the subject. Stand side-on.", "silhouette", ["classic", "backlit", "graphic"],
 [R("rim", "Rim", "Spot", [1.0, 0.97, 0.9], 6.0, 160, 1.8, 2.2, 1.65, cone=40, af=15, cs=False)],
 "White",
 {"Sunset": {"rim": [1.0, 0.7, 0.35], "bg": [1.0, 0.6, 0.25], "backdrop": [1.0, 0.55, 0.2]}},
 backdrop=B([1.0, 0.97, 0.9], 2.2, [4.0, 3.0, 1.0],
   R("bg", "Wall wash", "Point", [1.0, 0.97, 0.9], 8.0, 180, 1.2, 1.5, None, rng=6, cs=False, spec=False)),
 set_={"kind": "sweep", "colorRole": "backdrop", "props": [], "notes": "A bright sweep once the Set tab lands."},
 look={"slot": "dark-moody"},
 camera={"framing": "full-body", "height": "eye", "side": "right", "notes": "Full body, side-on."},
 pose={"tags": ["profile", "standing"]}))

S.append(scene("low-key", "Low key", "Classic",
 "One narrow key from the side, no fill and no backdrop light. Most of the frame stays dark.",
 "Dark scenes. Turn a little towards the light.", "low key", ["classic", "dark", "moody"],
 [R("key", "Key", "Spot", [1.0, 0.9, 0.78], 9.0, 80, 1.5, 1.8, 1.6, cone=25, af=6, os_=True)],
 "Warm white",
 {"Cool white": {"key": [0.8, 0.9, 1.0]}},
 look={"slot": "dark-moody"},
 camera={"framing": "close-portrait", "height": "eye", "side": "front", "notes": "Close portrait."},
 pose={"tags": ["three-quarter", "serious"]}))

# The dev-only six-light test Scene (not in scenes.json): shows whether all six lights render. Written to TestData.
SIX = scene("test-six-lights", "Six lights (test)", "Test",
 "Six small point lights in a ring around the subject, each a different colour. If the cap holds, all six show.",
 "Dev tab test only.", "test", ["test"],
 [R("key", "Light 1", "Point", [1.0, 0.2, 0.2], 3.0, 0, 1.6, 1.6, None, rng=8, cs=False, spec=False),
  R("fill", "Light 2", "Point", [1.0, 0.7, 0.1], 3.0, 60, 1.6, 1.6, None, rng=8, cs=False, spec=False),
  R("rim", "Light 3", "Point", [0.2, 1.0, 0.2], 3.0, 120, 1.6, 1.6, None, rng=8, cs=False, spec=False),
  R("kicker", "Light 4", "Point", [0.1, 0.8, 1.0], 3.0, 180, 1.6, 1.6, None, rng=8, cs=False, spec=False),
  R("kicker2", "Light 5", "Point", [0.3, 0.2, 1.0], 3.0, -120, 1.6, 1.6, None, rng=8, cs=False, spec=False),
  R("kicker3", "Light 6", "Point", [1.0, 0.2, 0.8], 3.0, -60, 1.6, 1.6, None, rng=8, cs=False, spec=False)],
 "Rainbow")

for s_ in S + [SIX]:
    for l in s_["lights"] + ([s_["backdrop"]["light"]] if s_.get("backdrop") and s_["backdrop"].get("light") else []):
        l["range"] = game_range(l)

SCENE_KEYS = ["id", "name", "category", "description", "bestIn", "rig", "tags", "legacyIds", "defaultPalette",
              "palettes", "references", "lights", "backdrop", "set", "atmosphere", "camera", "look", "pose", "animation"]
def scene_file_text(scenes):
    out = []
    for s_ in scenes:
        d = {k: s_[k] for k in SCENE_KEYS if k in s_}
        if d.get("backdrop") is None:
            d.pop("backdrop", None)
        out.append(d)
    return json.dumps({"formatVersion": 2, "scenes": out}, indent=2) + "\n"


def write_text(path, text):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)

def legacy_text():
    order = ["id","name","category","description","bestIn","references","lights","backdrop"]
    data = {"formatVersion": 1, "presets": [{k: p[k] for k in order} for p in P]}
    return json.dumps(data, indent=2) + "\n"

def main():
    args = sys.argv[1:]
    out = args[args.index("--out") + 1] if "--out" in args else None
    if "--legacy" in args:
        text = legacy_text()
        if "--stdout" in args:
            sys.stdout.buffer.write(text.encode("utf-8"))
        elif out:
            write_text(out, text)
        else:
            sys.exit("--legacy needs --out <file> or --stdout (the old presets.json no longer lives in src).")
        return
    text = scene_file_text(S)
    if "--stdout" in args:
        sys.stdout.buffer.write(text.encode("utf-8"))
        return
    write_text(out or os.path.join(OUT_DIR, "scenes.json"), text)
    write_part8_files(os.path.dirname(os.path.abspath(out)) if out else OUT_DIR)
    if not out:
        write_text(os.path.join(OUT_DIR, "TestData", "six-lights.json"), scene_file_text([SIX]))

# ---- Part 8: face-aimed lights, three animated Scenes, the gel list. Appended here so part 4's body is untouched. ----

# Key lights of the face rigs follow the head (aimAt "face"). Scene id -> roles.
FACE_AIMED = {"rembrandt": ["key"], "butterfly": ["key", "fill"], "loop": ["key"], "clamshell": ["key", "fill"],
              "split-light": ["key"], "low-key": ["key"]}

# Generic names only, no maker numbers. Hex values are display colours for a light's channels (value / 255).
GELS = [
    ("Warm white", "#fff1dc", "A little warmer than neutral. Skin looks kind."),
    ("Cool white", "#e6f0ff", "A little cooler than neutral."),
    ("Straw", "#ffe08a", "Pale yellow, the colour of a low afternoon."),
    ("Amber", "#ffb347", "Tungsten and candle."),
    ("Deep orange", "#ff7a1a", "Firelight and sunset."),
    ("Coral", "#ff7f6e", "Warm pink-orange; flatters most skin."),
    ("Rose", "#ff7aa8", "Soft pink."),
    ("Magenta", "#ff1ec8", "Hard club pink."),
    ("Red", "#ff2a2a", "Danger and neon."),
    ("Lavender", "#b69cff", "Cool, soft violet."),
    ("Violet", "#8040ff", "Night club and ultraviolet."),
    ("Deep blue", "#2646ff", "Moonlight and deep shadow fill."),
    ("Steel blue", "#6fa0e0", "Overcast sky."),
    ("Cyan", "#14d2ff", "Cold neon and screens."),
    ("Teal", "#10c0a8", "The usual partner for orange."),
    ("Mint", "#8fffc0", "Pale green, a little sickly on skin."),
    ("Green", "#2cff5a", "Hard neon green."),
    ("Lime", "#c6ff2e", "Acid yellow-green."),
]

def gels_text():
    return json.dumps({"formatVersion": 1, "gels": [{"name": n, "hex": h, "note": note} for n, h, note in GELS]}, indent=2) + "\n"

EXTRA_FILES = {"gels.json": gels_text()}

def animated():
    """The three animated Scenes (16 to 18). Appended to S by part8()."""
    out = []

    def seeded(e, seed):
        e["seed"] = seed
        return e

    out.append(scene("club-strobe", "Club strobe", "Animated",
     "Magenta and cyan washes from either side and a hard white strobe from the front. The strobe is OFF until you switch it on in the Lighting tab, and it never flashes faster than 3 times a second. Flashing light can affect people with photosensitive epilepsy.",
     "Dark interior, any framing. Turn the strobe on for one shot, then off again.", "club", ["animated", "strobe", "neon", "party"],
     [R("key", "Magenta wash", "Spot", [1.0, 0.05, 0.8], 7.0, 60, 1.6, 1.9, 1.55, cone=45, af=15),
      R("key2", "Cyan wash", "Spot", [0.05, 0.8, 1.0], 7.0, -60, 1.6, 1.9, 1.55, cone=45, af=15),
      R("kicker", "Strobe", "Spot", [1.0, 1.0, 1.0], 6.0, 15, 1.8, 2.5, 1.55, cone=60, af=20, cs=False)],
     "Magenta and cyan",
     {"Red and blue": {"key": [1.0, 0.1, 0.1], "key2": [0.1, 0.2, 1.0], "kicker": [0.9, 0.95, 1.0]},
      "Green and violet": {"key": [0.2, 1.0, 0.3], "key2": [0.55, 0.15, 1.0], "kicker": [1.0, 1.0, 1.0]}},
     look={"slot": "neon-night"},
     camera={"framing": "chest-up", "height": "eye", "side": "front", "notes": "Chest-up at eye level."},
     pose={"tags": ["dancing", "arms up"]},
     animation={"effects": [effect("kicker", "strobe", 1.0, 2.0, False),
                            effect("key", "pulse", 0.25, 0.5, False),
                            effect("key2", "pulse", 0.25, 0.5, False)]},
     references=[2, 11]))

    out.append(scene("campfire-flicker", "Campfire flicker", "Animated",
     "A low orange light in front, where a fire would be, with a faint blue moon from behind. The fire flickers and drifts. Switch the flicker off in the Lighting tab for a still frame.",
     "Outdoors at night, sitting or crouching.", "firelight", ["animated", "fire", "warm", "night"],
     [R("key", "Fire", "Point", [1.0, 0.42, 0.08], 5.0, 0, 1.1, 0.5, None, rng=8, cs=True, os_=True),
      R("rim", "Moon", "Spot", [0.25, 0.35, 0.7], 2.5, 160, 2.0, 2.6, 1.5, cone=50, af=25, cs=False)],
     "Campfire",
     {"Embers": {"key": [1.0, 0.2, 0.03], "rim": [0.15, 0.1, 0.4]}},
     look={"slot": "warm-grade"},
     camera={"framing": "chest-up", "height": "eye", "side": "front", "notes": "Chest-up, a little below eye level."},
     pose={"tags": ["sitting", "looking into the fire"]},
     animation={"effects": [seeded(effect("key", "flicker", 0.35, 9.0, True), 1701),
                            effect("key", "drift", 0.12, 0.6, True)]},
     references=[9]))

    out.append(scene("neon-sign-flicker", "Neon sign flicker", "Animated",
     "A pink sign that stutters, lighting one side of the face and washing the wall behind. The sign and its wall flicker together, as a failing tube does.",
     "Dark interior or alley, chest-up.", "neon sign", ["animated", "neon", "flicker", "backdrop"],
     [R("key", "Sign", "Spot", [1.0, 0.1, 0.55], 7.0, 50, 1.5, 1.8, 1.55, cone=40, af=12),
      R("fill", "Sign spill", "Point", [0.5, 0.05, 0.35], 1.8, -40, 2.0, 1.4, None, rng=8, cs=False, spec=False)],
     "Pink sign",
     {"Green sign": {"key": [0.1, 1.0, 0.4], "fill": [0.05, 0.5, 0.2], "backdrop": [0.02, 0.35, 0.12], "bg": [0.1, 1.0, 0.4]},
      "Blue sign": {"key": [0.15, 0.4, 1.0], "fill": [0.05, 0.15, 0.5], "backdrop": [0.03, 0.1, 0.4], "bg": [0.15, 0.4, 1.0]}},
     backdrop=B([0.35, 0.02, 0.18], 2.0, [4.0, 3.0, 1.0],
       R("bg", "Sign glow", "Point", [1.0, 0.1, 0.55], 3.5, 180, 1.2, 1.5, None, rng=6, cs=False, spec=False)),
     set_={"kind": "sweep", "colorRole": "backdrop", "props": [], "notes": "A dark sweep once the Set tab lands; the partition panel until then."},
     look={"slot": "neon-night"},
     camera={"framing": "chest-up", "height": "eye", "side": "front", "notes": "Chest-up at eye level."},
     pose={"tags": ["three-quarter", "looking away"]},
     animation={"effects": [seeded(effect("key", "flicker", 0.7, 5.0, True), 42),
                            seeded(effect("bg", "flicker", 0.8, 5.0, True), 42)]},
     references=[6, 12]))
    return out

def part8(scenes):
    """Flags the face-aimed lights, adds the animated Scenes (ranges by the brightness rule) and returns the list."""
    for s_ in scenes:
        for role in FACE_AIMED.get(s_["id"], []):
            for l in s_["lights"]:
                if l["role"] == role:
                    l["aimAt"] = "face"
    for s_ in animated():
        face_roles = {"club-strobe": ("key", "key2"), "neon-sign-flicker": ("key",)}.get(s_["id"], ())
        for l in s_["lights"]:
            if l["role"] in face_roles:
                l["aimAt"] = "face"
        for l in s_["lights"] + ([s_["backdrop"]["light"]] if s_.get("backdrop") and s_["backdrop"].get("light") else []):
            l["range"] = game_range(l)
        scenes.append(s_)
    return scenes

def write_part8_files(out_dir):
    for name, text in EXTRA_FILES.items():
        write_text(os.path.join(out_dir, name), text)

part8(S)


if __name__ == "__main__":
    main()
