import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
import numpy as np
from PIL import Image
import character_frames as frames


class CharacterFramesTests(unittest.TestCase):
    def test_align_recovers_translation_on_the_full_canvas(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            base = Image.new('RGBA', (80, 120))
            base.paste((80, 110, 160, 255), (18, 20, 59, 109))
            base.save(folder/'base.png')
            moved = Image.new('RGBA', base.size); moved.paste(base, (6, -4)); moved.save(folder/'moved.png')
            with contextlib.redirect_stdout(io.StringIO()):
                frames.align(SimpleNamespace(base=folder/'base.png', generated=folder/'moved.png', region=None, output=folder/'aligned.png'))
            result = json.loads((folder/'aligned.align.json').read_text())
            self.assertEqual((result['dx'], result['dy']), (-6, 4))
            self.assertLess(result['alpha_error'], .001)

    def test_premultiplied_composition_erases_pixels_and_keeps_outside_region(self):
        base = Image.new('RGBA', (80, 80), (120, 80, 30, 255))
        generated = Image.new('RGBA', (80, 80), (250, 0, 200, 0))
        result = frames.compose(base, generated, (10, 10, 60, 60))
        self.assertEqual(result.getpixel((40, 40)), (0, 0, 0, 0))
        self.assertEqual(result.getpixel((5, 5)), base.getpixel((5, 5)))
        self.assertEqual(result.getpixel((10, 10)), base.getpixel((10, 10)))
        alpha = np.asarray(result)[:, :, 3]
        self.assertTrue(np.any((alpha > 0) & (alpha < 255)))

    def test_region_rejects_out_of_bounds(self):
        image = Image.new('RGBA', (20, 20))
        with self.assertRaises(ValueError): frames.compose(image, image, (-1, 0, 20, 20))

    def test_patch_round_trip_replaces_alpha_and_reports_seam(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            base = Image.new('RGBA', (120, 120), (40, 60, 90, 255))
            generated = base.copy()
            generated.paste((0, 0, 0, 0), (50, 50, 60, 60))
            base.save(folder/'base.png'); generated.save(folder/'frame.png')
            with contextlib.redirect_stdout(io.StringIO()):
                frames.patch(SimpleNamespace(base=folder/'base.png', aligned=folder/'frame.png', region=(20, 20, 80, 80), feather=16, output=folder/'base.erase-1.png', report=False))
            patch = json.loads((folder/'patches.json').read_text())['base.erase-1.png']
            self.assertEqual(patch['x'] % 4, 0)
            self.assertTrue(np.array_equal(np.asarray(frames.resolve(folder, 'base.erase-1.png')), np.asarray(generated)))
            self.assertEqual(frames.seam(base, frames.rgba(folder/'base.erase-1.png'), patch['x'], patch['y'])['max_delta_e'], 0)

    def test_large_changes_keep_full_frame(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            Image.new('RGBA', (120, 120), (0, 0, 0, 255)).save(folder/'base.png')
            Image.new('RGBA', (120, 120), (255, 0, 0, 255)).save(folder/'frame.png')
            with contextlib.redirect_stdout(io.StringIO()) as output:
                frames.patch(SimpleNamespace(base=folder/'base.png', aligned=folder/'frame.png', region=(0, 0, 120, 120), feather=16, output=folder/'large.png', report=False))
            self.assertTrue(json.loads(output.getvalue())['full_frame'])
            self.assertEqual(frames.rgba(folder/'large.png').size, (120, 120))

    def test_magenta_is_unmixed_and_transparent_art_is_left_unchanged(self):
        image = Image.new('RGBA', (20, 20), (255, 0, 255, 255))
        image.putpixel((10, 10), (100, 100, 100, 255))
        result, removed = frames.unmatte(image)
        self.assertTrue(removed)
        self.assertEqual(result.getpixel((0, 0))[3], 0)
        self.assertEqual(result.getpixel((10, 10)), (100, 100, 100, 255))
        clear = Image.new('RGBA', (20, 20))
        self.assertIs(frames.unmatte(clear)[0], clear)

    def test_magenta_key_preserves_existing_alpha_and_opaque_purple_costumes(self):
        clear = Image.new('RGBA', (40, 40), (255, 0, 255, 0))
        self.assertIs(frames.unmatte(clear)[0], clear)
        image = Image.new('RGBA', (80, 80), (255, 0, 255, 255))
        image.paste((180, 40, 200, 255), (20, 20, 60, 60))
        image.paste((255, 0, 255, 255), (27, 27, 33, 33))
        result, keyed = frames.unmatte(image)
        self.assertTrue(keyed)
        self.assertEqual(result.getpixel((40, 40)), (180, 40, 200, 255))
        self.assertEqual(result.getpixel((0, 0))[3], 0)
        self.assertEqual(result.getpixel((30, 30))[3], 0)

    def test_check_rejects_missing_base_and_missing_patch(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            Image.new('RGBA', (10, 10)).save(folder/'patch.png')
            (folder/'patches.json').write_text(json.dumps({'patch.png': dict(base='missing.png', x=0, y=0), 'absent.png': dict(base='missing.png', x=0, y=0)}))
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(frames.check(SimpleNamespace(folder=folder, output=None)), 1)

    def test_preview_accepts_frame_paths_and_preserves_hold_times(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            Image.new('RGBA', (20, 30), (20, 40, 60, 255)).save(folder/'base.png')
            Image.new('RGBA', (20, 30), (60, 40, 20, 255)).save(folder/'frame.png')
            with contextlib.redirect_stdout(io.StringIO()):
                frames.preview(SimpleNamespace(base=folder/'base.png', frames=[str(folder/'base.png')+':.5', str(folder/'frame.png')+':.7'], output=folder/'preview.gif'))
            with Image.open(folder/'preview.gif') as image:
                self.assertEqual(image.n_frames, 2)
                self.assertEqual(image.info['duration'], 500)
                image.seek(1)
                self.assertEqual(image.info['duration'], 700)


if __name__ == '__main__':
    unittest.main()
