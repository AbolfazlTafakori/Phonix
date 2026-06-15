#!/usr/bin/env python3
"""Figma export helper for the Phonix store project.

Usage:
  python figma.py structure          # list pages -> top-level frames
  python figma.py node <id>          # dump a node subtree summary
  python figma.py export <id> [...]  # export node(s) as assets (svg/png)

Reads the access token from the FIGMA_TOKEN environment variable.
"""
import json
import os
import sys
import time
import urllib.parse
import urllib.request

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

FILE_KEY = "xE830tWxx3pq0TOTm3J7A2"
API = "https://api.figma.com/v1"
TOKEN = os.environ.get("FIGMA_TOKEN", "").strip()

HERE = os.path.dirname(os.path.abspath(__file__))


def api_get(path, params=None):
    url = f"{API}{path}"
    if params:
        url += "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers={"X-Figma-Token": TOKEN})
    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            return json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", "replace")
        raise RuntimeError(f"HTTP {e.code} for {url}\n{body}") from None


def fmt_box(node):
    b = node.get("absoluteBoundingBox")
    if not b:
        return "?x?"
    return f"{round(b.get('width', 0))}x{round(b.get('height', 0))}"


def cmd_structure():
    data = api_get(f"/files/{FILE_KEY}/nodes",
                   {"ids": "0:1", "depth": 2})
    nodes = data.get("nodes", {})
    for nid, wrapper in nodes.items():
        page = wrapper["document"]
        children = page.get("children", [])
        print(f"PAGE: {page['name']}  ({nid})  -> {len(children)} top-level nodes")
        print("-" * 70)
        for c in children:
            print(f"[{c['id']:>12}]  {c.get('name','?'):<35} {c['type']:<10} {fmt_box(c)}")


def cmd_node(node_id):
    data = api_get(f"/files/{FILE_KEY}/nodes", {"ids": node_id, "depth": 4})
    nodes = data.get("nodes", {})
    for nid, wrapper in nodes.items():
        root = wrapper["document"]
        print(f"NODE: {root.get('name')}  ({nid})  type={root['type']}  {fmt_box(root)}")
        def walk(n, d=0):
            if d > 4:
                return
            print("  " * d + f"- {n.get('name','?')} [{n['type']}] {fmt_box(n)} ({n['id']})")
            for ch in n.get("children", []):
                walk(ch, d + 1)
        for ch in root.get("children", []):
            walk(ch, 1)


def _hex(c):
    if not c:
        return None
    r = round(c.get("r", 0) * 255)
    g = round(c.get("g", 0) * 255)
    b = round(c.get("b", 0) * 255)
    a = c.get("a", 1)
    if a >= 0.999:
        return f"#{r:02X}{g:02X}{b:02X}"
    return f"rgba({r}, {g}, {b}, {round(a, 3)})"


def _fill_desc(fills):
    out = []
    for f in fills or []:
        if f.get("visible") is False:
            continue
        t = f.get("type")
        if t == "SOLID":
            out.append(_hex(f.get("color")))
        elif t and t.startswith("GRADIENT"):
            stops = ", ".join(_hex(s.get("color")) for s in f.get("gradientStops", []))
            out.append(f"{t.lower()}({stops})")
        elif t == "IMAGE":
            out.append(f"image(ref={f.get('imageRef')})")
    return [x for x in out if x]


def _effect_desc(effects):
    out = []
    for e in effects or []:
        if e.get("visible") is False:
            continue
        t = e.get("type")
        if t in ("DROP_SHADOW", "INNER_SHADOW"):
            off = e.get("offset", {})
            out.append(f"{t.lower()}: x={round(off.get('x',0))} y={round(off.get('y',0))} "
                       f"blur={round(e.get('radius',0))} spread={round(e.get('spread',0))} "
                       f"color={_hex(e.get('color'))}")
        elif t in ("LAYER_BLUR", "BACKGROUND_BLUR"):
            out.append(f"{t.lower()}: {round(e.get('radius',0))}")
    return out


def _inspect_node(n, depth=0, max_depth=6):
    if depth > max_depth:
        return
    pad = "  " * depth
    b = n.get("absoluteBoundingBox") or {}
    box = f"{round(b.get('width',0))}x{round(b.get('height',0))} @({round(b.get('x',0))},{round(b.get('y',0))})"
    print(f"{pad}> {n.get('name','?')}  [{n['type']}]  {box}  ({n['id']})")
    info = []
    if n.get("layoutMode") and n["layoutMode"] != "NONE":
        info.append(f"auto-layout={n['layoutMode']} gap={n.get('itemSpacing',0)} "
                    f"pad=({n.get('paddingTop',0)},{n.get('paddingRight',0)},"
                    f"{n.get('paddingBottom',0)},{n.get('paddingLeft',0)}) "
                    f"align={n.get('primaryAxisAlignItems','')}/{n.get('counterAxisAlignItems','')}")
    fills = _fill_desc(n.get("fills"))
    if fills:
        info.append("fill=" + ", ".join(fills))
    strokes = _fill_desc(n.get("strokes"))
    if strokes:
        info.append(f"stroke={', '.join(strokes)} w={n.get('strokeWeight',0)}")
    if n.get("cornerRadius"):
        info.append(f"radius={n['cornerRadius']}")
    elif n.get("rectangleCornerRadii"):
        info.append(f"radius={n['rectangleCornerRadii']}")
    eff = _effect_desc(n.get("effects"))
    if eff:
        info.append("; ".join(eff))
    if n.get("opacity") is not None and n.get("opacity") != 1:
        info.append(f"opacity={n['opacity']}")
    if n["type"] == "TEXT":
        st = n.get("style", {})
        info.append(f'text="{n.get("characters","")[:60]}"')
        info.append(f"font={st.get('fontFamily')} {st.get('fontWeight')} "
                    f"{st.get('fontSize')}px lh={st.get('lineHeightPx')} "
                    f"ls={st.get('letterSpacing')} align={st.get('textAlignHorizontal')}")
    for line in info:
        print(f"{pad}    {line}")
    for ch in n.get("children", []):
        _inspect_node(ch, depth + 1, max_depth)


def _collect(n, texts, images, vectors, path=""):
    here = f"{path}/{n.get('name','?')}"
    if n["type"] == "TEXT":
        st = n.get("style", {})
        texts.append({
            "path": here, "id": n["id"], "text": n.get("characters", ""),
            "font": st.get("fontFamily"), "weight": st.get("fontWeight"),
            "size": st.get("fontSize"), "lh": st.get("lineHeightPx"),
            "color": (_fill_desc(n.get("fills")) or [None])[0],
            "align": st.get("textAlignHorizontal"),
        })
    for f in n.get("fills") or []:
        if f.get("type") == "IMAGE" and f.get("imageRef"):
            b = n.get("absoluteBoundingBox") or {}
            images.append({"path": here, "id": n["id"], "ref": f["imageRef"],
                           "w": round(b.get("width", 0)), "h": round(b.get("height", 0))})
    if n["type"] in ("VECTOR", "BOOLEAN_OPERATION") and path.count("/") < 3:
        vectors.append({"path": here, "id": n["id"]})
    for ch in n.get("children", []):
        _collect(ch, texts, images, vectors, here)


def cmd_content(node_id):
    data = api_get(f"/files/{FILE_KEY}/nodes", {"ids": node_id, "depth": 30})
    texts, images, vectors = [], [], []
    for wrapper in data.get("nodes", {}).values():
        _collect(wrapper["document"], texts, images, vectors)
    print("===== TEXT NODES (%d) =====" % len(texts))
    for t in texts:
        txt = (t["text"] or "").replace("\n", " \\n ")
        print(f'[{t["id"]}] {t["font"]} {t["weight"]} {t["size"]}px lh={t["lh"]} '
              f'{t["color"]} {t["align"]}  ::  "{txt}"')
    print("\n===== IMAGE FILLS (%d) =====" % len(images))
    for im in images:
        print(f'[{im["id"]}] {im["w"]}x{im["h"]}  ref={im["ref"]}  @ {im["path"]}')
    # save mapping json
    out = os.path.join(HERE, f"content_{node_id.replace(':','-')}.json")
    with open(out, "w", encoding="utf-8") as f:
        json.dump({"texts": texts, "images": images, "vectors": vectors},
                  f, ensure_ascii=False, indent=2)
    print(f"\nSaved -> {out}")


def cmd_inspect(node_id, max_depth=6):
    data = api_get(f"/files/{FILE_KEY}/nodes", {"ids": node_id, "depth": max_depth})
    for nid, wrapper in data.get("nodes", {}).items():
        _inspect_node(wrapper["document"], 0, max_depth)


def cmd_export(node_ids, fmt="png", scale=2.0):
    out_dir = os.path.join(HERE, "assets", "exports")
    os.makedirs(out_dir, exist_ok=True)
    for nid in node_ids:
        try:
            res = api_get(f"/images/{FILE_KEY}",
                          {"ids": nid, "format": fmt, "scale": scale})
            url = (res.get("images") or {}).get(nid)
            if not url:
                print(f"  ! no image for {nid} err={res.get('err')}")
                continue
            dest = os.path.join(out_dir, f"{nid.replace(':', '-')}.{fmt}")
            n = _download(url, dest)
            print(f"  ok  {os.path.basename(dest)}  ({round(n/1024)} KB)")
        except Exception as e:
            print(f"  ! {nid}: {str(e).splitlines()[0]}")
        time.sleep(0.2)


def _slug(name, nid):
    keep = []
    for ch in name.strip():
        if ch.isalnum() or ch in " -_":
            keep.append(ch)
        elif ch.isalpha():  # keep non-latin letters (Persian)
            keep.append(ch)
    s = "".join(keep).strip().replace(" ", "_")
    s = s[:50] if s else "node"
    return f"{s}__{nid.replace(':', '-')}"


def _download(url, dest):
    req = urllib.request.Request(url, headers={"User-Agent": "phonix-figma/1.0"})
    with urllib.request.urlopen(req, timeout=300) as r:
        data = r.read()
    with open(dest, "wb") as f:
        f.write(data)
    return len(data)


def cmd_export_screens(scale=1.0, fmt="png"):
    out_dir = os.path.join(HERE, "screens")
    os.makedirs(out_dir, exist_ok=True)
    # get top-level nodes
    data = api_get(f"/files/{FILE_KEY}/nodes", {"ids": "0:1", "depth": 2})
    page = list(data["nodes"].values())[0]["document"]
    targets = []
    for c in page.get("children", []):
        t = c["type"]
        b = c.get("absoluteBoundingBox") or {}
        w = b.get("width", 0)
        # real screens/sections: frames, or large groups; skip tiny text/labels
        if t == "FRAME" or (t == "GROUP" and w >= 1000):
            targets.append(c)
    print(f"Rendering {len(targets)} screens at scale={scale} ({fmt}) ...")
    manifest = []
    for c in targets:
        name = _slug(c.get("name", "node"), c["id"])
        try:
            res = api_get(f"/images/{FILE_KEY}",
                          {"ids": c["id"], "format": fmt, "scale": scale})
            url = (res.get("images") or {}).get(c["id"])
            if not url:
                print(f"  ! no image for {c['id']} ({c.get('name')}) err={res.get('err')}")
                continue
            dest = os.path.join(out_dir, f"{name}.{fmt}")
            n = _download(url, dest)
            manifest.append({"id": c["id"], "name": c.get("name"),
                             "file": os.path.basename(dest), "bytes": n})
            print(f"  ok  {os.path.basename(dest)}  ({round(n/1024)} KB)")
        except Exception as e:
            msg = str(e).splitlines()[0] if str(e) else repr(e)
            print(f"  ! {c['id']} ({c.get('name')}): {msg}")
        time.sleep(0.3)
    with open(os.path.join(out_dir, "_manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
    print(f"Done. {len(manifest)} files -> {out_dir}")


def cmd_export_fills():
    out_dir = os.path.join(HERE, "assets", "images")
    os.makedirs(out_dir, exist_ok=True)
    res = api_get(f"/files/{FILE_KEY}/images")
    meta = res.get("meta", {})
    images = meta.get("images", {})
    print(f"Found {len(images)} raw image fills. Downloading ...")
    manifest = []
    ok = 0
    for ref, url in images.items():
        if not url:
            continue
        dest = os.path.join(out_dir, f"{ref}.png")
        try:
            n = _download(url, dest)
            manifest.append({"ref": ref, "file": os.path.basename(dest), "bytes": n})
            ok += 1
            if ok % 10 == 0:
                print(f"  ... {ok}/{len(images)}")
        except Exception as e:
            print(f"  ! failed {ref}: {e}")
    with open(os.path.join(out_dir, "_manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
    print(f"Done. {ok} image fills -> {out_dir}")


if __name__ == "__main__":
    if not TOKEN:
        print("ERROR: FIGMA_TOKEN env var is empty", file=sys.stderr)
        sys.exit(1)
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(0)
    cmd = sys.argv[1]
    if cmd == "structure":
        cmd_structure()
    elif cmd == "node" and len(sys.argv) >= 3:
        cmd_node(sys.argv[2])
    elif cmd == "content" and len(sys.argv) >= 3:
        cmd_content(sys.argv[2])
    elif cmd == "inspect" and len(sys.argv) >= 3:
        md = int(sys.argv[3]) if len(sys.argv) >= 4 else 6
        cmd_inspect(sys.argv[2], md)
    elif cmd == "export" and len(sys.argv) >= 3:
        fmt = sys.argv[3] if len(sys.argv) >= 4 else "png"
        sc = float(sys.argv[4]) if len(sys.argv) >= 5 else 2.0
        cmd_export(sys.argv[2].split(","), fmt, sc)
    elif cmd == "export-screens":
        sc = float(sys.argv[2]) if len(sys.argv) >= 3 else 1.0
        cmd_export_screens(scale=sc)
    elif cmd == "export-fills":
        cmd_export_fills()
    else:
        print(__doc__)
        sys.exit(1)
