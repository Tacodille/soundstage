# Validates the OLD v1 presets.json (kept as the baseline that gen_scenes.py --legacy must reproduce).
import json, os, re
d = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tests", "Soundstage.SelfCheck", "TestData", "baseline", "presets.json"), encoding="utf-8"))
assert d["formatVersion"] == 1
LF = {"name":str,"shape":str,"color":list,"intensity":(int,float),"range":(int,float),"falloff":str,
 "falloffFactor":(int,float),"coneDeg":(int,float),"angularFalloffDeg":(int,float),"azimuthDeg":(int,float),
 "distance":(int,float),"height":(int,float),"aimHeight":(int,float,type(None)),
 "characterShadows":bool,"objectShadows":bool,"specular":bool}
ids=set(); errs=[]
def chk(l, ctx):
    if set(l)!=set(LF): errs.append((ctx,"fields",set(l)^set(LF)))
    for k,t in LF.items():
        if not isinstance(l[k],t): errs.append((ctx,k,"type"))
    if l["shape"] not in ("Point","Spot","Flat"): errs.append((ctx,"shape"))
    if l["falloff"] not in ("Linear","Quadratic","Cubic"): errs.append((ctx,"falloff"))
    if len(l["color"])!=3 or not all(0<=c<=1 for c in l["color"]): errs.append((ctx,"color"))
    if not 0.8<=l["distance"]<=3: errs.append((ctx,"distance"))
for p in d["presets"]:
    assert re.fullmatch(r"[a-z0-9]+(-[a-z0-9]+)*",p["id"]) and p["id"] not in ids; ids.add(p["id"])
    assert p["category"] in ("Soft","Harsh","Neon","Natural")
    for k in ("name","description","bestIn"): assert isinstance(p[k],str) and p[k]
    assert all(isinstance(r,int) for r in p["references"])
    assert 1<=len(p["lights"])<=4
    for l in p["lights"]: chk(l,p["id"])
    b=p["backdrop"]
    if b:
        assert set(b)=={"color","distanceBehind","scale","light"}
        assert len(b["color"])==3 and all(0<=c<=1 for c in b["color"]) and len(b["scale"])==3
        if b["light"]: chk(b["light"],p["id"]+"/backdrop")
print("presets",len(d["presets"]),"errors",errs)
for p in d["presets"]: print(p["id"],p["category"],p["references"],len(p["lights"]),"backdrop" if p["backdrop"] else "-")
