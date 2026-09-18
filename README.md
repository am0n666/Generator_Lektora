# Generator Lektora

Profesjonalna aplikacja WPF dla Windows 11 / .NET 9 do generowania polskiego lektora AI na podstawie napisów z filmu.

## Najważniejsze elementy

- WPF + .NET 9 (`net9.0-windows`).
- Silnik pozostaje zgodny z pipeline'em z `generuj_lektora_v5.cmd`.
- Osobne okno ustawień generowania.
- Ustawienia zapisywane lokalnie w `%LOCALAPPDATA%\GeneratorLektora\settings.json`.
- Główne okno ma układ responsywny i `ScrollViewer`, dzięki czemu zawartość nie jest obcinana przy mniejszej wysokości okna.
- Osobne okno instalacji/naprawy komponentów.
- Pobieranie FFmpeg, Python 3.11 i `get-pip.py` pokazuje rzeczywisty postęp, rozmiar i prędkość pobierania.
- Instalacja komponentów pokazuje aktualny komponent i operację; dla operacji bez mierzalnego procentu używany jest pasek indeterminate.
- Anulowanie instalacji i procesów potomnych jest obsługiwane przez `CancellationToken`.

## Uruchomienie

Otwórz `GeneratorLektora.sln` w Visual Studio 2022 z obsługą WPF/.NET 9 i uruchom projekt `GeneratorLektora`.

Przy pierwszym użyciu wybierz **Instalacja / naprawa komponentów**. Program pobierze lokalne, przenośne komponenty do katalogu `tools` obok aplikacji.

> Projekt był przygotowany i zweryfikowany statycznie w środowisku roboczym. Pełna kompilacja WPF wymaga środowiska Windows z zainstalowanym .NET 9 SDK/Visual Studio.


### Test głosu
W oknie Ustawienia dostępny jest przycisk „Testuj głos”. Pozwala wybrać głos M1–M5/F1–F5, wpisać tekst i odsłuchać próbkę wygenerowaną przez zainstalowany Supertonic 3. Test działa asynchronicznie i nie blokuje interfejsu.
