# Windows App SDK: Integrationshinweise für SVN Notifier

Diese Notiz verbindet die für das PRP kritischen Recherchebefunde mit den Windows-Prüfungen vom 12.11.2026. Die Implementierung verwendet `Microsoft.WindowsAppSDK` Version `2.6.5` und .NET SDK `10.0.114`. Einzelne Laufzeitnachweise sind weiterhin offen; maßgeblich ist der [Umsetzungsstand](../winui-native-benachrichtigungen/03-umsetzungsstand.md).

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

Geprüfter Paketinhalt: `Microsoft.WindowsAppSDK.Base` Version `2.6.5`, Datei `build/Microsoft.WindowsAppSDK.SingleFile.targets`; gleicher Inhalt unter `buildTransitive`. Die tatsächlichen x64-/x86-Paketbuilds und EXE-Austausche sind zusätzlich unter `integration-validation.log` mit Exit-Code 0 nachgewiesen.

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

`IncludeAllContentForSelfExtract=true` entpackt auch verwaltete Assemblies. Die ursprüngliche Prüfung auf einen leeren `Assembly.Location` lehnt deshalb die gebündelte WinUI-Anwendung als nicht portabel ab. `portable-gui-contract.log` reproduziert diesen Fehler. Die Implementierung prüft den Bundlemarker der tatsächlich laufenden EXE einschließlich gültigem Headeroffset. Das entspricht dem Format in [Microsoft.NET.HostModel.Bundle.Bundler für .NET 10](https://github.com/dotnet/runtime/blob/v10.0.0/src/installer/managed/Microsoft.NET.HostModel/Bundle/Bundler.cs). Ein nur erfolgreicher Austausch durch den separaten Installertreiber beweist die Aktualisierbarkeit aus der WinUI-Ansicht nicht; deren Download-, Fehler- und Abbruchpfade werden zusätzlich ausgeführt.

Bei manuell verzögerter Initialisierung verlangt der Quellcode von `Microsoft.WindowsAppSDK.Foundation/2.6.5/include/UndockedRegFreeWinRT-AutoInitializer.cs` zusätzlich `MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY_PID` mit der aktuellen Prozess-ID in invariantem Zahlenformat. Damit übernimmt ein Kindprozess nicht versehentlich das Ressourcenverzeichnis seines Elternprozesses. Beide Werte vor dem Zugriff auf die SDK-Laufzeit setzen. Der aktuelle x64-Ansichtslauf `gui-check-application-definition.log` erreicht damit die Repository-Liste und Befehlsprüfungen; dies ist kein Nachweis für sauberes Windows oder native Shellzustellung.

Primärquellen: [Unverpackte Auslieferung und Single-file EXE](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app#single-file-exe), [Projektproperties](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/project-properties), [geprüftes NuGet-Paket](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.Base/2.6.5).

## WinUI-Controls und Gestaltung

Native Dateiöffnungen für die Arbeitskopiereservierung erhalten am Win32-Aufruf die erweiterte Windows-Pfadsyntax, einschließlich `\\?\UNC\` für UNC-Ziele. Verglichene und gespeicherte Arbeitskopiepfade bleiben unverändert. Der GUI-Lauf `gui-download-clean.log` reproduziert bei einer langen Testarbeitskopie `coordination-error:Win32Exception`; `gui-long-path-current.log` besteht danach denselben Befehlsablauf und alle zehn GUI-Gegenproben. Der anschließende Versuch, dasselbe SVN-Testverzeichnis erneut zu benutzen, wird vom Teststarter ausdrücklich abgewiesen. `lease-verified.log` verwendet neue Verzeichnisse und belegt 347 x64-Prüfungen einschließlich echtem SVN/TortoiseSVN und Reservierung, Konkurrenzschutz sowie erneuter Freigabe jenseits von 260 Zeichen. Quelle: [CreateFileW und erweiterte Pfade](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew).

Für sichtbare Shelltests ist eine entsperrte Windows-Sitzung erforderlich. Im Lauf `portable-contract-clean/native-shell/1baccb747a424db6897a45f86226d193/test.log` lehnt Windows Vordergrundwechsel und Eingabekopplung ab; die gezielte Prozessprüfung `input-session-current.txt` identifiziert `LockApp` im Vordergrund. API-Annahme einer Meldung ist in diesem Zustand kein Nachweis eines sichtbaren Banners. Der sichtbare Starter weist die Sperroberfläche vor Prozessstart ab und beendet eigene Instanzen, falls die Sitzung während des Laufs gesperrt wird.

`AppWindow.IsShownInSwitchers` benötigt in dieser Umgebung die Windows-Shell. Der tatsächliche Einstellungsdialog scheitert auf dem eigens angelegten inaktiven Desktop beim SDK-Aufruf `SetShownInSwitchers`. Derselbe Dialog besteht auf `Default` die Übernahme einschließlich Taskleistensichtbarkeit: `shell-settings-matrix.log` und `shell-settings-matrix-x86.log`, je viermal 497 GUI-Prüfungen und 17 erkannte Gegenproben, Exit-Code 0. Deshalb wird dieser Teil ausdrücklich mit `Test-Gui.ps1 -IncludeShellSettings` auf einem erlaubten Shell-Desktop geprüft; der Produktpfad bleibt unverändert. Programmgesteuerte eigene Dialogbedienung belegt keine Eingabefunktion auf einer gesperrten Sitzung.

- Die aktuelle Microsoft-Zuordnung nennt für WinForms `DataGridView` keinen direkten stabilen WinUI-Ersatz. Für die Repository-Liste ist eine passende native Listenansicht mit Spalten und den bisherigen Interaktionen vorgesehen; experimentelle Tabellencontrols sind keine Voraussetzung.
- Windows App SDK enthält keinen eigenen Tray-Control-Ersatz für `NotifyIcon`. Die Shell-Integration muss ausdrücklich erhalten werden.
- Kompakte Dichte und Theme-Ressourcen sind für eine datenorientierte Desktopoberfläche geeignete Ausgangspunkte. Sie ersetzen weder die eigene Informationshierarchie noch das Prüfen großer Schrift und schmaler Fenster.

Quellen: [WinForms-Patterns und WinUI-Entsprechungen](https://learn.microsoft.com/en-us/windows/apps/get-started/line-of-business/migrate-winforms-patterns), [Kompakte Dichte](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/compact-sizing), [Theme-Ressourcen](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-theme-resources).

## Noch zu erbringende Laufzeitnachweise

Einzel-EXE auf sauberem Windows, echte Benachrichtigungsaktivierung aus Banner und Historie vor/nach Programmende, neue Shellzustellung nach Verschieben des Programmordners sowie Windows-Einstellungen und Nicht stören bleiben offen. `start-modes-modules.log` belegt für x64/x86 die richtige Konfiguration beim Modus `start`, eigene extrahierte Laufzeitmodule und den ungültigen Installerstart ohne XAML-/App-Runtime-Initialisierung. `legacy-transition.log` belegt den tatsächlichen WinForms-zu-WinUI-Austausch mit erhaltenen Einstellungen und Stagingbereinigung. Beide Läufe enden mit Exit-Code 0.

Benutzerentscheidungen: Windows steuert die Banneranzeigedauer; normaler Betrieb ohne Administratorrechte genügt; Windows 10 1809 ist das Mindestziel. Alte Meldungen dürfen nach dem Verschieben des portablen Ordners ungültig werden, neue Meldungen müssen nach regulärem Start am neuen Ort wieder funktionieren. Diese Entscheidungen ersetzen keinen tatsächlichen Laufzeitnachweis.
