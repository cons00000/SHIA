# Agent conversationnel « Mike » — Bio-hacking du sommeil

Système Unity d'agent vocal adaptatif spécialisé dans le bio-hacking du sommeil. L'agent ajuste en temps réel son cadrage motivationnel, sa posture interactionnelle et la longueur de ses réponses selon le profil et le comportement de l'utilisateur.

---

## Architecture générale

Le système repose sur trois couches :

- **Perception** — transcription de la parole utilisateur (Whisper / dictation Windows)
- **Cognition** — construction du contexte conversationnel et appel au LLM
- **Expression** — synthèse vocale, expressions faciales et comportements non verbaux de l'avatar

| Couche | Technologie | Script principal |
|---|---|---|
| Entrée vocale | Whisper | `AvaturnLLMDialogManager.cs`, `LMStudioDialogManager.cs` |
| Cognition LLM | OpenWebUI / Ollama ou LM Studio | `SendToChat()` + `ComputationalModel.cs` |
| Expression faciale | Blendshapes ARKit Avaturn | `FacialExpressionAvaturn.cs` |
| Regard et tête | Contrôle cinématique | `Gaze.cs`, `TargetControl.cs` |
| Synthèse vocale | Piper ou MaryTTS | `postTTSRequest()`, `PlayAudio()` |
| Logging | Export Markdown horodaté | `InteractionLogger.cs` |

---

## Fichiers principaux

### `Assets/Scripts/ComputationalModel.cs`

Module central du système. Il maintient l'état adaptatif de la session et expose les méthodes suivantes :

- `RecordUserTurn()` / `RecordAgentTurn()` — mise à jour des compteurs bruts
- `RefreshDialogueMetrics()` — recalcul des métriques relationnelles
- `EstimateUserKnowledge()` — estimation du niveau utilisateur
- `UpdateDialogueSECs()` — recalcul des quatre SECs
- `DeterminePosture()` — sélection de la posture interactionnelle
- `UpdateRecommendedResponseLength()` — calcul du budget de réponse
- `BuildDynamicSystemInstructions()` — génération du bloc adaptatif injecté dans le prompt

Le cycle s'enchaîne toujours dans cet ordre : `RecordUserTurn` → `RefreshDialogueMetrics` → `UpdateDialogueSECs` → `UpdateScherer`.

### `Assets/Scripts/InteractionLogger.cs`

Gère l'export de session.

- `LogTurn()` — snapshotte les propriétés publiques du modèle dans un `TurnRecord`
- `SaveToMarkdown()` — convertit la liste des enregistrements en tableau Markdown
- L'export se déclenche sur `OnDestroy()` et à la sortie du mode Play

Le logger ne calcule pas de métriques ; il photographie l'état déjà calculé par `ComputationalModel`.

### `Assets/Scripts/LMStudioDialogManager.cs`

Pipeline LM Studio. L'historique conversationnel est une `Queue<string>` annotée `Utilisateur:` / `Agent:`.

### `Assets/Scripts/AvaturnLLMDialogManager.cs`

Pipeline Avaturn (OpenWebUI / Ollama). L'historique conversationnel est un tableau JSON `role/content`.

---

## Modèle computationnel affectif

### SECs — Stimulus Evaluation Checks

Quatre variables recalculées à chaque tour à partir du dialogue courant :

| SEC | Ce qu'elle mesure | Principaux facteurs |
|---|---|---|
| `Novelty` | Degré de nouveauté perçue | Recouvrement lexical avec l'historique, présence de questions, marqueurs de surprise |
| `Complexity` | Niveau de technicité du contenu | Niveau estimé de l'utilisateur, densité de termes techniques, longueur des tours |
| `CopingPotential` | Capacité supposée à suivre la conversation | Niveau estimé, longueur du tour utilisateur, équilibre agent/utilisateur |
| `GoalRelevance` | Pertinence pour les buts de l'utilisateur | Références au sommeil, ancrage personnel, recouvrement lexical question/réponse |

### Estimation du niveau utilisateur

`EstimateUserKnowledge()` analyse le texte utilisateur avec deux lexiques (expert et intermédiaire) :

- `Novice` — score lexical 0–1
- `Intermediate` — score lexical 2–4
- `Expert` — score lexical ≥ 5

### Posture interactionnelle

`DeterminePosture()` sélectionne une posture parmi quatre :

| Posture | Condition d'activation |
|---|---|
| `Pedagogical` | `CopingPotential < 0.45` ou agent trop long |
| `Enthusiastic` | `GoalRelevance > 0.7` et `Novelty > 0.6` et dialogue équilibré |
| `Neutral` | `Complexity > 0.7` et `CopingPotential > 0.6` |
| `Empathetic` | cas par défaut |

---

## Contrainte anti-effet tunnel

Le modèle estime la durée orale de chaque tour (débit de référence : 2,6 mots/seconde) et plafonne la longueur de la prochaine réponse de Mike :

| Longueur du dernier tour utilisateur | Ratio max Mike/utilisateur | Durée max recommandée |
|---|---|---|
| ≤ 6 mots | 3,0× | 8 s |
| 7–18 mots | 2,4× | 14 s |
| 19–35 mots | 2,0× | 20 s |
| > 35 mots | 1,6× | 28 s |

Si le tour précédent de Mike était déjà trop long, le plafond est abaissé en conséquence.

---

## Construction du prompt système

### Preprompt invariant (`PREPROMPT_BASE.txt`)

Définit la persona stable de Mike : journaliste en bio-hacking du sommeil, ton oral strict, deux phrases maximum, sans listes ni titres, avec recentrage naturel sur le sommeil. Autorise trois balises émotionnelles : `{JOY}`, `{EMPATHY}`, `{DOUBT}`.

### Bloc dynamique (`BuildDynamicSystemInstructions`)

Cinq sous-couches ajoutées à chaque tour :

1. Cadrage motivationnel (VI1 — Promotion ou Prévention)
2. Complexité discursive (VI2 — niveau estimé)
3. Ton journalistique modulé par la posture
4. Règles d'engagement conversationnel
5. Rythme dialogique et contrainte de longueur

---

## Conditions expérimentales

L'agent est piloté par le paramètre `motivationalProfile` dans l'Inspector Unity :

| Profil | Cadrage dominant |
|---|---|
| `Promotion` | Gains, bénéfices, effets positifs |
| `Prevention` | Risques, pertes, effets négatifs à éviter |

---

## Expressions faciales et comportements non verbaux

Les blendshapes ARKit sont animés par `FacialExpressionAvaturn.cs` avec interpolation progressive vers un état neutre. Les visèmes sont sélectionnés pseudo-aléatoirement pendant la lecture audio (pas de synchronisation phonème par phonème).

| Balise ou posture | AUs principales |
|---|---|
| `{JOY}` | AU6 + AU12 |
| `{EMPATHY}` | AU1 + AU15 |
| `{DOUBT}` | AU1 + AU4 + AU17 |
| `Enthusiastic` promotion | AU6 + AU12 + AU2 |
| `Pedagogical` prévention | AU1 + AU4 + AU17 |

---

## Logging — format Markdown

Les sessions sont exportées dans `Assets/Logs/Sessions/interaction_[ParticipantID]_[timestamp].md`.

### En-têtes du tableau exporté

| Colonne | Valeur |
|---|---|
| `Turn`, `Role` | Index et rôle du tour |
| `Length` | Nombre de caractères du message |
| `?` | Présence d'un `?` dans le message |
| `Time(s)` | `Time.time` au moment du snapshot |
| `Knowledge` | `UserKnowledge` estimé |
| `Posture` | `CurrentPosture` |
| `Profile` | `ActiveProfile` |
| `Novelty`, `Complex`, `Coping`, `Goal Rel.` | SECs courantes |
| `Avg Usr Len`, `Avg Agt Len` | Moyennes de caractères |
| `Last Usr W.`, `Last Agt W.` | Nombres de mots |
| `Est Usr(s)`, `Est Agt(s)` | Durées orales estimées |
| `Max Agt(s)`, `Max Ratio` | Contraintes de la prochaine réponse |
| `Bal.`, `Ratio` | Équilibre et ratio agent/utilisateur |

> `Time(s)` correspond à `Time.time` Unity, pas à la durée totale d'interaction.  
> `Emo. Int.` est renseignée uniquement dans le pipeline Avaturn si le module d'analyse émotionnelle retourne un score exploitable.
