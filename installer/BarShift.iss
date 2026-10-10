; ============================================================
;  Installeur de BarShift  (Inno Setup 6.3 ou plus récent)
;  Place ce fichier dans le dossier "installer" du projet,
;  à côté de wizard-large.bmp et wizard-small.bmp.
; ============================================================

#define MyAppName "BarShift"
#define MyAppVersion "1.0"
#define MyAppExeName "BarShift.exe"

[Setup]
AppId={{B7C1E2A4-5D3F-4B8E-9A61-3C2F7E0D4A18}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppName}

; Choix de l'emplacement (page proposée pendant l'installation)
DefaultDirName={autopf}\{#MyAppName}
DisableDirPage=no
DisableProgramGroupPage=yes

; Sans droits administrateur par défaut (dossier utilisateur), avec possibilité de choisir "pour tous"
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Apparence
WizardStyle=modern
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
SetupIconFile=..\BarShift.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; Fichier produit : installer\output\BarShift-Setup.exe
OutputDir=output
OutputBaseFilename=BarShift-Setup
Compression=lzma2
SolidCompression=yes

; Ferme BarShift s'il tourne déjà (mise à jour)
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Messages]
french.WelcomeLabel2=Cet assistant va installer [name/ver] sur ton ordinateur.%n%nBarShift remplace la barre des tâches Windows par une barre simple et rapide, avec aperçu des fenêtres et prise en charge de plusieurs écrans.%n%nFerme les autres applications avant de continuer.

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le &Bureau"; GroupDescription: "Raccourcis :"
Name: "startmenuicon"; Description: "Ajouter BarShift au menu &Démarrer"; GroupDescription: "Raccourcis :"

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Case cochée à la fin : lancer BarShift
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer {#MyAppName} maintenant"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; À la désinstallation : on arrête BarShift puis on remet la barre des tâches Windows
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillBarShift"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--restore-taskbar"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreTaskbar"
