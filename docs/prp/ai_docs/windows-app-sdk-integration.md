# Windows App SDK: Integrationshinweise für SVN Notifier

Diese Notiz hält die für das PRP kritischen Recherchebefunde fest. Sie ist keine Aussage über eine gebaute oder getestete WinUI-Ausgabe. Geprüft im Rahmen der Analyse am 12.11.2026: Microsoft-Dokumentation, SDK-Quellcode sowie NuGet-Metadaten und Build-Regeln. Bei Implementierung konkrete stabile Paketversion auswählen, erneut gegen deren Inhalt prüfen und durch Windows-Ausführung bestätigen.

## Benachrichtigungen und UI sind unabhängige Bausteine

Microsoft beschreibt `Microsoft.Windows.AppNotifications.AppNotificationManager` ausdrücklich auch für WinForms/WPF und unverpackte Desktopanwendungen. Eine WinUI-Migration ist für native Meldungen nicht technisch erforderlich; sie wird im zugehörigen PRP wegen der beauftragten Gesamtgestaltung geplant.

Quelle: [App notifications in .NET](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet), insbesondere Registrierung, Versand und Hinweise für unverpackte Anwendungen.

## Registrierung, Aktivierung und Lebensdauer

- Handler für `NotificationInvoked` vor `Register()` verbinden. Aktivierungen können auf einem Hintergrundthread eintreffen; UI-Arbeit auf den UI-Thread übertragen und Aktivierung vor fertiger Fenstererstellung zwischenspeichern.
- Bei unverpackten Anwendungen richtet `Register()` die COM-Aktivierung ein. Der SDK-Quellcode registriert dafür die aktuelle EXE unter `HKEY_CURRENT_USER`; die Registrierung ist daher an den Anwendungspfad gekoppelt.
- Normales `Unregister()` beim Beenden und dauerhaftes `UnregisterAll()` unterscheiden. Eine portable Anwendung benötigt ein geplantes Verhalten für Entfernen/Verschieben; gespeicherte Meldungen nicht bei jedem Programmende pauschal löschen.
- Der bestehende Updater kopiert dieselbe EXE als Installerhilfsprozess. Dieser Modus darf keine für ihn falsche Benachrichtigungsregistrierung erzeugen.
- Mehrere Instanzen und verschiedene Arbeitsverzeichnisse erfordern eine explizite Kontextzuordnung. Eine pauschale Single-Instance-Regel würde bestehendes Verhalten einschränken.

Quellen: [Register](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.register), [SDK-Registrierungshelfer](https://github.com/microsoft/WindowsAppSDK/blob/main/dev/AppNotifications/AppNotificationUtility.cpp), [AppNotificationManager-Quellcode](https://github.com/microsoft/WindowsAppSDK/blob/main/dev/AppNotifications/AppNotificationManager.cpp).

## Grenzen, die nicht als Funktionsverlust übergangen werden dürfen

- Die .NET-Anleitung dokumentiert keine Unterstützung erhöhter Anwendungen. Im geprüften `AppNotificationManager.cpp` verneint `IsSupported()` die Unterstützung erhöhter Prozesse. Daraus folgt ein Kompatibilitätsprüfpunkt, keine Freigabe zum Abschalten bisher nutzbarer Funktionen.
- Systemweite Benachrichtigungseinstellungen und Nicht stören beeinflussen die Anzeige. API-Aufruf und sichtbares Banner sind unterschiedliche Nachweise.
- Banneranzeigedauer und Ablauf in der Benachrichtigungszentrale sind unterschiedliche Größen. Den bestehenden `ShowBalloonInterval` nicht ohne Prüfung als `Expiration` übernehmen.
- Meldungsziel in jeder Payload sichern. Das globale `firstBalloonPath` ist für mehrere aufbewahrte Meldungen ungeeignet.
- Tag/Gruppe, Entfernen und Ablauf können verwendet werden, um inzwischen erledigte Änderungsmeldungen zu verwalten.

Quellen: [App notifications in .NET](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet), [Meldungen verwalten](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/manage-app-notifications), [Windows-Benachrichtigungseinstellungen](https://support.microsoft.com/en-us/windows/experience/notifications-and-do-not-disturb-in-windows).

## Einzel-EXE-Auslieferung

Microsofts Dokumentationsseiten enthielten bei der Recherche unterschiedliche Aussagen zur Einzel-EXE. Deshalb wurde zusätzlich das öffentlich ausgelieferte NuGet-Paket geprüft. Die Paketdateien wurden nur im Arbeitsspeicher gelesen, nicht installiert.

Geprüfter Paketinhalt: `Microsoft.WindowsAppSDK.Base` Version `2.6.5`, Datei `build/Microsoft.WindowsAppSDK.SingleFile.targets`; gleicher Inhalt unter `buildTransitive`. Diese konkrete Prüfung belegt die vorgesehenen Build-Bedingungen, keinen erfolgreichen Notifier-Build und keine Empfehlung, ungeprüft genau diese Version zu übernehmen.

| Property | Befund der geprüften Build-Regeln |
|---|---|
| `PublishSingleFile` | Einzel-EXE-Konfiguration wird geprüft. |
| `EnableMsixTooling=true` | Erforderlich für eingebettete Ressourcen; bedeutet allein keine MSIX-Auslieferung. |
| `WindowsPackageType=None` | Einzel-EXE nur für unverpackte Anwendungen. |
| `IncludeAllContentForSelfExtract=true` | Erforderlich für die native Ressourcen-/DLL-Auflösung. |
| `WindowsAppSDKSelfContained=true` | Als Voraussetzung für die eigenständig ausgelieferte SDK-Laufzeit vorgesehen. |
| `.NET SelfContained=true` | Auch die .NET-Laufzeit muss im Paket enthalten sein. |
| `WindowsAppSdkUndockedRegFreeWinRTInitialize` | Initialisierung bzw. die dokumentierte Basisverzeichnisbehandlung beachten. |

Nicht mit `WindowsAppSDKSingleFileVerifyConfiguration=false` die Prüfung abschalten, um ein ungeklärtes Auslieferungsproblem zu verdecken. Die tatsächliche Ressourcenentpackung, der Start ohne vorinstallierte Laufzeiten und die Hilfsmodi sind auf beiden Architekturen nachzuweisen.

Primärquellen: [Unverpackte Auslieferung und Single-file EXE](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app#single-file-exe), [Projektproperties](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/project-properties), [geprüftes NuGet-Paket](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.Base/2.6.5).

## WinUI-Controls und Gestaltung

- Die aktuelle Microsoft-Zuordnung nennt für WinForms `DataGridView` keinen direkten stabilen WinUI-Ersatz. Für die Repository-Liste ist eine passende native Listenansicht mit Spalten und den bisherigen Interaktionen vorgesehen; experimentelle Tabellencontrols sind keine Voraussetzung.
- Windows App SDK enthält keinen eigenen Tray-Control-Ersatz für `NotifyIcon`. Die Shell-Integration muss ausdrücklich erhalten werden.
- Kompakte Dichte und Theme-Ressourcen sind für eine datenorientierte Desktopoberfläche geeignete Ausgangspunkte. Sie ersetzen weder die eigene Informationshierarchie noch das Prüfen großer Schrift und schmaler Fenster.

Quellen: [WinForms-Patterns und WinUI-Entsprechungen](https://learn.microsoft.com/en-us/windows/apps/get-started/line-of-business/migrate-winforms-patterns), [Kompakte Dichte](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/compact-sizing), [Theme-Ressourcen](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-theme-resources).

## Noch zu erbringende Laufzeitnachweise

Einzel-EXE auf sauberem Windows, x86/x64, Alt-zu-Neu-Update mit unverändertem Paketvertrag, korrekte Versionsquelle, keine normale Initialisierung im Installer, echte Benachrichtigungsaktivierung vor/nach Programmende, verschobener Programmordner, mehrere Instanzen und Konfigurationen, Benachrichtigungsdauer und erhöhter Betrieb. Diese Punkte sind ausdrücklich keine bei PRP-Erstellung bestandenen Tests.
