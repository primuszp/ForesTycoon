"""Lay out actual OpenGL captures; never synthesize or retouch tree geometry."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[1] / 'artifacts' / 'tree-life-stages'
font_path = 'C:/Windows/Fonts/arial.ttf'
font = ImageFont.truetype(font_path, 24)
small = ImageFont.truetype(font_path, 18)
stages = [('Seedling', 'Csemete'), ('Young', 'Fiatal'), ('Mature', 'Középkorú'), ('Old', 'Idős')]
for species, title in [('Spruce', 'Lucfenyő'), ('Oak', 'Tölgy'), ('Birch', 'Nyír'), ('Beech', 'Bükk')]:
    sheet = Image.new('RGB', (1200, 1160), '#eef1eb')
    draw = ImageDraw.Draw(sheet)
    draw.text((24, 12), title + ' – 4 életfázis × 3 változat', fill='#243b2d', font=font)
    draw.text((24, 48), 'Azonos magasságra hozott modellek: itt a szerkezet különbsége látható.', fill='#243b2d', font=small)
    for row, (stage, label) in enumerate(stages):
        y = 85 + row * 265
        draw.text((24, y), label, fill='#243b2d', font=font)
        for col in range(3):
            draw.text((190 + col * 380, y), str(col + 1) + '. változat', fill='#243b2d', font=small)
        with Image.open(root / f'{species}-{stage}.png') as source:
            sheet.paste(source.crop((0, 270, 1200, 500)), (0, y + 32))
    sheet.save(root / f'{species}-comparison.png')
    print(root / f'{species}-comparison.png')
