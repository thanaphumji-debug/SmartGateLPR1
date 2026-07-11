from flask import Flask, request, jsonify
import torch
from transformers import AutoProcessor, AutoModelForImageTextToText
from peft import PeftModel
from PIL import Image
import io

app = Flask(__name__)

print("⏳ กำลังโหลดโมเดล AI... (รอสักครู่)")

# --- 1. ระบบตรวจจับการ์ดจออัตโนมัติ (Universal GPU Detection) ---
device = "cuda" if torch.cuda.is_available() else "cpu"
print(f"🎯 อุปกรณ์ที่สมอง AI กำลังใช้: {device.upper()}")

if device == "cuda":
    print(f"🖥️ ชื่อการ์ดจอที่พบ: {torch.cuda.get_device_name(0)}")
else:
    print("⚠️ แจ้งเตือน: ระบบไม่พบการ์ดจอ NVIDIA (กำลังใช้ CPU ซึ่งอาจจะทำงานช้า)")

# --- 2. โหลดโมเดลด้วยมาตรฐานประหยัดทรัพยากร ---
base_model_name = "scb10x/typhoon-ocr1.5-2b"
processor = AutoProcessor.from_pretrained(base_model_name)

# ใช้ float16 เพื่อบีบอัดขนาดโมเดลให้เข้ากับการ์ดจอทั่วไป (8GB - 12GB) 
# และใช้ device_map="auto" ให้ระบบฉลาดพอที่จะกระจายแรมเอง
base_model = AutoModelForImageTextToText.from_pretrained(
    base_model_name, 
    torch_dtype=torch.float16, 
    device_map="auto" 
)

model = PeftModel.from_pretrained(base_model, "Rattatammanoon/hurricane-ocr-tlpr-v1-LoRA")
model.eval()

print("✅ AI พร้อมทำงานแล้ว! สแตนด์บายรอรับรูปภาพที่ Port 5000")

@app.route('/predict', methods=['POST'])
def predict():
    if 'image' not in request.files:
        return jsonify({"status": "error", "message": "ไม่พบไฟล์รูปภาพ"})

    try:
        # รับรูปภาพจาก C#
        file = request.files['image']
        image = Image.open(io.BytesIO(file.read())).convert("RGB")

        # เตรียมคำสั่งให้ AI
        messages = [{"role": "user", "content": [{"type": "image"}, {"type": "text", "text": "อ่านตัวอักษรและตัวเลขบนป้ายทะเบียนนี้:"}]}]
        prompt = processor.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
        
        # --- 3. โยนงานขึ้นไปประมวลผลบนการ์ดจอที่ตรวจพบ ---
        inputs = processor(text=[prompt], images=[image], return_tensors="pt").to(device) 

        # เริ่มประมวลผลอ่านข้อความ
        with torch.no_grad():
            generated_ids = model.generate(**inputs, max_new_tokens=50)
            generated_ids_trimmed = [out_ids[len(in_ids):] for in_ids, out_ids in zip(inputs.input_ids, generated_ids)]
            result_text = processor.batch_decode(generated_ids_trimmed, skip_special_tokens=True)[0].strip()

        # --- 4. ล้างขยะใน VRAM ของการ์ดจอหลังอ่านเสร็จ ---
        if device == "cuda":
            torch.cuda.empty_cache()

        print(f"🚗 อ่านป้ายได้: {result_text}")
        return jsonify({"status": "success", "text": result_text})

    except Exception as e:
        print(f"❌ Error: {e}")
        return jsonify({"status": "error", "message": str(e)})

if __name__ == '__main__':
    # เปิดเซิร์ฟเวอร์
    app.run(host='0.0.0.0', port=5000)