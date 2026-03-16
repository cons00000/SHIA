import wave
from piper import PiperVoice

voice = PiperVoice.load("en_GB-alan-low.onnx")
with wave.open("test.wav", "wb") as wav_file:
    voice.synthesize_wav("Welcome, welcome, how are you doing today, I am really happy to meet you!", wav_file)