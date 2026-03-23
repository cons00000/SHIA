# Spécifications techniques de l'agent expert en bio-hacking du sommeil

## 1. Architecture générale du système

L'agent virtuel repose sur trois couches logicielles :

- une couche de perception, chargée de capter et transcrire la parole de l'utilisateur ;
- une couche de cognition, chargée de construire le contexte conversationnel et d'interroger le LLM ;
- une couche d'expression, chargée de produire la voix, les expressions faciales et les comportements non verbaux de l'avatar.

Le module central [ComputationalModel.cs](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/Assets/Scripts/ComputationalModel.cs) orchestre l'adaptation socio-affective et conversationnelle. Il conserve un état du dialogue, estime le niveau de connaissance de l'utilisateur, calcule les SECs inspirés du CPM de Scherer, détermine une posture interactionnelle et génère les instructions dynamiques injectées dans le prompt système du LLM.

| Couche | Technologie principale | Script Unity principal |
|---|---|---|
| Entrée vocale | Whisper | [AvaturnLLMDialogManager.cs](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/Assets/Scripts/AvaturnLLMDialogManager.cs), [LMStudioDialogManager.cs](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/Assets/Scripts/LMStudioDialogManager.cs) |
| Cognition LLM | OpenWebUI/Ollama ou LM Studio | `SendToChat()` + [ComputationalModel.cs](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/Assets/Scripts/ComputationalModel.cs) |
| Expression faciale | Blendshapes ARKit Avaturn | [FacialExpressionAvaturn.cs](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/Assets/Scripts/FacialExpressionAvaturn.cs) |
| Regard et tête | Contrôle cinématique simple | [Gaze.cs](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/Assets/Scripts/Gaze.cs), [TargetControl.cs](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/Assets/Scripts/TargetControl.cs) |
| Synthèse vocale | Piper ou MaryTTS selon la scène | `postTTSRequest()` dans [AvaturnLLMDialogManager.cs](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/Assets/Scripts/AvaturnLLMDialogManager.cs), `PlayAudio()` dans [LMStudioDialogManager.cs](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/Assets/Scripts/LMStudioDialogManager.cs) |
| Logging | Export CSV horodaté par participant | [InteractionLogger.cs](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/Assets/Scripts/InteractionLogger.cs) |

## 2. Modèle computationnel affectif

### 2.1 SECs du modèle

Le modèle maintient quatre Stimulus Evaluation Checks :

- `Novelty`
- `Complexity`
- `CopingPotential`
- `GoalRelevance`

Ces variables sont mises à jour à chaque tour de parole à partir du dialogue courant. Le calcul ne repose pas uniquement sur le texte produit par l'agent ; il tient compte à la fois du dernier message utilisateur, du dernier message agent, de l'historique récent et de l'équilibre conversationnel.

### 2.2 Variables de dialogue suivies par le modèle

Le modèle conserve notamment :

- le nombre de tours utilisateur ;
- le nombre de tours agent ;
- le nombre de questions posées par l'utilisateur ;
- la longueur moyenne des messages utilisateur ;
- la longueur moyenne des messages agent ;
- la longueur du dernier tour utilisateur et du dernier tour agent ;
- le nombre de mots du dernier tour utilisateur et du dernier tour agent ;
- un ratio de longueur agent/utilisateur ;
- un indicateur d'équilibre du dialogue ;
- une estimation de durée orale des derniers tours.

Ces variables servent à ajuster les SECs, la posture et les consignes de longueur injectées au LLM.

### 2.3 Calcul de `Novelty`

`Novelty` représente le degré de nouveauté perçue dans l'échange. Le calcul augmente lorsque :

- le dernier tour utilisateur contient une question ;
- le contenu du tour courant recouvre peu l'historique récent ;
- le texte de l'agent contient des formulations associées à la nouveauté ou à la surprise.

Le score diminue lorsque le dialogue devient répétitif et que le contenu du tour courant recouvre fortement les tours précédents.

### 2.4 Calcul de `Complexity`

`Complexity` représente le niveau de technicité du contenu. Le calcul dépend de :

- l'estimation du niveau de connaissance de l'utilisateur ;
- la densité de termes techniques dans le message utilisateur ;
- la densité de termes techniques dans la réponse agent ;
- la longueur des tours ;
- le fait que l'agent développe plus ou moins longuement une explication.

Le score est plus élevé lorsque l'utilisateur mobilise déjà un lexique spécialisé ou lorsque l'agent emploie un vocabulaire technique dense.

### 2.5 Calcul de `CopingPotential`

`CopingPotential` représente la capacité supposée de l'utilisateur à suivre le niveau de la conversation. Le calcul prend en compte :

- le niveau de connaissance estimé ;
- la longueur et la précision du dernier tour utilisateur ;
- la présence d'une question explicite ;
- la présence de termes liés au sommeil et à l'expérience personnelle ;
- l'équilibre conversationnel ;
- le fait que l'agent ait parlé trop longtemps par rapport à l'utilisateur.

Lorsque Mike parle beaucoup plus longtemps que l'utilisateur, `CopingPotential` diminue. Le modèle interprète alors ce déséquilibre comme un risque de surcharge ou d'effet tunnel.

### 2.6 Calcul de `GoalRelevance`

`GoalRelevance` représente la pertinence du contenu pour les buts de l'utilisateur. Le score augmente lorsque :

- l'utilisateur évoque directement le sommeil, la fatigue, l'endormissement, les réveils, l'énergie ou sa routine ;
- le message utilisateur contient des références à sa propre situation ;
- la réponse agent recouvre lexicalement la question posée ;
- la réponse agent contient une micro-action concrète liée au sommeil.

## 3. Estimation du niveau de connaissance utilisateur (VI2)

La méthode `EstimateUserKnowledge()` analyse le texte utilisateur à l'aide de deux lexiques :

- un lexique expert ;
- un lexique intermédiaire.

Le système attribue :

- `Novice` pour un score lexical de 0 à 1 ;
- `Intermediate` pour un score lexical de 2 à 4 ;
- `Expert` pour un score lexical supérieur ou égal à 5.

Le niveau estimé ne modifie pas directement `CopingPotential` de manière additive. Il est utilisé comme une composante du calcul global du modèle, aux côtés des métriques de dialogue.

## 4. Posture interactionnelle

### 4.1 Détermination de la posture

La méthode `DeterminePosture()` sélectionne une posture parmi :

- `Pedagogical`
- `Enthusiastic`
- `Neutral`
- `Empathetic`

La sélection suit la logique suivante :

| Posture | Condition d'activation |
|---|---|
| `Pedagogical` | `CopingPotential < 0.45` ou déséquilibre marqué du dialogue avec un agent trop long |
| `Enthusiastic` | `GoalRelevance > 0.7` et `Novelty > 0.6` et dialogue suffisamment équilibré |
| `Neutral` | `Complexity > 0.7` et `CopingPotential > 0.6` |
| `Empathetic` | cas par défaut |

### 4.2 Effets de la posture

La posture sélectionnée agit sur deux niveaux :

- elle module le bloc d'instructions textuelles injecté au LLM ;
- elle détermine les expressions faciales et les triggers Animator appliqués à l'avatar.

Par exemple :

- `Enthusiastic` favorise un ton plus dynamique et peut s'accompagner de `{JOY}` ;
- `Pedagogical` ralentit le rythme discursif et encourage une formulation plus guidée ;
- `Empathetic` favorise une reconnaissance brève de la difficulté avant l'information ;
- `Neutral` maintient un style de journaliste scientifique plus factuel.

## 5. Conditions expérimentales

L'agent est piloté par le paramètre `motivationalProfile` exposé dans l'Inspector Unity. Le code implémente deux profils :

- `Promotion`
- `Prevention`

Ces profils influencent la couche VI1 du prompt dynamique.

| Paramètre | Promotion | Prévention |
|---|---|---|
| Cadrage dominant | gains, bénéfices, effets positifs | risques, pertes, effets négatifs à éviter |
| Posture CPM | dynamique | dynamique |
| Adaptation VI2 | temps réel | temps réel |
| Ton journalistique | modulé par posture | modulé par posture |

Le reste du système reste identique d'une scène à l'autre : avatar, scripts, pipeline de perception et pipeline d'expression.

## 6. Construction du prompt système

### 6.1 Preprompt invariant

Le preprompt de base est stocké dans [PREPROMPT_BASE.txt](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/PREPROMPT_BASE.txt). Il définit la persona stable de Mike :

- journaliste spécialisé en bio-hacking et sommeil ;
- ton oral strict ;
- deux phrases maximum ;
- pas de listes, pas de titres ;
- alternance entre question courte et micro-action ou fait concret ;
- pas de conseils médicaux ;
- recentrage naturel sur le sommeil en cas de dérive.

Le preprompt autorise aussi trois balises émotionnelles :

- `{JOY}`
- `{EMPATHY}`
- `{DOUBT}`

Ces balises sont utilisées par le LLM dans le texte généré, puis retirées avant synthèse vocale.

### 6.2 Bloc dynamique généré à chaque tour

`BuildDynamicSystemInstructions()` produit un bloc adaptatif ajouté au preprompt de base. Ce bloc comprend cinq sous-couches :

1. cadrage motivationnel VI1 ;
2. complexité discursive VI2 ;
3. ton journalistique modulé par la posture ;
4. règles d'engagement conversationnel ;
5. rythme dialogique et contrainte de longueur.

La cinquième couche contient des informations calculées à partir du dialogue courant :

- nombre de mots du dernier tour utilisateur ;
- nombre de mots de la dernière réponse agent ;
- plage de mots visée pour la prochaine réponse ;
- durée orale estimée du dernier tour utilisateur ;
- durée maximale recommandée pour la prochaine réponse ;
- ratio maximal Mike/utilisateur.

## 7. Contrainte anti-effet tunnel

Le code implémente une contrainte explicite destinée à éviter que Mike monopolise la parole.

### 7.1 Principe

Le modèle estime la durée orale d'un tour à partir du nombre de mots, en utilisant un débit de référence de `2.6 mots/seconde`. Cette estimation permet de comparer la durée du dernier tour utilisateur à la durée de la dernière réponse agent.

### 7.2 Variables utilisées

Le modèle calcule :

- `EstimatedLastUserSpeechSec`
- `EstimatedLastAgentSpeechSec`
- `MaxRecommendedAgentSpeechSec`
- `MaxAgentToUserSpeechRatio`

Ces variables sont ensuite utilisées pour plafonner la longueur recommandée de la prochaine réponse.

### 7.3 Logique de plafonnement

Le plafond varie selon la longueur du dernier tour utilisateur :

| Longueur du dernier tour utilisateur | Ratio max Mike / utilisateur | Durée max recommandée |
|---|---|---|
| <= 6 mots | 3.0x | 8 s |
| 7 à 18 mots | 2.4x | 14 s |
| 19 à 35 mots | 2.0x | 20 s |
| > 35 mots | 1.6x | 28 s |

Si le tour précédent de Mike était déjà trop long, le système abaisse encore ce plafond. La longueur maximale en mots de la prochaine réponse est ensuite recalculée à partir de cette durée cible.

### 7.4 Effet sur le comportement du LLM

La réponse générée reste contrainte par le preprompt oral strict, mais elle est aussi pilotée par cette couche de rythme dialogique. Le système demande explicitement au LLM :

- de raccourcir sa réponse après un tour utilisateur très court ;
- de ne pas dépasser un certain multiple de la durée estimée du dernier tour utilisateur ;
- de laisser de la place à la relance ;
- d'éviter l'effet tunnel.

## 8. Pipeline conversationnel

### 8.1 Gestion d'un tour utilisateur

Dans les managers de dialogue :

1. la parole utilisateur est transcrite par Whisper ;
2. le texte transcrit est affiché dans l'interface ;
3. `RecordUserTurn()` met à jour les métriques du modèle ;
4. le tour utilisateur est ajouté à la mémoire conversationnelle ;
5. le prompt complet est envoyé au LLM.

Dans [LMStudioDialogManager.cs](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/Assets/Scripts/LMStudioDialogManager.cs), l'historique conversationnel est stocké comme une file de tours annotés `Utilisateur:` et `Agent:`. Dans [AvaturnLLMDialogManager.cs](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/Assets/Scripts/AvaturnLLMDialogManager.cs), l'historique est structuré comme un tableau JSON de messages `role/content`.

### 8.2 Gestion d'une réponse agent

Après réception de la réponse du LLM :

1. le contenu textuel est extrait de la réponse JSON ;
2. les balises émotionnelles sont détectées ;
3. ces balises déclenchent des AUs et des animations ;
4. les balises sont retirées du texte prononcé ;
5. `RecordAgentTurn()` met à jour le modèle ;
6. la posture courante est appliquée à l'avatar ;
7. la réponse est ajoutée à la mémoire de conversation ;
8. la synthèse vocale est lancée.

## 9. Expressions faciales et comportements non verbaux

### 9.1 Blendshapes Avaturn

Le script [FacialExpressionAvaturn.cs](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/Assets/Scripts/FacialExpressionAvaturn.cs) maintient un dictionnaire d'AUs reliées aux noms de blendshapes ARKit. Le système :

- définit une cible d'intensité pour chaque AU ;
- interpole progressivement les valeurs avec une `AnimationCurve` ;
- ramène progressivement le visage vers un état neutre ;
- gère un clignement automatique périodique ;
- anime des visèmes pendant la synthèse vocale.

### 9.2 Visèmes

Le système de visèmes ne repose pas sur une analyse phonémique de l'audio. Il sélectionne des visèmes ARKit de manière pseudo-aléatoire à intervalles réguliers pendant que l'audio est en lecture. Il produit donc un effet global de parole, sans synchronisation phonème par phonème.

### 9.3 Mapping des balises émotionnelles

| Balise ou posture | AUs principales |
|---|---|
| `{JOY}` | AU6 + AU12 |
| `{EMPATHY}` | AU1 + AU15 |
| `{DOUBT}` | AU1 + AU4 + AU17 |
| `{SAD}` | AU1 + AU4 + AU15 |
| `Enthusiastic` promotion | AU6 + AU12 + AU2 |
| `Pedagogical` prévention | AU1 + AU4 + AU17 |

### 9.4 Regard et tête

Le regard est contrôlé par [Gaze.cs](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/Assets/Scripts/Gaze.cs), qui oriente progressivement la colonne, la tête et les yeux vers une cible. [TargetControl.cs](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/Assets/Scripts/TargetControl.cs) pilote la position de cette cible dans l'espace pour produire des changements d'orientation.

## 10. Logging et collecte des métriques

Le script [InteractionLogger.cs](/Users/constance/Documents/CS_3A/3eme%20trimestre/Sciences%20de%20l'humain%20et%20IA/sleep_exp/miketpa-main/Assets/Scripts/InteractionLogger.cs) enregistre un export CSV horodaté à la fin de la session.

### 10.1 Métriques exportées par tour

Le logger enregistre pour chaque tour :

- l'index du tour ;
- le rôle ;
- la longueur du message ;
- la présence d'une question ;
- le niveau estimé ;
- la posture courante ;
- le profil motivationnel ;
- les quatre SECs ;
- les longueurs moyennes utilisateur et agent ;
- les nombres de mots des derniers tours ;
- les durées orales estimées ;
- la durée maximale recommandée pour la prochaine réponse agent ;
- le ratio maximal Mike/utilisateur ;
- l'équilibre conversationnel ;
- le ratio agent/utilisateur ;
- l'horodatage.

### 10.2 Résumé agrégé

`GetEngagementSummary()` synthétise :

- le nombre total de tours utilisateur ;
- le nombre de questions utilisateur ;
- la longueur moyenne utilisateur ;
- la longueur moyenne agent ;
- les durées estimées des derniers tours ;
- le ratio de longueur agent/utilisateur ;
- la posture courante ;
- le profil motivationnel actif.

## 11. Synthèse fonctionnelle

Le code implémente un agent conversationnel spécialisé sur le sommeil, capable :

- d'adapter son cadrage motivationnel selon un profil Promotion ou Prévention ;
- d'estimer le niveau de connaissance de l'utilisateur ;
- d'ajuster la technicité et la posture interactionnelle au fil du dialogue ;
- de moduler ses réponses en fonction de la longueur relative des tours ;
- de limiter sa durée de parole pour conserver un rythme de conversation crédible ;
- de synchroniser texte, voix, expressions faciales et posture non verbale dans Unity.
