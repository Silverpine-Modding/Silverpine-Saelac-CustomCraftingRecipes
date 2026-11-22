"""Check the test price fixture against native pickupable prefab templates.

Run in the extracted Silverpine 1.7.3 workspace with UnityPy available.
The stripped MonoBehaviours have no typetree: this reads only the prefix of
the two serialized classes used by Pickupable.GenerateItem in this build.
"""
import json
import re
import struct
import sys
from pathlib import Path

project = Path(__file__).resolve().parents[1]
game = project.parent.parent
sys.path.insert(0, str(game / ".codex-tools" / "unitypy"))
import UnityPy

expected = json.loads((project / "Tests" / "WorkshopFurniturePrices.json").read_text())
seen = {}
env = UnityPy.load(str(game / "Silverpine_Data"))
for obj in env.objects:
    if obj.type.name != "GameObject" or obj.assets_file.name != "resources.assets":
        continue
    go = obj.read()
    if not go.m_Name.startswith("prefab_furniture_"):
        continue
    components = [p.component.deref() for p in go.m_Component]
    transform = next(c.read() for c in components if c.type.name == "Transform")
    if transform.m_Father.m_PathID:
        continue
    name = price = None
    for c in components:
        if c.type.name != "MonoBehaviour":
            continue
        mono = c.parse_as_object(check_read=False)
        script = mono.m_Script.read().m_Name
        if script not in ("Pickupable", "NPCVisibleObject"):
            continue
        raw = c.get_raw_data()
        if mono.m_Name or len(raw) < 40:
            raise ValueError(f"Unexpected component layout: {go.m_Name}/{script}")
        if script == "Pickupable":
            price = struct.unpack_from("<i", raw, 36)[0]  # header, bulk, value
        else:
            n = struct.unpack_from("<I", raw, 32)[0]
            name = re.sub(r"^an? ", "", raw[36:36+n].decode(), flags=re.I).title()
    if name in expected and price is not None:
        if price != expected[name]:
            raise ValueError(f"{go.m_Name}: {name} costs {price}, expected {expected[name]}")
        seen[name] = price
if seen != expected:
    raise ValueError(f"Missing native furniture: {sorted(expected.keys() - seen.keys())}")
print(f"Verified {len(seen)} native furniture item names and prices from resources.assets.")
