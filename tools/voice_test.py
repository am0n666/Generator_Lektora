import argparse
import os
import sys
import numpy as np
import soundfile as sf


def die(msg):
    print(msg, file=sys.stderr)
    raise SystemExit(1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--text-file", required=True)
    ap.add_argument("--output", required=True)
    ap.add_argument("--voice", default="M3")
    ap.add_argument("--steps", type=int, default=12)
    ap.add_argument("--speed", type=float, default=1.0)
    args = ap.parse_args()

    try:
        with open(args.text_file, "r", encoding="utf-8") as f:
            text = f.read().strip()
    except Exception as e:
        die(f"Nie można odczytać tekstu: {e}")

    if not text:
        die("Tekst do syntezy jest pusty.")

    print("Ładowanie Supertonic 3...")
    try:
        from supertonic import TTS
    except ImportError as e:
        die(f"Brak pakietu supertonic: {e}")

    try:
        tts = TTS(auto_download=True)
        try:
            style = tts.get_voice_style(voice_name=args.voice.upper())
        except Exception:
            print(f"Nie znaleziono stylu {args.voice}; używam M1.")
            style = tts.get_voice_style(voice_name="M1")

        print(f"Generowanie próbki głosu {args.voice.upper()}...")
        try:
            wav, _ = tts.synthesize(
                text=text,
                lang="pl",
                voice_style=style,
                total_steps=args.steps,
                speed=args.speed,
            )
        except TypeError:
            wav, _ = tts.synthesize(
                text=text,
                lang="pl",
                voice_style=style,
                total_steps=args.steps,
            )

        if hasattr(wav, "squeeze"):
            arr = wav.squeeze()
        else:
            arr = np.array(wav).flatten()
        arr = np.asarray(arr, dtype=np.float32).flatten()
        sf.write(args.output, arr, 44100, subtype="PCM_16")
        print("Próbka gotowa.")
    except Exception as e:
        die(f"Błąd syntezy: {e}")


if __name__ == "__main__":
    main()
