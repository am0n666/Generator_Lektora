@echo off
cls
setlocal DisableDelayedExpansion
chcp 65001 >nul 2>nul
title Lektor PL (Supertonic 3 AI) - generator sciezki lektora v3

rem ============================================================
rem generuj_lektora_v3.cmd -i "wejscie.mkv|mp4|..." -o "wyjscie.mkv" [-s "napisy.srt"] [-v M1..M5|F1..F5] [--steps 5..16] [--max-speed 1.0..1.6]
rem     [--base-speed 0.85..1.3] [--no-duck] [--duck-threshold 0.01..0.2]
rem
rem Automatycznie pobiera i konfiguruje (portable, w katalogu .\tools):
rem - FFmpeg (ekstrakcja napisow, miks audio ducking, mux MKV)
rem - Python embeddable + odblokowanie site-packages + pip
rem - Supertonic 3 (onnxruntime, soundfile, supertonic, numpy)
rem
rem NOWOSCI w v2:
rem - Sklejanie fragmentow SRT nalezacych do jednego zdania przed synteza
rem   (lepsza intonacja pytan/wykrzyknien, poprawne przecinki/kropki)
rem - Wzmacnianie interpunkcji koncowej (podwojne znaki) dla wyraznej prozodii
rem - Natywna kontrola tempa syntezy (--base-speed, mniej agresywny atempo)
rem - Automatyczna konwersja niekompatybilnych napisow (np. mov_text z MP4)
rem   przy mux-owaniu do MKV, wiec pliki inne niz MKV (np. MP4) dzialaja bez
rem   bledow kodeka napisow
rem - Ostrzezenia o pominietych kwestiach lektora zamiast cichego ucinania
rem
rem NOWOSCI w v3:
rem - Szacowany czas pozostaly (ETA) wyswietlany po prawej stronie kazdego
rem   paska postepu
rem
rem NOWOSCI w v3.1 (poprawka techniczna):
rem - Naprawiono zakonczenia linii (CRLF) - poprzednia wersja mogla powodowac
rem   blad "The system cannot find the batch label specified" w cmd.exe
rem
rem Wynik: plik MKV z DODATKOWA sciezka audio "Polski" (ustawiona jako domyslna)
rem (oryginalne audio filmu jest automatycznie przyciszane w tle)
rem ============================================================

set "SD=%~dp0"
set "SELF=%~f0"
set "TOOLS=%SD%tools"
set "INPUT="
set "OUTPUT="
set "SRT="
set "VOICE=M3"
set "STEPS=12"
set "MAX_SPEED=1.20"
set "BASE_SPEED=1.00"
set "DUCK_THRESHOLD=0.05"
set "NO_DUCK="

:parseargs
if "%~1"=="" goto argsdone
if /i "%~1"=="-i" goto arg_i
if /i "%~1"=="-o" goto arg_o
if /i "%~1"=="-s" goto arg_s
if /i "%~1"=="-v" goto arg_v
if /i "%~1"=="--voice" goto arg_v
if /i "%~1"=="--steps" goto arg_steps
if /i "%~1"=="--max-speed" goto arg_speed
if /i "%~1"=="--speed" goto arg_speed
if /i "%~1"=="--base-speed" goto arg_basespeed
if /i "%~1"=="--duck-threshold" goto arg_duckth
if /i "%~1"=="--no-duck" goto arg_no_duck
if /i "%~1"=="-h" goto usage
if /i "%~1"=="--help" goto usage
if /i "%~1"=="\/?" goto usage
echo Nieznany parametr: %1
goto usage

:arg_i
set "INPUT=%~2"
shift
shift
goto parseargs

:arg_o
set "OUTPUT=%~2"
shift
shift
goto parseargs

:arg_s
set "SRT=%~2"
shift
shift
goto parseargs

:arg_v
set "VOICE=%~2"
shift
shift
goto parseargs

:arg_steps
set "STEPS=%~2"
shift
shift
goto parseargs

:arg_speed
set "MAX_SPEED=%~2"
shift
shift
goto parseargs

:arg_basespeed
set "BASE_SPEED=%~2"
shift
shift
goto parseargs

:arg_duckth
set "DUCK_THRESHOLD=%~2"
shift
shift
goto parseargs

:arg_no_duck
set "NO_DUCK=--no-duck"
shift
goto parseargs

:argsdone

if not defined INPUT goto usage
if not defined OUTPUT goto usage
if exist "%INPUT%" goto input_ok
echo BLAD: plik wejsciowy nie istnieje: "%INPUT%"
exit /b 1
:input_ok

where curl.exe >nul 2>nul
if not errorlevel 1 goto curl_ok
echo BLAD: brak curl.exe (wymagany Windows 10 1803+ lub nowszy).
exit /b 1
:curl_ok

if not exist "%TOOLS%" mkdir "%TOOLS%"

rem ---------- FFmpeg ----------
if exist "%TOOLS%\ffmpeg\bin\ffmpeg.exe" goto have_ffmpeg
echo [instalacja 1/3] Pobieranie FFmpeg z GitHub (ok. 170 MB)...
curl.exe -L --retry 3 -o "%TOOLS%\ffmpeg.zip" "https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip"
if errorlevel 1 curl.exe -L --retry 3 -o "%TOOLS%\ffmpeg.zip" "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
if errorlevel 1 goto dlfail
echo [instalacja 1/3] Rozpakowywanie FFmpeg...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%TOOLS%\ffmpeg.zip' -DestinationPath '%TOOLS%\ffmpeg_tmp' -Force"
if errorlevel 1 goto dlfail
for /d %%D in ("%TOOLS%\ffmpeg_tmp\ffmpeg-*") do move "%%D" "%TOOLS%\ffmpeg" >nul
del "%TOOLS%\ffmpeg.zip" >nul 2>nul
rd /s /q "%TOOLS%\ffmpeg_tmp" >nul 2>nul
if not exist "%TOOLS%\ffmpeg\bin\ffmpeg.exe" goto dlfail
:have_ffmpeg
set "FFMPEG=%TOOLS%\ffmpeg\bin\ffmpeg.exe"
set "FFPROBE=%TOOLS%\ffmpeg\bin\ffprobe.exe"

rem ---------- Python embeddable (portable) ----------
if exist "%TOOLS%\python\python.exe" goto check_python_pth
echo [instalacja 2/3] Pobieranie Python embeddable (ok. 11 MB)...
curl.exe -L --retry 3 -o "%TOOLS%\python.zip" "https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip"
if errorlevel 1 goto dlfail
echo [instalacja 2/3] Rozpakowywanie Pythona...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%TOOLS%\python.zip' -DestinationPath '%TOOLS%\python' -Force"
if errorlevel 1 goto dlfail
del "%TOOLS%\python.zip" >nul 2>nul
if not exist "%TOOLS%\python\python.exe" goto dlfail

:check_python_pth
powershell -NoProfile -ExecutionPolicy Bypass -Command "$pthFiles = Get-ChildItem -Path '%TOOLS%\python\*._pth'; foreach ($f in $pthFiles) { $c = [IO.File]::ReadAllText($f.FullName); if ($c -notmatch '(?m)^\s*import site\s*$') { $c = $c -replace '(?m)^\s*#\s*import site', 'import site'; if ($c -notmatch 'import site') { $c += \"`r`nimport site`r`n\" }; }; if ($c -notmatch '(?m)^\s*Lib[\\/]site-packages\s*$') { $c += \"`r`nLib\site-packages`r`n\" }; [IO.File]::WriteAllText($f.FullName, $c) }"

if not exist "%TOOLS%\python\Lib\site-packages" mkdir "%TOOLS%\python\Lib\site-packages"

"%TOOLS%\python\python.exe" -m pip --version >nul 2>nul
if not errorlevel 1 goto have_python

echo [instalacja 2/3] Instalacja i konfiguracja pakietu pip w portable Python...
curl.exe -sSL --retry 3 -o "%TOOLS%\python\get-pip.py" "https://bootstrap.pypa.io/get-pip.py"
if errorlevel 1 goto dlfail
"%TOOLS%\python\python.exe" "%TOOLS%\python\get-pip.py" --no-warn-script-location
del "%TOOLS%\python\get-pip.py" >nul 2>nul

"%TOOLS%\python\python.exe" -m pip --version >nul 2>nul
if errorlevel 1 (
echo BLAD: Nie udalo sie zainstalowac narzedzia pip w srodowisku portable Python.
goto dlfail
)

:have_python

rem ---------- Supertonic 3 i zaleznosci ----------
"%TOOLS%\python\python.exe" -c "import supertonic, soundfile, onnxruntime, numpy" >nul 2>nul
if not errorlevel 1 goto have_supertonic
echo [instalacja 3/3] Instalacja pakietu Supertonic oraz bibliotek AI (onnxruntime, soundfile, numpy)...
"%TOOLS%\python\python.exe" -m pip install --no-warn-script-location supertonic soundfile onnxruntime numpy
if errorlevel 1 (
echo BLAD: Nie udalo sie zainstalowac biblioteki Supertonic.
goto dlfail
)

:have_supertonic

rem ---------- Wyodrebnienie wbudowanego skryptu Python ----------
set "LEKTOR_SELF=%SELF%"
set "LEKTOR_PY=%TOOLS%\lektor.py"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$c=[IO.File]::ReadAllText($env:LEKTOR_SELF);$m='#PY'+'BEGIN';$i=$c.IndexOf($m);if($i -lt 0){exit 1};[IO.File]::WriteAllText($env:LEKTOR_PY,$c.Substring($i+$m.Length),(New-Object System.Text.UTF8Encoding($false)))"
if errorlevel 1 goto extractfail
if not exist "%TOOLS%\lektor.py" goto extractfail

set "PYTHONIOENCODING=utf-8"
set "PYTHONWARNINGS=ignore::DeprecationWarning"

echo.
echo ============================================================
echo Generowanie lektora PL (Supertonic 3)
echo Glos: %VOICE% ^| Jakosc (steps): %STEPS% ^| Baz. tempo: %BASE_SPEED%x ^| Maks. tempo: %MAX_SPEED%x
echo Plik wejsciowy: "%INPUT%"
echo ============================================================

set ARGS_EXTRA=
if defined SRT set ARGS_EXTRA=--srt "%SRT%"

"%TOOLS%\python\python.exe" -W ignore::DeprecationWarning "%TOOLS%\lektor.py" --input "%INPUT%" --output "%OUTPUT%" --ffmpeg "%FFMPEG%" --ffprobe "%FFPROBE%" --voice "%VOICE%" --steps "%STEPS%" --max-speed "%MAX_SPEED%" --base-speed "%BASE_SPEED%" --duck-threshold "%DUCK_THRESHOLD%" %NO_DUCK% %ARGS_EXTRA%
set "RET=%errorlevel%"
if %RET% equ 0 (
echo.
echo Sukces: utworzono film ze sciezka lektora Supertonic 3!
)
exit /b %RET%

:usage
echo.
echo Uzycie:
echo %~nx0 -i "wejscie.mkv|mp4|..." -o "wyjscie.mkv" [-s "napisy.srt"] [-v GLOS] [--steps N] [--max-speed X.XX] [--base-speed X.XX] [--no-duck] [--duck-threshold X.XX]
echo.
echo Parametry:
echo -i "plik"           Plik wejsciowy wideo (MKV, MP4 i inne kontenery obslugiwane przez ffmpeg)
echo -o "plik"           Sciezka do pliku wyjsciowego MKV (wyjscie jest zawsze kontenerem MKV,
echo                     napisy niekompatybilne z MKV np. mov_text z MP4 sa automatycznie
echo                     konwertowane, wiec plik wejsciowy inny niz MKV nie powoduje bledow kodeka)
echo -s "plik"           (Opcjonalnie) Zewnetrzny plik napisow SRT
echo -v "glos"           Glos Supertonic: M1..M5 (meskie), F1..F5 (zenskie). Domyslnie: M3
echo --steps N           Liczba krokow denoisera: 5 (szybko), 8 (standard), 12-16 (maks jakosc). Domyslnie: 12
echo --base-speed X.XX   Bazowe tempo syntezy TTS (natywne, bez utraty jakosci). Domyslnie: 1.00
echo --max-speed X.XX    Maksymalne tempo dla dlugich kwestii. Domyslnie: 1.20
echo --no-duck           Nie przyciszaj oryginalnego audio podczas mowy lektora
echo --duck-threshold X  Prog duckingu - nizszy = mocniejsze przyciszanie. Domyslnie: 0.05
echo.
echo Dostepne glosy:
echo Meskie: M1 (glowny filmowy), M2 (wyrazisty), M3, M4, M5
echo Zenskie: F1 (glowna lektorka), F2, F3, F4, F5
echo.
exit /b 1

:dlfail
echo.
echo BLAD: Pobieranie lub instalacja komponentow nie powiodla sie.
echo Sprawdz polaczenie z internetem i sprobuj ponownie.
exit /b 1

:extractfail
echo.
echo BLAD: Nie udalo sie wyodrebnic wbudowanego skryptu Python (tools\lektor.py).
echo Upewnij sie, ze plik %~nx0 nie zostal obciety.
exit /b 1

rem ============================================================
rem Ponizej znajduje sie wbudowany skrypt Python (nie usuwac!)
rem Jest on automatycznie wyodrebniany do tools\lektor.py
rem ============================================================
exit /b
#PYBEGIN
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


GAP = 0.12
NATIVE_SPEED_CAP = 1.6
RESYNTH_TRIGGER = 1.03


def clip_dur(path):
    info = sf.info(path)
    return info.frames / float(info.samplerate)


def synthesize_and_fit(entries, tts, style, total_steps, base_speed, max_speed, ffmpeg, tmp):
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

        actual = max(start, cur_end + GAP)
        if i + 1 < n:
            slot = entries[i + 1][0] - actual - GAP
        else:
            slot = dur
        slot = max(slot, 0.8)

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
                                           args.ffmpeg, tmp)
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
