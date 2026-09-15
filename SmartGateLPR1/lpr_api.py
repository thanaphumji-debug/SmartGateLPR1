# -*- coding: utf-8 -*-
import os
import threading
import io
import time
import sys

import cv2
import numpy as np
import torch
from flask import Flask, request, jsonify
from ultralytics import YOLO

from thai_plate import parse_plate_lines

model_lock = threading.RLock()

# ---------- ให้สิทธิ์ /predict (อ่านตัวอักษร) มาก่อน /detect (กรอบโชว์บนจอ) ----------
# ฝั่ง C# ยิง /detect ถี่มากเพื่ออัปเดตกรอบแดงให้ลื่น (ทุก ๆ ไม่กี่สิบ ms ต่อกล้อง)
# ซึ่งรวมกันแล้วเกินกำลังที่เครื่องประมวลผลทัน งานตรวจจับจึงยึด model_lock ไว้
# แทบตลอดเวลา พอ /predict เข้ามาขอ lock (ต้องขอหลายรอบ: YOLO + OCR) เลยโดนแย่ง
# จนอดตาย (RLock ของ Python ไม่มีคิวที่เป็นธรรม) สุดท้ายฝั่ง C# timeout
# → เห็นกรอบป้ายแต่ตัวอักษรไม่เคยขึ้น
#
# แก้โดยให้ /detect "หลบทาง" ทันทีถ้ามีงานอ่านป้ายค้างอยู่ ไม่ต้องไปแย่ง lock เลย
_predict_pending = 0
_predict_lock = threading.Lock()


def _predict_busy():
    with _predict_lock:
        return _predict_pending > 0


# บังคับ stdout เป็น UTF-8 กันภาษาไทยเพี้ยนบน Windows
try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

if getattr(sys, 'frozen', False):
    BASE_DIR = sys._MEIPASS                                  # ตอนรันเป็น .exe (PyInstaller)
else:
    BASE_DIR = os.path.dirname(os.path.abspath(__file__))    # ตอนรันด้วย Python ปกติ

PLATE_DETECTOR_PATH = os.path.join(BASE_DIR, "plate_detector.pt")
# ========================= ค่าตั้งค่า =========================
DETECT_CONF = 0.25                           # เกณฑ์ความมั่นใจขั้นต่ำของ YOLO
# ขนาดภาพที่ป้อนให้ YOLO ตอนตรวจจับ  ตั้งทับได้ด้วย environment variable: LPR_IMGSZ
#
# ทำไมถึงเป็น 1280 ทั้งที่โมเดลเทรนมาที่ 640:
#   วัดจริงกับ plate_detector.pt (YOLO11n) โดยจำลองรถอยู่ไกลบนเฟรม 1920x1080
#   ยิ่งป้ายเล็กในเฟรม ยิ่งต้องใช้ imgsz ใหญ่ ไม่งั้นตรวจไม่เจอ
#
#     ป้ายกว้าง   imgsz=640   imgsz=960   imgsz=1280
#       193px       0.835       0.899       0.851
#        77px       0.760       0.807       0.822
#        48px       0.613       0.703       0.739
#        28px      ไม่เจอ       0.557       0.624
#
#   ถ้ากล้องตั้งใกล้และป้ายใหญ่เต็มเฟรมเสมอ ลดเป็น 960 ได้ (เร็วขึ้น ~30%)
#   แต่อย่าลดเหลือ 640 ถ้ารถต้องถูกตรวจจับตั้งแต่ยังอยู่ไกล
DETECT_IMGSZ = int(os.environ.get("LPR_IMGSZ", "1280"))

# ขนาดภาพตอน "เกาะติด" ป้ายที่เจอไปแล้ว (ตั้งทับได้ด้วย LPR_TRACK_IMGSZ)
#
# ตอนยังไม่เจอป้าย เราอยากได้ความไวสูงสุดเพื่อจับรถที่เพิ่งเข้ามาไกล ๆ ให้ได้
# เร็วที่สุด จึงใช้ DETECT_IMGSZ (1280) แต่พอล็อกป้ายได้แล้ว ป้ายอยู่ใกล้และ
# ใหญ่พอสมควรแล้ว ไม่ต้องใช้ความละเอียดสูงขนาดนั้นอีก สลับมาใช้ค่านี้แทนเพื่อ
# ให้กรอบเกาะตามป้ายได้ลื่นขึ้นมาก
#
# วัดจริงกับ plate_detector.pt บนเฟรม 1280x720 (ขนาดกล้องทั่วไป):
#
#   imgsz   เวลา/ครั้ง   ครั้ง/วินาที   conf ตอนป้ายใกล้   conf ตอนป้ายเล็ก 20px
#    1280     188 ms        5.3           0.852               0.669
#     960      59 ms       17.0           0.827               0.260  <-- เริ่มหลุด
#     736      40 ms       24.8           0.828               0.273
#     640      36 ms       28.1           0.822               0.322
#
# 960 เร็วกว่า 1280 ถึง 3.2 เท่าโดย conf ตอนป้ายใกล้แทบไม่ต่าง (0.827 vs 0.852)
# เสียแค่ความไวตอนป้ายเล็กมาก ซึ่งไม่สำคัญในโหมดเกาะติด เพราะกว่าจะถึงโหมดนี้
# ก็เจอป้ายไปแล้ว
TRACK_IMGSZ = int(os.environ.get("LPR_TRACK_IMGSZ", "960"))

# ---------- โหมดเกาะติดแบบค้นเฉพาะรอบกรอบเดิม (ROI) ----------
# เร็วที่สุด: แทนที่จะค้นทั้งเฟรมทุกครั้ง ถ้ารู้อยู่แล้วว่าเฟรมก่อนหน้าป้ายอยู่ตรงไหน
# ก็ครอปเฉพาะบริเวณนั้นมาค้น ภาพที่ป้อนเข้าโมเดลเล็กลงมาก จึงเร็วขึ้นหลายเท่า
#
# วัดจริงบนเฟรม 1280x720 ป้ายกว้าง 64px (ขนาดเท่ารถจอดหน้าไม้กั้น):
#
#   วิธี                        เวลา/ครั้ง   ครั้ง/วินาที   conf
#   ค้นทั้งเฟรม imgsz=960          69.5 ms      14.4       0.818
#   ROI imgsz=640                  36.1 ms      27.7       0.867
#   ROI imgsz=416                  22.9 ms      43.7       0.822
#   ROI imgsz=320                  15.8 ms      63.3       0.826
#
# conf ไม่ได้ลดลงเลย กลับดีขึ้นด้วยซ้ำ เพราะป้ายกินพื้นที่ในภาพที่ป้อนเข้าโมเดล
# มากกว่าเดิมมาก (ต่างจากการลด imgsz ทั้งเฟรม ซึ่งทำให้ป้ายเล็กลงตามไปด้วย)
ROI_IMGSZ = int(os.environ.get("LPR_ROI_IMGSZ", "416"))
# ขยายขอบเขตการค้นออกจากกรอบเดิมกี่เท่าของขนาดกรอบ (เผื่อรถขยับระหว่างเฟรม)
# 1.0 = ค้นครอบคลุมพื้นที่ 3x3 เท่าของกรอบเดิม ซึ่งเผื่อการขยับไว้เยอะพอ
ROI_MARGIN = float(os.environ.get("LPR_ROI_MARGIN", "1.0"))
CROP_PADDING = 12                             # ขยายกรอบ crop เล็กน้อย (พิกเซล)
# สัดส่วนกว้าง/สูงขั้นต่ำของกรอบที่ถือว่า "น่าจะเป็นป้ายจริง"
# ป้ายไทยจริงกว้างกว่าสูงชัดเจน วัดจากภาพทดสอบได้ ~1.6 เท่าขึ้นไป
# ถ้าต่ำกว่านี้ = สงสัยว่า YOLO ตัดกรอบพลาด (ดู "กันกรอบพลาด" ใน /predict)
MIN_PLATE_ASPECT = 1.3
# ขยายภาพ crop ที่เตี้ยกว่านี้ให้สูงขึ้นก่อนส่งเข้า PaddleOCR (ดู _resize_for_ocr)
#
# ค่าสองตัวนี้ปรับจากผลทดสอบจริง 4 ภาพตอนลอง text_det_limit_type="min"
# (ให้ PaddleOCR ขยายเองแบบไม่จำกัด, เทียบกับ limit_side_len=736):
#
#   ไฟล์                      สูงก่อนขยาย  อัตราขยายตอนนั้น  ผล
#   578A495.jpg (fallback)        289          2.55x        province ถูก (conf 0.99)
#   S__17285141.jpg (crop ตรง)    144          5.11x        province ถูก (fuzzy match กู้ได้)
#   กม3976_เชียงราย.png (fallback) 354          2.08x        ยังพลาด (คุณภาพภาพเอง ไม่ใช่ขนาด)
#   กย3779_กาญจนบุรี.png (fallback) 120          6.13x        ⚠️ พังหมด อ่านได้แต่ขยะ
#
# สรุปได้ว่าขยาย ~2-5 เท่าช่วยได้ แต่ยิ่งภาพต้นทางเล็ก/คุณภาพต่ำ ยิ่งทนอัตรา
# ขยายสูงไม่ได้ (120px โดน 6 เท่าแล้วพัง) จึงตั้งเป้าให้สูงพอจะช่วยภาพขนาด
# กลาง ๆ (289px) ได้แบบไม่ต้องขยายแรง และจำกัดเพดานไว้กันภาพเล็กสุดพังซ้ำ
TARGET_OCR_HEIGHT = 350
MAX_UPSCALE = 3.0   # ห้ามขยายเกินกี่เท่า กันภาพเบลออยู่แล้วเละไปมากกว่าเดิม
MIN_LINE_SCORE = 0.15                        # ทิ้งบรรทัดที่ OCR มั่นใจต่ำกว่านี้
# บันทึกภาพป้ายที่ crop ได้ลง debug_plate.jpg ทุกครั้งที่อ่าน (ใช้ตอน debug เท่านั้น)
# เปิดได้โดยตั้ง environment variable: LPR_DEBUG_PLATE=1
SAVE_DEBUG_PLATE = os.environ.get("LPR_DEBUG_PLATE", "0") == "1"
# ============================================================
# โมเดลอ่านข้อความไทยของ PaddleOCR (ดาวน์โหลดเองอัตโนมัติครั้งแรกที่รัน)
PADDLE_REC_MODEL = os.environ.get("LPR_PADDLE_REC", "th_PP-OCRv5_mobile_rec")

app = Flask(__name__)

# ---------- ตรวจจับอุปกรณ์อัตโนมัติ (GPU หรือ CPU) ----------
USE_GPU = torch.cuda.is_available()
YOLO_DEVICE = 0 if USE_GPU else "cpu"

print("=" * 55)
if USE_GPU:
    print(f"🎯 พบการ์ดจอ: {torch.cuda.get_device_name(0)}  (รันบน GPU)")
else:
    print("⚠️  ไม่พบการ์ดจอ NVIDIA — รันบน CPU")
print("=" * 55)

# ---------- โหลดโมเดลตรวจจับป้าย (YOLO) ----------
# ultralytics โหลด YOLOv8 และ YOLO11 ด้วยคำสั่งเดียวกัน ไม่ต้องแก้โค้ดเวลาเปลี่ยนรุ่น
# แค่วางไฟล์ .pt รุ่นใหม่ทับ (ต้องใช้ ultralytics >= 8.3.0 ถึงจะรองรับ YOLO11)
print("⏳ กำลังโหลด YOLO ตรวจจับป้าย...")
detector = YOLO(PLATE_DETECTOR_PATH)
try:
    print(f"   รุ่นโมเดล: {getattr(detector.model, 'yaml_file', '') or type(detector.model).__name__}"
          f" | คลาส: {list(detector.names.values())}")
except Exception:
    pass

# ---------- โหลด PaddleOCR อ่านตัวอักษรภาษาไทย ----------
from paddleocr import PaddleOCR

PADDLE_DEVICE = "gpu:0" if USE_GPU else "cpu"
print(f"⏳ กำลังโหลด PaddleOCR ภาษาไทย ({PADDLE_REC_MODEL})...")
# ปิดโมดูลที่ไว้จัดการเอกสาร (หมุนหน้า/ดัดกระดาษ) ป้ายทะเบียนไม่ต้องใช้ และทำให้ช้า
ocr = PaddleOCR(
    text_recognition_model_name=PADDLE_REC_MODEL,
    use_doc_orientation_classify=False,
    use_doc_unwarping=False,
    use_textline_orientation=True,   # ช่วยตอนป้ายเอียงเล็กน้อย
    device=PADDLE_DEVICE,
    # ปล่อย text_det_limit_* ไว้ที่ค่าเริ่มต้น (limit_type="max", 960px) —
    # ลองสลับเป็น "min" มาก่อนแล้วแต่ขยายภาพใหญ่ (เช่นภาพเต็มเฟรมตอน fallback)
    # แบบไม่จำกัดจนช้าลง 5 เท่า และขยายภาพ crop เล็ก ๆ 6 เท่าแบบไม่ควบคุม
    # จนภาพที่เบลออยู่แล้วเละจนอ่านไม่ออกเลย — คุมการขยายภาพ crop เองแทน
    # (ดู _resize_for_ocr ด้านล่าง) แม่นกว่าและเร็วกว่าปล่อยให้ Paddle ทำเอง
)

# ---------- warm-up: ซ้อมอ่านภาพเปล่า 1 ครั้ง กันภาพแรกช้าผิดปกติ ----------
print("🔥 กำลัง warm-up โมเดล...")
try:
    _dummy = np.full((80, 240, 3), 255, dtype=np.uint8)
    detector(_dummy, verbose=False, device=YOLO_DEVICE)
    ocr.predict(_dummy)
except Exception as e:
    print(f"(warm-up เตือน: {e})")

print("✅ AI พร้อมทำงานแล้ว! สแตนด์บายรอรับรูปภาพที่ Port 5000")


def _extract_lines(result):
    """
    แกะผลจาก PaddleOCR ให้เป็น list ของ (text, score, y_top)
    เรียงจากบรรทัดบนลงล่าง  (บนสุด = เลขทะเบียน, ล่าง = จังหวัด)
    เขียนแบบเผื่อ API เวอร์ชันต่างกัน (เข้าถึงได้ทั้งแบบ dict และ .json)
    """
    if not result:
        return []
    res = result[0]

    texts, scores, boxes = [], [], []
    try:
        texts = list(res["rec_texts"])
        scores = list(res["rec_scores"])
        boxes = res["rec_boxes"]
    except Exception:
        try:
            d = res.json
            d = d.get("res", d)
            texts = list(d.get("rec_texts", []))
            scores = list(d.get("rec_scores", []))
            boxes = d.get("rec_boxes", [])
        except Exception:
            return []

    lines = []
    for i, t in enumerate(texts):
        sc = float(scores[i]) if i < len(scores) else 0.0
        # y ด้านบนของกล่องข้อความ ใช้จัดเรียงบรรทัด
        try:
            y_top = float(boxes[i][1])
        except Exception:
            y_top = float(i)
        if sc >= MIN_LINE_SCORE and str(t).strip():
            lines.append((str(t).strip(), sc, y_top))

    lines.sort(key=lambda x: x[2])   # บนลงล่าง
    return lines

def detect_best_plate(frame, imgsz=None):
    """
    รัน YOLO หาป้าย คืน [x1,y1,x2,y2] ของกล่องที่มั่นใจสุด หรือ None ถ้าไม่เจอ

    imgsz: ไม่ใส่ = ใช้ DETECT_IMGSZ (ความไวสูงสุด สำหรับตอนค้นหาป้ายครั้งแรก)
           ใส่ TRACK_IMGSZ = โหมดเกาะติดป้ายที่เจอแล้ว เร็วกว่ามาก
    """
    with model_lock:
       det = detector(frame, conf=DETECT_CONF, imgsz=imgsz or DETECT_IMGSZ, half=False, verbose=False, device=YOLO_DEVICE)
    boxes = det[0].boxes
    if boxes is None or len(boxes) == 0:
        return None
    best_i = int(boxes.conf.argmax())
    x1, y1, x2, y2 = map(int, boxes.xyxy[best_i].tolist())
    return [x1, y1, x2, y2]

def detect_in_roi(frame, last_box):
    """
    ค้นป้ายเฉพาะบริเวณรอบ ๆ กรอบเดิม (เร็วกว่าค้นทั้งเฟรมหลายเท่า)
    คืน [x1,y1,x2,y2] บนพิกัดของเฟรมเต็ม หรือ None ถ้าไม่เจอในบริเวณนั้น

    last_box: กรอบจากเฟรมก่อนหน้า [x1,y1,x2,y2] (พิกัดเฟรมเต็ม)
    """
    H, W = frame.shape[:2]
    lx1, ly1, lx2, ly2 = last_box
    bw, bh = lx2 - lx1, ly2 - ly1
    if bw <= 0 or bh <= 0:
        return None

    # ขยายออกจากกรอบเดิมเผื่อรถขยับระหว่างเฟรม
    mx, my = int(bw * ROI_MARGIN), int(bh * ROI_MARGIN)
    rx1, ry1 = max(0, lx1 - mx), max(0, ly1 - my)
    rx2, ry2 = min(W, lx2 + mx), min(H, ly2 + my)
    if rx2 - rx1 < 16 or ry2 - ry1 < 16:
        return None

    roi = frame[ry1:ry2, rx1:rx2]
    with model_lock:
        det = detector(roi, conf=DETECT_CONF, imgsz=ROI_IMGSZ, half=False,
                       verbose=False, device=YOLO_DEVICE)
    boxes = det[0].boxes
    if boxes is None or len(boxes) == 0:
        return None

    best_i = int(boxes.conf.argmax())
    bx1, by1, bx2, by2 = map(int, boxes.xyxy[best_i].tolist())
    # แปลงพิกัดจากใน ROI กลับเป็นพิกัดบนเฟรมเต็ม
    return [bx1 + rx1, by1 + ry1, bx2 + rx1, by2 + ry1]


def deskew_plate(img, max_angle=25.0):
    """หมุนภาพป้ายที่เอียงให้ตรงก่อนอ่านตัวอักษร (คืนภาพเดิมถ้าประเมินมุมไม่ได้)"""
    try:
        if img is None or img.size == 0:
            return img
        h, w = img.shape[:2]
        if h < 10 or w < 20:
            return img

        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        gray = cv2.GaussianBlur(gray, (3, 3), 0)
        th = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY_INV + cv2.THRESH_OTSU)[1]

        coords = cv2.findNonZero(th)
        if coords is None or len(coords) < 20:
            return img

        angle = cv2.minAreaRect(coords)[-1]
        if angle > 45:    angle -= 90
        elif angle < -45: angle += 90

        # เอียงน้อยมาก = ไม่ต้องยุ่ง / เอียงเกินไป = ประเมินมั่ว ไม่หมุนดีกว่า
        if abs(angle) < 2 or abs(angle) > max_angle:
            return img

        M = cv2.getRotationMatrix2D((w / 2.0, h / 2.0), angle, 1.0)
        return cv2.warpAffine(img, M, (w, h),
                              flags=cv2.INTER_CUBIC,
                              borderMode=cv2.BORDER_REPLICATE)
    except Exception:
        return img

def _resize_for_ocr(img):
    """
    ขยายภาพเล็กให้ใหญ่ขึ้นก่อนส่งให้ PaddleOCR แบบควบคุมเอง (ไม่พึ่งค่าเริ่มต้น
    ของ text detection) — บรรทัดจังหวัดบนป้ายตัวเล็กกว่าทะเบียนมาก ถ้าภาพเดิม
    เล็กเกินไป PaddleOCR อาจตรวจไม่เจอบรรทัดนั้นเลย

    จำกัดอัตราขยายไว้ไม่ให้เกินไป (MAX_UPSCALE) เพราะเคยลองปล่อยให้ PaddleOCR
    ขยายเองแบบไม่จำกัด (text_det_limit_type="min") แล้วภาพเล็กมาก ๆ ที่คุณภาพ
    ต่ำอยู่แล้วโดนขยายเกิน 6 เท่า กลายเป็นเบลอจนอ่านไม่ออกเลยทั้งภาพ
    (แย่กว่าตอนไม่ขยายเลยเสียอีก) จึงขยายแค่พอประมาณและเฉพาะตอนภาพเล็กจริง ๆ
    """
    h = img.shape[0]
    if h >= TARGET_OCR_HEIGHT:
        return img
    scale = min(MAX_UPSCALE, TARGET_OCR_HEIGHT / h)
    if scale <= 1.05:      # ใกล้เคียงเป้าหมายอยู่แล้ว ไม่คุ้มเสียเวลาขยาย
        return img
    new_w = max(1, int(round(img.shape[1] * scale)))
    new_h = max(1, int(round(h * scale)))
    return cv2.resize(img, (new_w, new_h), interpolation=cv2.INTER_CUBIC)


def read_plate_paddle(plate_img, return_raw=False):
    """
    อ่านป้ายด้วย PaddleOCR ภาษาไทย แล้วดัดผลให้เข้ารูปแบบป้ายไทย
    คืน (เลขทะเบียน, จังหวัด, ความมั่นใจ) — หรือเพิ่ม list บรรทัดดิบต่อท้ายเป็น
    ค่าที่ 4 ถ้า return_raw=True (ไว้ debug ว่า PaddleOCR เห็นข้อความอะไรบ้าง
    ก่อนดัด เช่น ไม่เจอบรรทัดจังหวัดเลย VS เจอแต่ดัดไม่ตรง)

    โมเดลที่ใช้เป็นโมเดลอ่านข้อความไทยทั่วไป ไม่ได้เทรนเฉพาะป้ายทะเบียน
    ผลดิบจึงมักเพี้ยน ต้องพึ่ง thai_plate.py ช่วยดัด (แก้เลข/เทียบชื่อจังหวัด)

    ⚠️ ข้อจำกัดที่รู้อยู่แล้ว (known limitation) — เลขทะเบียนแม่นยำสูงมาก
    (วัดได้ 4/4 ถูกต้องทั้งภาพ close-up และภาพ CCTV เต็มเฟรมจริงทั้งกลางวัน/
    กลางคืน) แต่ "จังหวัด" ยังอ่านไม่นิ่งกับภาพ CCTV จริงที่ป้ายเป็นแค่จุดเล็ก ๆ
    ในเฟรม (วัดได้แค่ 1/4 ถูกต้อง — ตัวที่ผ่านคือภาพ close-up คุณภาพสูงเท่านั้น)
    เพราะตัวอักษรจังหวัดเล็กกว่าทะเบียนมาก พอถูก crop+ย่อภาพมาแล้วจิ๋วเกินกว่า
    ตัวตรวจจับข้อความจะเห็น ลองขยายภาพเองใน _resize_for_ocr() แล้วช่วยได้บ้าง
    แต่ไม่พอสำหรับภาพคุณภาพต่ำ/บีบอัดหนักจาก CCTV จริง

    ตัดสินใจ (2569-09-15): ไม่ไล่แก้ต่อตอนนี้ เพราะ "จังหวัด" ไม่ถูกใช้ตัดสินใจ
    เปิด-ปิดไม้กั้นเลย (Form1.cs เทียบแค่ NormPlate(p) กับคอลัมน์ plate_number
    ในฐานข้อมูล ไม่เอา province มาเทียบด้วย) ใช้แค่โชว์บนหน้าจอ/บันทึก log
    เท่านั้น ถ้าจะแก้ต่อในอนาคต ทางที่น่าจะช่วยได้จริงคือปรับฮาร์ดแวร์กล้อง
    (ซูม/ความละเอียดให้ป้ายใหญ่ขึ้นในเฟรม) มากกว่าไล่ปรับพารามิเตอร์ซอฟต์แวร์ต่อ
    """
    if plate_img is not None and plate_img.size > 0:
        # ขยายก่อนเติมขอบ (ดู _resize_for_ocr) กันบรรทัดจังหวัดตัวเล็กเกินตรวจจับ
        plate_img = _resize_for_ocr(plate_img)

        # เติมขอบขาวรอบภาพก่อนส่งให้ OCR — ตัวตรวจจับข้อความของ Paddle มักหาไม่เจอ
        # ถ้าตัวหนังสือชิดขอบภาพพอดี (ซึ่งเป็นเรื่องปกติของ crop ที่ได้จาก YOLO)
        pad = max(8, int(plate_img.shape[0] * 0.15))
        plate_img = cv2.copyMakeBorder(plate_img, pad, pad, pad, pad,
                                       cv2.BORDER_CONSTANT, value=(255, 255, 255))

    # PaddleOCR ไม่ปลอดภัยเมื่อถูกเรียกพร้อมกันหลาย thread และ Flask รันแบบ threaded
    # จึงต้องล็อกไว้เหมือนตอนเรียก YOLO
    with model_lock:
        result = ocr.predict(plate_img)

    # lines: [(ข้อความ, คะแนน, y_top), ...] — ใช้ debug ได้ว่า PaddleOCR เจอกี่บรรทัด
    # (ถ้าเจอบรรทัดเดียว = text detection ไม่เจอบรรทัดจังหวัดเลย ไม่ใช่ดัดไม่ตรง)
    lines = _extract_lines(result)
    if not lines:
        return ("", "", 0.0, []) if return_raw else ("", "", 0.0)

    parsed = parse_plate_lines(lines)
    plate_text = parsed["plate"]
    province = parsed["province"]

    if SAVE_DEBUG_PLATE:
        print(f"   [paddle] อ่านดิบ: {parsed['raw']}")

    # ความมั่นใจ: ใช้ของบรรทัดเลขทะเบียนเป็นหลัก เพราะเป็นตัวตัดสินการเข้า-ออก
    # ถ้าดัดเป็นทะเบียนไม่ได้เลย ให้ถือว่าอ่านไม่สำเร็จ (คะแนนเฉลี่ยไว้ดูเฉย ๆ)
    if plate_text:
        conf = parsed["plate_score"]
    else:
        conf = sum(l[1] for l in lines) / len(lines)

    if return_raw:
        return plate_text, province, conf, lines
    return plate_text, province, conf


@app.route("/detect", methods=["POST"])
def detect():
    if "image" not in request.files:
        return jsonify({"status": "error", "message": "ไม่พบรูป"})

    # มีงานอ่านป้ายค้างอยู่ → คืนค่าทันทีโดยไม่แตะโมเดลเลย ปล่อยให้ /predict
    # ได้ใช้เครื่องเต็มที่  ฝั่ง C# เห็น status นี้แล้วจะข้ามเฟรมไปเฉย ๆ
    # (ต้องไม่ใช่ "error" เพราะฝั่งนั้นจะไปล้างสถานะกรอบทิ้ง)
    if _predict_busy():
        return jsonify({"status": "busy"})

    try:
        file_bytes = np.frombuffer(request.files["image"].read(), np.uint8)
        frame = cv2.imdecode(file_bytes, cv2.IMREAD_COLOR)
        # ฝั่ง C# บอกมาว่ากล้องตัวนี้ "เกาะติดป้ายอยู่แล้ว" หรือ "ยังค้นหาอยู่"
        # (ฝั่งนั้นรู้อยู่แล้วจาก hasPlateBox จึงไม่ต้องให้ Python จำสถานะเอง
        #  — ไม่มี state ค้าง ไม่ต้องกังวลเรื่อง thread safety)
        #   ค้นหาอยู่   -> ใช้ DETECT_IMGSZ (ช้าแต่ไว) จับรถที่เพิ่งเข้ามาไกล ๆ ให้เร็วที่สุด
        #   เกาะติดแล้ว -> ใช้ TRACK_IMGSZ (เร็วกว่า 3 เท่า) ให้กรอบตามป้ายลื่น ๆ
        tracking = request.form.get("tracking") == "1"

        # ถ้าฝั่ง C# ส่งกรอบจากเฟรมก่อนหน้ามาด้วย ให้ค้นเฉพาะบริเวณรอบ ๆ กรอบนั้น
        # (เร็วกว่าค้นทั้งเฟรม ~4 เท่า และ conf ดีกว่าด้วย เพราะป้ายกินพื้นที่
        #  ในภาพที่ป้อนเข้าโมเดลมากกว่า) ถ้าหาในบริเวณนั้นไม่เจอ ค่อยถอยไปค้นทั้งเฟรม
        last_box = None
        if tracking:
            try:
                last_box = [int(request.form[k]) for k in ("bx1", "by1", "bx2", "by2")]
            except (KeyError, ValueError):
                last_box = None

        box = None
        if last_box is not None:
            box = detect_in_roi(frame, last_box)
        if box is None:
            box = detect_best_plate(frame, imgsz=TRACK_IMGSZ if tracking else None)
        # ไม่พิมพ์ log ทุกครั้งที่เรียก — endpoint นี้ถูกยิงหลายครั้งต่อวินาทีต่อกล้อง
        # การเขียน console บน Windows ช้ากว่าที่คิด (บล็อกจริง) และทำให้ log จมจน
        # หา error ที่สำคัญไม่เจอ  เปิดดูได้ด้วย LPR_DEBUG_PLATE=1 ถ้าต้องการ
        if box is None:
            if SAVE_DEBUG_PLATE:
                print("🔍 /detect: ไม่เจอป้าย")
            return jsonify({"status": "error", "message": "ไม่พบป้าย"})
        if SAVE_DEBUG_PLATE:
            print(f"🟥 /detect: เจอป้าย box={box}")
        return jsonify({"status": "success", "box": box})
    except Exception as e:
        print(f"❌ /detect error: {e}")     # error ยังพิมพ์เสมอ ไม่ควรเงียบหาย
        return jsonify({"status": "error", "message": str(e)})

@app.route("/predict", methods=["POST"])
def predict():
    if "image" not in request.files:
        return jsonify({"status": "error", "message": "ไม่พบไฟล์รูปภาพ"})

    # ประกาศตัวว่ากำลังจะอ่านป้าย เพื่อให้ /detect หลบทางให้ (ดู _predict_busy)
    # ต้องนับตั้งแต่ "ก่อน" เริ่มทำงานจริง และคืนค่าใน finally เสมอ ไม่งั้นถ้า
    # หลุด exception ตัวนับจะค้าง แล้ว /detect จะหลบทางตลอดกาล = กรอบไม่ขึ้นเลย
    global _predict_pending
    with _predict_lock:
        _predict_pending += 1

    try:
        t0 = time.time()

        # --- 1. อ่านภาพจาก C# (ได้มาเป็น BGR ตามมาตรฐาน OpenCV) ---
        file_bytes = np.frombuffer(request.files["image"].read(), np.uint8)
        frame = cv2.imdecode(file_bytes, cv2.IMREAD_COLOR)
        if frame is None:
            return jsonify({"status": "error", "message": "ภาพเสียหาย อ่านไม่ได้"})

        # --- 2. YOLO หากล่องป้ายในภาพเต็ม ---
        with model_lock:
            det = detector(frame, conf=DETECT_CONF, imgsz=DETECT_IMGSZ, augment=True, half=False, verbose=False, device=YOLO_DEVICE)
        boxes = det[0].boxes
        if boxes is None or len(boxes) == 0:
            print("… ไม่พบป้ายในเฟรมนี้")
            return jsonify({"status": "error", "message": "ไม่พบป้ายทะเบียนในภาพ"})

        # เลือกกล่องที่มั่นใจสูงสุด
        best_i = int(boxes.conf.argmax())
        x1, y1, x2, y2 = map(int, boxes.xyxy[best_i].tolist())

        # ขยายกรอบเล็กน้อย กันตัวอักษรริมป้ายโดนตัด
        h, w = frame.shape[:2]
        x1 = max(0, x1 - CROP_PADDING)
        y1 = max(0, y1 - CROP_PADDING)
        x2 = min(w, x2 + CROP_PADDING)
        y2 = min(h, y2 + CROP_PADDING)
        plate = frame[y1:y2, x1:x2]

        if plate.size == 0:
            return jsonify({"status": "error", "message": "crop ป้ายว่าง"})
        plate = deskew_plate(plate)      # หมุนป้ายที่เอียงให้ตรงก่อนอ่าน

        # --- 3. อ่านตัวอักษรบนป้าย (PaddleOCR หรือ YOLO ตามที่ตั้งไว้) ---
        if SAVE_DEBUG_PLATE:
            cv2.imwrite("debug_plate.jpg", plate)

        plate_text, province, confidence = read_plate_paddle(plate)

        # กันกรณี YOLO ตัดกรอบพลาด (พบว่าเกิดได้เมื่อป้ายกินพื้นที่เกือบเต็มเฟรม —
        # โมเดลไม่ค่อยเจอภาพแบบนี้ตอนเทรน จึงหากรอบผิดจนตัดตัวเลขขาดไปครึ่งป้าย)
        #
        # ⚠️ ห้ามเช็คแค่ "plate_text ว่างไหม" — วัดจากตัวอย่างจริงพบว่ากรอบที่ตัด
        # ขาด (เช่น เห็นแค่ "กย 3" จากป้ายจริง "กย 3779") ยังอ่านออกมาเป็น "กย3"
        # ซึ่งรูปแบบถูกต้องตามไวยากรณ์ป้ายไทยทุกอย่าง (พยัญชนะ+เลข) thai_plate.py
        # จึงไม่ทิ้ง กลายเป็นทะเบียนผิดที่ดูน่าเชื่อถือแทนที่จะฟ้อง error
        #
        # สัญญาณที่แยกได้จริง (วัดจากภาพทดสอบ 4 ภาพ: 3 ภาพกรอบพัง + 1 ภาพกรอบปกติ)
        # คือ "สัดส่วนกว้าง/สูงของกรอบ" — ป้ายไทยจริงกว้างกว่าสูงชัดเจน (~1.6 เท่าขึ้นไป)
        # ส่วนกรอบที่ YOLO ตัดพลาดจะออกมาเกือบเป็นสี่เหลี่ยมจัตุรัสหรือแคบกว่านั้น
        crop_ratio = (x2 - x1) / max(1, (y2 - y1))
        suspicious = crop_ratio < MIN_PLATE_ASPECT or not plate_text or len(plate_text) < 5
        if suspicious:
            alt_text, alt_province, alt_conf = read_plate_paddle(frame)
            # เลือกผลที่ "สมบูรณ์กว่า" โดยดูจากความยาว — ถ้ากรอบตัดขาดจริง ผลจาก
            # ภาพเต็มควรยาวกว่า (ไม่ได้ตัด) ถ้าอ่านจากกรอบได้ครบอยู่แล้วก็ไม่เปลี่ยน
            if alt_text and (not plate_text or len(alt_text) > len(plate_text)):
                print(f"   ⚠️ กรอบต้องสงสัย (ratio={crop_ratio:.2f}, อ่านได้ '{plate_text}')"
                      f" — ใช้ผลจากภาพเต็มแทน: '{alt_text}'")
                plate_text, province, confidence = alt_text, alt_province, alt_conf

        print(f"🔤 อ่านตัวอักษร: '{plate_text}' | จังหวัด: '{province}' | conf {confidence:.2f}")

        if not plate_text:
            return jsonify({"status": "error", "message": "อ่านตัวอักษรบนป้ายไม่ได้"})

        full_text = f"{plate_text} {province}".strip()

        elapsed = time.time() - t0
        print(f"🚗 อ่านได้: {plate_text}  | เต็ม: {full_text}  "
              f"| conf {confidence:.2f} | ⏱️ {elapsed:.2f}s")

        # คีย์ 'text' คือค่าที่ฝั่ง C# เอาไปใช้ (result.text) — ต้องมีเสมอ
        return jsonify({
            "status": "success",
            "text": plate_text,
            "full_text": full_text,
            "province": province,
            "confidence": round(confidence, 4),
            "box": [x1, y1, x2, y2], 
        })

    except Exception as e:
        print(f"❌ Error: {e}")
        return jsonify({"status": "error", "message": str(e)})

    finally:
        # ต้องลดตัวนับเสมอ ไม่ว่าจะจบทางไหน (สำเร็จ / return กลางทาง / exception)
        # ไม่งั้น /detect จะหลบทางค้างตลอดกาล แล้วกรอบจะไม่ขึ้นอีกเลย
        with _predict_lock:
            _predict_pending -= 1


if __name__ == "__main__":
    # threaded=False กันโมเดลถูกเรียกซ้อนกันจนพัง (ฝั่ง C# มี isAIProcessing กันคิวอยู่แล้ว)
    app.run(host="0.0.0.0", port=5000, threaded=True)