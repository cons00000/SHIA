# Dictionnaire des colonnes — CSV JASP 

---

## IDENTIFICATION

| Colonne | Type | Valeurs | Description |
|---|---|---|---|
| Participant | Texte | ex: Léa, Jules | Identifiant du participant |
| Profil_Agent | Texte | Promotion / Prevention | Profil motivationnel de l'agent |
| Profil_Utilisateur | Texte | Promotion / Prevention | **À remplir manuellement** d'après Q1 |
| Fit | Texte | Fit / Non-Fit / MANQUANT | Calculé via `--compute-fit` |

---

## COMPTEURS DE TOURS

| Colonne | Type | Description |
|---|---|---|
| Nb_Tours_total | Entier | Nombre total de tours de parole (User + Agent) |

---

## MÉTRIQUES UTILISATEUR — composantes de VD1

| Colonne | Type | Description |
|---|---|---|
| Longueur_User_moy | Décimal | Longueur moyenne des messages utilisateur (caractères) |
| Mots_User_moy | Décimal | Nombre moyen de mots par message utilisateur |
| Emo_Int_User_moy | Décimal [0–100] | Intensité émotionnelle moyenne (proxy engagement affectif) |
| Nb_Questions_Agent | Entier | Nombre de tours agent avec question |
| Balance_moy | Décimal [0–1] | Balance du dialogue (0.5 = équilibre parfait) |
| Duree_totale_sec | Décimal | Durée totale de la session (secondes) |



---

## SCORE COMPOSITE VD1

| Colonne | Type [0–1] | Description |
|---|---|---|
| VD1_Comportemental | Décimal | Moyenne normalisée de : Longueur_User_moy/500, Mots_User_moy/100, Duree_totale_sec/1800, Nb_Questions_Agent/Nb_Tours_Agent |

---

## MODÈLE CPM DE SCHERER

| Colonne | Type [0–1] | Description |
|---|---|---|
| Novelty_moy | Décimal | Nouveauté perçue du discours de l'agent |
| Complexity_moy | Décimal | Complexité du discours (proxy VI2) |
| Coping_moy | Décimal | Potentiel de maîtrise estimé de l'utilisateur |
| GoalRel_moy | Décimal | Pertinence des buts perçue par l'utilisateur |

---

## ALIGNEMENT COMPORTEMENTAL

| Colonne | Type [0–1] | Description |
|---|---|---|
| Posture_Match_Distance | Décimal | Distance entre distributions de postures agent et utilisateur. 0 = alignement parfait |



---

## FRÉQUENCES DE POSTURE


| Colonne | Type [0–1] | Description |
|---|---|---|
| Agent_Posture_Pedagogical | Décimal | Part des tours agent en posture pédagogique |
| Agent_Posture_Empathetic | Décimal | Part des tours agent en posture empathique |
| Agent_Posture_Enthusiastic | Décimal | Part des tours agent en posture enthousiaste |
| User_Posture_Pedagogical | Décimal | Part des tours utilisateur en posture pédagogique |
| User_Posture_Empathetic | Décimal | Part des tours utilisateur en posture empathique |
| User_Posture_Enthusiastic | Décimal | Part des tours utilisateur en posture enthousiaste |

---

## FRÉQUENCES DE KNOWLEDGE — AGENT & USER

> `Agent_Knowledge_Expert` et `User_Knowledge_Expert` supprimés (toujours à 0).

| Colonne | Type [0–1] | Description |
|---|---|---|
| User_Knowledge_Novice | Décimal | Part des tours utilisateur avec estimation Novice |
| User_Knowledge_Intermediate | Décimal | Part des tours utilisateur avec estimation Intermédiaire |
| Agent_Knowledge_Novice | Décimal | Part des tours où l'agent estime l'utilisateur Novice |
| Agent_Knowledge_Intermediate | Décimal | Part des tours où l'agent estime l'utilisateur Intermédiaire |

---

## QUESTIONNAIRE INITIAL Q1

| Colonne | Type | Description |
|---|---|---|
| Q1_Genre | Texte | Genre du participant |
| Q1_Age | Texte | Âge du participant |
| Q1_Score_Contexte_Sommeil | Décimal [0–1] | Score composite d'hygiène de sommeil à T0 — moyenne de 13 indicateurs normalisés (situation couchage, état psycho, stress, qualité sommeil T0, activité physique, écrans, caféine, alcool, médicaments, pathologies, température/luminosité/bruit chambre). 1 = contexte optimal |



---

## JOURNAL DE SOMMEIL Q2 — MÉTRIQUES T0 UNIQUEMENT

> **T1 supprimé** : seulement 4 participants sur 12 ont rempli le journal post-interaction, ce qui ne permet pas d'analyses comparatives T0/T1 robustes. Les colonnes `Q2_T1_*`, `Q2_Delta_*` et `VD4_Score_Bien_Etre_Delta` sont donc supprimées.

| Colonne | Description |
|---|---|
| Q2_Nb_nuits_total | Nombre total de nuits enregistrées (T0) |
| Q2_T0_Nb_nuits | Nombre de nuits T0 |
| Q2_T0_Heure_coucher_moy | Heure moyenne de coucher |
| Q2_T0_Heure_lever_moy | Heure moyenne de lever |
| Q2_T0_Duree_sommeil_moy | Durée moyenne de sommeil (heures) |
| Q2_T0_Qualite_sommeil_moy | Qualité subjective (1–5) |
| Q2_T0_Pct_nuits_avec_reveil | % de nuits avec réveil nocturne |
| Q2_T0_Duree_eveil_moy_min | Durée moyenne d'éveil nocturne (min) |
| Q2_T0_Humeur_reveil_moy | Humeur au réveil (1–5) |
| Q2_T0_Forme_physique_reveil_moy | Forme physique au réveil (1–5) |
| Q2_T0_Difficulte_endormissement_moy | Difficulté à s'endormir (1–5) |
| Q2_T0_Stress_soir_moy | Stress le soir (1–5) |
| Q2_T0_Alcool_soir_pct | % nuits avec alcool  |
| Q2_T0_Ecrans_soir_pct | % nuits avec écrans |
| Q2_T0_Sieste_pct | % jours avec sieste |
| Q2_T0_Cauchemars_pct | % nuits avec cauchemars |

---

## SCORE COMPOSITE VD3 — QUALITÉ DE SOMMEIL BASELINE

| Colonne | Type [0–1] | Description |
|---|---|---|
| VD3_Score_Sommeil_T0 | Décimal | Baseline de qualité de sommeil. Moyenne normalisée de : Qualite_sommeil_moy [1–5], Duree_sommeil_moy [4–9h], inverse(Difficulte_endormissement_moy) [1–5]. Utilisé comme covariable de contrôle ou comme VD3 si les données T1 sont collectées ultérieurement |


---

## QUESTIONNAIRE POST-INTERACTION Q3

| Colonne | Type | Description |
|---|---|---|
| Q3_Score_Interet_Engagement | Décimal [1–5] | Moyenne de 12 items. **VD1 déclaratif** |
| Q3_Score_Intention_Changement | Décimal [1–5] | Moyenne de 2 items. **VD2** |
| Q3_Score_Satisfaction_Globale | Décimal [1–5] | Moyenne de 4 items |
| Q3_Score_Engagement_Concret | Décimal [0–1] | Score composite délai de changement + accord journal T1. Formule : (délai/4 + accord/2) / 2 |


---

## CORRESPONDANCE VI / VD / HYPOTHÈSES

| Variable | Colonnes |
|---|---|
| **VI1** — Fit motivationnel | `Fit` |
| **VI2** — Complexité | `Complexity_moy`, `Agent_Knowledge_Novice`, `Agent_Knowledge_Intermediate` |
| **VD1** comportemental | `VD1_Comportemental`, `Longueur_User_moy`, `Mots_User_moy`, `Balance_moy`, `Duree_totale_sec` |
| **VD1** déclaratif | `Q3_Score_Interet_Engagement`, `Emo_Int_User_moy` |
| **VD2** | `Q3_Score_Intention_Changement`, `Q3_Score_Engagement_Concret` |
| **VD3** baseline sommeil | `VD3_Score_Sommeil_T0`, `Q2_T0_Qualite_sommeil_moy`, `Q2_T0_Duree_sommeil_moy` |
| **CPM** | `Novelty_moy`, `Complexity_moy`, `Coping_moy`, `GoalRel_moy` |
| **H7** posture → engagement | `Agent_Posture_Pedagogical/Empathetic/Enthusiastic` × `VD1_Comportemental` |
| **H8** contrôle nocturne | `Q2_T0_Sieste_pct`, `Q2_T0_Cauchemars_pct` (+ alcool/écrans si bug corrigé) |
| **Covariable** | `Q1_Score_Contexte_Sommeil` |

---
