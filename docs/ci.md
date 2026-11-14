# CI/CD: GitHub Actions

Workflowy są w katalogu `.github/workflows/`. Stan przebiegów: zakładka **Actions** repozytorium.

| Workflow | Plik | Kiedy | Co robi |
|---|---|---|---|
| CI | `ci.yml` | push i pull request do `main`, ręcznie | build rozwiązania (Release, `windows-latest`), testy jednostkowe (`Category!=Integration`), wyniki testów `.trx` jako artefakt |
| Dokumentacja | `docs.yml` | push do `main` ze zmianą w `docs/` lub `GeoBench.Core`, ręcznie | build `GeoBench.Core`, strona DocFX (markdown + referencja API z komentarzy XML), publikacja na GitHub Pages |
| Wydanie | `release.yml` | push tagu `v*` | testy, `dotnet publish` aplikacji WPF (win-x64), paczka ZIP, GitHub Release z notatkami, powiadomienie e-mail |

Testy integracyjne wymagają baz SQL Server i PostgreSQL z danymi, więc w CI nie są uruchamiane; uruchamia się je lokalnie
(patrz [instrukcja dla programisty](developer.md#5-testy)).

## Konfiguracja jednorazowa

### GitHub Pages

**Settings → Pages → Build and deployment → Source: GitHub Actions.** Po pierwszym przebiegu workflowu Dokumentacja
strona jest pod adresem `https://<użytkownik>.github.io/<repozytorium>/`.

### Powiadomienia e-mail (SMTP)

Workflow Wydanie wysyła e-mail akcją [dawidd6/action-send-mail](https://github.com/dawidd6/action-send-mail).
Dane serwera są w sekretach repozytorium: **Settings → Secrets and variables → Actions → New repository secret**.

| Sekret | Przykład (Gmail) | Opis |
|---|---|---|
| `MAIL_SERVER` | `smtp.gmail.com` | serwer SMTP |
| `MAIL_PORT` | `465` | port SSL (domyślnie 465, gdy sekretu nie ma) |
| `MAIL_USERNAME` | `adres@gmail.com` | login i adres nadawcy |
| `MAIL_PASSWORD` | hasło aplikacji | **nie** zwykłe hasło do konta |
| `MAIL_TO` | `ja@example.com, prowadzacy@example.com` | odbiorcy, po przecinku |

Gmail: włącz weryfikację dwuetapową, a potem utwórz hasło aplikacji (Konto Google → Bezpieczeństwo →
Hasła do aplikacji) i wpisz je jako `MAIL_PASSWORD`. Sekrety są maskowane w logach i niedostępne dla pull requestów
z forków.

Gdy `MAIL_SERVER` lub `MAIL_TO` nie są ustawione, krok e-mail jest pomijany, a wydanie kończy się sukcesem.

## Wydanie nowej wersji

```bash
git tag v1.0.0
git push origin v1.0.0
```

Po kilku minutach w zakładce **Releases** pojawi się wydanie `v1.0.0` z plikiem `GeoBench-v1.0.0-win-x64.zip`
i notatkami wygenerowanymi z commitów i pull requestów, a odbiorcy z `MAIL_TO` dostaną e-mail z linkiem.

Paczka wymaga zainstalowanego środowiska [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
Po rozpakowaniu skopiuj `appsettings.example.json` jako `appsettings.json`, uzupełnij connection stringi
i uruchom `GeoBench.Wpf.exe`. Raporty trafiają do katalogu `results` obok programu.

## Znaczki statusu

```markdown
[![CI](https://github.com/olimo54323/GeoBench-Projekt/actions/workflows/ci.yml/badge.svg)](https://github.com/olimo54323/GeoBench-Projekt/actions/workflows/ci.yml)
```
