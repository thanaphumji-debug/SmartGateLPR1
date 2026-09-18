# -*- coding: utf-8 -*-
"""
วัดความแม่นยำของระบบอ่านป้ายทะเบียน (YOLO11 ตรวจจับ + PaddleOCR อ่าน)

วิธีใช้
-------
1. วางภาพลงโฟลเดอร์ test_images  (ภาพเต็มฉากก็ได้ เดี๋ยวสคริปต์ crop ให้เอง)
2. ตั้งชื่อไฟล์เป็นเลขทะเบียนจริงเพื่อให้วัดความแม่นยำได้ เช่น  กย3779.jpg
   ใส่ชื่อจังหวัดต่อท้ายหลังขีดล่างได้ (เช่น กย3779_กาญจนบุรี.jpg) แต่จะถูก
   มองข้าม เพราะระบบเลิกอ่านชื่อจังหวัดแล้ว วัดเฉพาะเลขทะเบียนอย่างเดียว
   (ไฟล์ที่ชื่อไม่ใช่ภาษาไทย จะรายงานผลที่อ่านได้เฉย ๆ ไม่คิดเปอร์เซ็นต์)
3. รัน:  python test_ocr_accuracy.py

ผลสรุปพิมพ์บนจอ และเขียนรายละเอียดลง test_ocr_accuracy.csv
"""

import os
import sys
import csv
import time

import cv2

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
TEST_DIR = os.path.join(BASE_DIR, "test_images")
CSV_PATH = os.path.join(BASE_DIR, "test_ocr_accuracy.csv")

# อ่านโค้ดหลักมาใช้ซ้ำ จะได้วัดของจริงที่ระบบใช้ ไม่ใช่โค้ดคนละชุด
# (lpr_api จะเปิด ocr_api.py ให้เองถ้ายังไม่มีใครเปิดไว้)
import lpr_api    # noqa: E402  โหลดโมเดลตอน import


def norm(s):
    """ตัดช่องว่างและขีดก่อนเทียบ ให้ตรงกับที่ฝั่ง C# ทำ"""
    return (s or "").replace(" ", "").replace("-", "")


def is_thai(s):
    return any("฀" <= c <= "๿" for c in s or "")


def crop_plate(frame):
    """หากรอบป้ายด้วย YOLO แล้ว crop + ดัดเอียง เหมือนที่ /predict ทำ"""
    box = lpr_api.detect_best_plate(frame)
    if box is None:
        return None
    x1, y1, x2, y2 = box
    h, w = frame.shape[:2]
    p = lpr_api.CROP_PADDING
    plate = frame[max(0, y1 - p):min(h, y2 + p), max(0, x1 - p):min(w, x2 + p)]
    if plate.size == 0:
        return None
    return lpr_api.deskew_plate(plate)


def main():
    if not os.path.isdir(TEST_DIR):
        print(f"❌ ไม่พบโฟลเดอร์ {TEST_DIR}")
        print("   สร้างโฟลเดอร์ test_images แล้ววางภาพลงไปก่อน")
        return 1

    files = sorted(f for f in os.listdir(TEST_DIR)
                   if f.lower().endswith((".jpg", ".jpeg", ".png", ".bmp")))
    if not files:
        print(f"❌ ไม่มีไฟล์ภาพใน {TEST_DIR}")
        return 1

    rows = []
    n_total = len(files)
    n_found = n_read = 0            # เจอป้าย / อ่านออก
    n_graded = n_plate_ok = 0       # มีเฉลย / ทะเบียนถูก
    t_detect = t_read = 0.0

    for fname in files:
        path = os.path.join(TEST_DIR, fname)
        frame = cv2.imread(path)
        if frame is None:
            print(f"⚠️  อ่านไฟล์ไม่ได้: {fname}")
            continue

        # แกะเฉลยจากชื่อไฟล์:  ทะเบียน_จังหวัด.jpg
        stem = os.path.splitext(fname)[0]
        parts = stem.split("_")
        truth_plate = norm(parts[0]) if is_thai(parts[0]) else ""

        row = {"ไฟล์": fname, "เฉลยทะเบียน": truth_plate}

        t0 = time.time()
        plate_img = crop_plate(frame)
        dt_det = time.time() - t0
        t_detect += dt_det

        if plate_img is None:
            print(f"🔍 {fname}: ไม่เจอป้าย")
            row["หมายเหตุ"] = "ไม่เจอป้าย"
            rows.append(row)
            if truth_plate:
                n_graded += 1
            continue

        n_found += 1
        t0 = time.time()
        raw_lines = []
        try:
            text, conf, raw_lines = lpr_api.read_plate_paddle(plate_img, return_raw=True)

            # กัน YOLO ตัดกรอบพลาด — เหมือนที่ /predict ใน lpr_api.py ทำ
            # (ห้ามเช็คแค่ "text ว่างไหม" เพราะกรอบที่ตัดขาดยังอ่านออกมาเป็น
            # ทะเบียนรูปแบบถูกต้องได้ เช่น "กย3" จากป้ายจริง "กย3779")
            ph, pw = plate_img.shape[:2]
            crop_ratio = pw / max(1, ph)
            suspicious = crop_ratio < lpr_api.MIN_PLATE_ASPECT or not text or len(text) < 5
            if suspicious:
                alt_text, alt_conf, alt_raw = lpr_api.read_plate_paddle(frame, return_raw=True)
                if alt_text and (not text or len(alt_text) > len(text)):
                    text, conf = alt_text, alt_conf
                    raw_lines = alt_raw
            err = ""
        except Exception as e:
            text, conf, err = "", 0.0, str(e)
        dt_read = time.time() - t0
        t_read += dt_read

        if text:
            n_read += 1
        plate_ok = bool(truth_plate) and norm(text) == truth_plate
        if truth_plate:
            n_graded += 1
            n_plate_ok += int(plate_ok)

        # ข้อความดิบทุกบรรทัดที่ PaddleOCR เห็น (ก่อนดัดด้วย thai_plate.py) —
        # ไว้วินิจฉัยตอนจังหวัด/ทะเบียนผิดว่า OCR ไม่เจอบรรทัดนั้นเลย
        # หรือเจอแต่ดัดไม่ตรง (สองสาเหตุนี้แก้คนละจุดกัน)
        raw_str = " | ".join(f"{t!r}({sc:.2f})" for t, sc, _ in raw_lines)

        row.update({"ทะเบียนที่อ่านได้": text,
                    "conf": round(conf, 4),
                    "วินาที_ตรวจจับ": round(dt_det, 3), "วินาที_อ่าน": round(dt_read, 3),
                    "ทะเบียนถูก": ("✓" if plate_ok else "✗") if truth_plate else "",
                    "ข้อความดิบจาก_OCR": raw_str,
                    "หมายเหตุ": err})
        rows.append(row)

        mark = ("✓" if plate_ok else "✗") if truth_plate else " "
        print(f"{mark} {fname:28} '{text}' | conf {conf:.2f} | "
              f"ตรวจจับ {dt_det:.2f}s + อ่าน {dt_read:.2f}s")
        print(f"     ดิบจาก OCR: {raw_str or '(ไม่เจอข้อความเลย)'}")

    # ---------- สรุป ----------
    def pct(a, b):
        return f"{a}/{b} ({a / b * 100:.1f}%)" if b else "-"

    print("\n" + "=" * 58)
    print(f"ภาพทั้งหมด {n_total} ไฟล์")
    print(f"  ตรวจจับเจอป้าย   {pct(n_found, n_total)}")
    print(f"  อ่านตัวอักษรออก  {pct(n_read, n_total)}")
    print(f"  ทะเบียนถูกต้อง   {pct(n_plate_ok, n_graded)}")
    if n_total:
        print(f"  เวลาเฉลี่ย       ตรวจจับ {t_detect / n_total:.2f}s + "
              f"อ่าน {t_read / max(1, n_found):.2f}s")
    print("=" * 58)

    if rows:
        keys = list(dict.fromkeys(k for r in rows for k in r))
        with open(CSV_PATH, "w", newline="", encoding="utf-8-sig") as f:
            w = csv.DictWriter(f, fieldnames=keys)
            w.writeheader()
            w.writerows(rows)
        print(f"📄 รายละเอียด: {CSV_PATH}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
