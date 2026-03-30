# README

Ce projet exporte un log de session en Markdown dans `Assets/Logs/Sessions/interaction_[ParticipantID]_[timestamp].md`.
Le fichier est ecrit par `Assets/Scripts/InteractionLogger.cs`.

## Objectif

Le systeme suit une interaction vocale avec un agent sur le sommeil, met a jour un modele socio-cognitif a chaque tour, puis exporte un snapshot des metriques utiles pour l'analyse.

## Architecture

Le code repose sur trois couches.

1. Couche dialogue
   un gestionnaire recoit la parole, maintient une memoire de conversation, construit l'appel LLM, puis declenche la synthese vocale.
2. Couche modele
   `Assets/Scripts/ComputationalModel.cs` maintient l'etat adaptatif de la session.
3. Couche logging
   `Assets/Scripts/InteractionLogger.cs` copie les valeurs du modele et les ecrit dans le `.md`.

Point cle : le logger ne calcule pas les metriques. Il snapshotte l'etat deja calcule par `ComputationalModel`.

## Fichiers principaux

### `Assets/Scripts/ComputationalModel.cs`

Ce fichier centralise la logique du modele.

- Il stocke l'etat recent du dialogue : derniers messages, nombres de mots, moyennes, ratio agent/utilisateur, equilibre du dialogue et durees orales estimees.
- Il estime le niveau utilisateur via `EstimateUserKnowledge()`.
- Il recalcule les SECs via `UpdateDialogueSECs()`.
- Il derive la posture via `DeterminePosture()`.
- Il derive la longueur recommandee pour la prochaine reponse via `UpdateRecommendedResponseLength()`.
- Il genere le bloc adaptatif du prompt via `BuildDynamicSystemInstructions()`.

Le cycle est toujours le meme :

1. `RecordUserTurn(...)` ou `RecordAgentTurn(...)` met a jour les compteurs bruts.
2. `RefreshDialogueMetrics()` recalcule les metriques relationnelles.
3. `UpdateDialogueSECs()` recalcule `Novelty`, `Complexity`, `CopingPotential` et `GoalRelevance`.
4. `UpdateScherer(...)` borne les scores, choisit la posture et met a jour les contraintes de longueur.

### `Assets/Scripts/InteractionLogger.cs`

Ce fichier gere l'export.

- `LogTurn(...)` lit les proprietes publiques du modele et les copie dans un `TurnRecord`.
- `SaveToMarkdown()` convertit ensuite la liste des `TurnRecord` en tableau Markdown.
- L'export se declenche sur `OnDestroy()` et a la sortie du mode Play.

Chaque ligne du fichier exporte represente donc un snapshot du modele juste apres un tour traite.

### `Assets/Scripts/LMStudioDialogManager.cs`

Ce gestionnaire implemente le pipeline LM Studio.

- Le texte utilisateur arrive par dictation Windows ou Whisper.
- `HandleUserInput(...)` appelle `model.RecordUserTurn(...)`, puis `logger.LogTurn("User", ...)`.
- La memoire conversationnelle est une `Queue<string>` avec des tours explicites `Utilisateur:` / `Agent:`.
- `SendToChat(...)` concatene le preprompt et `BuildDynamicSystemInstructions()`.
- Apres la reponse LLM, `postRequest(...)` affiche le texte, retire les balises affectives, met a jour le modele avec `RecordAgentTurn(...)`, puis logge `Agent`.

Dans ce pipeline, le `.md` peut donc contenir des lignes `User` et `Agent`.

### `Assets/Scripts/AvaturnLLMDialogManager.cs`

Ce gestionnaire implemente le pipeline Avaturn avec OpenWebUI ou Ollama.

- Les tours utilisateur et agent mettent bien a jour `ComputationalModel`.
- La memoire conversationnelle est une liste JSON `role/content`, pas une `Queue<string>`.
- `SendToChat(...)` injecte aussi `BuildDynamicSystemInstructions()` dans un message `system`.
- `ChatRequest(...)` logge les tours `Agent` via `logger.LogTurn("Agent", ...)`.

Difference importante verifiee dans le code : les tours utilisateur appellent `computationalModel.RecordUserTurn(...)`, mais ce gestionnaire ne fait pas d'appel symetrique a `logger.LogTurn("User", ...)`.

## Fonctionnement reel

### 1. Tour utilisateur

Dans `LMStudioDialogManager`, le chemin est le suivant :

1. le texte est recupere ;
2. `RecordUserTurn(...)` met a jour le modele ;
3. `LogTurn("User", ...)` snapshotte cet etat ;
4. le message est ajoute a la memoire ;
5. le prompt systeme dynamique est reconstruit ;
6. le message est envoye au LLM.

Dans `AvaturnLLMDialogManager`, le debut est similaire, sauf que le snapshot `User` n'est pas ecrit dans le logger.

### 2. Reponse agent

Dans les deux pipelines, l'ordre est globalement le meme :

1. la reponse LLM est recue ;
2. le texte est affiche ;
3. `ProcessAffectiveContent(...)` retire les balises comme `{JOY}`, `{EMPATHY}` ou `{DOUBT}` et declenche les effets non verbaux associes ;
4. `RecordAgentTurn(...)` met a jour le modele avec la version nettoyee ;
5. le logger ecrit un snapshot `Agent` si le gestionnaire l'appelle ;
6. `ApplySchererPosture(...)` applique la posture globale ;
7. la reponse est ajoutee a la memoire puis envoyee au module audio.

Il y a donc deux niveaux non verbaux distincts :

- un niveau local, porte par les balises emotionnelles presentes dans une reponse ;
- un niveau global, porte par `CurrentPosture`.

## Ce que le modele calcule

### Etat de dialogue

`ComputationalModel` maintient notamment :

- `LastUserWordCount`, `LastAgentWordCount`
- `AverageMessageLength`, `AverageAgentMessageLength`
- `AgentToUserLengthRatio`
- `DialogueBalance`
- `EstimatedLastUserSpeechSec`, `EstimatedLastAgentSpeechSec`
- `MaxRecommendedAgentSpeechSec`
- `MaxAgentToUserSpeechRatio`

### SECs

Les SECs sont recalculees a chaque tour a partir de l'etat courant du dialogue.

- `Novelty` depend surtout du recouvrement lexical avec l'historique recent, des marqueurs de nouveaute et des questions utilisateur.
- `Complexity` depend du niveau estime, de la longueur des tours et de la densite de termes techniques.
- `CopingPotential` depend du niveau utilisateur, de la longueur du tour utilisateur et du desequilibre agent/utilisateur.
- `GoalRelevance` depend de la proximite avec les themes sommeil, de l'ancrage personnel du message utilisateur et du recouvrement lexical avec la reponse agent.

### Posture et budget de reponse

`DeterminePosture()` derive `Pedagogical`, `Enthusiastic`, `Neutral` ou `Empathetic` a partir des SECs et du desequilibre du dialogue.

`UpdateRecommendedResponseLength()` derive ensuite :

- un minimum de mots ;
- un maximum de mots ;
- une duree orale maximale ;
- un ratio maximal agent/utilisateur.

Ces contraintes dependent de la longueur du dernier tour utilisateur, du niveau estime, de la posture courante et du desequilibre produit par l'agent.

## Comment lire le Markdown

### Metadonnees de tour

- `Turn`, `Role` : indexes du logger.
- `Length` : nombre de caracteres du message, pas nombre de mots.
- `?` : presence de `?` dans le message.
- `Time(s)` : valeur de `Time.time` au moment du snapshot ; ce n'est pas `TotalInteractionTime`.

### Etat adaptatif

- `Knowledge` : `UserKnowledge`
- `Posture` : `CurrentPosture`
- `Profile` : `ActiveProfile`
- `Novelty`, `Complex`, `Coping`, `Goal Rel.` : SECs courantes

### Rythme dialogique

- `Avg Usr Len`, `Avg Agt Len` : moyennes de caracteres
- `Last Usr W.`, `Last Agt W.` : nombres de mots
- `Est Usr(s)`, `Est Agt(s)` : durees orales estimees
- `Max Agt(s)`, `Max Ratio` : contraintes de la prochaine reponse agent
- `Bal.`, `Ratio` : equilibre du dialogue et ratio agent/utilisateur

## Limites actuelles verifiees dans le code

- Le fichier exporte est un Markdown, pas un CSV.
- La colonne `Cond.` existe dans `TurnRecord`, mais n'est pas renseignee.
- La colonne `Emo. Int.` existe, mais n'est pas alimentee dans l'etat actuel du code car `PatchLastEmotionalIntensity(...)` n'est appele nulle part.
- Dans le pipeline Avaturn, les tours utilisateur mettent a jour le modele mais ne sont pas ecrits dans le logger.
- Le texte peut apparaitre avant la parole de l'agent : l'affichage est fait avant la requete TTS, puis l'audio n'est joue qu'apres reception complete et decodage du WAV.
