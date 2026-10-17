"""Build the embedded heading font: Noto Serif SC, fixed at weight 600, subset to common Chinese.

Usage: python subset_font.py <NotoSerifSC-VF.ttf> <source-root> <output.ttf>
"""
import pathlib
import re
import sys

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

FAMILY = "QuickMenu Serif SC"


def gb2312_level1() -> str:
    # GB2312 level-1 hanzi: rows 0xB0-0xD7 cover the 3755 most common characters.
    chars = []
    for high in range(0xB0, 0xD8):
        for low in range(0xA1, 0xFF):
            try:
                chars.append(bytes((high, low)).decode("gb2312"))
            except UnicodeDecodeError:
                pass
    return "".join(chars)


def source_text(root: pathlib.Path) -> str:
    text = []
    for pattern in ("*.xaml", "*.cs"):
        for path in root.rglob(pattern):
            if "obj" in path.parts or "bin" in path.parts:
                continue
            text.append(path.read_text(encoding="utf-8", errors="ignore"))
    return "".join(re.findall(r"[^\x00-\x7f]", "".join(text)))


def main() -> None:
    source, root, output = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]), pathlib.Path(sys.argv[3])
    ranges = [(0x20, 0x7E), (0xA0, 0xFF), (0x2000, 0x206F), (0x2190, 0x21FF), (0x3000, 0x303F), (0xFF00, 0xFFEF)]
    chars = set(gb2312_level1()) | set(source_text(root))
    unicodes = {ord(c) for c in chars} | {cp for start, end in ranges for cp in range(start, end + 1)}

    font = TTFont(source)
    options = subset.Options()
    options.layout_features = ["*"]
    options.name_IDs = ["*"]
    options.notdef_outline = True
    subsetter = subset.Subsetter(options)
    subsetter.populate(unicodes=sorted(unicodes))
    subsetter.subset(font)

    font = instancer.instantiateVariableFont(font, {"wght": 600})
    font["OS/2"].usWeightClass = 600
    names = font["name"]
    for name_id in (16, 17, 21, 22):
        names.removeNames(nameID=name_id)
    for name_id, value in ((1, FAMILY), (2, "Regular"), (4, FAMILY), (6, "QuickMenuSerifSC-SemiBold")):
        names.setName(value, name_id, 3, 1, 0x409)
        names.setName(value, name_id, 1, 0, 0)
    output.parent.mkdir(parents=True, exist_ok=True)
    font.save(output)
    print(f"{output} {output.stat().st_size / 1024 / 1024:.2f} MB, {len(unicodes)} code points")


if __name__ == "__main__":
    main()
