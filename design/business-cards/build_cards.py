#!/usr/bin/env python3
"""Create the 90 × 50 mm Mirovoy Internet business cards.

Dependencies: reportlab, PyMuPDF; Noto Sans Regular/Bold fonts.
No working QR code is embedded: replace qr_placeholder() before printing.
"""
from pathlib import Path
import hashlib
import json
import math
import re
import zipfile

import fitz
from reportlab.lib.colors import HexColor
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas

ROOT = Path(__file__).resolve().parent
FONT_DIR = Path('/usr/share/fonts/truetype/noto')
for name, filename in [('Regular', 'NotoSans-Regular.ttf'), ('Bold', 'NotoSans-Bold.ttf')]:
    pdfmetrics.registerFont(TTFont(name, str(FONT_DIR / filename)))

BG = '#090D1C'
TEXT = '#ECF3FF'
MUTED = '#A7B7D0'
CYAN = '#55D8F1'
VIOLET = '#7C8EFA'
FOOTER = 'сервис не является ВПН, прокси или средством обхода'
QR_TARGET = 'https://moskompremont.ru/account.html'


class Face:
    """Drawing coordinates in millimetres from the top left of the trim."""
    def __init__(self, pdf, bleed=0):
        self.c = pdf
        self.bleed = bleed

    def xy(self, x, y):
        return (x + self.bleed) * mm, (50 + self.bleed - y) * mm

    def line(self, points, color=CYAN, width=.22, alpha=1):
        c = self.c
        c.saveState()
        c.setStrokeColor(HexColor(color))
        c.setStrokeAlpha(alpha)
        c.setLineWidth(width * mm)
        c.setLineCap(1)
        c.setLineJoin(1)
        p = c.beginPath()
        p.moveTo(*self.xy(*points[0]))
        for point in points[1:]:
            p.lineTo(*self.xy(*point))
        c.drawPath(p)
        c.restoreState()

    def circle(self, x, y, radius, stroke=CYAN, fill=None, width=.25):
        c = self.c
        c.saveState()
        c.setLineWidth(width * mm)
        if stroke:
            c.setStrokeColor(HexColor(stroke))
        if fill:
            c.setFillColor(HexColor(fill))
        c.circle(*self.xy(x, y), radius * mm, stroke=bool(stroke), fill=bool(fill))
        c.restoreState()

    def rect(self, x, y, w, h, fill=None, stroke=None, radius=0, width=.2):
        c = self.c
        c.saveState()
        c.setLineWidth(width * mm)
        if fill:
            c.setFillColor(HexColor(fill))
        if stroke:
            c.setStrokeColor(HexColor(stroke))
        px, py = self.xy(x, y+h)
        if radius:
            c.roundRect(px, py, w*mm, h*mm, radius*mm, stroke=bool(stroke), fill=bool(fill))
        else:
            c.rect(px, py, w*mm, h*mm, stroke=bool(stroke), fill=bool(fill))
        c.restoreState()

    def text(self, x, y, content, size=7, font='Regular', color=TEXT, align='left'):
        c = self.c
        c.setFillColor(HexColor(color))
        c.setFont(font, size)
        px, py = self.xy(x, y)
        if align == 'center':
            c.drawCentredString(px, py, content)
        else:
            c.drawString(px, py, content)

    def neon(self, points, color):
        for width, alpha in [(2.4, .035), (1.2, .07), (.6, .12), (.16, .85)]:
            self.line(points, color, width, alpha)

    def background(self):
        b = self.bleed
        self.rect(-b, -b, 90+2*b, 50+2*b, BG)
        # Quiet geometric accents extend into the bleed, away from copy.
        self.neon([(-3, 11), (11, -3)], CYAN)
        self.neon([(79, 53), (93, 39)], '#A36DFC')
        self.line([(56, 4), (80, 4), (81.5, 2.5), (85, 2.5)], CYAN, .18, .65)
        self.circle(85.8, 2.5, .65, CYAN, BG, .22)

    def footer(self):
        self.line([(5.5, 43.5), (84.5, 43.5)], '#293550', .16)
        self.text(45, 46.2, FOOTER, 6.2, color=MUTED, align='center')

    def qr_placeholder(self):
        x, y, s = 59.5, 12.2, 24.5
        self.rect(x, y, s, s, '#F7FBFF', radius=.9)
        # Deliberately empty finder-corner design. This is not a QR payload.
        for dx, dy in [(1.5, 1.5), (17.5, 1.5), (1.5, 17.5)]:
            self.rect(x+dx, y+dy, 5.5, 5.5, BG)
            self.rect(x+dx+.8, y+dy+.8, 3.9, 3.9, '#F7FBFF')
            self.rect(x+dx+1.6, y+dy+1.6, 2.3, 2.3, BG)
        self.text(x+s/2, y+14.4, 'QR', 14, 'Bold', BG, 'center')
        self.text(x+s/2, 40.4, 'Личный кабинет', 7.2, 'Bold', align='center')

    def icon(self, kind, x, y):
        if kind == 'person':
            self.circle(x+3, y+1.6, 1.5)
            pts = [(x, y+7.1)]
            for n in range(17):
                a = math.pi - n*math.pi/16
                pts.append((x+3+3*math.cos(a), y+7.1-3*math.sin(a)))
            pts += [(x+6, y+7.1), (x, y+7.1)]
            self.line(pts)
        elif kind == 'clock':
            self.circle(x+3.2, y+3.6, 3.2)
            self.line([(x+3.2, y+1.3), (x+3.2, y+3.6), (x+4.8, y+4.6)])
        elif kind == 'shield':
            self.line([(x+3.1,y), (x+6.1,y+1.2), (x+5.8,y+4.7), (x+4.8,y+6.2),
                       (x+3.1,y+7.2), (x+1.4,y+6.2), (x+.4,y+4.7), (x+.1,y+1.2), (x+3.1,y)])
            self.line([(x+1.5,y+3.6), (x+2.7,y+4.8), (x+4.9,y+2.5)])
        elif kind == 'apps':
            self.rect(x, y+.5, 5.5, 4.2, stroke=CYAN, radius=.3, width=.25)
            self.line([(x+2.7,y+4.7), (x+2.7,y+6.5)])
            self.line([(x+1.4,y+6.5), (x+3.8,y+6.5)])
            self.rect(x+4.6, y+3, 2.4, 4.5, fill=BG, stroke=CYAN, radius=.3, width=.25)

    def front(self):
        self.background()
        px, py = self.xy(4.8, 15.3)
        self.c.drawImage(str(ROOT/'logo.png'), px, py, width=11.5*mm, height=11.5*mm,
                         mask='auto', preserveAspectRatio=True)
        self.line([(17.7, 6.9), (17.7, 12.1)], VIOLET, .25)
        self.text(20, 10.6, 'МОСКОМПРЕМОНТ', 7.4, 'Bold', CYAN)
        self.text(5.5, 23, 'Мировой', 21, 'Bold')
        self.text(5.5, 31.2, 'интернет', 21, 'Bold')
        self.text(5.5, 36.5, 'Ваш сервис всегда под рукой', 6.9, color=MUTED)
        self.text(5.5, 41.7, 'moskompremont.ru', 9.1, 'Bold', CYAN)
        self.qr_placeholder()
        self.footer()

    def back(self):
        self.background()
        self.text(5.5, 6.1, 'МИРОВОЙ ИНТЕРНЕТ', 5.8, 'Bold', CYAN)
        self.text(5.5, 13, 'Всё для вашего удобства', 13.3, 'Bold')
        features = [
            (5.5, 19.1, 'person', 'Личный кабинет', 'Подписки, оплата и поддержка'),
            (48, 19.1, 'clock', 'Сайт доступен 24/7', 'В удобное для вас время'),
            (5.5, 31.8, 'shield', 'Стабильная работа', 'Надёжность каждый день'),
            (48, 31.8, 'apps', 'Свои приложения', 'Windows · macOS · Android'),
        ]
        for x, y, kind, title, detail in features:
            self.icon(kind, x, y)
            self.line([(x+8, y+.2), (x+8, y+7.7)], VIOLET, .17, .65)
            self.text(x+10, y+2.9, title, 7.25, 'Bold')
            self.text(x+10, y+6.7, detail, 5.35, color=MUTED)
        self.footer()


def make_pdf(filename, bleed):
    size = ((90+2*bleed)*mm, (50+2*bleed)*mm)
    c = canvas.Canvas(str(ROOT/filename), pagesize=size, pageCompression=1)
    c.setTitle('Мировой интернет — визитка 90 × 50 мм')
    c.setAuthor('МосКомпРемонт')
    c.setSubject('Две стороны; QR-заглушка для личного кабинета')
    for side in ['front', 'back']:
        c.setTrimBox((bleed*mm, bleed*mm, (90+bleed)*mm, (50+bleed)*mm))
        c.setBleedBox((0, 0, size[0], size[1]))
        getattr(Face(c, bleed), side)()
        c.showPage()
    c.save()


def main():
    make_pdf('mirovoy-internet-90x50.pdf', 0)
    make_pdf('mirovoy-internet-90x50-bleed-3mm.pdf', 3)
    with fitz.open(ROOT/'mirovoy-internet-90x50.pdf') as doc:
        for n, name in enumerate(['front', 'back']):
            doc[n].get_pixmap(dpi=450).save(ROOT/f'{name}-preview.png')
            svg = doc[n].get_svg_image(text_as_path=True)
            svg = svg.replace(f'width="{doc[n].rect.width:g}pt"', 'width="90mm"')
            svg = svg.replace(f'height="{doc[n].rect.height:g}pt"', 'height="50mm"')
            # Explicit physical size, independently of MuPDF float formatting.
            svg = re.sub(r'width="[^"]+" height="[^"]+"', 'width="90mm" height="50mm"', svg, count=1)
            (ROOT/f'{name}.svg').write_text(svg, encoding='utf-8')
        # A document preview sheet, preserving both actual 90:50 card proportions.
        preview = fitz.open()
        sheet = preview.new_page(width=110*mm, height=126*mm)
        sheet.draw_rect(sheet.rect, color=None, fill=(.92,.94,.97))
        for n, top in [(0, 8), (1, 68)]:
            sheet.show_pdf_page(fitz.Rect(10*mm, top*mm, 100*mm, (top+50)*mm), doc, n)
        preview.save(ROOT/'preview-sheet.pdf', garbage=4, deflate=True)
        sheet.get_pixmap(dpi=220).save(ROOT/'preview-sheet.png')

    manifest = {
        'service': 'Мировой интернет',
        'trim_mm': [90,50],
        'bleed_mm': 3,
        'pages': ['Лицевая сторона', 'Оборотная сторона'],
        'qr': {'type':'placeholder', 'future_target': QR_TARGET},
        'colors': {'background': BG, 'cyan': CYAN, 'violet': VIOLET, 'text': TEXT},
        'footer': FOOTER,
    }
    (ROOT/'design.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    archive = ROOT/'mirovoy-internet-business-cards.zip'
    files = [
        'mirovoy-internet-90x50.pdf', 'mirovoy-internet-90x50-bleed-3mm.pdf',
        'front.svg', 'back.svg', 'front-preview.png', 'back-preview.png',
        'preview-sheet.png', 'design-concept.png', 'logo.png',
        'build_cards.py', 'design.json', 'README.txt',
    ]
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for file in files:
            z.write(ROOT/file, arcname=f'mirovoy-internet-cards/{file}')
    print(json.dumps({'zip': str(archive), 'bytes': archive.stat().st_size,
                      'sha256': hashlib.sha256(archive.read_bytes()).hexdigest()}, indent=2))


if __name__ == '__main__':
    main()
