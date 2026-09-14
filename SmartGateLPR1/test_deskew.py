# -*- coding: utf-8 -*-
"""
สคริปต์ทดสอบเปรียบเทียบวิธีปรับแก้ความเอียงของแผ่นป้าย (เวอร์ชัน 2)

เทียบ 4 วิธี:
  1. ไม่แก้เอียง
  2. แอฟฟีน (ของเดิมใน lpr_api.py — วัดมุมจากพิกเซลทั้งภาพ)
  3. แอฟฟีนแก้แล้ว (วัดมุมจาก contour ของตัวป้าย)
  4. เพอร์สเปกทีฟ (ถ้าไม่ผ่านเงื่อนไข ถอยไปใช้แอฟฟีนแก้แล้ว)

จุดต่างจากเวอร์ชันแรก
---------------------
* รับภาพเต็มฉากได้ (รูปถ่ายมือถือ / เฟรมจากกล้อง) — จะใช้ plate_detector.pt
  หาป้ายและ crop ให้อัตโนมัติก่อน เหมือนที่ lpr_api.py ทำ
* ถ้าใส่ภาพป้ายที่ crop มาแล้ว ก็ยังใช้ได้ (ตั้ง AUTO_CROP = False)
* รายงานมุมที่แต่ละวิธีวัดได้ ทำให้เห็นชัดว่าวิธีไหนวัดติด วิธีไหนวัดไม่ติด

วิธีใช้
-------
1. วางไฟล์นี้ในโฟลเดอร์เดียวกับ lpr_api.py
2. สร้างโฟลเดอร์ test_images แล้ววางภาพลงไป (ภาพเต็มฉากก็ได้)
3. (ทางเลือก) ตั้งชื่อไฟล์เป็นเลขทะเบียนจริง เช่น กย3779.jpg
   เพื่อให้คำนวณความแม่นยำได้
4. รัน:
   python C:\\Users\\Gigabyte_2\\source\\repos\\thanaphumji-debug\\SmartGateLPR1\\SmartGateLPR1\\test_deskew2.py
"""

import os
import sys
import csv
import time

import cv2
import numpy as np
import torch
from ultralytics import YOLO

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
PLATE_DETECTOR_PATH = os.path.join(BASE_DIR, "plate_detector.pt")
CHAR_DETECTOR_PATH = os.path.join(BASE_DIR, "char_detector.pt")
TEST_DIR = os.path.join(BASE_DIR, "test_images")
OUT_DIR = os.path.join(BASE_DIR, "test_deskew_output")
CSV_PATH = os.path.join(BASE_DIR, "test_deskew_result.csv")

# ---------------- ค่าตั้งค่า (ตรงกับ lpr_api.py) ----------------
AUTO_CROP = True        # True = ใช้ YOLO หาป้ายและ crop ให้ก่อน
DETECT_CONF = 0.25
CHAR_CONF = 0.25
CROP_PADDING = 12
SAVE_IMAGES = True
# ---------------------------------------------------------------

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
    "SKM": "สมุทรสงคราม", "SKN": "สมุทรสาคร",
    "SRI": "สระบุรี", "SBR": "สิงห์บุรี",
    "STI": "สุโขทัย", "BTG": "?",
}


# ===============================================================
#  วิธีที่ 1: ไม่แก้เอียง
# ===============================================================
def deskew_none(img):
    return img, ""


# ===============================================================
#  วิธีที่ 2: แอฟฟีนของเดิม — วัดมุมจากพิกเซลทั้งภาพ
#  (ยกมาจาก lpr_api.py ตรง ๆ เพิ่มแค่การรายงานมุม)
# ===============================================================
def deskew_affine_old(img, max_angle=25.0):
    try:
        if img is None or img.size == 0:
            return img, "ภาพว่าง"
        h, w = img.shape[:2]
        if h < 10 or w < 20:
            return img, "ภาพเล็กเกินไป"

        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        gray = cv2.GaussianBlur(gray, (3, 3), 0)
        th = cv2.threshold(gray, 0, 255,
                           cv2.THRESH_BINARY_INV + cv2.THRESH_OTSU)[1]

        coords = cv2.findNonZero(th)
        if coords is None or len(coords) < 20:
            return img, "พิกเซลน้อยเกินไป"

        angle = cv2.minAreaRect(coords)[-1]
        if angle > 45:
            angle -= 90
        elif angle < -45:
            angle += 90

        if abs(angle) < 2 or abs(angle) > max_angle:
            return img, f"วัดได้ {angle:.2f}° → ไม่หมุน"

        M = cv2.getRotationMatrix2D((w / 2.0, h / 2.0), angle, 1.0)
        out = cv2.warpAffine(img, M, (w, h),
                             flags=cv2.INTER_CUBIC,
                             borderMode=cv2.BORDER_REPLICATE)
        return out, f"หมุน {angle:.2f}°"
    except Exception as e:
        return img, f"ผิดพลาด ({e})"


# ===============================================================
#  วิธีที่ 3: แอฟฟีนแก้แล้ว — วัดมุมจาก contour ของตัวป้าย
# ===============================================================
def deskew_affine_fixed(img, max_angle=25.0):
    try:
        if img is None or img.size == 0:
            return img, "ภาพว่าง"
        h, w = img.shape[:2]
        if h < 10 or w < 20:
            return img, "ภาพเล็กเกินไป"

        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        gray = cv2.GaussianBlur(gray, (3, 3), 0)
        th = cv2.threshold(gray, 0, 255,
                           cv2.THRESH_BINARY + cv2.THRESH_OTSU)[1]
        th = cv2.morphologyEx(th, cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8))

        cnts, _ = cv2.findContours(th, cv2.RETR_EXTERNAL,
                                   cv2.CHAIN_APPROX_SIMPLE)
        if not cnts:
            return img, "ไม่พบ contour"

        c = max(cnts, key=cv2.contourArea)
        frac = cv2.contourArea(c) / float(h * w)
        if frac < 0.15:
            return img, f"contour เล็กไป ({frac*100:.1f}%)"

        angle = cv2.minAreaRect(c)[-1]
        if angle > 45:
            angle -= 90
        elif angle < -45:
            angle += 90

        if abs(angle) < 2 or abs(angle) > max_angle:
            return img, f"วัดได้ {angle:.2f}° → ไม่หมุน"

        M = cv2.getRotationMatrix2D((w / 2.0, h / 2.0), angle, 1.0)
        out = cv2.warpAffine(img, M, (w, h),
                             flags=cv2.INTER_CUBIC,
                             borderMode=cv2.BORDER_REPLICATE)
        return out, f"หมุน {angle:.2f}°"
    except Exception as e:
        return img, f"ผิดพลาด ({e})"


# ===============================================================
#  วิธีที่ 4: เพอร์สเปกทีฟ (ถอยไปแอฟฟีนแก้แล้วถ้าไม่ผ่าน)
# ===============================================================
def deskew_perspective(img):
    try:
        if img is None or img.size == 0:
            return img, "ภาพว่าง"
        h, w = img.shape[:2]
        if h < 10 or w < 20:
            return img, "ภาพเล็กเกินไป"

        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        gray = cv2.GaussianBlur(gray, (3, 3), 0)
        th = cv2.threshold(gray, 0, 255,
                           cv2.THRESH_BINARY + cv2.THRESH_OTSU)[1]
        th = cv2.morphologyEx(th, cv2.MORPH_CLOSE, np.ones((7, 7), np.uint8))
        th = cv2.morphologyEx(th, cv2.MORPH_OPEN, np.ones((7, 7), np.uint8))

        cnts, _ = cv2.findContours(th, cv2.RETR_EXTERNAL,
                                   cv2.CHAIN_APPROX_SIMPLE)
        if not cnts:
            out, _ = deskew_affine_fixed(img)
            return out, "ไม่พบ contour → ถอยไปแอฟฟีน"

        c = max(cnts, key=cv2.contourArea)
        frac = cv2.contourArea(c) / float(h * w)
        if frac < 0.35:
            out, _ = deskew_affine_fixed(img)
            return out, f"contour เล็กไป ({frac*100:.1f}%) → ถอยไปแอฟฟีน"

        ap = cv2.approxPolyDP(c, 0.02 * cv2.arcLength(c, True), True)
        if len(ap) != 4:
            out, _ = deskew_affine_fixed(img)
            return out, f"ได้ {len(ap)} มุม → ถอยไปแอฟฟีน"

        pts = ap.reshape(4, 2).astype("float32")
        s = pts.sum(1)
        d = np.diff(pts, axis=1).ravel()
        src = np.array([pts[np.argmin(s)], pts[np.argmin(d)],
                        pts[np.argmax(s)], pts[np.argmax(d)]], dtype="float32")
        tl, tr, br, bl = src

        Wt = int(max(np.linalg.norm(tr - tl), np.linalg.norm(br - bl)))
        Ht = int(max(np.linalg.norm(bl - tl), np.linalg.norm(br - tr)))
        if Wt < 20 or Ht < 10:
            out, _ = deskew_affine_fixed(img)
            return out, "ผลลัพธ์เล็กเกินไป → ถอยไปแอฟฟีน"

        ratio = Wt / float(Ht)
        if not (1.5 < ratio < 6.0):
            out, _ = deskew_affine_fixed(img)
            return out, f"สัดส่วนผิดปกติ ({ratio:.2f}) → ถอยไปแอฟฟีน"

        dst = np.array([[0, 0], [Wt - 1, 0],
                        [Wt - 1, Ht - 1], [0, Ht - 1]], dtype="float32")
        M = cv2.getPerspectiveTransform(src, dst)
        out = cv2.warpPerspective(img, M, (Wt, Ht), flags=cv2.INTER_CUBIC)
        return out, f"สำเร็จ {Wt}x{Ht} (สัดส่วน {ratio:.2f})"
    except Exception as e:
        out, _ = deskew_affine_fixed(img)
        return out, f"ผิดพลาด → ถอยไปแอฟฟีน ({e})"


# ===============================================================
#  crop ป้ายจากภาพเต็มฉากด้วย plate_detector (เหมือน lpr_api.py)
# ===============================================================
def crop_plate(detector, frame, device):
    det = detector(frame, conf=DETECT_CONF, imgsz=1280,
                   half=False, verbose=False, device=device)
    boxes = det[0].boxes
    if boxes is None or len(boxes) == 0:
        return None

    best_i = int(boxes.conf.argmax())
    x1, y1, x2, y2 = map(int, boxes.xyxy[best_i].tolist())

    h, w = frame.shape[:2]
    x1 = max(0, x1 - CROP_PADDING)
    y1 = max(0, y1 - CROP_PADDING)
    x2 = min(w, x2 + CROP_PADDING)
    y2 = min(h, y2 + CROP_PADDING)

    plate = frame[y1:y2, x1:x2]
    return plate if plate.size else None


# ===============================================================
#  อ่านตัวอักษร (ยกมาจาก lpr_api.py)
# ===============================================================
def read_plate_chars(char_detector, plate_img, device):
    res = char_detector(plate_img, conf=CHAR_CONF,
                        verbose=False, device=device)
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

    chars.sort(key=lambda c: c[1])
    plate_text = "".join(c[0] for c in chars)

    province = ""
    if provinces:
        province = max(provinces, key=lambda p: p[1])[0]

    avg_conf = sum(scores) / len(scores) if scores else 0.0
    return plate_text, province, avg_conf


# ===============================================================
#  โปรแกรมหลัก
# ===============================================================
def main():
    if not os.path.isdir(TEST_DIR):
        print(f"❌ ไม่พบโฟลเดอร์ {TEST_DIR}")
        print("   สร้างโฟลเดอร์ test_images แล้ววางภาพลงไปก่อน")
        return

    exts = (".jpg", ".jpeg", ".png", ".bmp")
    files = sorted(f for f in os.listdir(TEST_DIR) if f.lower().endswith(exts))
    if not files:
        print(f"❌ ไม่พบไฟล์ภาพในโฟลเดอร์ {TEST_DIR}")
        return

    if SAVE_IMAGES:
        os.makedirs(OUT_DIR, exist_ok=True)

    device = 0 if torch.cuda.is_available() else "cpu"
    print("=" * 64)
    print(f"อุปกรณ์: {'GPU - ' + torch.cuda.get_device_name(0) if device == 0 else 'CPU'}")
    print(f"จำนวนภาพ: {len(files)}   |   crop อัตโนมัติ: {'เปิด' if AUTO_CROP else 'ปิด'}")
    print("=" * 64)

    detector = None
    if AUTO_CROP:
        print("⏳ กำลังโหลดโมเดลหาป้าย...")
        detector = YOLO(PLATE_DETECTOR_PATH)
    print("⏳ กำลังโหลดโมเดลอ่านตัวอักษร...")
    char_detector = YOLO(CHAR_DETECTOR_PATH)
    print("✅ พร้อมแล้ว\n")

    methods = [
        ("ไม่แก้เอียง", deskew_none),
        ("แอฟฟีนเดิม", deskew_affine_old),
        ("แอฟฟีนแก้แล้ว", deskew_affine_fixed),
        ("เพอร์สเปกทีฟ", deskew_perspective),
    ]

    rows = []
    stats = {name: {"read": 0, "conf": 0.0, "time": 0.0, "correct": 0}
             for name, _ in methods}
    has_truth = 0
    skipped = 0

    for idx, fname in enumerate(files, 1):
        path = os.path.join(TEST_DIR, fname)
        img = cv2.imread(path)
        if img is None:
            print(f"[{idx}/{len(files)}] ⚠️  อ่านไฟล์ไม่ได้: {fname}")
            skipped += 1
            continue

        stem = os.path.splitext(fname)[0].strip()
        truth_valid = bool(stem) and not stem[0].isascii()
        if truth_valid:
            has_truth += 1

        print(f"[{idx}/{len(files)}] {fname}  ({img.shape[1]}x{img.shape[0]})")

        plate = img
        if AUTO_CROP:
            cropped = crop_plate(detector, img, device)
            if cropped is None:
                print("     ⚠️  หาป้ายไม่เจอ — ข้ามภาพนี้\n")
                skipped += 1
                continue
            plate = cropped
            print(f"     crop ป้ายได้ {plate.shape[1]}x{plate.shape[0]}")

        row = {"ไฟล์": fname, "เลขจริง": stem if truth_valid else ""}

        for name, fn in methods:
            t0 = time.time()
            out_img, note = fn(plate)
            plate_text, province, conf = read_plate_chars(
                char_detector, out_img, device)
            elapsed = time.time() - t0

            stats[name]["time"] += elapsed
            if plate_text:
                stats[name]["read"] += 1
                stats[name]["conf"] += conf
            if truth_valid and plate_text == stem:
                stats[name]["correct"] += 1

            row[f"{name}_ผล"] = plate_text
            row[f"{name}_จังหวัด"] = province
            row[f"{name}_conf"] = round(conf, 4)
            row[f"{name}_หมายเหตุ"] = note

            mark = ""
            if truth_valid:
                mark = " ✅" if plate_text == stem else " ❌"
            print(f"     {name:<16} '{plate_text}' {province} "
                  f"(conf {conf:.2f}){mark}")
            if note:
                print(f"     {'':<16} └ {note}")

            if SAVE_IMAGES:
                safe = name.replace(" ", "_")
                cv2.imwrite(os.path.join(OUT_DIR, f"{stem}_{safe}.jpg"), out_img)

        rows.append(row)
        print()

    n = len(rows)
    if n == 0:
        print("❌ ไม่มีภาพที่ประมวลผลได้")
        return

    print("=" * 64)
    print("สรุปผลการทดสอบ")
    print("=" * 64)
    print(f"ประมวลผลสำเร็จ {n} ภาพ" + (f" (ข้าม {skipped} ภาพ)" if skipped else ""))
    if has_truth:
        print(f"ภาพที่มีเลขทะเบียนจริงกำกับ: {has_truth}")
    print()

    print(f"{'วิธี':<18}{'อ่านได้':<16}{'conf เฉลี่ย':<14}{'เวลาเฉลี่ย':<14}"
          + ("ถูกต้อง" if has_truth else ""))
    print("-" * 64)

    for name, _ in methods:
        s = stats[name]
        read_txt = f"{s['read']}/{n} ({100.0*s['read']/n:.0f}%)"
        avg_conf = s["conf"] / s["read"] if s["read"] else 0.0
        avg_time = s["time"] / n
        line = f"{name:<18}{read_txt:<16}{avg_conf:<14.3f}{avg_time:<14.3f}"
        if has_truth:
            line += f"{s['correct']}/{has_truth} ({100.0*s['correct']/has_truth:.0f}%)"
        print(line)

    print()
    if not has_truth:
        print("💡 ตั้งชื่อไฟล์เป็นเลขทะเบียนจริง (เช่น กย3779.jpg)")
        print("   เพื่อให้สคริปต์คำนวณความแม่นยำให้ด้วย")

    keys = list(rows[0].keys())
    with open(CSV_PATH, "w", newline="", encoding="utf-8-sig") as f:
        writer = csv.DictWriter(f, fieldnames=keys)
        writer.writeheader()
        writer.writerows(rows)
    print(f"📄 ผลละเอียด: {CSV_PATH}")
    if SAVE_IMAGES:
        print(f"🖼️  ภาพผลลัพธ์: {OUT_DIR}")


if __name__ == "__main__":
    main()