"""Make art-review GIFs from the exact clip catalog exported by --qa."""
import argparse
import json
import sys
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import character_frames as frames


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('catalog', type=Path)
    parser.add_argument('-o', '--output', type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    args.output.mkdir(parents=True, exist_ok=True)
    reports = []
    for pose in json.loads(args.catalog.read_text(encoding='utf-8')):
        folder = root/'src/QuickMenu/Assets/Characters'/pose['Folder']
        for clip in pose['Clips']:
            name = f"{pose['Character']}-{pose['Pose']}-{clip['Cue'].lower()}-{clip['Index']}"
            sequence = [f"{f['File']}:{f['Seconds']}" for f in clip['Frames']]
            if not clip['Loop']:
                sequence += [f"{pose['Base']}:0.8"]
            frames.preview(SimpleNamespace(base=str(folder/pose['Base']), frames=sequence, output=str(args.output/(name+'.gif'))))
            reports.append(dict(name=name, character=pose['Character'], pose=pose['Pose'], clip=clip,
                note='Bitmap preview; vector effects are verified separately in runtime captures.'))
    (args.output/'index.json').write_text(json.dumps(reports,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')


if __name__ == '__main__':
    main()
