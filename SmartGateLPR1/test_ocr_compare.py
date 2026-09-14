# -*- coding: utf-8 -*-
"""
เทียบผลการอ่านป้ายระหว่าง PaddleOCR กับ YOLO char_detector บนภาพชุดเดียวกัน

วิธีใช้
-------
1. วางภาพลงโฟลเดอร์ test_images  (ภาพเต็มฉากก็ได้ เดี๋ยวสคริปต์ crop ให้เอง)
2. ตั้งชื่อไฟล์เป็นเลขทะเบียนจริงเพื่อให้วัดความแม่นยำได้ เช่น  กย3779.jpg
   (ถ้าไม่ตั้ง จะรายงานผลที่อ่านได้เฉย ๆ ไม่คิดเปอร์เซ็นต์)
3. รัน:  python test_ocr_compare.py

ผลสรุปจะพิมพ์บนจอ และเขียนรายละเอียดลง test_ocr_compare.csv
"""

import os
import sys
import csv
import time

import cv2
import numpy as np

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
TEST_DIR = os.path.join(BASE_DIR, "test_images")
CSV_PATH = os.path.join(BASE_DIR, "test_ocr_compare.csv")

# อ่านโค้ดหลักมาใช้ซ้ำ จะได้เทียบกับของจริงที่ระบบใช้ ไม่ใช่โค้ดคนละชุด
# (ตั้ง engine เป็น paddle ก่อน เพื่อให้ lpr_api โหลด PaddleOCR ขึ้นมาด้วย)
os.environ.setdefault("LPR_OCR_ENGINE", "paddle")
import lpr_api                      # noqa: E402  โหลดโมเดลทั้งหมดตอน import
from ultralytics import YOLO        # noqa: E402

# โหลด char_detector เพิ่มเอง เพราะ lpr_api โหลดเฉพาะตัวที่เลือกไว้
if lpr_api.char_detector is None:
    print("⏳ กำลังโหลด YOLO อ่านตัวอักษร (ไว้เทียบ)...")
    lpr_api.char_detector = YOLO(lpr_api.CHAR_DETECTOR_PATH)


def norm(s):
    """ตัดช่องว่างและขีดก่อนเทียบ ให้ตรงกับที่ฝั่ง C# ทำ"""
    return (s or "").replace(" ", "").replace("-", "")


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

    methods = [("paddle", lpr_api.read_plate_paddle),
               ("yolo", lpr_api.read_plate_chars)]
    stats = {name: {"hit": 0, "read": 0, "time": 0.0} for name, _ in methods}
    rows = []
    graded = 0

    for fname in files:
        path = os.path.join(TEST_DIR, fname)
        frame = cv2.imread(path)
        if frame is None:
            print(f"⚠️  อ่านไฟล์ไม่ได้: {fname}")
            continue

        truth = norm(os.path.splitext(fname)[0])
        # ถือว่าเป็นเฉลยก็ต่อเมื่อชื่อไฟล์มีอักษรไทย (กันชื่อแบบ S__17285141)
        has_truth = any("฀" <= c <= "๿" for c in truth)
        if has_truth:
            graded += 1

        plate_img = crop_plate(frame)
        if plate_img is None:
            print(f"🔍 {fname}: ไม่เจอป้าย")
            rows.append({"ไฟล์": fname, "เฉลย": truth if has_truth else "",
                         "หมายเหตุ": "ไม่เจอป้าย"})
            continue

        row = {"ไฟล์": fname, "เฉลย": truth if has_truth else "", "หมายเหตุ": ""}
        line = [f"📄 {fname}"]

        for name, fn in methods:
            t0 = time.time()
            try:
                text, province, conf = fn(plate_img)
                err = ""
            except Exception as e:
                text, province, conf, err = "", "", 0.0, str(e)
            elapsed = time.time() - t0

            stats[name]["time"] += elapsed
            if text:
                stats[name]["read"] += 1
            correct = has_truth and norm(text) == truth
            if correct:
                stats[name]["hit"] += 1

            row[f"{name}_ผล"] = text
            row[f"{name}_จังหวัด"] = province
            row[f"{name}_conf"] = round(conf, 4)
            row[f"{name}_วินาที"] = round(elapsed, 3)
            row[f"{name}_ถูก"] = ("✓" if correct else "✗") if has_truth else ""
            if err:
                row["หมายเหตุ"] = f"{name}: {err}"

            mark = ("✓" if correct else "✗") if has_truth else " "
            line.append(f"   {mark} {name:7} '{text}' | {province} | "
                        f"conf {conf:.2f} | {elapsed:.2f}s")

        rows.append(row)
        print("\n".join(line))

    # ---------- สรุป ----------
    total = len(files)
    print("\n" + "=" * 58)
    print(f"ภาพทั้งหมด {total} ไฟล์ | มีเฉลย {graded} ไฟล์")
    for name, _ in methods:
        s = stats[name]
        avg = s["time"] / total if total else 0
        acc = f"{s['hit']}/{graded} ({s['hit'] / graded * 100:.1f}%)" if graded else "-"
        print(f"  {name:7} อ่านออก {s['read']}/{total} | ถูกต้อง {acc} | "
              f"เฉลี่ย {avg:.2f}s/ภาพ")
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
