import socket
import json
import torch
import wave
import io
import numpy as np
from transformers import Wav2Vec2Processor, Wav2Vec2ForCTC

HOST = "127.0.0.1"
PORT = 50007

print("Loading Wav2Vec2 model...")

from transformers import Wav2Vec2FeatureExtractor, Wav2Vec2CTCTokenizer, Wav2Vec2Processor, Wav2Vec2ForCTC

feature_extractor = Wav2Vec2FeatureExtractor.from_pretrained(
    "facebook/wav2vec2-lv-60-espeak-cv-ft"
)

tokenizer = Wav2Vec2CTCTokenizer.from_pretrained(
    "facebook/wav2vec2-lv-60-espeak-cv-ft"
)

processor = Wav2Vec2Processor(feature_extractor=feature_extractor, tokenizer=tokenizer)

model = Wav2Vec2ForCTC.from_pretrained(
    "facebook/wav2vec2-lv-60-espeak-cv-ft"
)
model.eval()

print("Model loaded. Starting server...")

def process_wav_bytes(wav_bytes):
    try:
        buffer = io.BytesIO(wav_bytes)
        with wave.open(buffer, "rb") as wf:
            sample_rate = wf.getframerate()
            n_channels = wf.getnchannels()
            n_frames = wf.getnframes()
            sampwidth = wf.getsampwidth()
            audio_raw = wf.readframes(n_frames)

        if sampwidth not in (2, 4):
            return {"error": "Unsupported WAV sample width"}

        dtype = np.int16 if sampwidth == 2 else np.int32
        audio_np = np.frombuffer(audio_raw, dtype=dtype).astype(np.float32)

        if n_channels > 1:
            audio_np = audio_np.reshape(-1, n_channels).mean(axis=1)

        max_val = np.max(np.abs(audio_np))
        if max_val > 0:
            audio_np /= max_val

        inputs = processor(audio_np, sampling_rate=sample_rate, return_tensors="pt")

        with torch.no_grad():
            logits = model(inputs.input_values).logits

        predicted_ids = torch.argmax(logits, dim=-1)
        transcription = processor.batch_decode(predicted_ids)[0]

        return {"transcription": transcription}

    except Exception as e:
        return {"error": str(e)}


with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
    s.bind((HOST, PORT))
    s.listen()
    print(f"Server running on {HOST}:{PORT}")

    while True:
        conn, addr = s.accept()
        with conn:
            length_bytes = conn.recv(4)
            if not length_bytes:
                continue

            length = int.from_bytes(length_bytes, "big")

            data = b""
            while len(data) < length:
                packet = conn.recv(4096)
                if not packet:
                    break
                data += packet

            result = process_wav_bytes(data)

            conn.sendall(json.dumps(result,ensure_ascii=False).encode("utf-8"))
