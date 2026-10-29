"""Export native-size and 200% art crops on light/dark mattes from the compiled QA catalog."""
import argparse
import json
import sys
from pathlib import Path
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from character_frames import resolve, rgba


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('catalog', type=Path)
    p.add_argument('-o', '--output', type=Path, required=True)
    args = p.parse_args()
    root = Path(__file__).resolve().parents[2]
    seen, report = set(), []
    for pose in json.loads(args.catalog.read_text(encoding='utf-8')):
        folder = root/'src/QuickMenu/Assets/Characters'/pose['Folder']
        patches = json.loads((folder/'patches.json').read_text(encoding='utf-8'))
        for clip in pose['Clips']:
            for frame in clip['Frames']:
                file = frame['File']; key = (pose['Folder'], file)
                if key in seen or file == pose['Base']: continue
                seen.add(key)
                picture = resolve(folder, file)
                patch = patches.get(file)
                if patch:
                    basis = rgba(folder/patch['base']); delta = rgba(folder/file)
                    box = (max(0,patch['x']-16),max(0,patch['y']-16),min(basis.width,patch['x']+delta.width+16),min(basis.height,patch['y']+delta.height+16))
                else:
                    basis = rgba(folder/pose['Base']); box=(0,0,*basis.size)
                dest=args.output/pose['Folder']; dest.mkdir(parents=True,exist_ok=True)
                outputs=[]
                for scale in (1,2):
                    for matte in ('white','#25252d'):
                        before,after=basis.crop(box),picture.crop(box)
                        canvas=Image.new('RGB',(before.width*2,before.height+24),matte)
                        canvas.paste(before,(0,24),before.getchannel('A'));canvas.paste(after,(before.width,24),after.getchannel('A'))
                        ImageDraw.Draw(canvas).text((4,4),'BASE | '+file,fill='black' if matte=='white' else 'white')
                        if scale==2: canvas=canvas.resize((canvas.width*2,canvas.height*2),Image.Resampling.NEAREST)
                        output=dest/(Path(file).stem+f'-{scale*100}-'+('light' if matte=='white' else 'dark')+'.png')
                        canvas.save(output);outputs.append(str(output))
                report.append(dict(folder=pose['Folder'],file=file,outputs=outputs,note='Review artifacts only; never embedded in the application.'))
    (args.output/'index.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'{len(report)} unique frames exported at 100% and 200% on both mattes.')


if __name__=='__main__': main()
