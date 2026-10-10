"""Рисует assets/stress_meter.png — текстуру шкалы стресса (StressMeterHudService).

Раскладка листа 16×8 (координаты дублируются в StressMeterHudService):
  0,0  8×8 — рамка для 9-slice, бордюр 2 px, центр прозрачный;
  8,0  4×8 — заливка горизонтальной шкалы: блик сверху, тень снизу (серая, тонируется цветом стресса);
 12,0  4×4 — то же для вертикальной шкалы: блик слева.
Запуск: python scripts/generate_stress_meter.py
"""
import os

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'assets', 'stress_meter.png')

OUTLINE = (86, 22, 12, 255)    # тёмно-коричневый контур, как у ванильных шкал HUD
BEVEL = (246, 180, 92, 255)    # светлая «деревянная» фаска
CORNER = (178, 98, 38, 255)    # мягкий угол фаски

# Яркость заливки по поперечной оси: блик → тело → тень.
SHADE = [255, 236, 214, 214, 205, 196, 178, 150]
SHADE_SHORT = [255, 224, 205, 170]

img = Image.new('RGBA', (16, 8), (0, 0, 0, 0))
px = img.load()

for x in range(8):
    for y in range(8):
        edge = min(x, y, 7 - x, 7 - y)
        if edge == 0:
            px[x, y] = OUTLINE
        elif edge == 1:
            px[x, y] = BEVEL
# Скруглённые углы: внешние пиксели угла прозрачные, фаска на углу темнее.
for cx, cy in ((0, 0), (7, 0), (0, 7), (7, 7)):
    px[cx, cy] = (0, 0, 0, 0)
for cx, cy in ((1, 1), (6, 1), (1, 6), (6, 6)):
    px[cx, cy] = CORNER

for y, v in enumerate(SHADE):
    for x in range(8, 12):
        px[x, y] = (v, v, v, 255)

for x, v in enumerate(SHADE_SHORT):
    for y in range(4):
        px[12 + x, y] = (v, v, v, 255)

img.save(OUT)
print('written', OUT)
