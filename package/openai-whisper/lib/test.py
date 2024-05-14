from src.model import WhisperModelCT2

import unittest
import wave
import time

model_path = "../../../model/whisper-base"

if __name__ == "__main__":
    unittest.main()

class Test(unittest.TestCase):
    def test_transcribe(self):
        with wave.open("../jfk.wav", "rb") as wave_file:
            samples = wave_file.readframes(int(wave_file.getnframes()))
            model = WhisperModelCT2(
                model_path=model_path,
                device="cuda",
                compute_type="float16",
            )
            for _ in range(10):
                self.run_test(model, samples)

    def run_test(self, model, samples):
        start_time = time.time()
        i = 10
        samples_batch = [samples] * i
        lang_codes = ["en"] * i
        response = model.transcribe_with_vad(samples_batch=samples_batch, lang_codes=lang_codes, batch_size=32)
        elapsed_time = time.time() - start_time
        print(f"elapsed time: {elapsed_time} seconds")
        expected = "And so my fellow Americans, ask not what your country can do for you, ask what you can do for your country."
        self.assertEqual(expected, response[0]["text"])