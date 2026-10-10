#!/usr/bin/env python3
"""Draw the placeholder iPhone app icon: a 1024 x 1024 opaque sRGB PNG.

    python3 scripts/generate_app_icon.py

Geometry and colours follow the master artwork in docs/design/01-design-system.md section 8.2:
a pale Daylight tile, a blue speech bubble and three white voice bars. The blurred shadow is
approximated from the shape's distance field. The system applies the icon mask, so the tile is
full-bleed with square corners.

Uses only the Python standard library; a run takes under a minute.
"""

from __future__ import annotations

import argparse
import math
import os
import struct
import zlib

SIZE = 1024
DEFAULT_OUTPUT = os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "ios", "App", "Assets.xcassets", "AppIcon.appiconset", "AppIcon.png"
)

Color = tuple[float, float, float]


def rgb(value: int) -> Color:
    return ((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF)


WHITE = rgb(0xFFFFFF)
TILE_STOPS = [(0.0, rgb(0xFFFFFF)), (0.5, rgb(0xE8F0FC)), (1.0, rgb(0xC9DCF7))]
TILE_BLOOM = rgb(0x8DB9FF)
BUBBLE_STOPS = [(0.0, rgb(0x3D82F0)), (0.6, rgb(0x0B57D0)), (1.0, rgb(0x0A47AE))]
BUBBLE_SHADOW = rgb(0x0B57D0)

# Bubble body: rounded rectangle x 232, y 222, 560 x 460, corner radius 150.
BODY_CENTER, BODY_HALF, BODY_RADIUS = (512.0, 452.0), (280.0, 230.0), 150.0
BUBBLE_TOP, BUBBLE_BOTTOM = 222.0, 822.0
# Voice bars: capsules 68 wide centred on y = 452, as (centre x, height).
BARS = [(392.0, 150.0), (512.0, 270.0), (632.0, 200.0)]


def clamp(value: float, low: float = 0.0, high: float = 1.0) -> float:
    return low if value < low else high if value > high else value


def smoothstep(edge0: float, edge1: float, value: float) -> float:
    t = clamp((value - edge0) / (edge1 - edge0))
    return t * t * (3 - 2 * t)


def over(below: Color, above: Color, alpha: float) -> Color:
    """Source-over in gamma-encoded sRGB, as the design system specifies."""
    return (
        below[0] + (above[0] - below[0]) * alpha,
        below[1] + (above[1] - below[1]) * alpha,
        below[2] + (above[2] - below[2]) * alpha,
    )


def gradient(stops: list[tuple[float, Color]], position: float) -> Color:
    position = clamp(position)
    for (start, first), (end, second) in zip(stops, stops[1:]):
        if position <= end:
            return over(first, second, (position - start) / (end - start))
    return stops[-1][1]


def coverage(distance: float) -> float:
    """Anti-aliased coverage of a pixel whose centre is `distance` outside a shape's edge."""
    return clamp(0.5 - distance)


def rounded_rect_distance(x: float, y: float, center, half, radius: float) -> float:
    qx = abs(x - center[0]) - (half[0] - radius)
    qy = abs(y - center[1]) - (half[1] - radius)
    return math.hypot(max(qx, 0.0), max(qy, 0.0)) + min(max(qx, qy), 0.0) - radius


def tail_outline() -> list[tuple[float, float]]:
    """M 330 640 L 296 800 C 292 818 306 826 320 816 L 508 680 Z, with the curve flattened."""
    p0, p1, p2, p3 = (296.0, 800.0), (292.0, 818.0), (306.0, 826.0), (320.0, 816.0)
    points = [(330.0, 640.0)]
    for step in range(13):
        t = step / 12
        u = 1 - t
        points.append(
            (
                u**3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t**3 * p3[0],
                u**3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t**3 * p3[1],
            )
        )
    points.append((508.0, 680.0))
    return points


TAIL = tail_outline()


def polygon_distance(x: float, y: float, points: list[tuple[float, float]]) -> float:
    best = float("inf")
    inside = False
    previous = points[-1]
    for current in points:
        ex, ey = previous[0] - current[0], previous[1] - current[1]
        wx, wy = x - current[0], y - current[1]
        t = clamp((wx * ex + wy * ey) / (ex * ex + ey * ey))
        best = min(best, (wx - ex * t) ** 2 + (wy - ey * t) ** 2)
        if (current[1] > y) != (previous[1] > y) and x < (previous[0] - current[0]) * (y - current[1]) / (previous[1] - current[1]) + current[0]:
            inside = not inside
        previous = current
    return -math.sqrt(best) if inside else math.sqrt(best)


def bubble_distance(x: float, y: float) -> float:
    distance = rounded_rect_distance(x, y, BODY_CENTER, BODY_HALF, BODY_RADIUS)
    if 150.0 < x < 650.0 and 500.0 < y < 960.0:
        distance = min(distance, polygon_distance(x, y, TAIL))
    return distance


def pixel(column: int, row: int) -> Color:
    x, y = column + 0.5, row + 0.5

    # Tile: diagonal gradient, a bloom low on the right, a sheen across the top.
    along = ((x - 128) * 768 + (y - 32) * 960) / (768 * 768 + 960 * 960)
    color = gradient(TILE_STOPS, along)
    color = over(color, TILE_BLOOM, 0.60 * clamp(1 - math.hypot(x - 832, y - 896) / 544))
    sheen_edge = (math.hypot((x - 512) / 704, (y - 32) / 320) - 1) * 320
    color = over(color, WHITE, 0.70 * clamp(1 - y / 416) * coverage(sheen_edge))

    if not (120 < x < 900 and 110 < y < 990):
        return color

    # Bubble shadow: offset 40 down, blur 80.
    color = over(color, BUBBLE_SHADOW, 0.30 * (1 - smoothstep(-40, 40, bubble_distance(x, y - 40))))

    distance = bubble_distance(x, y)
    inside = coverage(distance)
    if inside <= 0:
        return color

    down = (y - BUBBLE_TOP) / (BUBBLE_BOTTOM - BUBBLE_TOP)
    bubble = gradient(BUBBLE_STOPS, down)

    # Gloss: ellipse centred (432, 290), radii 250 x 130, rotated by -12 degrees.
    angle = math.radians(-12)
    dx, dy = x - 432, y - 290
    gx = dx * math.cos(angle) + dy * math.sin(angle)
    gy = -dx * math.sin(angle) + dy * math.cos(angle)
    gloss_edge = (math.hypot(gx / 250, gy / 130) - 1) * 130
    bubble = over(bubble, WHITE, 0.35 * clamp(1 - (gy + 130) / 260) * coverage(gloss_edge))

    # Rim: 6 wide, inside the outline.
    rim = coverage(distance) - coverage(distance + 6)
    bubble = over(bubble, WHITE, (0.55 + (0.10 - 0.55) * clamp(down)) * rim)

    for center_x, height in BARS:
        bar = rounded_rect_distance(x, y, (center_x, 452.0), (34.0, height / 2), 34.0)
        bubble = over(bubble, WHITE, coverage(bar))

    return over(color, bubble, inside)


def png(width: int, height: int, rows: list[bytes]) -> bytes:
    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)  # 8-bit RGB, no alpha
    body = b"".join(b"\x00" + row for row in rows)
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", header)
        + chunk(b"sRGB", b"\x00")
        + chunk(b"IDAT", zlib.compress(body, 9))
        + chunk(b"IEND", b"")
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--output", default=DEFAULT_OUTPUT)
    arguments = parser.parse_args()

    rows = []
    for row in range(SIZE):
        line = bytearray()
        for column in range(SIZE):
            line.extend(int(round(clamp(channel, 0, 255))) for channel in pixel(column, row))
        rows.append(bytes(line))

    output = os.path.normpath(arguments.output)
    os.makedirs(os.path.dirname(output), exist_ok=True)
    with open(output, "wb") as handle:
        handle.write(png(SIZE, SIZE, rows))
    print(f"Wrote {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
