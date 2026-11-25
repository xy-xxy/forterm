#!/usr/bin/env python3
"""Draws the Studio icon (a prompt on a dark tile) into src/Forterm.Studio/Assets/forterm.ico.
Standard library only, and the same bytes every time."""
import pathlib, struct, zlib, math
OUT = pathlib.Path(__file__).resolve().parents[1] / 'src/Forterm.Studio/Assets/forterm.ico'
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

def segment(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return math.hypot(px - ax - t * dx, py - ay - t * dy)

def rounded(px, py, half, radius):
    qx, qy = abs(px - .5) - half + radius, abs(py - .5) - half + radius
    return math.hypot(max(qx, 0), max(qy, 0)) + min(max(qx, qy), 0) - radius

def pixel(x, y, size):
    """Straight RGBA of one pixel, from 5 x 5 samples; small sizes get heavier strokes."""
    weight = .062 if size >= 48 else .075 if size >= 24 else .09
    r = g = b = a = 0.0
    for i in range(5):
        for j in range(5):
            px, py = (x + (i + .5) / 5) / size, (y + (j + .5) / 5) / size
            if rounded(px, py, .5 if size < 32 else .48, .22) > 0: continue
            c = (0x1B + (0x0B - 0x1B) * py, 0x3A + (0x1E - 0x3A) * py, 0x42 + (0x26 - 0x42) * py)
            if min(segment(px, py, .25, .30, .45, .50), segment(px, py, .45, .50, .25, .70)) < weight: c = (0x43, 0xC9, 0x86)
            elif segment(px, py, .54, .70, .75, .70) < weight: c = (0xF2, 0xF6, 0xF5)
            r += c[0]; g += c[1]; b += c[2]; a += 1
    if a == 0: return (0, 0, 0, 0)
    return (round(r / a), round(g / a), round(b / a), round(255 * a / 25))

def png(rows, size):
    raw = b''.join(b'\0' + b''.join(bytes(p) for p in row) for row in rows)
    chunk = lambda kind, data: struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')

def bitmap(rows, size):
    """32-bit BMP without its file header, bottom up, followed by an empty mask: what older icon readers expect."""
    head = struct.pack('<IiiHHIIiiII', 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    body = b''.join(bytes((p[2], p[1], p[0], p[3])) for row in reversed(rows) for p in row)
    return head + body + b'\0' * (((size + 31) // 32) * 4 * size)

images = []
for size in SIZES:
    rows = [[pixel(x, y, size) for x in range(size)] for y in range(size)]
    images.append((size, png(rows, size) if size >= 128 else bitmap(rows, size)))
offset = 6 + 16 * len(images)
head = struct.pack('<HHH', 0, 1, len(images)); body = b''
for size, data in images:
    head += struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32, len(data), offset + len(body)); body += data
OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_bytes(head + body)
print(OUT, len(head + body))
