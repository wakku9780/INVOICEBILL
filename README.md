# Saree Billing

Offline GST billing and invoicing for a saree wholesale business — a Windows desktop app
(.NET 8, WPF, SQLite). No internet, cloud or subscription is needed.

- Company profile, customers (GSTIN validation, state codes)
- GST invoices with automatic CGST + SGST (same state) or IGST (other state), round off,
  amount in words (Lakh/Crore), invoice numbering per financial year
- Tally-style A4 invoice: preview, print, save as PDF
- Invoice history with search, reprint and cancel (cancelled numbers are never reused)
- Backup & restore, plus automatic daily backups

## Installing on a shop computer (Windows 10 or 11, 64-bit)

1. Copy `SareeBilling-Setup-<version>.exe` to the computer (pen drive, email or WhatsApp Web).
2. Double-click it. No administrator password is needed.
   - If Windows shows **"Windows protected your PC"**, click **More info → Run anyway**.
     This appears because the installer is not code-signed; it is expected.
3. Finish the setup and open **Saree Billing** from the desktop or Start menu.
4. First time only: open **Company Settings**, enter your details and GSTIN, and set
   **Next invoice number** (e.g. your last Tally invoice number + 1).

### Updating

Run the newer `SareeBilling-Setup-<version>.exe`. Close the app first (setup will ask).
Invoices, customers and settings are kept.

### Where the data is

| What | Folder |
| --- | --- |
| Database | `%LOCALAPPDATA%\SareeBilling\Data\sareebilling.db` |
| Automatic backups (daily, last 30 days; before every update or restore) | `%LOCALAPPDATA%\SareeBilling\AutoBackups` |
| Manual backups (default) | `Documents\SareeBilling Backups` |
| Saved invoice PDFs (default) | `Documents\SareeBilling Invoices` |
| Error log (only if something goes wrong) | `%LOCALAPPDATA%\SareeBilling\Logs` |

Updating, reinstalling or uninstalling the app never touches the data folder.
**Make a backup to a pen drive regularly** (Backup & Restore → Change folder… → Back up now).
To move to a new computer: install the app there, then Backup & Restore → Restore from another file….

## Building from source (developer)

Requirements: .NET 8 SDK (pinned by `global.json`), Inno Setup 6
(`winget install JRSoftware.InnoSetup --scope user`).

```powershell
.\build-installer.ps1          # tests → self-contained publish → installer
# Output: artifacts\installer\SareeBilling-Setup-<version>.exe
```

Raise `<Version>` in `Directory.Build.props` before each release.

Debug builds store their data in `%LOCALAPPDATA%\SareeBilling-Dev`, so development never
touches real business data.

### Solution layout

| Project | Purpose |
| --- | --- |
| `SareeBilling.Domain` | Entities and pure business rules: GST calculation, GSTIN check, amount in words |
| `SareeBilling.Application` | Use cases and validation: invoices, customers, company, backups |
| `SareeBilling.Infrastructure` | SQLite / EF Core, repositories, QuestPDF invoice, backup service |
| `SareeBilling.Desktop` | WPF screens (MVVM) |
| `tests/SareeBilling.Tests` | Unit and SQLite integration tests |

Database changes are made with EF Core migrations
(`dotnet ef migrations add <Name> -p src/SareeBilling.Infrastructure -s src/SareeBilling.Infrastructure -o Persistence/Migrations`);
the app applies them automatically at start-up after taking a safety backup.

PDF generation uses [QuestPDF](https://www.questpdf.com) under its Community licence
(free for businesses with annual revenue under US$1M).
