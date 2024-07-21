from src.model import WhisperModelCT2

import torch
import wave
import time

model_path = "."

if __name__ == "__main__":
    print("transcribing jfk.wav")
    #unittest.main()

#class Test(unittest.TestCase):
    #def test_transcribe(self):
    with wave.open("jfk.wav", "rb") as wave_file:
        samples = wave_file.readframes(int(wave_file.getnframes()))
        print(f"found samples: {len(samples)}")
        print(f"cuda is available: {torch.cuda.is_available()}")
        model = WhisperModelCT2(
            model_path=model_path,
            device="cuda",
            #compute_type="float16",
            compute_type="float32",
        )
        start_time = time.time()
        samples_batch = [samples]
        lang_codes = ["en"]
        response = model.transcribe_with_vad(samples_batch=samples_batch, lang_codes=lang_codes, batch_size=8)
        elapsed_time = time.time() - start_time
        print(f"elapsed time: {elapsed_time} seconds")
        expected = "And so my fellow Americans, ask not what your country can do for you, ask what you can do for your country."
        print(f"transcription: {response[0]["text"]}")
        input("Finished. Press enter to close this window.")