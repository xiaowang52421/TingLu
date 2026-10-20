"""Inspect real Player PNG sequences; preserve their pixels in animated evidence."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image


def rgb(path):
    return np.asarray(Image.open(path).convert("RGB"), dtype=float)


def red_excess(pixels):
    return pixels[..., 0] - (pixels[..., 1] + pixels[..., 2]) / 2


def side_samples(pixels, inset):
    h, w = pixels.shape[:2]
    d = int(round(min(h, w) * inset))
    span = max(3, int(min(h, w) * .025))
    return {
        "top": pixels[d:d+2, w//2-span:w//2+span],
        "bottom": pixels[h-d-2:h-d, w//2-span:w//2+span],
        "left": pixels[h//2-span:h//2+span, d:d+2],
        "right": pixels[h//2-span:h//2+span, w-d-2:w-d],
    }


def inspect_sequence(frames):
    live = [f for f in frames if f.get("phase") != "before"]
    strongest = max(live, key=lambda f: f["alpha"])
    faded = live[-1]
    onset, end = rgb(strongest["screenshot"]), rgb(faded["screenshot"])
    delta = onset - end
    h, w = onset.shape[:2]
    checks = {}
    for side in ("top", "bottom", "left", "right"):
        outer = float(np.median(red_excess(side_samples(delta, .008)[side])))
        middle = float(np.median(red_excess(side_samples(delta, .07)[side])))
        inner = float(np.median(red_excess(side_samples(delta, .16)[side])))
        checks[side] = dict(outerRedGain=outer, middleRedGain=middle,
                            innerRedGain=inner, passed=outer > 4 and outer > middle+1 and abs(inner) < 3)
    # HUD/text/world can change between frames. The central median tolerates their
    # sparse changes while detecting an unwanted full-screen red overlay.
    center = delta[h//3:2*h//3, w//3:2*w//3]
    center_gain = float(np.median(red_excess(center)))
    pixels_over_time = [float(np.median(red_excess(side_samples(rgb(f["screenshot"]), .008)["right"]))) for f in live]
    early = [v for f, v in zip(live, pixels_over_time) if f["actualAge"] <= .181]
    differences = np.diff(early)
    flicker = bool(np.any(differences > .5) and np.any(differences < -.5))
    return dict(passed=all(v["passed"] for v in checks.values()) and abs(center_gain) < 3 and flicker,
                fourEdges=checks, centerRedGain=center_gain, flickerObserved=flicker,
                rightEdgeRedTimeline=pixels_over_time,
                strongestScreenshot=strongest["screenshot"], fadedScreenshot=faded["screenshot"])


def animate(frames, output):
    images = [Image.open(f["screenshot"]).convert("RGB") for f in frames]
    durations = []
    for i, f in enumerate(frames):
        if f.get("phase") == "before":
            durations.append(500)
        elif i == len(frames)-1:
            durations.append(650)
        else:
            durations.append(max(20, round((frames[i+1]["actualAge"]-f["actualAge"])*1000)))
    images[0].save(output, save_all=True, append_images=images[1:], duration=durations,
                   loop=0, optimize=False, disposal=2)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("evidence", type=Path)
    args = parser.parse_args()
    report = json.loads((args.evidence / "results.json").read_text(encoding="utf-8-sig"))
    output = dict(status="PASS", sourcePlayerReport=str(args.evidence / "results.json"),
                  scope="Captured Player pixels; GIF preserves PNG sequence at recorded simulation ages. Before/last frames held for review.", runs=[])
    for run in report["runs"]:
        run_result = dict(width=run["width"], height=run["height"], cases=[])
        for case in run["probe"]["collisionMatrix"]["cases"]:
            result = inspect_sequence(case["frames"])
            result["id"] = case["id"]
            gif = Path(case["fixtureJsonPath"]).parent / "collision.gif"
            animate(case["frames"], gif)
            result["animation"] = str(gif)
            result["animationSha256"] = hashlib.sha256(gif.read_bytes()).hexdigest()
            if not result["passed"]:
                output["status"] = "FAIL"
            run_result["cases"].append(result)
        output["runs"].append(run_result)
    destination = args.evidence / "visual-analysis.json"
    destination.write_text(json.dumps(output, ensure_ascii=False, indent=2), encoding="utf-8")
    print(output["status"], destination)
    raise SystemExit(0 if output["status"] == "PASS" else 1)


if __name__ == "__main__":
    main()
