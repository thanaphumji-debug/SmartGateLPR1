import sys
import json
import torch
from transformers import AutoProcessor, AutoModelForImageTextToText
from peft import PeftModel
from PIL import Image

# 1. บังคับ Encoding ให้เป็น UTF-8 เพื่อให้ C# อ่านภาษาไทยได้
if sys.stdout.encoding != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8')

def process_hurricane_ocr(image_path):
    try:
        base_model_name = "scb10x/typhoon-ocr1.5-2b"
        
        # 2. โหลด Processor และ Model (ตอนนี้ไฟล์อยู่ในเครื่องคุณแล้ว จะโหลดเร็วขึ้นมาก)
        processor = AutoProcessor.from_pretrained(base_model_name)
        base_model = AutoModelForImageTextToText.from_pretrained(
            base_model_name,
            torch_dtype=torch.float16,
            device_map="auto" 
        )

        # 3. โหลดส่วนเสริม (LoRA) ป้ายทะเบียนไทย
        model = PeftModel.from_pretrained(base_model, "Rattatammanoon/hurricane-ocr-tlpr-v1-LoRA")
        model.eval()

        # 4. เปิดรูปภาพ
        image = Image.open(image_path).convert("RGB")

        # 5. สร้างคำสั่งแบบใหม่ (บังคับโครงสร้างสำหรับ AI รุ่นล่าสุด)
        messages = [
            {
                "role": "user",
                "content": [
                    {"type": "image"},
                    {"type": "text", "text": "อ่านตัวอักษรและตัวเลขบนป้ายทะเบียนนี้:"}
                ]
            }
        ]
        
        # ใช้ processor ในการจัดฟอร์แมตคำสั่ง
        prompt = processor.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
        
        # 6. ส่งรูปและคำสั่งให้ AI
        inputs = processor(text=[prompt], images=[image], return_tensors="pt").to(model.device)

        # 7. ประมวลผล
        with torch.no_grad():
            generated_ids = model.generate(**inputs, max_new_tokens=50)
            
            # ตัดเอาเฉพาะคำตอบ ไม่เอาคำสั่งมาโชว์ซ้ำ
            generated_ids_trimmed = [
                out_ids[len(in_ids):] for in_ids, out_ids in zip(inputs.input_ids, generated_ids)
            ]
            generated_texts = processor.batch_decode(generated_ids_trimmed, skip_special_tokens=True)
        
        result_text = generated_texts[0].strip()

        # จำลองค่า Confidence เนื่องจาก AI แบบข้อความไม่มีค่าตัวเลขนี้
        return [{"text": result_text, "confidence": 0.99}]

    except Exception as e:
        return [{"error": str(e)}]

if __name__ == "__main__":
    if len(sys.argv) > 1:
        image_input = sys.argv[1]
        final_result = process_hurricane_ocr(image_input)
        print(json.dumps(final_result, ensure_ascii=False))