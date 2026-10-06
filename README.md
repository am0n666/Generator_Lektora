# Generator Lektora

Aplikacja WPF dla Visual Studio 2022 / .NET 9.0, zaprojektowana jako GUI dla skryptu `lektor.cmd`.

## Funkcje
- wybór MKV, zewnętrznego SRT i pliku wyjściowego;
- analiza ścieżek przez FFprobe bez otwierania konsoli;
- wybór silnika Supertonic 3 / Edge TTS, głosu i kodeka;
- ustawienia duckingu, rozwijania liczb i czyszczenia SDH;
- pasek postępu, status i anulowanie;
- przygotowana architektura pod pełny pipeline FFmpeg/MKVToolNix/TTS.

## Uruchomienie
Otwórz `Generator_Lektora.sln` w Visual Studio 2022 z SDK .NET 9.0, przywróć projekt i uruchom. FFmpeg (`ffprobe.exe`) musi być dostępny w PATH dla analizy pliku.

Uwaga: obecna wersja GUI nie uruchamia `cmd.exe`; właściwa synteza wymaga implementacji backendu TTS i miksowania jako natywnych usług C# lub wywołań procesów narzędziowych z przekierowaniem strumieni.
