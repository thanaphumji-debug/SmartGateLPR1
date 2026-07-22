# -*- coding: utf-8 -*-

import io
import time
import sys

import cv2
import numpy as np
import torch
from flask import Flask, request, jsonify
from ultralytics import YOLO
from paddleocr import PaddleOCR

# บังคับ stdout เป็น UTF-8 กันภาษาไทยเพี้ยนบน Windows
try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

# ========================= ค่าตั้งค่า =========================
PLATE_DETECTOR_PATH = r"C:\Users\Gigabyte_2\source\repos\thanaphumji-debug\SmartGateLPR1\SmartGateLPR1\plate_detector.pt"   # ไฟล์ YOLO ที่โหลดมา
DETECT_CONF = 0.35                           # เกณฑ์ความมั่นใจขั้นต่ำของ YOLO
CROP_PADDING = 6                             # ขยายกรอบ crop เล็กน้อย (พิกเซล)
MIN_LINE_SCORE = 0.15                        # ทิ้งบรรทัดที่ OCR มั่นใจต่ำกว่านี้
# ============================================================
CHAR_DETECTOR_PATH = r"C:\Users\Gigabyte_2\source\repos\thanaphumji-debug\SmartGateLPR1\SmartGateLPR1\char_detector.pt"
CHAR_CONF = 0.25          # เกณฑ์ความมั่นใจของตัวอักษร

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
PADDLE_DEVICE = "gpu:0" if USE_GPU else "cpu"
YOLO_DEVICE = 0 if USE_GPU else "cpu"

print("=" * 55)
if USE_GPU:
    print(f"🎯 พบการ์ดจอ: {torch.cuda.get_device_name(0)}  (รันบน GPU)")
else:
    print("⚠️  ไม่พบการ์ดจอ NVIDIA — รันบน CPU")
print("=" * 55)

# ---------- โหลดโมเดลตรวจจับป้าย (YOLO) ----------
print("⏳ กำลังโหลด YOLO ตรวจจับป้าย...")
detector = YOLO(PLATE_DETECTOR_PATH)
print("⏳ กำลังโหลด YOLO อ่านตัวอักษร...")
char_detector = YOLO(CHAR_DETECTOR_PATH)

# ---------- โหลด PaddleOCR อ่านภาษาไทย ----------
# ใช้ pipeline เต็ม (detection + recognition) แต่ทั้งหมดเป็นรุ่น mobile จึงเบา
# ปิดโมดูลที่ไม่จำเป็นสำหรับป้ายทะเบียน (orientation/unwarping ของเอกสาร)
print("⏳ กำลังโหลด PaddleOCR (th_PP-OCRv5_mobile_rec)...")
ocr = PaddleOCR(
    text_recognition_model_name="th_PP-OCRv5_mobile_rec",
    use_doc_orientation_classify=False,
    use_doc_unwarping=False,
    use_textline_orientation=True,   # ช่วยเวลาป้ายเอียงเล็กน้อย
    device=PADDLE_DEVICE,
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

def detect_best_plate(frame):
    """รัน YOLO หาป้าย คืน [x1,y1,x2,y2] ของกล่องที่มั่นใจสุด หรือ None ถ้าไม่เจอ"""
    det = detector(frame, conf=DETECT_CONF, verbose=False, device=YOLO_DEVICE)
    boxes = det[0].boxes
    if boxes is None or len(boxes) == 0:
        return None
    best_i = int(boxes.conf.argmax())
    x1, y1, x2, y2 = map(int, boxes.xyxy[best_i].tolist())
    return [x1, y1, x2, y2]

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
        det = detector(frame, conf=DETECT_CONF, verbose=False, device=YOLO_DEVICE)
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

        # --- 3. YOLO ตัวที่ 2 อ่านตัวอักษรทีละตัว ---
        cv2.imwrite("debug_plate.jpg", plate)

        plate_text, province, confidence = read_plate_chars(plate)
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
    app.run(host="0.0.0.0", port=5000, threaded=False)