# SchoolDesk 0.5.1

Application scolaire native Windows 11 en C# / .NET 8 / WPF. Fonctionnement local, sans serveur ni dépendance réseau.

## Fonctionnalités
- Tableau de bord avec cours actuel, prochain cours, horloge et compte à rebours.
- Alternance A/B calculée automatiquement depuis le 11 novembre 2026 (semaine A) ; le planning livré est volontairement vide, sans cours fictifs ni données personnelles.
- Compteurs des tâches terminées et à faire sur l'accueil.
- Agenda organisé par journée, avec catégories Devoir, Évaluation, Cours et DM, matière, heure, description et état fait/pas fait.
- Agenda filtrable par jour, semaine (lundi à dimanche) ou mois ; l'heure d'une tâche est facultative.
- Liste des matières préremplies depuis l'emploi du temps, modifiable dans Paramètres. Retirer une matière ne supprime aucun fichier existant.
- Espace documentaire classé par matière et catégorie.
- Explorateur des documents DOCX classés et notes rapides locales avec sauvegarde automatique.
- Éditeur WYSIWYG WPF : rédaction directement visuelle, sans syntaxe Markdown, avec gras, italique, souligné, barré, couleurs, surlignage, styles de titres, polices, tailles, alignements, listes à puces et numérotées, annuler/rétablir.
- Interface claire et épurée : surfaces mates, navigation latérale, contrôles cohérents et accent bleu discret.
- Navigation intégrée dans une fenêtre unique : tableau de bord, agenda, documents, explorateur, notes et paramètres sont affichés dans la zone principale, sans fenêtres de module séparées.
- Emploi du temps personnalisable pour chaque utilisateur depuis Paramètres ; le fichier personnel est conservé dans `%LOCALAPPDATA%\\SchoolDesk\\schedule.json` et n'est jamais partagé avec le dépôt.
- Contraste renforcé pour les listes déroulantes et menus ; fond clair opaque sur toute la fenêtre maximisée.
- Éditeur avec commandes de mise en forme regroupées et raccourcis Ctrl+N, Ctrl+O et Ctrl+S.
- Ouverture DOCX asynchrone et export ZIP en arrière-plan, génération XML fractionnée pour laisser respirer l'interface ; sauvegarde automatique uniquement après modification, sans réécritures périodiques à vide.
- Journal des erreurs inattendues dans `%LOCALAPPDATA%\\SchoolDesk\\errors.log`.
- Avertissement à l'ouverture des documents contenant des images ou des blocs non pris en charge ; le document source est conservé dans un fichier `.bak` avant son premier remplacement.
- Import/export DOCX local via les API intégrées de .NET, sans package NuGet.
- Prise en charge des paragraphes, styles de titres courants, mises en forme de texte, alignements et tableaux simples. Les images, zones de texte, SmartArt, notes, champs avancés et mises en page complexes ne sont pas préservés par ce convertisseur léger.
- Option de lancement à l'ouverture de session Windows, enregistrée dans la clé utilisateur HKCU, sans privilèges administrateur.
- Agenda et paramètres stockés dans `%LOCALAPPDATA%\\SchoolDesk`; documents dans `Documents\\SchoolDesk`.
- Les fichiers JSON sauvegardés conservent une copie `.bak`; une donnée JSON illisible est préservée dans un fichier `.corrupt-*` et l'erreur est signalée au lieu d'afficher une liste vide.
- Les nouvelles tâches conservent leurs dates de création et de modification ; les documents modifiés ont une copie `.bak`.

## Compilation
Depuis le dossier contenant `SchoolDesk.csproj` :

```powershell
dotnet build -c Release
dotnet run
```

Aucun package NuGet externe n'est requis. Le traitement DOCX repose sur le format Open XML (archive ZIP et fichiers XML) avec les API intégrées de .NET.

## Configurer son emploi du temps

Dans **Paramètres**, choisis **Ouvrir mon fichier d'emploi du temps**. Le fichier est créé localement au premier lancement. Ajoute tes propres événements au tableau JSON, puis redémarre SchoolDesk. Le modèle versionné `Assets/schedule.json` reste vide : tes horaires et tes salles ne sont pas publiés.

Chaque entrée utilise les propriétés suivantes :

| Propriété | Valeurs |
|---|---|
| `day` | Jour anglais de la semaine : `Monday`, `Tuesday`, `Wednesday`, `Thursday`, `Friday`, `Saturday` ou `Sunday` |
| `week` | `A`, `B` ou `AB` pour les deux semaines |
| `start`, `end` | Heures au format `HH:mm` |
| `title` | Nom du cours, de la pause ou de l'événement |
| `room` | Salle, ou `null` |
| `kind` | `Lesson`, `Break`, `Lunch`, `Free` ou `HomeMeal` |

Les horaires qui se chevauchent et les durées nulles ou négatives sont refusés. Un tableau vide `[]` est valide et affiche un message invitant à configurer le planning.

## Limites DOCX
SchoolDesk fournit un éditeur visuel WPF et un convertisseur DOCX local ciblant les éléments courants. Ce n'est pas un moteur de mise en page Word complet : les documents très complexes peuvent perdre des éléments lors de l'import/export. Conserver une copie du DOCX original avant de convertir un document important ou riche en objets intégrés.
