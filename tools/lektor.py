
# -*- coding: utf-8 -*-
# lektor.py - generator sciezki lektora PL (Supertonic 3) miksowany do kontenera MKV (v3)
import argparse, json, os, re, shutil, subprocess, sys, tempfile, time, warnings
warnings.filterwarnings("ignore", category=DeprecationWarning)

import numpy as np
import soundfile as sf

BAR_W = 34
_bar_state = {}  # prefix -> [start_time, start_frac] - do liczenia ETA per-operacja


def format_eta(seconds):
    if seconds is None or seconds != seconds or seconds < 0:
        return "--:--"
    seconds = int(round(seconds))
    if seconds >= 3600:
        h, rem = divmod(seconds, 3600)
        m, s = divmod(rem, 60)
        return "%d:%02d:%02d" % (h, m, s)
    m, s = divmod(seconds, 60)
    return "%02d:%02d" % (m, s)


def bar(frac, prefix):
    frac = max(0.0, min(1.0, frac))
    now = time.time()
    st = _bar_state.get(prefix)
    if st is None or frac < st[1] - 1e-9:
        st = [now, frac]
        _bar_state[prefix] = st
    start_t, start_frac = st
    elapsed = now - start_t
    if frac - start_frac > 1e-6:
        rate = elapsed / (frac - start_frac)
        remaining = rate * (1.0 - frac)
        eta_str = format_eta(remaining) if frac < 1.0 else "00:00"
    else:
        eta_str = "--:--" if frac < 1.0 else "00:00"
    n = int(frac * BAR_W)
    sys.stdout.write("\r%-22s [%s%s] %3d%%  ETA %s" %
                      (prefix, "#" * n, "." * (BAR_W - n), int(frac * 100), eta_str))
    sys.stdout.flush()


def endbar():
    sys.stdout.write("\n")
    sys.stdout.flush()


def die(msg):
    sys.stdout.write("\nBLAD: %s\n" % msg)
    sys.exit(1)


def run(cmd, **kw):
    return subprocess.run(cmd, capture_output=True, text=True,
                           encoding="utf-8", errors="replace", **kw)


def probe(ffprobe, path):
    r = run([ffprobe, "-v", "error", "-print_format", "json",
             "-show_streams", "-show_format", path])
    if r.returncode != 0:
        die("ffprobe nie mogl odczytac pliku: " + r.stderr.strip())
    return json.loads(r.stdout)


TEXT_SUB_CODECS = {"subrip", "srt", "ass", "ssa", "webvtt", "mov_text", "text"}
NEEDS_SRT_CONVERSION = {"mov_text", "webvtt"}


def pick_subtitle(info):
    subs = [s for s in info.get("streams", []) if s.get("codec_type") == "subtitle"]
    if not subs:
        die("plik wejsciowy nie zawiera zadnych napisow (uzyj opcji -s z plikiem SRT)")
    text_subs = [s for s in subs if s.get("codec_name", "") in TEXT_SUB_CODECS]
    if not text_subs:
        die("napisy sa obrazkowe (PGS/VobSub) - wymagaja OCR; uzyj opcji -s z plikiem SRT")

    def score(s):
        tags = s.get("tags") or {}
        disp = s.get("disposition") or {}
        lang = (tags.get("language") or "").lower()
        title = (tags.get("title") or "").lower()
        sc = 0
        if lang in ("pol", "pl"):
            sc += 100
        if "pol" in title or "pl " in title or title == "pl":
            sc += 40
        if disp.get("forced") or "forced" in title or "napisy wymuszone" in title:
            sc -= 60
        if disp.get("hearing_impaired") or "sdh" in title:
            sc -= 10
        if disp.get("default"):
            sc += 5
        return sc

    best = max(text_subs, key=score)
    tags = best.get("tags") or {}
    lang = (tags.get("language") or "?").lower()
    print("Wybrane napisy: strumien #%s, jezyk=%s, tytul=%s" %
          (best.get("index"), lang, tags.get("title") or "-"))
    if lang not in ("pol", "pl"):
        print("UWAGA: napisy nie sa oznaczone jako polskie - lektor przeczyta je doslownie!")
    return best


TIME_RE = re.compile(r"(\d+):(\d\d):(\d\d)[,.](\d{1,3})\s*-->\s*(\d+):(\d\d):(\d\d)[,.](\d{1,3})")
SENT_END_RE = re.compile(r'[.!?.]"?\)?\s*$')


def ends_sentence(t):
    return bool(SENT_END_RE.search(t.strip()))


def clean_text(t):
    t = re.sub(r"<[^>]+>", "", t)
    t = re.sub(r"\{[^}]*\}", "", t)
    t = t.replace("\\N", "\n").replace("\\n", "\n").replace("\\h", " ")
    dash_count = 0
    lines = []
    for l in t.splitlines():
        stripped = l.strip()
        if re.match(r"^\s*[-\u2013\u2014]+\s*", stripped):
            dash_count += 1
        stripped = re.sub(r"^\s*[-\u2013\u2014]+\s*", "", stripped)
        if stripped:
            lines.append(stripped)
    if dash_count >= 2 and len(lines) >= 2:
        parts = []
        for l in lines:
            if not ends_sentence(l):
                l = l + "."
            parts.append(l)
        t = " ".join(parts)
    else:
        t = " ".join(lines)
    t = re.sub(r"[\u266a\u266b\u266c]+", "", t)
    t = re.sub(r"\s+", " ", t).strip()
    if not re.search(r"\w", t, re.UNICODE):
        return ""
    return t


def enhance_prosody(t):
    t = t.strip()
    if not t:
        return t
    m = re.search(r'([.!?]+)("|\')?$', t)
    if m:
        punct = m.group(1)
        quote = m.group(2) or ""
        if "?" in punct and punct.count("?") < 2:
            t = t[: -len(m.group(0))] + punct + "?" + quote
        elif "!" in punct and punct.count("!") < 2:
            t = t[: -len(m.group(0))] + punct + "!" + quote
    else:
        t += "."
    return t


def parse_srt(path):
    with open(path, "r", encoding="utf-8-sig", errors="replace") as f:
        content = f.read()
    entries = []
    for block in re.split(r"\r?\n\s*\r?\n", content):
        lines = block.strip().splitlines()
        if not lines:
            continue
        ti = None
        for i, l in enumerate(lines):
            if TIME_RE.search(l):
                ti = i
                break
        if ti is None:
            continue
        m = TIME_RE.search(lines[ti])
        g = [int(x) for x in m.groups()]
        start = g[0] * 3600 + g[1] * 60 + g[2] + g[3] / 1000.0
        end = g[4] * 3600 + g[5] * 60 + g[6] + g[7] / 1000.0
        text = clean_text("\n".join(lines[ti + 1:]))
        if text and end > start:
            entries.append([start, end, text])
    entries.sort(key=lambda e: e[0])
    return entries


def merge_fragments(entries, max_gap=0.5, max_chars=220, max_total_dur=9.0):
    if not entries:
        return entries
    merged = []
    cur_start, cur_end, cur_text = entries[0]
    for start, end, text in entries[1:]:
        gap = start - cur_end
        combined_len = len(cur_text) + 1 + len(text)
        combined_dur = end - cur_start
        if (not ends_sentence(cur_text)) and 0 <= gap <= max_gap \
                and combined_len <= max_chars and combined_dur <= max_total_dur:
            cur_text = cur_text + " " + text
            cur_end = end
        else:
            merged.append([cur_start, cur_end, enhance_prosody(cur_text)])
            cur_start, cur_end, cur_text = start, end, text
    merged.append([cur_start, cur_end, enhance_prosody(cur_text)])
    return merged


def load_tts():
    print("Inicjalizacja modelu Supertonic 3 (ladowanie silnika)...")
    try:
        from supertonic import TTS
    except ImportError as e:
        die("Brak zainstalowanego pakietu supertonic: %s" % e)
    return TTS(auto_download=True)


def get_style(tts, voice_name):
    voice_name = voice_name.upper()
    try:
        return tts.get_voice_style(voice_name=voice_name)
    except Exception:
        print("Nie znaleziono stylu '%s', uzywam domyslnego stylu M1..." % voice_name)
        return tts.get_voice_style(voice_name="M1")


def synth_one(tts, style, text, total_steps, speed, out_wav, rate):
    try:
        wav, _dur = tts.synthesize(
            text=text,
            lang="pl",
            voice_style=style,
            total_steps=total_steps,
            speed=speed,
        )
    except TypeError:
        wav, _dur = tts.synthesize(
            text=text,
            lang="pl",
            voice_style=style,
            total_steps=total_steps,
        )
    if hasattr(wav, "squeeze"):
        wav_arr = wav.squeeze()
    else:
        wav_arr = np.array(wav).flatten()
    sf.write(out_wav, wav_arr, rate, subtype="PCM_16")


DEFAULT_MIN_PAUSE = 0.20
PAUSE_SENTENCE = 0.25
PAUSE_QUESTION = 0.30
PAUSE_EXCLAMATION = 0.30
PAUSE_ELLIPSIS = 0.42
PAUSE_COMMA = 0.14
NATIVE_SPEED_CAP = 1.6
RESYNTH_TRIGGER = 1.03


def pause_after(text, min_pause, smart=True):
    """Dobiera naturalną pauzę po wypowiedzi bez naruszania synchronizacji."""
    min_pause = max(0.08, min(0.50, float(min_pause)))
    if not smart:
        return min_pause
    t = text.rstrip()
    if not t:
        return min_pause
    if t.endswith(("...", "…")):
        return max(min_pause, PAUSE_ELLIPSIS)
    if t.endswith(("?", "¿")):
        return max(min_pause, PAUSE_QUESTION)
    if t.endswith("!"):
        return max(min_pause, PAUSE_EXCLAMATION)
    if t.endswith((",", ";", ":")):
        return max(min_pause, PAUSE_COMMA)
    return max(min_pause, PAUSE_SENTENCE)


def clip_dur(path):
    info = sf.info(path)
    return info.frames / float(info.samplerate)


def atempo_filter_chain(factor):
    """Buduje poprawny filtr atempo także dla bardzo dużych współczynników."""
    factor = max(0.01, float(factor))
    parts = []
    # FFmpeg atempo bezpiecznie przyjmuje pojedynczy współczynnik 0.5–2.0.
    while factor > 2.0:
        parts.append("atempo=2.0000")
        factor /= 2.0
    while factor < 0.5:
        parts.append("atempo=0.5000")
        factor /= 0.5
    parts.append("atempo=%.4f" % factor)
    return ",".join(parts)


def synthesize_and_fit(entries, tts, style, total_steps, base_speed, max_speed, ffmpeg, tmp, min_pause=DEFAULT_MIN_PAUSE, smart_pauses=True):
    clips = os.path.join(tmp, "clips")
    os.makedirs(clips, exist_ok=True)
    rate = 44100
    n = len(entries)
    fitted = []
    cur_end = 0.0
    skipped = 0

    for i, (start, end, text) in enumerate(entries):
        src = os.path.join(clips, "%06d.wav" % i)
        try:
            synth_one(tts, style, text, total_steps, base_speed, src, rate)
        except Exception as ex:
            sys.stderr.write("\nOstrzezenie przy linii %d: %s\n" % (i, ex))
            skipped += 1
            bar((i + 1) / float(max(1, n)), "Synteza + dopasowanie")
            continue

        try:
            dur = clip_dur(src)
        except Exception:
            bar((i + 1) / float(max(1, n)), "Synteza + dopasowanie")
            continue

        actual = max(start, cur_end + min_pause)

        # Naturalna pauza po bieżącej kwestii. Jeśli kolejna kwestia jest blisko,
        # pauza zostaje automatycznie skrócona, aby nie powodować nakładania audio.
        desired_pause = pause_after(text, min_pause, smart_pauses)
        if i + 1 < n:
            next_start = entries[i + 1][0]
            available = next_start - actual
            pause = desired_pause
            if available < pause + 0.05:
                pause = max(0.05, min(min_pause, available - 0.05))
            slot = max(0.05, next_start - actual - pause)
        else:
            slot = dur

        use = src
        if dur > slot + 0.02:
            needed_tempo = dur / slot
            if needed_tempo <= NATIVE_SPEED_CAP and needed_tempo >= RESYNTH_TRIGGER:
                native_speed = min(needed_tempo, max_speed, NATIVE_SPEED_CAP) * base_speed
                resynth = os.path.join(clips, "%06d_r.wav" % i)
                try:
                    synth_one(tts, style, text, total_steps, native_speed, resynth, rate)
                    use = resynth
                    dur = clip_dur(resynth)
                except Exception:
                    use = src
            if dur > slot + 0.02:
                tempo = min(dur / slot, max_speed)
                if tempo > 1.01:
                    dst = os.path.join(clips, "%06d_f.wav" % i)
                    r = run([ffmpeg, "-y", "-v", "error", "-i", use,
                             "-filter:a", "atempo=%.4f" % tempo,
                             "-ar", str(rate), "-ac", "1", dst])
                    if r.returncode == 0 and os.path.isfile(dst):
                        use = dst
                        try:
                            dur = clip_dur(dst)
                        except Exception:
                            dur = dur / tempo

            # Ostatnia deska ratunku: gdy dialog jest ekstremalnie długi względem
            # dostępnego miejsca, dopasuj go dokładnie do slotu. To zapobiega
            # nachodzeniu jednej kwestii na początek następnej.
            if i + 1 < n and dur > slot + 0.02 and slot > 0.05:
                emergency_tempo = dur / slot
                if emergency_tempo > 1.01:
                    dst = os.path.join(clips, "%06d_e.wav" % i)
                    r = run([ffmpeg, "-y", "-v", "error", "-i", use,
                             "-filter:a", atempo_filter_chain(emergency_tempo),
                             "-ar", str(rate), "-ac", "1", dst])
                    if r.returncode == 0 and os.path.isfile(dst):
                        use = dst
                        try:
                            dur = clip_dur(dst)
                        except Exception:
                            dur = slot

        fitted.append((actual, use))
        cur_end = actual + dur
        bar((i + 1) / float(max(1, n)), "Synteza + dopasowanie")

    endbar()
    if skipped:
        print("UWAGA: pominieto %d kwestii lektora z powodu bledow syntezy." % skipped)
    return fitted, rate


def assemble(fitted, rate, total_dur, out_wav):
    total_samples = int((total_dur + 2.0) * rate)
    buf = np.zeros(total_samples, dtype=np.float32)
    n = max(1, len(fitted))
    dropped = 0

    for i, (start, path) in enumerate(fitted):
        try:
            data, sr = sf.read(path, dtype="float32")
            if data.ndim > 1:
                data = data.mean(axis=1)
        except Exception:
            dropped += 1
            continue

        start_idx = int(start * rate)
        if start_idx >= len(buf):
            dropped += 1
            continue
        end_idx = min(start_idx + len(data), len(buf))
        chunk_len = end_idx - start_idx
        buf[start_idx:end_idx] += data[:chunk_len]
        bar((i + 1) / n, "Montaz sciezki")
    endbar()

    if dropped:
        print("UWAGA: %d kwestii lektora wypadlo poza koniec pliku i zostalo pominietych." % dropped)

    max_val = np.max(np.abs(buf))
    if max_val > 1.0:
        buf = buf / max_val

    sf.write(out_wav, buf, rate, subtype="PCM_16")


def build_subtitle_codec_args(info):
    subs = [s for s in info.get("streams", []) if s.get("codec_type") == "subtitle"]
    args = []
    for i, s in enumerate(subs):
        codec = s.get("codec_name", "")
        if codec in NEEDS_SRT_CONVERSION:
            args += ["-c:s:%d" % i, "srt"]
        else:
            args += ["-c:s:%d" % i, "copy"]
    return args


def mux(ffmpeg, inp, wavp, outp, n_audio, total_dur, voice_tag, info, no_duck=False, duck_threshold=0.05):
    title_meta = "Polski"
    fc = ("[0:a:0]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[org];"
          "[1:a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,"
          "volume=1.45,asplit=2[lk1][lk2];"
          "[org][lk1]sidechaincompress=threshold=%.3f:ratio=10:attack=20:release=450[duck];"
          "[duck][lk2]amix=inputs=2:duration=first:dropout_transition=0:normalize=0,"
          "alimiter=limit=0.95[mix]") % duck_threshold
    if no_duck:
        fc = ("[0:a:0]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[org];"
              "[1:a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,volume=1.45[lk];"
              "[org][lk]amix=inputs=2:duration=first:dropout_transition=0:normalize=0,"
              "alimiter=limit=0.95[mix]")
    N = n_audio
    cmd = [ffmpeg, "-y", "-v", "error", "-i", inp, "-i", wavp,
           "-filter_complex", fc,
           "-map", "0", "-map", "[mix]",
           "-c", "copy"]
    cmd += build_subtitle_codec_args(info)
    cmd += ["-c:a:%d" % N, "aac", "-b:a:%d" % N, "256k",
            "-metadata:s:a:%d" % N, "language=pol",
            "-metadata:s:a:%d" % N, "title=%s" % title_meta]
    # Tylko nowa sciezka lektora ma byc domyslna - zdejmij default z oryginalnych audio.
    for i in range(N):
        cmd += ["-disposition:a:%d" % i, "0"]
    cmd += ["-disposition:a:%d" % N, "default",
            "-progress", "pipe:1", "-nostats",
            outp]
    proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                             text=True, encoding="utf-8", errors="replace")
    for line in proc.stdout:
        line = line.strip()
        if line.startswith("out_time_us=") or line.startswith("out_time_ms="):
            try:
                t = int(line.split("=", 1)[1]) / 1000000.0
            except ValueError:
                continue
            if total_dur > 0:
                bar(t / total_dur, "Tworzenie MKV")
    proc.wait()
    err = proc.stderr.read()
    if proc.returncode != 0:
        die("ffmpeg (tworzenie MKV): " + err.strip()[-2000:])
    bar(1.0, "Tworzenie MKV")
    endbar()


def verify_lektor_track(ffprobe, path, expected_title="Polski"):
    info = probe(ffprobe, path)
    audios = [s for s in info.get("streams", []) if s.get("codec_type") == "audio"]
    if not audios:
        print("UWAGA: w pliku wyjsciowym nie znaleziono zadnej sciezki audio.")
        return False
    print("Sciezki audio w pliku wyjsciowym:")
    found = None
    defaults = []
    for i, s in enumerate(audios):
        tags = s.get("tags") or {}
        disp = s.get("disposition") or {}
        title = tags.get("title") or "-"
        lang = (tags.get("language") or "?").lower()
        try:
            is_def = bool(int(disp.get("default") or 0))
        except (TypeError, ValueError):
            is_def = bool(disp.get("default"))
        mark = " [DEFAULT]" if is_def else ""
        print("  a:%d  lang=%s  title=%s%s" % (i, lang, title, mark))
        if title == expected_title:
            found = (i, is_def)
        if is_def:
            defaults.append((i, title))
    if found is None:
        print("UWAGA: nie znaleziono sciezki audio o nazwie '%s'." % expected_title)
        return False
    idx, is_def = found
    if is_def and len(defaults) == 1:
        print("OK: sciezka '%s' (a:%d) jest ustawiona jako jedyna domyslna." % (expected_title, idx))
        return True
    if is_def:
        others = ", ".join("a:%d '%s'" % (i, t) for i, t in defaults if not (i == idx and t == expected_title))
        print("UWAGA: sciezka '%s' ma flage default, ale default maja tez inne sciezki: %s" % (expected_title, others))
        return False
    print("UWAGA: sciezka '%s' (a:%d) NIE jest ustawiona jako domyslna." % (expected_title, idx))
    return False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", required=True)
    ap.add_argument("--output", required=True)
    ap.add_argument("--ffmpeg", required=True)
    ap.add_argument("--ffprobe", required=True)
    ap.add_argument("--voice", default="M1")
    ap.add_argument("--steps", type=int, default=12)
    ap.add_argument("--max-speed", type=float, default=1.20)
    ap.add_argument("--base-speed", type=float, default=1.00)
    ap.add_argument("--duck-threshold", type=float, default=0.05)
    ap.add_argument("--min-pause", type=float, default=DEFAULT_MIN_PAUSE)
    ap.add_argument("--smart-pauses", action="store_true", default=True)
    ap.add_argument("--no-smart-pauses", dest="smart_pauses", action="store_false")
    ap.add_argument("--srt")
    ap.add_argument("--keep-temp", action="store_true")
    ap.add_argument("--no-duck", action="store_true", help="Nie przyciszaj oryginalnej sciezki podczas lektora")
    args = ap.parse_args()

    info = probe(args.ffprobe, args.input)
    try:
        total_dur = float(info["format"]["duration"])
    except (KeyError, ValueError):
        die("nie mozna odczytac czasu trwania pliku")
    n_audio = len([s for s in info.get("streams", []) if s.get("codec_type") == "audio"])
    if n_audio == 0:
        die("plik nie zawiera zadnej sciezki audio")

    container = os.path.splitext(args.input)[1].lower()
    if container != ".mkv":
        print("Info: plik wejsciowy to '%s' - wyjscie bedzie kontenerem MKV, "
              "a niekompatybilne napisy (np. mov_text) zostana automatycznie "
              "przekonwertowane do SRT." % container)

    tmp = tempfile.mkdtemp(prefix="lektor_st3_")
    try:
        if args.srt:
            srt = args.srt
            print("Uzywam zewnetrznych napisow: " + srt)
        else:
            s = pick_subtitle(info)
            srt = os.path.join(tmp, "subs.srt")
            r = run([args.ffmpeg, "-y", "-v", "error", "-i", args.input,
                     "-map", "0:%d" % s["index"], "-c:s", "srt", srt])
            if r.returncode != 0 or not os.path.isfile(srt):
                die("nie udalo sie wyeksportowac napisow: " + r.stderr.strip()[-500:])

        raw_entries = parse_srt(srt)
        if not raw_entries:
            die("nie znaleziono zadnych linii dialogowych w napisach")
        entries = merge_fragments(raw_entries)
        print("Linii dialogowych: %d (po sklejeniu fragmentow zdan: %d) | dlugosc filmu: %d min" %
              (len(raw_entries), len(entries), int(total_dur // 60)))

        tts = load_tts()
        style = get_style(tts, args.voice)

        fitted, rate = synthesize_and_fit(entries, tts, style, args.steps,
                                           args.base_speed, args.max_speed,
                                           args.ffmpeg, tmp, args.min_pause, args.smart_pauses)
        if not fitted:
            die("Nie udalo sie dopasowac zadnych probek lektora")

        master = os.path.join(tmp, "lektor.wav")
        assemble(fitted, rate, total_dur, master)
        mux(args.ffmpeg, args.input, master, args.output, n_audio, total_dur,
            args.voice.upper(), info, args.no_duck, args.duck_threshold)
        print("\nGOTOWE! Plik z lektorem PL wygenerowany pomyslnie: %s" % args.output)
        print("(Nowa sciezka: 'Polski', AAC 256k, 48kHz)")
        verify_lektor_track(args.ffprobe, args.output, "Polski")
    finally:
        if not args.keep_temp:
            shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    main()
