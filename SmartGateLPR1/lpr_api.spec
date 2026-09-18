# -*- mode: python ; coding: utf-8 -*-
#
# แพ็กฝั่ง AI เป็น .exe สองตัวที่รันแยกโปรเซสกัน
#
#   lpr_api.exe  = YOLO + torch   (พอร์ต 5000) — ตัวที่โปรแกรม C# เรียก
#   ocr_api.exe  = PaddleOCR      (พอร์ต 5001) — lpr_api.exe เปิดให้เองอัตโนมัติ
#
# ที่ต้องแยกเพราะ torch กับ paddle อยู่โปรเซสเดียวกันไม่ได้เมื่อทั้งคู่เป็นรุ่น GPU
# (ชนกันตอนลงทะเบียนชนิดข้อมูลของ pybind11: _gpuDeviceProperties already registered)
# แยกแล้วใช้การ์ดจอได้ทั้งคู่
#
# สร้างด้วย:  pyinstaller lpr_api.spec
# ได้ผลลัพธ์ที่ dist/lpr_api/ ซึ่งมี lpr_api.exe กับ ocr_api.exe อยู่ด้วยกัน

from PyInstaller.utils.hooks import collect_all


def gather(*packages):
    datas, binaries, hiddenimports = [], [], []
    for pkg in packages:
        d, b, h = collect_all(pkg)
        datas += d
        binaries += b
        hiddenimports += h
    return datas, binaries, hiddenimports


# ---------- lpr_api: YOLO เท่านั้น (ไม่มี paddle) ----------
lpr_datas = [('plate_detector.pt', '.')]
lpr_binaries = []
lpr_hidden = []
d, b, h = gather('ultralytics', 'torch', 'torchvision', 'cv2')
lpr_datas += d
lpr_binaries += b
lpr_hidden += h

lpr_a = Analysis(
    ['lpr_api.py'],
    pathex=[],
    binaries=lpr_binaries,
    datas=lpr_datas,
    hiddenimports=lpr_hidden,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    # กัน PyInstaller ลาก paddle เข้ามาใน exe นี้โดยไม่ตั้งใจ — ถ้าหลุดเข้ามา
    # จะกลายเป็นโปรเซสเดียวที่มีทั้ง torch และ paddle ซึ่งคือปัญหาที่เราแยกหนี
    excludes=['paddle', 'paddleocr', 'paddlex'],
    noarchive=False,
    optimize=0,
)

# ---------- ocr_api: PaddleOCR เท่านั้น (ไม่มี torch) ----------
ocr_datas = [('thai_plate.py', '.')]
ocr_binaries = []
ocr_hidden = ['thai_plate']
d, b, h = gather('paddleocr', 'paddle', 'paddlex', 'cv2')
ocr_datas += d
ocr_binaries += b
ocr_hidden += h

ocr_a = Analysis(
    ['ocr_api.py'],
    pathex=[],
    binaries=ocr_binaries,
    datas=ocr_datas,
    hiddenimports=ocr_hidden,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=['torch', 'torchvision', 'ultralytics'],
    noarchive=False,
    optimize=0,
)

MERGE((lpr_a, 'lpr_api', 'lpr_api'), (ocr_a, 'ocr_api', 'ocr_api'))

lpr_pyz = PYZ(lpr_a.pure)
ocr_pyz = PYZ(ocr_a.pure)

lpr_exe = EXE(
    lpr_pyz,
    lpr_a.scripts,
    [],
    exclude_binaries=True,
    name='lpr_api',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=True,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)

ocr_exe = EXE(
    ocr_pyz,
    ocr_a.scripts,
    [],
    exclude_binaries=True,
    name='ocr_api',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=True,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)

# รวมทุกอย่างไว้โฟลเดอร์เดียว ให้ ocr_api.exe อยู่ข้าง lpr_api.exe
# (lpr_api หา ocr_api.exe จากโฟลเดอร์เดียวกับตัวเอง — ดู ensure_ocr_service)
coll = COLLECT(
    lpr_exe,
    lpr_a.binaries,
    lpr_a.datas,
    ocr_exe,
    ocr_a.binaries,
    ocr_a.datas,
    strip=False,
    upx=True,
    upx_exclude=[],
    name='lpr_api',
)
