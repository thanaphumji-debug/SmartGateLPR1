import cv2
import sys
import json
from ultralytics import YOLO
from paddleocr import PaddleOCR

# 1. โหลดสมอง AI ทั้ง 2 ตัว
# YOLOv8n คือตัวที่หาป้ายเก่งและเร็วมาก
model_detector = YOLO('yolov8n.pt') # หรือใช้โมเดลที่เทรนหาป้ายทะเบียนโดยเฉพาะ
ocr = PaddleOCR(use_angle_cls=True, lang='th', show_log=False)

def process_oblique_view(image_path):
    # อ่านภาพต้นฉบับ
    img = cv2.imread(image_path)
    
    # --- STEP 1: หาตำแหน่งป้ายทะเบียนด้วย YOLO ---
    # (ในขั้นตอนนี้ YOLO จะบอกพิกัด x, y, w, h ของป้าย)
    results = model_detector(img)
    
    final_results = []
    
    for r in results:
        boxes = r.boxes
        for box in boxes:
            # ตัดภาพ (Crop) เฉพาะตรงที่ AI คิดว่าเป็นป้ายทะเบียน
            x1, y1, x2, y2 = map(int, box.xyxy[0])
            cropped_plate = img[y1:y2, x1:x2]
            
            # --- STEP 2: ปรับปรุงภาพ (ทำให้ชัดขึ้นสำหรับมุมเฉียง) ---
            gray = cv2.cvtColor(cropped_plate, cv2.COLOR_BGR2GRAY)
            # เพิ่มความคมชัด (Contrast)
            enhanced = cv2.detailEnhance(gray, sigma_s=10, sigma_r=0.15)
            
            # --- STEP 3: ส่งให้อ่านภาษาไทย ---
            # บันทึกภาพชั่วคราวเพื่อส่งให้ OCR
            cv2.imwrite('temp_crop.jpg', enhanced)
            ocr_result = ocr.ocr('temp_crop.jpg', cls=True)
            
            if ocr_result[0]:
                for line in ocr_result[0]:
                    text = line[1][0]
                    conf = line[1][1]
                    if conf > 0.4:
                        final_results.append({"text": text, "confidence": float(conf)})
                        
    return final_results

if __name__ == "__main__":
    # บังคับการส่งค่าเป็น UTF-8 เพื่อ C# จะได้ไม่อ่านเป็นต่างด้าว
    sys.stdout.reconfigure(encoding='utf-8')
    if len(sys.argv) > 1:
        img_path = sys.argv[1]
        print(json.dumps(process_oblique_view(img_path), ensure_ascii=False))