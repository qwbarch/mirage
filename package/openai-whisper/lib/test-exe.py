from src.model import WhisperModelCT2

import wave
import time

print("program start")

model_path = "model/whisper-base"

def run_test(model, samples):
    print("running test")
    start_time = time.time()
    i = 10
    samples_batch = [samples] * i
    lang_codes = ["en"] * i
    model.transcribe_with_vad(samples_batch=samples_batch, lang_codes=lang_codes, batch_size=32)
    elapsed_time = time.time() - start_time
    print(f"elapsed time: {elapsed_time} seconds")

print("opening jfk.wav")
with wave.open("jfk.wav", "rb") as wave_file:
    print("finished opening jfk.wav")
    samples = wave_file.readframes(int(wave_file.getnframes()))
    model = WhisperModelCT2(
        model_path=model_path,
        device="cuda",
        compute_type="float16",
    )
    for _ in range(10):
        run_test(model, samples)