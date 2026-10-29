"""Offline character alignment, premultiplied patch composition and inspectable previews."""
import argparse
import json
import math
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter, ImageDraw


def rgba(path):
    with Image.open(path) as image:
        return image.convert('RGBA')


def emit(value, output=None):
    text = json.dumps(value, ensure_ascii=False, indent=2)
    if output:
        Path(output).write_text(text + '\n', encoding='utf-8')
    print(text)
    return value


def region(value):
    box = tuple(map(int, value.split(',')))
    if len(box) != 4 or min(box[2:]) <= 0:
        raise argparse.ArgumentTypeError('Use x,y,width,height with positive dimensions.')
    return box


def unmatte(image):
    a = np.asarray(image).astype(np.float32) / 255
    corners = a[[0, 0, -1, -1], [0, -1, 0, -1], :3]
    corner_alpha = a[[0, 0, -1, -1], [0, -1, 0, -1], 3]
    if np.any(corner_alpha < .95) or not np.all(np.linalg.norm(corners - [1, 0, 1], axis=1) < .12):
        return image, False
    # Only key the connected background and its antialiasing fringe, not opaque purple hair or clothing.
    color = a[:, :, :3]
    key = Image.fromarray((np.linalg.norm(color - [1, 0, 1], axis=2) < .12).astype('uint8') * 255)
    for corner in [(0, 0), (image.width-1, 0), (0, image.height-1), (image.width-1, image.height-1)]:
        if key.getpixel(corner) == 255: ImageDraw.floodfill(key, corner, 128)
    # Exact reserved-key pixels can also occur in enclosed background holes between limbs.
    background = Image.fromarray(((np.asarray(key) == 128) | (np.linalg.norm(color - [1, 0, 1], axis=2) < 1/255)).astype('uint8') * 255)
    fringe = np.asarray(background.filter(ImageFilter.MaxFilter(5))) > 0
    matte = np.where(fringe, np.clip(np.minimum(color[:, :, 0], color[:, :, 2]) - color[:, :, 1], 0, 1), 0)
    alpha = 1 - matte
    color = np.clip((color - matte[:, :, None] * [1, 0, 1]) / np.maximum(alpha[:, :, None], 1e-6), 0, 1)
    a[:, :, :3] = color
    a[:, :, 3] *= alpha
    return Image.fromarray(np.rint(a * 255).astype('uint8')), True


def place(image, size, scale, dx, dy):
    image = image.resize((max(1, round(image.width * scale)), max(1, round(image.height * scale))), Image.Resampling.LANCZOS)
    out = Image.new('RGBA', size)
    out.paste(image, ((size[0] - image.width) // 2 + round(dx), (size[1] - image.height) // 2 + round(dy)))
    return out


def align(args):
    base = rgba(args.base)
    generated, keyed = unmatte(rgba(args.generated))
    generated = generated.resize((round(generated.width * base.height / generated.height), base.height), Image.Resampling.LANCZOS)
    ratio = min(1, 180 / base.height)
    size = (round(base.width * ratio), round(base.height * ratio))
    reference = np.asarray(base.resize(size).getchannel('A'), dtype=float) / 255
    small = generated.resize((round(generated.width * ratio), size[1]))
    mask = np.ones(reference.shape)
    if args.region:
        x, y, w, h = args.region
        mask[round(y * ratio):round((y + h) * ratio), round(x * ratio):round((x + w) * ratio)] = 0
    best = (math.inf, 1., 0, 0)
    def search(scales, xs, ys):
        nonlocal best
        for scale in scales:
            for dx in xs:
                for dy in ys:
                    alpha = np.asarray(place(small, size, scale, dx * ratio, dy * ratio).getchannel('A'), dtype=float) / 255
                    error = float(np.sum(np.abs(alpha - reference) * mask) / max(1, mask.sum()))
                    candidate = (error, float(scale), int(dx), int(dy))
                    rank = lambda c: (round(c[0], 12), abs(c[1]-1) + (abs(c[2])+abs(c[3]))*.001)
                    if rank(candidate) < rank(best):
                        best = candidate
    search(np.linspace(.97, 1.03, 7), range(-40, 41, 8), range(-40, 41, 8))
    _, s, x, y = best
    search(np.clip(np.arange(s - .008, s + .009, .002), .97, 1.03), range(max(-40, x - 7), min(40, x + 7) + 1, 2), range(max(-40, y - 7), min(40, y + 7) + 1, 2))
    # Recheck the neighbourhood at the actual canvas resolution; thumbnail quantization can hide a few pixels.
    _, s, x, y = best
    ratio, size = 1, base.size
    reference = np.asarray(base.getchannel('A'), dtype=float) / 255
    small = generated
    mask = np.ones(reference.shape)
    if args.region:
        rx, ry, rw, rh = args.region; mask[ry:ry+rh, rx:rx+rw] = 0
    best = (math.inf, 1., 0, 0)
    search(np.clip(np.linspace(s-.004, s+.004, 5), .97, 1.03), range(max(-40,x-4), min(40,x+4)+1, 2), range(max(-40,y-4), min(40,y+4)+1, 2))
    _, s, x, y = best
    search(np.unique(np.clip(np.r_[np.linspace(s-.004, s+.004, 5), 1.], .97, 1.03)), range(max(-40,x-1), min(40,x+1)+1), range(max(-40,y-1), min(40,y+1)+1))
    out = Path(args.output)
    out.parent.mkdir(parents=True, exist_ok=True)
    place(generated, base.size, *best[1:]).save(out)
    emit(dict(output=str(out), alpha_error=best[0], scale=best[1], dx=best[2], dy=best[3], magenta_removed=keyed), out.with_suffix('.align.json'))


def premultiplied(image):
    a = np.asarray(image).astype(np.float64) / 255
    return np.concatenate((a[:, :, :3] * a[:, :, 3:], a[:, :, 3:]), axis=2)


def compose(base, generated, box, feather=16):
    if base.size != generated.size:
        raise ValueError('Align the frame to the base canvas first.')
    x, y, w, h = box
    if x < 0 or y < 0 or x + w > base.width or y + h > base.height:
        raise ValueError('The edit region must fit inside the canvas.')
    yy, xx = np.mgrid[:h, :w]
    distance = np.minimum.reduce([xx, yy, w - 1 - xx, h - 1 - yy]).astype(float)
    blend = np.clip(distance / max(1, feather), 0, 1)
    blend = blend * blend * (3 - 2 * blend)
    blend = blend[:, :, None]
    b, f = premultiplied(base), premultiplied(generated)
    mix = b[y:y+h, x:x+w] * (1 - blend) + f[y:y+h, x:x+w] * blend
    alpha = mix[:, :, 3:]
    color = np.divide(mix[:, :, :3], alpha, out=np.zeros_like(mix[:, :, :3]), where=alpha > 0)
    out = np.array(base)
    out[y:y+h, x:x+w] = np.rint(np.clip(np.concatenate((color, alpha), axis=2), 0, 1) * 255).astype('uint8')
    return Image.fromarray(out)


def lab(rgb):
    linear = np.where(rgb <= .04045, rgb / 12.92, ((rgb + .055) / 1.055) ** 2.4)
    xyz = linear @ np.array([[.4124564, .3575761, .1804375], [.2126729, .7151522, .0721750], [.0193339, .1191920, .9503041]]).T
    xyz /= [.95047, 1, 1.08883]
    f = np.where(xyz > (6/29)**3, np.cbrt(xyz), xyz / (3 * (6/29)**2) + 4/29)
    return np.stack((116*f[..., 1]-16, 500*(f[..., 0]-f[..., 1]), 200*(f[..., 1]-f[..., 2])), axis=-1)


def seam(base, patch, x, y):
    b = premultiplied(base.crop((x, y, x+patch.width, y+patch.height)))
    p = premultiplied(patch)
    ring = np.zeros(b.shape[:2], dtype=bool)
    ring[:2] = ring[-2:] = True
    ring[:, :2] = ring[:, -2:] = True
    differences = []
    for background in (0., 1.):
        delta = lab(b[:, :, :3] + (1-b[:, :, 3:]) * background) - lab(p[:, :, :3] + (1-p[:, :, 3:]) * background)
        differences.extend(np.linalg.norm(delta[ring], axis=1).tolist())
    return dict(mean_delta_e=float(np.mean(differences)), max_delta_e=float(np.max(differences)))


def patch(args):
    base, frame = rgba(args.base), rgba(args.aligned)
    composed = compose(base, frame, args.region, args.feather)
    difference = np.any(np.asarray(base) != np.asarray(composed), axis=2)
    yy, xx = np.where(difference)
    if not len(xx):
        raise ValueError('The frame contains no changes inside the allowed region.')
    box = (max(0, (int(xx.min())-8)//4*4), max(0, (int(yy.min())-8)//4*4), min(base.width, (int(xx.max())+12)//4*4), min(base.height, (int(yy.max())+12)//4*4))
    fraction = (box[2]-box[0])*(box[3]-box[1]) / (base.width*base.height)
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    manifest = output.parent / 'patches.json'
    data = json.loads(manifest.read_text(encoding='utf-8')) if manifest.exists() else {}
    if fraction > .6:
        composed.save(output)
        data.pop(output.name, None)
        report = dict(file=output.name, full_frame=True, fraction=fraction, reason='Patch covers more than 60 percent of the canvas.')
    else:
        result = composed.crop(box)
        result.save(output)
        entry = dict(base=Path(args.base).name, x=box[0], y=box[1])
        data[output.name] = entry
        report = dict(file=output.name, full_frame=False, fraction=fraction, **entry, **seam(base, result, box[0], box[1]))
    manifest.write_text(json.dumps(data, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    emit(report, output.with_suffix('.check.json') if args.report else None)


def resolve(folder, file):
    manifest = folder/'patches.json'
    data = json.loads(manifest.read_text(encoding='utf-8')) if manifest.exists() else {}
    image = rgba(folder/file)
    if file not in data:
        return image
    entry = data[file]
    base = rgba(folder/entry['base'])
    base.paste(image, (entry['x'], entry['y']))  # Replace alpha as well; source-over cannot erase old pixels.
    return base


def check(args):
    folder = Path(args.folder)
    manifest = folder/'patches.json'
    data = json.loads(manifest.read_text(encoding='utf-8')) if manifest.exists() else {}
    results, errors = [], []
    for file in sorted(folder.glob('*.png')):
        image = rgba(file)
        pixels = np.asarray(image)
        entry = dict(file=file.name, width=image.width, height=image.height)
        if file.name in data:
            p = data[file.name]
            basefile = folder/p['base']
            if not basefile.exists() or p['base'] in data:
                errors.append(f'{file.name}: missing or non-full base'); continue
            base = rgba(basefile)
            fits = p['x'] >= 0 and p['y'] >= 0 and p['x']+image.width <= base.width and p['y']+image.height <= base.height
            if not fits: errors.append(f'{file.name}: patch outside canvas'); continue
            entry.update(patch=True, **seam(base, image, p['x'], p['y']))
            if entry['max_delta_e'] >= 3: errors.append(f'{file.name}: seam delta E >= 3')
        elif file.name != 'avatar.png':
            corners = pixels[[0, 0, -1, -1], [0, -1, 0, -1], 3]
            if image.height != 1200 or np.any(corners): errors.append(f'{file.name}: canvas or opaque corners')
        edge = (pixels[:, :, 3] > 0) & (pixels[:, :, 3] < 240)
        entry['magenta_edge_pixels'] = int(np.sum(edge & (pixels[:, :, 0] > 220) & (pixels[:, :, 1] < 40) & (pixels[:, :, 2] > 220)))
        entry['white_edge_pixels'] = int(np.sum(edge & np.all(pixels[:, :, :3] > 245, axis=2)))
        entry['edge_note'] = 'Counts are inspection hints; white costume highlights cannot be classified as matte residue from color alone.'
        results.append(entry)
    for name in data:
        if not (folder/name).exists(): errors.append(f'{name}: manifest entry has no patch')
    total = sum(p.stat().st_size for p in folder.rglob('*') if p.is_file())
    if total > 40_000_000: errors.append('Folder exceeds 40 MB')
    emit(dict(folder=str(folder), files=results, bytes=total, errors=errors, passed=not errors), args.output)
    return 1 if errors else 0


def preview(args):
    base = Path(args.base)
    images, durations = [], []
    for item in args.frames:
        name, seconds = item.rsplit(':', 1)
        source = Path(name)
        image = resolve(source.parent, source.name) if source.is_file() else resolve(base.parent, name)
        if image.size != rgba(base).size: raise ValueError('Frame canvas does not match the base.')
        if float(seconds) <= 0: raise ValueError('Frame holds must be positive.')
        image.thumbnail((600, 600))
        canvas = Image.new('RGB', image.size, '#eeeeee')
        canvas.paste(image, mask=image.getchannel('A'))
        images.append(canvas); durations.append(round(float(seconds)*1000))
    Path(args.output).parent.mkdir(parents=True, exist_ok=True)
    images[0].save(args.output, save_all=True, append_images=images[1:], duration=durations, loop=0, disposal=2)
    emit(dict(output=str(args.output), durations_ms=durations))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    subs = parser.add_subparsers(dest='command', required=True)
    p = subs.add_parser('align'); p.add_argument('base'); p.add_argument('generated'); p.add_argument('--region', type=region); p.add_argument('-o', '--output', required=True)
    p = subs.add_parser('patch'); p.add_argument('base'); p.add_argument('aligned'); p.add_argument('--region', type=region, required=True); p.add_argument('--feather', type=int, default=16); p.add_argument('--report', action='store_true'); p.add_argument('-o', '--output', required=True)
    p = subs.add_parser('check'); p.add_argument('folder'); p.add_argument('-o', '--output')
    p = subs.add_parser('preview'); p.add_argument('base'); p.add_argument('frames', nargs='+'); p.add_argument('-o', '--output', required=True)
    args = parser.parse_args()
    return globals()[args.command](args) or 0


if __name__ == '__main__':
    raise SystemExit(main())
