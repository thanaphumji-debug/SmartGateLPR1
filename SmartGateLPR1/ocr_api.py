# -*- coding: utf-8 -*-
"""
บริการอ่านตัวอักษรบนป้ายทะเบียน (PaddleOCR) — รันแยกโปรเซสจาก lpr_api.py

ทำไมต้องแยกโปรเซส
-----------------
ตอนแรกโค้ดทั้งหมดอยู่ไฟล์เดียว: YOLO (ใช้ torch) ตรวจจับกรอบป้าย + PaddleOCR
(ใช้ paddle) อ่านตัวอักษร  ซึ่งใช้ได้ตราบใดที่ paddle เป็นรุ่น CPU

แต่พอลง paddlepaddle รุ่น GPU เพื่อให้ OCR เร็วขึ้น จะพังทันทีตอน import:

    ImportError: generic_type: type "_gpuDeviceProperties" is already registered!

สาเหตุ: ทั้ง torch และ paddle ต่างก็ผูก struct ของ CUDA (cudaDeviceProp)
เข้ากับ Python ผ่าน pybind11 โดยใช้ "ชื่อชนิดข้อมูล" เดียวกัน และ pybind11
เก็บทะเบียนชนิดข้อมูลไว้ที่เดียวต่อหนึ่งโปรเซส  ใครโหลดทีหลังจึงชนกับคนแรก
ปัญหานี้แก้ที่โค้ดเราไม่ได้ เพราะอยู่ในไลบรารีทั้งสองตัว

ทางแก้คือ "อย่าให้ torch กับ paddle อยู่โปรเซสเดียวกัน" — คนละโปรเซสคือคนละ
address space ต่างคนต่างมีทะเบียน pybind11 ของตัวเอง จึงไม่ชนกัน
และได้ผลพลอยได้คือทั้งคู่ใช้การ์ดจอได้พร้อมกัน

    lpr_api.py  (พอร์ต 5000)  = YOLO + torch   → คุยกับโปรแกรม C#
    ocr_api.py  (พอร์ต 5001)  = PaddleOCR      → รับภาพป้ายที่ crop มาแล้ว

ฝั่ง C# ไม่ต้องแก้อะไรเลย ยังยิงไปที่พอร์ต 5000 เหมือนเดิม
โดยปกติ lpr_api.py จะเปิดไฟล์นี้ให้เองอัตโนมัติ (ดู ensure_ocr_service ในนั้น)
แต่เปิดเองแยกก็ได้:  python ocr_api.py
"""

import os
import sys
import threading
import time

import cv2
import numpy as np
from flask import Flask, request, jsonify

from thai_plate import parse_plate_lines

# บังคับ stdout เป็น UTF-8 กันภาษาไทยเพี้ยนบน Windows
try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

# ========================= ค่าตั้งค่า =========================
OCR_PORT = int(os.environ.get("LPR_OCR_PORT", "5001"))

# โมเดลอ่านข้อความไทยของ PaddleOCR (ดาวน์โหลดเองอัตโนมัติครั้งแรกที่รัน)
PADDLE_REC_MODEL = os.environ.get("LPR_PADDLE_REC", "th_PP-OCRv5_mobile_rec")

# โมเดล "ตรวจจับตำแหน่งข้อความ"
#
# ⚠️ ถ้าไม่ระบุ PaddleOCR จะเลือก PP-OCRv5_server_det ให้เอง (เห็นได้ที่
# paddleocr/_pipelines/ocr.py:391) ซึ่งเป็นรุ่น server ตัวใหญ่ หนักกว่ารุ่น
# mobile หลายเท่า — เคยเผลอปล่อยไว้ที่ค่าเริ่มต้นเพราะไปตั้งแค่โมเดล "อ่าน
# ตัวอักษร" (rec) เป็น mobile อย่างเดียว ทำให้ /predict ใช้เวลา 4-6 วินาที
#
# งานของเราเป็นภาพป้ายที่ crop มาแล้ว มีข้อความแค่ 1-2 บรรทัดตัวใหญ่ ๆ
# ไม่ต้องใช้ตัวตรวจจับข้อความระดับเอกสารทั้งหน้า รุ่น mobile เพียงพอมาก
PADDLE_DET_MODEL = os.environ.get("LPR_PADDLE_DET", "PP-OCRv5_mobile_det")

# หมุนบรรทัดข้อความที่กลับหัว — ฝั่ง lpr_api ดัดป้ายให้ตรงมาแล้วด้วย deskew
# จึงปิดได้ ประหยัดการรันโมเดลเพิ่มอีกตัวต่อทุกบรรทัดที่เจอ
PADDLE_TEXTLINE_ORI = os.environ.get("LPR_TEXTLINE_ORI", "0") == "1"

MIN_LINE_SCORE = 0.15        # ทิ้งบรรทัดที่ OCR มั่นใจต่ำกว่านี้

# ขยายภาพ crop ที่เตี้ยกว่านี้ให้สูงขึ้นก่อนส่งเข้า PaddleOCR (ดู _resize_for_ocr)
#
# ปรับจากผลทดสอบจริง 4 ภาพตอนลอง text_det_limit_type="min"
# (ให้ PaddleOCR ขยายเองแบบไม่จำกัด, เทียบกับ limit_side_len=736):
#
#   ไฟล์                      สูงก่อนขยาย  อัตราขยายตอนนั้น  ผล
#   578A495.jpg (fallback)        289          2.55x        อ่านครบทุกบรรทัด
#   S__17285141.jpg (crop ตรง)    144          5.11x        อ่านครบทุกบรรทัด
#   กม3976_เชียงราย.png            354          2.08x        ยังพลาด (คุณภาพภาพเอง)
#   กย3779_กาญจนบุรี.png           120          6.13x        ⚠️ พังหมด อ่านได้แต่ขยะ
#
# สรุปว่าขยาย ~2-5 เท่าช่วยได้ แต่ยิ่งภาพต้นทางเล็ก/คุณภาพต่ำ ยิ่งทนอัตราขยาย
# สูงไม่ได้ จึงตั้งเป้าให้สูงพอช่วยภาพขนาดกลาง และจำกัดเพดานกันภาพเล็กสุดพัง
TARGET_OCR_HEIGHT = 350
MAX_UPSCALE = 5.0

SAVE_DEBUG_PLATE = os.environ.get("LPR_DEBUG_PLATE", "0") == "1"

# ---------- oneDNN (mkldnn) ----------
#
# ⛔ บน CPU ห้ามเปิดกับ paddlepaddle รุ่นที่ใช้อยู่ — จะพังด้วย
#      (Unimplemented) ConvertPirAttribute2RuntimeAttribute not support
#      [pir::ArrayAttribute<pir::DoubleAttribute>]
#      (at ...new_executor/instruction/onednn/onednn_instruction.cc:118)
#
# เคยลองแก้ด้วย enable_new_ir=False แล้วไม่ได้ผล เหตุผลอยู่ที่
# paddlex/inference/models/runners/paddle_static/runner.py:493-494 :
#
#     if hasattr(config, "enable_new_ir"):
#         config.enable_new_ir(self._config.get("enable_new_ir", True))
#     if hasattr(config, "enable_new_executor"):
#         config.enable_new_executor()          # <-- เรียกตายตัว ปิดไม่ได้เลย
#
# คือ "new IR" กับ "new executor" เป็นคนละสวิตช์ เราปิดได้แต่ตัวแรก ส่วน
# executor รุ่นใหม่ถูกเปิดตายตัว และไฟล์ที่พัง (onednn_instruction.cc) อยู่ใน
# new executor พอดี → บน CPU + เปิด oneDNN = พังเสมอ
#
# บน GPU ไม่เกี่ยวเลย เพราะ oneDNN เป็นไลบรารีเร่งความเร็วของ CPU อย่างเดียว
ENABLE_MKLDNN = os.environ.get("LPR_ENABLE_MKLDNN", "0") == "1"
if not ENABLE_MKLDNN:
    # ต้องตั้ง "ก่อน" import paddleocr/paddlex เพราะ paddlex อ่านธงพวกนี้ตอน
    # import (paddlex/utils/flags.py) ถ้าตั้งทีหลังจะไม่มีผล
    # ค่า PADDLE_PDX_ENABLE_MKLDNN_BYDEFAULT เดิมเป็น True แปลว่า paddlex จะ
    # เลือก run_mode="mkldnn" ให้เองแม้เราส่ง enable_mkldnn=False ในบางเส้นทาง
    os.environ.setdefault("PADDLE_PDX_ENABLE_MKLDNN_BYDEFAULT", "False")
    os.environ.setdefault("FLAGS_use_mkldnn", "0")

app = Flask(__name__)

# PaddleOCR ไม่ปลอดภัยเมื่อถูกเรียกพร้อมกันหลาย thread และ Flask รันแบบ threaded
ocr_lock = threading.RLock()

print("=" * 55)
print(f"🔤 บริการอ่านตัวอักษร (PaddleOCR) — พอร์ต {OCR_PORT}")
print("=" * 55)

from paddleocr import PaddleOCR
import paddle

# ---------- PaddleOCR จะรันบน GPU หรือ CPU ----------
#
# ⚠️ ห้ามใช้ torch.cuda.is_available() ตัดสินแทน — torch กับ paddle เป็นคนละ
# ไลบรารี ติดตั้งแยกกัน มี/ไม่มี CUDA ไม่จำเป็นต้องตรงกัน  และในโปรเซสนี้
# ไม่มี torch อยู่แล้ว (นั่นคือเหตุผลที่แยกไฟล์นี้ออกมา)
try:
    PADDLE_HAS_GPU = (paddle.device.is_compiled_with_cuda() and
                      paddle.device.cuda.device_count() > 0)
except Exception:
    PADDLE_HAS_GPU = False

PADDLE_DEVICE = os.environ.get("LPR_PADDLE_DEVICE") or \
    ("gpu:0" if PADDLE_HAS_GPU else "cpu")

if PADDLE_DEVICE.startswith("gpu"):
    print(f"🎯 PaddleOCR: รันบน GPU ({PADDLE_DEVICE})")
else:
    print("⚠️  PaddleOCR: รันบน CPU")
    print("   💡 อยากให้เร็วขึ้น ลง paddlepaddle รุ่น GPU ให้ตรงกับ CUDA ของเครื่อง")
    print("      (ตอนนี้แยกโปรเซสกับ YOLO แล้ว จึงลงรุ่น GPU ได้โดยไม่ชนกับ torch)")

print(f"⏳ กำลังโหลดโมเดล: อ่านตัวอักษร={PADDLE_REC_MODEL} | ตรวจจับข้อความ={PADDLE_DET_MODEL}")

_PADDLE_ENGINE_CFG = {
    "paddle_static": {
        "run_mode": "mkldnn" if ENABLE_MKLDNN else "paddle",
        "enable_new_ir": False,
        # ต้องใส่ cpu_threads เองด้วย เพราะพอส่ง engine_config เข้าไป PaddleOCR
        # จะใช้ค่านี้แทนค่าที่มันสร้างให้เอง ถ้าไม่ใส่จะหล่นไปใช้ค่าดีฟอลต์ที่น้อยกว่า
        "cpu_threads": int(os.environ.get("LPR_CPU_THREADS", "10")),
    }
}

_ocr_kwargs = dict(
    text_recognition_model_name=PADDLE_REC_MODEL,
    text_detection_model_name=PADDLE_DET_MODEL,
    # ปิดโมดูลที่ไว้จัดการเอกสาร (หมุนหน้า/ดัดกระดาษ) ป้ายทะเบียนไม่ต้องใช้ และทำให้ช้า
    use_doc_orientation_classify=False,
    use_doc_unwarping=False,
    use_textline_orientation=PADDLE_TEXTLINE_ORI,
    device=PADDLE_DEVICE,
    enable_mkldnn=ENABLE_MKLDNN,
    # ปล่อย text_det_limit_* ไว้ที่ค่าเริ่มต้น (limit_type="max", 960px) —
    # ลองสลับเป็น "min" มาก่อนแล้วแต่ขยายภาพใหญ่แบบไม่จำกัดจนช้าลง 5 เท่า
    # และขยายภาพ crop เล็ก ๆ 6 เท่าจนภาพที่เบลออยู่แล้วเละจนอ่านไม่ออกเลย
    # — คุมการขยายเองแทน (ดู _resize_for_ocr) แม่นกว่าและเร็วกว่า
    engine_config=_PADDLE_ENGINE_CFG,
)

try:
    ocr = PaddleOCR(**_ocr_kwargs)
except (TypeError, ValueError) as e:
    # paddleocr รุ่นเก่ายังไม่มีพารามิเตอร์ engine_config — ถอยไปใช้แบบเดิม
    if "engine_config" not in str(e):
        raise
    print(f"ℹ️  paddleocr รุ่นนี้ไม่รองรับ engine_config ({e}) — ใช้ค่าเริ่มต้นแทน")
    _ocr_kwargs.pop("engine_config", None)
    ocr = PaddleOCR(**_ocr_kwargs)

# ---------- warm-up: ซ้อมอ่านภาพเปล่า 1 ครั้ง กันภาพแรกช้าผิดปกติ ----------
print("🔥 กำลัง warm-up โมเดล...")
try:
    with ocr_lock:
        ocr.predict(np.full((80, 240, 3), 255, dtype=np.uint8))
except Exception as e:
    print(f"(warm-up เตือน: {e})")

print(f"✅ พร้อมอ่านตัวอักษรแล้ว — รออยู่ที่พอร์ต {OCR_PORT}")


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


def _resize_for_ocr(img):
    """
    ขยายภาพเล็กให้ใหญ่ขึ้นก่อนส่งให้ PaddleOCR แบบควบคุมเอง (ไม่พึ่งค่าเริ่มต้น
    ของ text detection) — ถ้าภาพเดิมเล็กเกินไป PaddleOCR อาจตรวจไม่เจอข้อความเลย

    จำกัดอัตราขยายไว้ไม่ให้เกินไป (MAX_UPSCALE) เพราะเคยลองปล่อยให้ PaddleOCR
    ขยายเองแบบไม่จำกัดแล้วภาพเล็กมาก ๆ ที่คุณภาพต่ำอยู่แล้วโดนขยายเกิน 6 เท่า
    กลายเป็นเบลอจนอ่านไม่ออกเลยทั้งภาพ (แย่กว่าตอนไม่ขยายเลยเสียอีก)
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


def read_plate(plate_img, return_raw=False):
    """
    อ่านป้ายด้วย PaddleOCR ภาษาไทย แล้วดัดผลให้เข้ารูปแบบป้ายไทย
    คืน (เลขทะเบียน, ความมั่นใจ) — หรือเพิ่ม list บรรทัดดิบต่อท้ายเป็นค่าที่ 3
    ถ้า return_raw=True (ไว้ debug ว่า PaddleOCR เห็นข้อความอะไรบ้างก่อนดัด)

    โมเดลที่ใช้เป็นโมเดลอ่านข้อความไทยทั่วไป ไม่ได้เทรนเฉพาะป้ายทะเบียน
    ผลดิบจึงมักเพี้ยน ต้องพึ่ง thai_plate.py ช่วยดัดให้เข้ารูปแบบป้ายไทย

    หมายเหตุ: อ่านเฉพาะ "เลขทะเบียน" ไม่อ่านชื่อจังหวัดแล้ว (ตัดออกเมื่อ
    2569-09-15) เพราะตัวอักษรจังหวัดเล็กกว่าเลขทะเบียนมาก อ่านได้ไม่นิ่ง
    และไม่เคยถูกใช้ตัดสินเปิด-ปิดไม้กั้นเลย
    """
    if plate_img is not None and plate_img.size > 0:
        # ขยายก่อนเติมขอบ (ดู _resize_for_ocr) กันตัวหนังสือเล็กเกินตรวจจับ
        plate_img = _resize_for_ocr(plate_img)

        # เติมขอบขาวรอบภาพก่อนส่งให้ OCR — ตัวตรวจจับข้อความของ Paddle มักหาไม่เจอ
        # ถ้าตัวหนังสือชิดขอบภาพพอดี (ซึ่งเป็นเรื่องปกติของ crop ที่ได้จาก YOLO)
        pad = max(8, int(plate_img.shape[0] * 0.15))
        plate_img = cv2.copyMakeBorder(plate_img, pad, pad, pad, pad,
                                       cv2.BORDER_CONSTANT, value=(255, 255, 255))

    with ocr_lock:
        result = ocr.predict(plate_img)

    # lines: [(ข้อความ, คะแนน, y_top), ...] — ใช้ debug ได้ว่า PaddleOCR เจอกี่บรรทัด
    lines = _extract_lines(result)
    if not lines:
        return ("", 0.0, []) if return_raw else ("", 0.0)

    parsed = parse_plate_lines(lines)
    plate_text = parsed["plate"]

    if SAVE_DEBUG_PLATE:
        print(f"   [paddle] อ่านดิบ: {parsed['raw']}")

    # ความมั่นใจ: ใช้ของบรรทัดเลขทะเบียนเป็นหลัก เพราะเป็นตัวตัดสินการเข้า-ออก
    # ถ้าดัดเป็นทะเบียนไม่ได้เลย ให้ถือว่าอ่านไม่สำเร็จ (คะแนนเฉลี่ยไว้ดูเฉย ๆ)
    conf = parsed["plate_score"] if plate_text else sum(l[1] for l in lines) / len(lines)

    if return_raw:
        return plate_text, conf, lines
    return plate_text, conf


@app.route("/health", methods=["GET"])
def health():
    """ให้ lpr_api.py เช็คว่าบริการนี้พร้อมหรือยังก่อนเริ่มรับงาน"""
    return jsonify({
        "status": "ok",
        "device": PADDLE_DEVICE,
        "rec_model": PADDLE_REC_MODEL,
        "det_model": PADDLE_DET_MODEL,
    })


@app.route("/read", methods=["POST"])
def read():
    """
    รับภาพป้ายที่ crop + ดัดเอียงมาแล้วจาก lpr_api.py คืนเลขทะเบียนที่อ่านได้

    รับ: form-data field "image" (ไฟล์ภาพ)
    คืน: {"status": "success", "text": "กย3779", "confidence": 0.93,
          "raw": ["กย 3779", "กาญจนบุรี"], "elapsed": 0.31}
    """
    if "image" not in request.files:
        return jsonify({"status": "error", "message": "ไม่พบรูป"})

    try:
        t0 = time.time()
        file_bytes = np.frombuffer(request.files["image"].read(), np.uint8)
        img = cv2.imdecode(file_bytes, cv2.IMREAD_COLOR)
        if img is None:
            return jsonify({"status": "error", "message": "ภาพเสียหาย อ่านไม่ได้"})

        text, conf, lines = read_plate(img, return_raw=True)
        elapsed = time.time() - t0

        if not text:
            # อ่านไม่ออก — พิมพ์รายละเอียดให้เสมอ (ไม่ซ่อนหลัง debug flag) เพราะนี่คือ
            # ข้อมูลเดียวที่ใช้ไล่ปัญหาได้ว่าติดที่ "ป้ายเล็กเกินไป" หรือ "OCR เห็น
            # ข้อความแต่ดัดไม่เข้ารูปทะเบียน" ซึ่งแก้กันคนละทาง
            h, w = img.shape[:2]
            seen = " | ".join(f"{t!r}({sc:.2f})" for t, sc, _ in lines) or "(ไม่เห็นข้อความเลย)"
            print(f"   ℹ️ อ่านไม่ออก: ภาพป้าย {w}x{h}px | OCR เห็น: {seen} | ⏱️ {elapsed:.2f}s")
            return jsonify({
                "status": "error",
                "message": f"อ่านตัวอักษรบนป้ายไม่ได้ (ป้าย {w}x{h}px)",
                "raw": [t for t, _, _ in lines],
                "elapsed": round(elapsed, 3),
            })

        print(f"🔤 อ่านได้: '{text}' | conf {conf:.2f} | ⏱️ {elapsed:.2f}s")
        return jsonify({
            "status": "success",
            "text": text,
            "confidence": round(conf, 4),
            "raw": [t for t, _, _ in lines],
            "elapsed": round(elapsed, 3),
        })

    except Exception as e:
        print(f"❌ /read error: {e}")
        return jsonify({"status": "error", "message": str(e)})


if __name__ == "__main__":
    # threaded=True ได้ เพราะมี ocr_lock กันการเรียกโมเดลซ้อนกันอยู่แล้ว
    app.run(host="127.0.0.1", port=OCR_PORT, threaded=True)
