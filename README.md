<p align="center">
  <img src="BarShift.ico" width="96" alt="BarShift">
</p>

<h1 align="center">BarShift</h1>

<p align="center">Une barre des tâches Windows simple et rapide, avec aperçu des fenêtres et prise en charge de plusieurs écrans.</p>

## Fonctionnalités

- **Barres personnalisables** : plusieurs profils d'applications (Général, Jeux, Code, Pro…), changement rapide par raccourci clavier
- **Aperçu des fenêtres au survol** : miniatures en direct, clic sur une miniature pour aller à la fenêtre
- **Plusieurs écrans** : une barre sur chaque écran, avec aperçu sur chacune
- **Plein écran** : la barre se cache sur l'écran où une vidéo ou un jeu est en plein écran
- **Remplace la barre Windows** (optionnel) : masque la barre des tâches d'origine
- Éléments au choix : bouton Démarrer, recherche, vue des tâches, Wi-Fi, volume, batterie, heure, afficher le bureau

## Installation

Télécharge `BarShift-Setup.exe` dans l'onglet **Releases** et lance-le. Windows 10/11 64 bits.

Pour revenir à la barre Windows d'origine : décoche l'option dans les paramètres, ou lance `BarShift.exe --restore-taskbar`.

### Windows affiche « Windows a protégé votre ordinateur » ?

C'est normal : le filtre SmartScreen de Windows prévient pour tous les programmes récents qui ne sont pas signés par un certificat payant. BarShift est open source, tu peux lire tout le code dans ce dépôt.

Pour continuer l'installation :

1. Clique sur **Informations complémentaires**
2. Clique sur **Exécuter quand même**

Si ton antivirus met le fichier en quarantaine, c'est un faux positif : BarShift masque la barre des tâches et surveille les fenêtres, ce qui ressemble au comportement de certains logiciels malveillants. Tu peux aussi compiler BarShift toi-même (voir plus bas).

## Compiler soi-même

Il faut le [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

L'installeur se fabrique ensuite avec [Inno Setup](https://jrsoftware.org/isdl.php) en ouvrant `installer/BarShift.iss`.

## Licence

BarShift est publié sous licence [MIT](LICENSE).

## Signature du code et confidentialité

**Signature.** Les versions Windows de BarShift sont destinées à être signées gratuitement par la [SignPath Foundation](https://signpath.org). La demande est en cours ; ce paragraphe sera mis à jour une fois la signature active.

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

**Rôles de signature.**

| Rôle | Personne |
|---|---|
| Auteur (code source) | [TristanCremault](https://github.com/TristanCremault) |
| Relecteur | [TristanCremault](https://github.com/TristanCremault) |
| Approbateur des signatures | [TristanCremault](https://github.com/TristanCremault) |

**Confidentialité.** BarShift ne collecte aucune donnée personnelle et n'envoie rien sur Internet. Ses réglages (profils, applications épinglées, raccourcis) restent sur ton ordinateur, dans `%APPDATA%\BarShift`.
