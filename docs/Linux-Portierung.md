# Zusätzliche native Linux-Ausgaben

**Priorität:** `very low`

**Status:** Zurückgestellte Zukunftsaufgabe, keine Umsetzung beauftragt.

## Ziel und Umfang

Zusätzliche native Linux-Ausgaben für **x64 und ARM64** ohne Wine. Die Windows-Ausgaben für x64 und x86 mit WinUI bleiben erhalten. Vorhandene Funktionen sollen auch unter Linux nutzbar sein; Einschränkungen dürfen nicht stillschweigend durch entfallende Befehle entstehen.

## Einschätzung

Technisch grundsätzlich machbar, aber ein größeres Portierungsprojekt statt einer Erweiterung der Build-Matrix. Teile des C#- und SVN-Codes sind wiederverwendbar. Oberfläche, Desktop-Integration und sicherheitsrelevante Plattformzugriffe benötigen zusätzliche Implementierungen. Eine belastbare Aufwandsschätzung setzt einen begrenzten technischen Prototyp voraus.

Die Einschätzung beruht auf Quellcodeprüfung und Herstellerdokumentation. Linux-Builds, Laufzeittests und ein Portierungsprototyp sind nicht durchgeführt. Ein bloßer Frameworkwechsel oder das Ändern des Runtime Identifier ist kein nachgewiesener Lösungsweg.

## Erforderliche Arbeiten

| Bereich | Erforderlicher Umfang |
| --- | --- |
| Gemeinsamer Kern | `SVN_Notifier.Core` von `net10.0-windows` entkoppeln und Windows-Zugriffe über Plattformimplementierungen anbinden. SVN-Auswertung, INI-Verarbeitung und geeignete Ablaufsteuerung gemeinsam nutzen. |
| Oberfläche | Linux-Oberfläche mit Repository-Liste, Einstellungen und sämtlichen benötigten Dialogen. Sprache, Schrift, kompakte Darstellung und Tastaturbedienung erhalten. |
| SVN-Bedienung | Linux-Client `svn` anbinden. Ersatz für die durch `TortoiseProc` bereitgestellten Abläufe Aktualisieren, Commit, Protokoll, Änderungsprotokoll und Cleanup festlegen, einschließlich Anmeldung und Fehlerbehandlung. |
| Arbeitskopiereservierung | Registry-Identität, Windows-Dateihandles und Freigabe über Handles in `WorkingCopyLease` durch eine Linux-Implementierung mit denselben Schutzwirkungen ergänzen. Mehrere Instanzen, Absturz, laufende Kindprozesse, Prozess-ID-Wiederverwendung und Dateiaustausch prüfen. Keine automatischen SVN-Cleanups oder Änderungen an `wc.db`. |
| Pfade und Prozesse | Groß-/Kleinschreibung, symbolische Links, natürliche Sortierung, Umgebungsvariablen, Konfigurationsorte, Dateirechte und das Beenden eigener Prozessbäume berücksichtigen. |
| Desktop | Native Benachrichtigungen samt Meldungsklick und Aktivierung nach Programmende, abschaltbare Meldungen, Tray-Menü, vier Statusfarben, Autostart und Dateimanager-Anbindung umsetzen. Unterstützung unter GNOME/KDE sowie X11/Wayland ausdrücklich festlegen und prüfen. |
| Programmupdate | Linux-Pakete, Architektur- und Integritätsprüfung sowie gegebenenfalls Austausch und Neustart implementieren. Der aktuelle Updater erwartet Windows-ZIPs und PE/EXE-Dateien. |
| Build und Release | Zusätzliche Ziele `linux-x64` und `linux-arm64`, architekturspezifische Tests, Pakete und Prüfsummen ergänzen. Windows-Pakete und deren bestehende Updatewege erhalten. |

## Offene Entscheidungen

- **UI-Framework:** Avalonia als Kandidat für eine zusätzliche Linux-Oberfläche prüfen. Uno Platform wegen der Nähe zu WinUI als Alternative bewerten. Die tatsächliche Wiederverwendung des vorhandenen Oberflächencodes ist nicht nachgewiesen. Eine Umstellung der Windows-Oberfläche ist dafür nicht beschlossen.
- **Zielsysteme:** Verbindliche Distributionen, Desktopumgebungen und Display-Protokolle bestimmen. Native Bibliotheken und die verwendete C-Laufzeit begrenzen die Portabilität eines Pakets.
- **SVN-Dialoge:** Eigene Dialoge oder eine auf beiden Architekturen geprüfte externe Client-Anbindung auswählen. Der Kommandozeilenclient allein ersetzt die bisherigen Dialogfunktionen nicht.
- **Verteilung und Updates:** Portable Archive mit eigenem Updater oder Pakete mit Aktualisierung durch die Paketverwaltung auswählen.

## Einstieg bei späterer Beauftragung

1. Eine repräsentative Repository-Ansicht mit echter SVN-Statusprüfung sowie sicherer Reservierung prototypisch unter Linux x64 und ARM64 prüfen.
2. Native Meldung samt Klick und Wiederaktivierung sowie Tray-Menü und Statusfarben auf den gewählten Desktops nachweisen.
3. Funktionsparität einschließlich SVN-Dialogen festlegen und die verbleibenden Plattformimplementierungen und Ansichten übertragen.
4. Paketierung, Aktualisierung und zusätzliche GitHub-Actions-Jobs integrieren. Windows-Regressionen und Linux-Laufzeitprüfungen auf beiden Architekturen ausführen.

Erfolgreiche Builds oder Tests auf einem virtuellen X11-Display belegen allein keine funktionierende Integration in eine echte Desktop-Sitzung. ARM64 benötigt Ausführungsnachweise auf ARM64. Ob Windows- und Linux-Clients dieselbe Arbeitskopie sicher verwenden können, ist nicht nachgewiesen und darf nicht aus dem gemeinsamen Reservierungsformat abgeleitet werden.

## Referenzen für die spätere Prüfung

- [Microsoft: .NET 10, unterstützte Betriebssysteme und Architekturen](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
- [Avalonia: Plattformunterstützung](https://docs.avaloniaui.net/docs/supported-platforms) und [TrayIcon](https://docs.avaloniaui.net/controls/navigation/trayicon)
- [Uno Platform: Plattformanforderungen](https://platform.uno/docs/articles/getting-started/requirements.html)
- [XDG Desktop Portal: Benachrichtigungen und Aktivierung](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.Notification.html)
- [GitHub: native ARM64-Runner für öffentliche Repositories](https://github.blog/changelog/2025-08-07-arm64-hosted-runners-for-public-repositories-are-now-generally-available/)

Plattformunterstützung und Frameworkvoraussetzungen bei einer späteren Beauftragung erneut prüfen.
