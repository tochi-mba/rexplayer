#!/usr/bin/env python3
"""Build an offline, accessible review index for Windows screenshot evidence.

This does not approve a baseline or infer visual correctness from a hash.
Reviewers inspect the PNGs themselves; the index keeps the associated oracles
and missing evidence visible next to the actual, repeated, baseline and diff.
"""
import argparse
import html
import json
from pathlib import Path


def build(inventory_path: Path, root: Path, output: Path) -> int:
    scenes = json.loads(inventory_path.read_text(encoding="utf-8"))["scenes"]
    output.parent.mkdir(parents=True, exist_ok=True)
    cards = []
    missing = 0
    for scene in scenes:
        lane = scene.get("requiredLane", "ci-windows")
        base = root / lane
        metadata_path = base / "actual" / (scene["id"] + ".json")
        evidence = {}
        if metadata_path.is_file():
            try:
                evidence = json.loads(metadata_path.read_text(encoding="utf-8"))
            except (ValueError, OSError) as error:
                evidence = {"metadataError": str(error)}
        else:
            missing += 1

        expected_text = scene["expectedVisibleData"]
        observed_text = evidence.get("observedVisibleData")
        if not expected_text:
            semantic_status = "no expected text"
        elif not isinstance(observed_text, list):
            semantic_status = "NOT CAPTURED"
        else:
            seen = {item["automationId"]: item for item in observed_text
                    if isinstance(item, dict) and "automationId" in item}
            mismatches = []
            for expected in expected_text:
                control = seen.get(expected["automationId"], {})
                actual_name = control.get("Name", control.get("name"))
                if not control.get("shown") or actual_name != expected["name"]:
                    mismatches.append(
                        f'{expected["automationId"]}: expected {expected["name"]!r}, '
                        f'observed {actual_name!r}, visible={control.get("shown", "unknown")}'
                    )
            semantic_status = "MATCH" if not mismatches else "MISMATCH: " + " | ".join(mismatches)

        actual_scene = evidence.get("sceneId")
        identity_status = ("MATCH" if actual_scene == scene["id"]
                           else "NOT CAPTURED" if actual_scene is None
                           else f'MISMATCH: metadata belongs to {actual_scene!r}')

        fields = [
            ("Scene identity", identity_status),
            ("Semantic text witness", semantic_status),
            ("Fixture", scene["fixture"]),
            ("Route / state", f'{scene["route"]} / {scene["state"]}'),
            ("Viewport / theme", f'{scene["dimensions"]} / {scene["theme"]}'),
            ("Lane", lane),
            ("Declared UIA", ", ".join(scene["expectedAutomationIds"])),
            ("Expected text", ", ".join(f'{item["automationId"]}: {item["name"]}'
                                      for item in scene["expectedVisibleData"]) or "none"),
            ("Readiness", str(evidence.get("readinessAchieved", "not captured"))),
            ("Pixel witness", str(evidence.get("pixelReadinessAchieved", "not captured"))),
            ("Repeatable", str(evidence.get("repeatabilityAchieved", "not captured"))),
            ("Declared baseline state", scene.get("baseline", "not declared")),
            ("Baseline file witness", str(evidence.get("baselineExists", "not captured"))),
            ("DPI / contrast", f'{evidence.get("measuredDpi", "not captured")} / '
                               f'{evidence.get("actualHighContrast", "not captured")}'),
        ]
        if scene["id"].startswith("stage-"):
            controls = evidence.get("observedStageAffordances")
            witnesses = []
            if isinstance(controls, list):
                for control in controls:
                    if not isinstance(control, dict):
                        continue
                    name = control.get("automationId", "?")
                    shown = control.get("shown", "?")
                    enabled = control.get("IsEnabled", control.get("isEnabled", "?"))
                    focused = control.get("HasKeyboardFocus", control.get("hasKeyboardFocus", "?"))
                    spoken_name = control.get("Name", control.get("name", ""))
                    help_text = control.get("HelpText", control.get("helpText", ""))
                    witnesses.append(
                        f"{name}: visible={shown}, enabled={enabled}, focused={focused}, "
                        f"name={spoken_name}, help={help_text}"
                    )
            fields.append(("Observed stage UI Automation", " | ".join(witnesses) or "not captured"))

        metadata = "".join(f"<dt>{html.escape(k)}</dt><dd>{html.escape(str(v))}</dd>"
                           for k, v in fields)
        images = []
        for folder in ("window", "actual", "repeat", "baseline", "diff", "repeat-diff"):
            picture = base / folder / (scene["id"] + ".png")
            if picture.is_file():
                rel = Path("..") / lane / folder / (scene["id"] + ".png")
                src = html.escape(rel.as_posix(), quote=True)
                title = html.escape(folder)
                images.append(f'<figure><a href="{src}"><img src="{src}" loading="lazy" '
                              f'alt="{html.escape(scene["id"])} {title} screenshot"></a>'
                              f'<figcaption>{title}</figcaption></figure>')
        if not (base / "actual" / (scene["id"] + ".png")).is_file():
            missing += 1
            images.append('<p class="missing">MISSING CURRENT IMAGE - not approved</p>')
        cards.append(f'<article><h2>{html.escape(scene["id"])}</h2>'
                     f'<dl>{metadata}</dl><div class="pictures">{"".join(images)}</div></article>')

    document = """<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport"
content="width=device-width, initial-scale=1"><title>Windows screenshot evidence review</title>
<style>
body{font:16px/1.5 system-ui,sans-serif;max-width:1300px;padding:20px;margin:auto;color:#20232a}
h1{font-size:1.8rem}h2{font-size:1.25rem;overflow-wrap:anywhere}
article{border-top:2px solid #777;padding:20px 0 28px}
dl{display:grid;grid-template-columns:minmax(150px,200px) 1fr;gap:2px 18px}
dt{font-weight:700}dd{margin:0;overflow-wrap:anywhere}
.pictures{display:flex;flex-wrap:wrap;gap:16px}figure{margin:0;flex:1 1 270px;max-width:590px}
img{max-width:100%;height:auto;border:1px solid #444}
figcaption{font-weight:700}.missing{font-weight:700;text-decoration:underline}
a:focus-visible{outline:3px solid #1556ad;outline-offset:3px}
</style></head><body><main>
<h1>Windows screenshot evidence review</h1>
<p>Images are unapproved until a reviewer inspects the pixels and the semantic
witnesses. A baseline's existence, repeated pixels, hashes and dimensions are
not visual approval. Dedicated DPI and contrast lanes must be separately run.</p>
""" + "".join(cards) + "</main></body></html>\n"
    output.write_text(document, encoding="utf-8")
    print(f"Indexed {len(scenes)} scene contracts and identified {missing} missing evidence files: {output}")
    return missing


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--inventory", type=Path, default=Path("tests/screenshot-scenes.json"))
    parser.add_argument("--root", type=Path, default=Path("artifacts/screenshots"))
    parser.add_argument("--output", type=Path, default=Path("artifacts/screenshots/review/index.html"))
    args = parser.parse_args()
    build(args.inventory, args.root, args.output)


if __name__ == "__main__":
    main()
