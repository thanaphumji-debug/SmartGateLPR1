# -*- coding: utf-8 -*-
import os
import threading
import io
import subprocess
import time
import sys
import atexit

import cv2
import numpy as np
import requests
import torch
from flask import Flask, request, jsonify
from ultralytics import YOLO

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
# บันทึกภาพป้ายที่ crop ได้ลง debug_plate.jpg ทุกครั้งที่อ่าน (ใช้ตอน debug เท่านั้น)
# เปิดได้โดยตั้ง environment variable: LPR_DEBUG_PLATE=1
SAVE_DEBUG_PLATE = os.environ.get("LPR_DEBUG_PLATE", "0") == "1"
# ============================================================
# ---------- บริการอ่านตัวอักษร (ocr_api.py) ----------
#
# PaddleOCR ถูกแยกไปรันอีกโปรเซสหนึ่ง เพราะ torch (ที่ YOLO ใช้) กับ paddle
# อยู่โปรเซสเดียวกันไม่ได้เมื่อทั้งคู่เป็นรุ่น GPU — จะพังตอน import ด้วย
#     ImportError: generic_type: type "_gpuDeviceProperties" is already registered!
# (ทั้งสองไลบรารีผูก struct ของ CUDA เข้ากับ Python ผ่าน pybind11 โดยใช้ชื่อ
#  ชนิดข้อมูลเดียวกัน และ pybind11 มีทะเบียนชนิดข้อมูลชุดเดียวต่อหนึ่งโปรเซส)
#
# แยกโปรเซสแล้วต่างคนต่างมีทะเบียนของตัวเอง จึงใช้การ์ดจอได้ทั้งคู่
OCR_PORT = int(os.environ.get("LPR_OCR_PORT", "5001"))
OCR_URL = f"http://127.0.0.1:{OCR_PORT}"
# เปิด ocr_api.py ให้เองอัตโนมัติไหม (ตั้ง 0 ถ้าอยากเปิดเองแยกหน้าต่าง)
OCR_AUTOSTART = os.environ.get("LPR_OCR_AUTOSTART", "1") == "1"
# รอบริการ OCR พร้อมนานสุดกี่วินาทีตอนเริ่มโปรแกรม (โหลดโมเดลครั้งแรกใช้เวลา)
OCR_STARTUP_TIMEOUT = float(os.environ.get("LPR_OCR_STARTUP_TIMEOUT", "180"))
# รอผลอ่านตัวอักษรนานสุดกี่วินาทีต่อหนึ่งภาพ (ฝั่ง C# ตั้ง timeout ไว้ 15 วิ)
OCR_TIMEOUT = float(os.environ.get("LPR_OCR_TIMEOUT", "12"))

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

# ---------- warm-up: ซ้อมอ่านภาพเปล่า 1 ครั้ง กันภาพแรกช้าผิดปกติ ----------
print("🔥 กำลัง warm-up โมเดล...")
try:
    _dummy = np.full((80, 240, 3), 255, dtype=np.uint8)
    detector(_dummy, verbose=False, device=YOLO_DEVICE)
except Exception as e:
    print(f"(warm-up เตือน: {e})")


# ---------- บริการอ่านตัวอักษร: เปิดให้ + รอให้พร้อม + เรียกใช้ ----------
_ocr_process = None


def _ocr_alive(timeout=1.0):
    """บริการ OCR ตอบอยู่ไหม"""
    try:
        r = requests.get(f"{OCR_URL}/health", timeout=timeout)
        return r.ok and r.json().get("status") == "ok"
    except Exception:
        return False


def ensure_ocr_service():
    """
    เปิด ocr_api.py เป็นอีกโปรเซสถ้ายังไม่มีใครเปิดไว้ แล้วรอจนพร้อมใช้งาน

    ที่ต้องเปิดให้เองเพราะฝั่ง C# รู้จักแค่บริการเดียว (พอร์ต 5000) การแยก
    โปรเซสเป็นรายละเอียดภายในของฝั่ง Python ไม่ควรไปเพิ่มภาระให้ผู้ใช้ต้อง
    เปิดสองหน้าต่างเอง
    """
    global _ocr_process

    if _ocr_alive():
        print(f"🔤 พบบริการอ่านตัวอักษรที่เปิดอยู่แล้ว ({OCR_URL})")
        return True

    if not OCR_AUTOSTART:
        print(f"⚠️  ยังไม่มีบริการอ่านตัวอักษรที่ {OCR_URL} และปิด autostart ไว้")
        print("   เปิดเองด้วย:  python ocr_api.py")
        return False

    # ตอนรันเป็น .exe (PyInstaller) จะมี ocr_api.exe วางไว้ข้าง ๆ กัน
    # ตอนรันด้วย Python ปกติก็เรียก python ocr_api.py
    exe_dir = os.path.dirname(sys.executable)
    ocr_exe = os.path.join(exe_dir, "ocr_api.exe")
    if getattr(sys, "frozen", False) and os.path.exists(ocr_exe):
        cmd = [ocr_exe]
    else:
        cmd = [sys.executable, os.path.join(BASE_DIR, "ocr_api.py")]

    print(f"🚀 กำลังเปิดบริการอ่านตัวอักษร (พอร์ต {OCR_PORT})...")
    try:
        _ocr_process = subprocess.Popen(cmd, cwd=BASE_DIR)
    except Exception as e:
        print(f"❌ เปิดบริการอ่านตัวอักษรไม่สำเร็จ: {e}")
        return False

    # โหลดโมเดลครั้งแรกใช้เวลานาน (ต้องดาวน์โหลดโมเดลด้วยถ้ายังไม่เคยรัน)
    t0 = time.time()
    while time.time() - t0 < OCR_STARTUP_TIMEOUT:
        if _ocr_alive():
            print(f"✅ บริการอ่านตัวอักษรพร้อมแล้ว ({time.time() - t0:.1f}s)")
            return True
        if _ocr_process.poll() is not None:
            print(f"❌ บริการอ่านตัวอักษรปิดตัวเอง (exit code {_ocr_process.returncode})")
            return False
        time.sleep(1.0)

    print(f"⚠️  รอบริการอ่านตัวอักษรเกิน {OCR_STARTUP_TIMEOUT:.0f} วิแล้วยังไม่พร้อม")
    return False


def read_plate_remote(plate_img):
    """
    ส่งภาพป้ายไปให้บริการ OCR อ่าน คืน (เลขทะเบียน, ความมั่นใจ, บรรทัดดิบ)

    บีบเป็น JPEG คุณภาพ 95 ก่อนส่ง — ภาพป้ายที่ crop มามีขนาดไม่กี่หมื่นไบต์
    การส่งผ่าน HTTP บน 127.0.0.1 จึงใช้เวลาไม่ถึงหนึ่งมิลลิวินาที
    """
    if plate_img is None or plate_img.size == 0:
        return "", 0.0, []

    ok, buf = cv2.imencode(".jpg", plate_img, [int(cv2.IMWRITE_JPEG_QUALITY), 95])
    if not ok:
        return "", 0.0, []

    try:
        r = requests.post(f"{OCR_URL}/read",
                          files={"image": ("plate.jpg", buf.tobytes(), "image/jpeg")},
                          timeout=OCR_TIMEOUT)
        data = r.json()
    except Exception as e:
        print(f"❌ เรียกบริการอ่านตัวอักษรไม่สำเร็จ: {e}")
        return "", 0.0, []

    raw = data.get("raw", []) or []
    if data.get("status") != "success":
        return "", 0.0, raw
    return data.get("text", ""), float(data.get("confidence", 0.0)), raw


ensure_ocr_service()
print("✅ AI พร้อมทำงานแล้ว! สแตนด์บายรอรับรูปภาพที่ Port 5000")


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

def _read_box_hint(form):
    """แกะกรอบที่ฝั่ง C# ส่งมาให้ (bx1,by1,bx2,by2) คืน None ถ้าไม่ได้ส่งมาหรือค่าเพี้ยน"""
    try:
        b = [int(float(form[k])) for k in ("bx1", "by1", "bx2", "by2")]
    except (KeyError, ValueError, TypeError):
        return None
    return b if b[2] > b[0] and b[3] > b[1] else None


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

        # --- 2. หากล่องป้าย ---
        #
        # ฝั่ง C# ส่ง "กรอบที่ /detect เพิ่งหาเจอ" มาให้ด้วย (ถ้ามี) จึงไม่ต้อง
        # ค้นทั้งเฟรมใหม่ทั้งหมด แค่ค้นซ้ำเฉพาะบริเวณรอบกรอบนั้นเพื่อความแม่นยำ
        #
        # วัดจริงบนภาพทดสอบ 1245x1300 (กรอบป้าย 183x124):
        #   ค้นทั้งเฟรม imgsz=1280 + augment (TTA)   329.8 ms   conf 0.845
        #   ค้นเฉพาะ ROI imgsz=416                     23.1 ms   conf 0.826
        # เร็วขึ้น 14 เท่าโดยความมั่นใจแทบไม่ต่าง (ป้ายกินพื้นที่ในภาพที่ป้อนเข้า
        # โมเดลมากกว่าเดิมมาก จึงไม่ต้องพึ่งความละเอียดสูงหรือ TTA)
        # ถ้าค้นใน ROI ไม่เจอ (รถขยับเร็วจนหลุดกรอบเดิม) ค่อยถอยไปค้นทั้งเฟรม
        t_decode = time.time() - t0
        t_yolo0 = time.time()
        hint = _read_box_hint(request.form)
        box = detect_in_roi(frame, hint) if hint else None
        used_roi = box is not None

        if box is None:
            with model_lock:
                det = detector(frame, conf=DETECT_CONF, imgsz=DETECT_IMGSZ, augment=True,
                               half=False, verbose=False, device=YOLO_DEVICE)
            boxes = det[0].boxes
            if boxes is None or len(boxes) == 0:
                print("… ไม่พบป้ายในเฟรมนี้")
                return jsonify({"status": "error", "message": "ไม่พบป้ายทะเบียนในภาพ"})
            # เลือกกล่องที่มั่นใจสูงสุด
            best_i = int(boxes.conf.argmax())
            box = list(map(int, boxes.xyxy[best_i].tolist()))

        x1, y1, x2, y2 = box
        t_yolo = time.time() - t_yolo0

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

        # --- 3. อ่านตัวอักษรบนป้าย (ส่งไปให้ ocr_api.py อีกโปรเซส) ---
        if SAVE_DEBUG_PLATE:
            cv2.imwrite("debug_plate.jpg", plate)

        # ขอ raw lines มาด้วยตั้งแต่รอบแรก จะได้ไม่ต้องเรียก OCR ซ้ำตอนอ่านไม่ออก
        t_ocr0 = time.time()
        plate_text, confidence, raw_lines = read_plate_remote(plate)
        t_ocr = time.time() - t_ocr0
        t_ocr_extra = 0.0

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
            t_extra0 = time.time()
            alt_text, alt_conf, alt_raw = read_plate_remote(frame)
            t_ocr_extra = time.time() - t_extra0
            # เลือกผลที่ "สมบูรณ์กว่า" โดยดูจากความยาว — ถ้ากรอบตัดขาดจริง ผลจาก
            # ภาพเต็มควรยาวกว่า (ไม่ได้ตัด) ถ้าอ่านจากกรอบได้ครบอยู่แล้วก็ไม่เปลี่ยน
            if alt_text and (not plate_text or len(alt_text) > len(plate_text)):
                print(f"   ⚠️ กรอบต้องสงสัย (ratio={crop_ratio:.2f}, อ่านได้ '{plate_text}')"
                      f" — ใช้ผลจากภาพเต็มแทน: '{alt_text}'")
                plate_text, confidence = alt_text, alt_conf
                raw_lines = alt_raw

        print(f"🔤 อ่านตัวอักษร: '{plate_text}' | conf {confidence:.2f}")

        if not plate_text:
            # อ่านไม่ออก — พิมพ์รายละเอียดให้เสมอ (ไม่ซ่อนหลัง debug flag) เพราะนี่คือ
            # ข้อมูลเดียวที่ใช้ไล่ปัญหาได้ว่าติดที่ "ป้ายเล็กเกินไป" หรือ "OCR เห็น
            # ข้อความแต่ดัดไม่เข้ารูปทะเบียน" ซึ่งแก้กันคนละทาง
            ch, cw = plate.shape[:2]
            seen = " | ".join(repr(t) for t in raw_lines) or "(ไม่เห็นข้อความเลย)"
            print(f"   ℹ️ crop ป้าย {cw}x{ch}px | OCR เห็น: {seen}")
            return jsonify({"status": "error", "message": f"อ่านตัวอักษรบนป้ายไม่ได้ (ป้าย {cw}x{ch}px)"})

        # แยกเวลาแต่ละขั้นให้เห็นชัดว่าช้าตรงไหน (ไม่งั้นเดาไม่ถูกว่าจะไปแก้จุดไหน)
        elapsed = time.time() - t0
        extra = f" + OCR ซ้ำภาพเต็ม {t_ocr_extra:.2f}s" if t_ocr_extra else ""
        print(f"🚗 อ่านได้: {plate_text} | conf {confidence:.2f} | ⏱️ รวม {elapsed:.2f}s "
              f"= ถอดภาพ {t_decode:.2f}s + หากรอบ {t_yolo:.2f}s"
              f"{' (ใช้กรอบจาก /detect)' if used_roi else ' (ค้นทั้งเฟรม)'}"
              f" + อ่านตัวอักษร {t_ocr:.2f}s{extra}")

        # คีย์ 'text' คือค่าที่ฝั่ง C# เอาไปใช้ (result.text) — ต้องมีเสมอ
        return jsonify({
            "status": "success",
            "text": plate_text,
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


@atexit.register
def _stop_ocr_service():
    """ปิดโปรเซสลูกตามไปด้วย ไม่ให้ค้างเป็นผีกินแรมและจอง GPU ทิ้งไว้"""
    if _ocr_process is None or _ocr_process.poll() is not None:
        return
    try:
        _ocr_process.terminate()
        _ocr_process.wait(timeout=5)
    except Exception:
        try:
            _ocr_process.kill()
        except Exception:
            pass


if __name__ == "__main__":
    # threaded=False กันโมเดลถูกเรียกซ้อนกันจนพัง (ฝั่ง C# มี isAIProcessing กันคิวอยู่แล้ว)
    app.run(host="0.0.0.0", port=5000, threaded=True)