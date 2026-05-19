import torch
from flask import Flask, request, jsonify
from transformers import AutoProcessor, AutoModelForImageTextToText
from peft import PeftModel
from PIL import Image
import io

app = Flask(__name__)

# --- โหลด AI รอไว้เลยตั้งแต่ออกตัว ---
print("กำลังโหลดสมอง AI... กรุณารอครู่เดียว")
base_model_name = "scb10x/typhoon-ocr1.5-2b"
processor = AutoProcessor.from_pretrained(base_model_name)
base_model = AutoModelForImageTextToText.from_pretrained(
    base_model_name, torch_dtype=torch.float16, device_map="auto"
)
model = PeftModel.from_pretrained(base_model, "Rattatammanoon/hurricane-ocr-tlpr-v1-LoRA")
model.eval()
print("AI พร้อมทำงานแล้ว! (Listening on port 5000)")

@app.route('/predict', methods=['POST'])
def predict():
    try:
        # รับไฟล์รูปจาก C#
        file = request.files['image'].read()
        image = Image.open(io.BytesIO(file)).convert("RGB")

        # ส่งให้ AI อ่าน
        messages = [{"role": "user", "content": [{"type": "image"}, {"type": "text", "text": "อ่านตัวอักษรและตัวเลขบนป้ายทะเบียนนี้:"}]}]
        prompt = processor.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
        inputs = processor(text=[prompt], images=[image], return_tensors="pt").to(model.device)

        with torch.no_grad():
            generated_ids = model.generate(**inputs, max_new_tokens=50)
            generated_ids_trimmed = [out_ids[len(in_ids):] for in_ids, out_ids in zip(inputs.input_ids, generated_ids)]
            result_text = processor.batch_decode(generated_ids_trimmed, skip_special_tokens=True)[0].strip()

        return jsonify({"text": result_text, "status": "success"})
    except Exception as e:
        return jsonify({"error": str(e), "status": "error"})

if __name__ == '__main__':
    app.run(host='0.0.0.0', port=5000)