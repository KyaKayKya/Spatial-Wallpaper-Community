"""Small, in-memory diagnostic images from the exact inference frame."""
import base64
import math

import cv2


def encode_preview(rgb, face):
    height, width = rgb.shape[:2]
    scale = min(640 / width, 480 / height, 1)
    image = cv2.cvtColor(cv2.resize(rgb, (max(1, round(width*scale)),
                                       max(1, round(height*scale)))), cv2.COLOR_RGB2BGR)
    h, w = image.shape[:2]
    for i, point in enumerate(face):
        if math.isfinite(point.x) and math.isfinite(point.y) and 0 <= point.x <= 1 and 0 <= point.y <= 1:
            cv2.circle(image, (round(point.x*(w-1)), round(point.y*(h-1))),
                       3 if i in (468, 473) else 1,
                       (0, 160, 255) if i in (468, 473) else (80, 220, 80), -1)
    image = cv2.flip(image, 1)
    for quality in (75, 55, 35, 15):
        ok, jpeg = cv2.imencode('.jpg', image, [cv2.IMWRITE_JPEG_QUALITY, quality])
        if not ok:
            raise ValueError('PREVIEW_ENCODING_FAILED')
        if jpeg.nbytes <= 40000:
            break
    else:
        image = cv2.resize(image, (max(1,w//2), max(1,h//2)))
        h, w = image.shape[:2]
        ok, jpeg = cv2.imencode('.jpg', image, [cv2.IMWRITE_JPEG_QUALITY, 55])
        if not ok or jpeg.nbytes > 40000:
            raise ValueError('PREVIEW_ENCODING_FAILED')
    return {'jpeg': base64.b64encode(jpeg).decode('ascii'), 'width': w, 'height': h,
            'mirrored': True, 'face_found': bool(face)}
