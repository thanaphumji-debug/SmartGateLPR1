# -*- coding: utf-8 -*-
import os
import threading
model_lock = threading.RLock()
import io
import time
import sys

import cv2
import numpy as np
import torch
from flask import Flask, request, jsonify
from ultralytics import YOLO

from thai_plate import parse_plate_lines

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
CHAR_DETECTOR_PATH  = os.path.join(BASE_DIR, "char_detector.pt")
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
CROP_PADDING = 12                             # ขยายกรอบ crop เล็กน้อย (พิกเซล)
MIN_LINE_SCORE = 0.15                        # ทิ้งบรรทัดที่ OCR มั่นใจต่ำกว่านี้
# บันทึกภาพป้ายที่ crop ได้ลง debug_plate.jpg ทุกครั้งที่อ่าน (ใช้ตอน debug เท่านั้น)
# เปิดได้โดยตั้ง environment variable: LPR_DEBUG_PLATE=1
SAVE_DEBUG_PLATE = os.environ.get("LPR_DEBUG_PLATE", "0") == "1"
# ============================================================
CHAR_CONF = 0.25          # เกณฑ์ความมั่นใจของตัวอักษร (ใช้เฉพาะเครื่องอ่านแบบ yolo)

# เลือกว่าจะอ่านตัวอักษรบนป้ายด้วยอะไร  ตั้งผ่าน environment variable: LPR_OCR_ENGINE
#   paddle = PaddleOCR ภาษาไทย (ค่าเริ่มต้น)
#   yolo   = โมเดล YOLO อ่านตัวอักษรทีละตัว (char_detector.pt) ของเดิม เอาไว้เทียบผล
OCR_ENGINE = os.environ.get("LPR_OCR_ENGINE", "paddle").strip().lower()
# โมเดลอ่านข้อความไทยของ PaddleOCR (ดาวน์โหลดเองอัตโนมัติครั้งแรกที่รัน)
PADDLE_REC_MODEL = os.environ.get("LPR_PADDLE_REC", "th_PP-OCRv5_mobile_rec")

CHAR_MAP = {
    "A01": "ก", "A02": "ข", "A04": "ค", "A05": "ฅ", "A06": "ฆ",
    "A07": "ง", "A08": "จ", "A09": "ฉ", "A10": "ช", "A12": "ฌ",
    "A13": "ญ", "A14": "ฎ", "A16": "ฐ", "A18": "ฒ", "A19": "ณ",
    "A20": "ด", "A21": "ต", "A22": "ถ", "A23": "ท", "A24": "ธ",
    "A25": "น", "A26": "บ", "A28": "ผ", "A30": "พ", "A31": "ฟ",
    "A32": "ภ", "A33": "ม", "A34": "ย", "A35": "ร", "A36": "ล",
    "A37": "ว", "A38": "ศ", "A39": "ษ", "A40": "ส", "A41": "ห",
    "A42": "ฬ", "A43": "อ", "A44": "ฮ",
}

PROVINCE_MAP = {
    "BKK": "กรุงเทพมหานคร", "CMI": "เชียงใหม่", "CRI": "เชียงราย",
    "NMA": "นครราชสีมา", "CBI": "ชลบุรี", "CCO": "ฉะเชิงเทรา",
    "KKN": "ขอนแก่น", "PKT": "ภูเก็ต", "RYG": "ระยอง",
    "NBI": "นนทบุรี", "PTE": "ปทุมธานี", "SPK": "สมุทรปราการ",
    "NPT": "นครปฐม", "AYA": "พระนครศรีอยุธยา", "ATG": "อ่างทอง",
    "ACR": "อำนาจเจริญ", "BKN": "บึงกาฬ", "BRM": "บุรีรัมย์",
    "CNT": "ชัยนาท", "CPM": "ชัยภูมิ", "CPN": "ชุมพร", "CTI": "จันทบุรี",
    "KBI": "กระบี่", "KPT": "กำแพงเพชร", "KRI": "กาญจนบุรี", "KSN": "กาฬสินธุ์",
    "LEI": "เลย", "LPG": "ลำปาง", "LPN": "ลำพูน", "LRI": "ลพบุรี",
    "MDH": "มุกดาหาร", "MKM": "มหาสารคาม", "MSN": "แม่ฮ่องสอน", "NAN": "น่าน",
    "NBP": "หนองบัวลำภู", "NKI": "หนองคาย", "NPM": "นครพนม",
    "NSN": "นครสวรรค์", "NST": "นครศรีธรรมราช", "NYK": "นครนายก",
    "PBI": "เพชรบุรี", "PCT": "พิจิตร", "PKN": "ประจวบคีรีขันธ์",
    "PLG": "พัทลุง", "PLK": "พิษณุโลก", "PNA": "พังงา", "PNB": "เพชรบูรณ์",
    "PRE": "แพร่", "PRI": "ปราจีนบุรี", "PTN": "ปัตตานี", "PYO": "พะเยา",
    "RBR": "ราชบุรี", "RET": "ร้อยเอ็ด", "RNG": "ระนอง",
    "SKA": "สงขลา", "SKW": "สระแก้ว", "SSK": "ศรีสะเกษ", "SRN": "สุรินทร์",
    "SPB": "สุพรรณบุรี", "SNI": "สุราษฎร์ธานี", "SNK": "สกลนคร",
    "TAK": "ตาก", "TRG": "ตรัง", "TRT": "ตราด",
    "UBN": "อุบลราชธานี", "UDN": "อุดรธานี", "UTI": "อุทัยธานี", "UTT": "อุตรดิตถ์",
    "YLA": "ยะลา", "YST": "ยโสธร",
    "SKM": "สมุทรสงคราม", "SKN": "สมุทรสาคร",   # ⚠️ ยังไม่ยืนยัน
    "SRI": "สระบุรี", "SBR": "สิงห์บุรี",          # ⚠️ ยังไม่ยืนยัน
    "STI": "สุโขทัย", "BTG": "?",                # ⚠️ ยังไม่ยืนยัน
}

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

# ---------- โหลดตัวอ่านตัวอักษรตามเครื่องที่เลือก ----------
char_detector = None
ocr = None

if OCR_ENGINE == "yolo":
    print("⏳ กำลังโหลด YOLO อ่านตัวอักษร (char_detector.pt)...")
    char_detector = YOLO(CHAR_DETECTOR_PATH)
else:
    OCR_ENGINE = "paddle"
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
    )

print(f"🔤 เครื่องอ่านตัวอักษร: {OCR_ENGINE}")

# ---------- warm-up: ซ้อมอ่านภาพเปล่า 1 ครั้ง กันภาพแรกช้าผิดปกติ ----------
print("🔥 กำลัง warm-up โมเดล...")
try:
    _dummy = np.full((80, 240, 3), 255, dtype=np.uint8)
    detector(_dummy, verbose=False, device=YOLO_DEVICE)
    if ocr is not None:
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

def detect_best_plate(frame):
    """รัน YOLO หาป้าย คืน [x1,y1,x2,y2] ของกล่องที่มั่นใจสุด หรือ None ถ้าไม่เจอ"""
    with model_lock:
       det = detector(frame, conf=DETECT_CONF, imgsz=DETECT_IMGSZ, half=False, verbose=False, device=YOLO_DEVICE)
    boxes = det[0].boxes
    if boxes is None or len(boxes) == 0:
        return None
    best_i = int(boxes.conf.argmax())
    x1, y1, x2, y2 = map(int, boxes.xyxy[best_i].tolist())
    return [x1, y1, x2, y2]

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

def read_plate_paddle(plate_img):
    """
    อ่านป้ายด้วย PaddleOCR ภาษาไทย แล้วดัดผลให้เข้ารูปแบบป้ายไทย
    คืน (เลขทะเบียน, จังหวัด, ความมั่นใจ)

    โมเดลที่ใช้เป็นโมเดลอ่านข้อความไทยทั่วไป ไม่ได้เทรนเฉพาะป้ายทะเบียน
    ผลดิบจึงมักเพี้ยน ต้องพึ่ง thai_plate.py ช่วยดัด (แก้เลข/เทียบชื่อจังหวัด)
    """
    # เติมขอบขาวรอบภาพก่อนส่งให้ OCR — ตัวตรวจจับข้อความของ Paddle มักหาไม่เจอ
    # ถ้าตัวหนังสือชิดขอบภาพพอดี (ซึ่งเป็นเรื่องปกติของ crop ที่ได้จาก YOLO)
    if plate_img is not None and plate_img.size > 0:
        pad = max(8, int(plate_img.shape[0] * 0.15))
        plate_img = cv2.copyMakeBorder(plate_img, pad, pad, pad, pad,
                                       cv2.BORDER_CONSTANT, value=(255, 255, 255))

    # PaddleOCR ไม่ปลอดภัยเมื่อถูกเรียกพร้อมกันหลาย thread และ Flask รันแบบ threaded
    # จึงต้องล็อกไว้เหมือนตอนเรียก YOLO
    with model_lock:
        result = ocr.predict(plate_img)

    lines = _extract_lines(result)
    if not lines:
        return "", "", 0.0

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

    return plate_text, province, conf


def read_plate_chars(plate_img):
    """
    รัน YOLO ตัวที่ 2 บน crop ป้าย -> อ่านตัวอักษรทีละตัว
    คืน (เลขทะเบียน, จังหวัด, ความมั่นใจ)
    """
    res = char_detector(plate_img, conf=CHAR_CONF, verbose=False, device=YOLO_DEVICE)
    boxes = res[0].boxes
    if boxes is None or len(boxes) == 0:
        return "", "", 0.0

    names = char_detector.names
    chars, provinces, scores = [], [], []

    for i in range(len(boxes)):
        cls_name = names[int(boxes.cls[i])]
        conf = float(boxes.conf[i])
        x_center = float(boxes.xywh[i][0])
        scores.append(conf)

        if cls_name in PROVINCE_MAP:
            provinces.append((PROVINCE_MAP[cls_name], conf))
        elif cls_name in CHAR_MAP:
            chars.append((CHAR_MAP[cls_name], x_center))
        elif cls_name.isdigit():
            chars.append((cls_name, x_center))

    chars.sort(key=lambda c: c[1])            # เรียงซ้าย -> ขวา
    plate_text = "".join(c[0] for c in chars)

    province = ""
    if provinces:
        province = max(provinces, key=lambda p: p[1])[0]   # เอาตัวที่มั่นใจสุด

    avg_conf = sum(scores) / len(scores) if scores else 0.0
    return plate_text, province, avg_conf


def read_plate(plate_img):
    """อ่านป้ายด้วยเครื่องอ่านที่เลือกไว้ (ดู OCR_ENGINE)"""
    if OCR_ENGINE == "yolo":
        return read_plate_chars(plate_img)
    return read_plate_paddle(plate_img)

@app.route("/detect", methods=["POST"])
def detect():
    if "image" not in request.files:
        return jsonify({"status": "error", "message": "ไม่พบรูป"})
    try:
        file_bytes = np.frombuffer(request.files["image"].read(), np.uint8)
        frame = cv2.imdecode(file_bytes, cv2.IMREAD_COLOR)
        box = detect_best_plate(frame)
        if box is None:
            print("🔍 /detect: ไม่เจอป้าย")          # <-- เพิ่ม
            return jsonify({"status": "error", "message": "ไม่พบป้าย"})
        print(f"🟥 /detect: เจอป้าย box={box}")       # <-- เพิ่ม
        return jsonify({"status": "success", "box": box})
    except Exception as e:
        print(f"❌ /detect error: {e}")               # <-- เพิ่ม
        return jsonify({"status": "error", "message": str(e)})

@app.route("/predict", methods=["POST"])
def predict():
    if "image" not in request.files:
        return jsonify({"status": "error", "message": "ไม่พบไฟล์รูปภาพ"})

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

        plate_text, province, confidence = read_plate(plate)
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


if __name__ == "__main__":
    # threaded=False กันโมเดลถูกเรียกซ้อนกันจนพัง (ฝั่ง C# มี isAIProcessing กันคิวอยู่แล้ว)
    app.run(host="0.0.0.0", port=5000, threaded=True)