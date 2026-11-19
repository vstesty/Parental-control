# Parental-control

Program dla Windows 11 składa się z usługi Windows i niewielkiego programu
działającego w sesji użytkownika. Usługa mierzy dzienny czas aktywnej sesji
konsoli i wylogowuje ją po wykorzystaniu limitu. Program użytkownika obsługuje
skrót `Ctrl+H`; wpisanie hasła pozwala dodać 15, 30 lub 60 minut albo wyłączyć
limit do końca dnia.

## Konfiguracja

Plik `C:\DD\config.txt` ma zawierać hasło oraz wpis dla każdego dnia tygodnia.
Limity są dziennymi pulami czasu w formacie `HH:mm`; wartość `off` wyłącza
ograniczenie danego dnia. Przykład znajduje się w [config.example.txt](./config.example.txt).
Po zmianie pliku usługa wczytuje konfigurację automatycznie.

```text
Password=ustaw-tutaj-haslo
Monday=02:00
Tuesday=02:00
Wednesday=02:00
Thursday=02:00
Friday=02:00
Saturday=03:00
Sunday=off
```

Hasło jest zapisane w pliku jako zwykły tekst. Skrypt instalacyjny ogranicza
dostęp do `C:\DD` do kont SYSTEM i Administratorzy. Zmiana konfiguracji wymaga
uprawnień administratora. Nie używaj hasła wykorzystywanego do logowania do
Windows.

## Budowanie i instalacja

Wymagany jest .NET 10 SDK. W katalogu repozytorium uruchom PowerShell jako
administrator:

```powershell
dotnet publish .\ParentalControl.Service\ParentalControl.Service.csproj -c Release -r win-x64 --self-contained true
dotnet publish .\ParentalControl.Agent\ParentalControl.Agent.csproj -c Release -r win-x64 --self-contained true
.\install.ps1
```

Jeśli komputer jest ARM64, opublikuj aplikacje dla `win-arm64` i odpowiednio
zmień ścieżki publikacji w `install.ps1`. Skrypt tworzy usługę automatycznie
uruchamianą, rejestruje program skrótu dla użytkowników komputera i nie
nadpisuje istniejącego pliku konfiguracji. Po instalacji ustaw hasło i limity
w `C:\DD\config.txt`, a następnie wyloguj się i zaloguj ponownie.

## Uwagi

- Czas jest liczony, gdy aktywna jest lokalna sesja konsoli; blokada ekranu
  również jest liczona. Sesje pulpitu zdalnego nie są monitorowane.
- Dzienny licznik zeruje się o północy czasu lokalnego, a wykorzystany czas
  jest zapisywany w `C:\ProgramData\ParentalControl\state.json`.
- Wybór „Wyłącz” pomija limit do północy. Zwiększenia limitu obowiązują tylko
  w danym dniu.
