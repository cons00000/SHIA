"""
md_to_jasp.py
=============
Transforme les fichiers de log .md (InteractionLogger Unity) en CSV JASP.
Fusionne les données des questionnaires Google Form :
  - Q1_questionnaire_initial.csv
  - Q2_journal_quotidien_de_sommeil.csv  (T0 avant 04/04 / T1 à partir du 04/04)
  - Q3_post_interaction.csv

UTILISATION :
    python cleaned_result_to_jasp_without_T1.py --input ./logs --output resultats_jasp.csv --q1 Q1_questionnaire_initial.csv --q2 Q2_journal_quotidien_de_sommeil.csv --q3 Q3_post_interaction.csv

    python cleaned_result_to_jasp_without_T1.py --compute-fit resultats_jasp_without_T1.csv
"""

import os
import re
import csv
import argparse
import unicodedata
from collections import Counter, defaultdict

# ─────────────────────────────────────────────
#  VALEURS CONNUES (log Unity)
# ─────────────────────────────────────────────

KNOWN_POSTURES = ['Neutral', 'Pedagogical', 'Empathetic', 'Enthusiastic']
KNOWN_KNOWLEDGE = ['Novice', 'Intermediate', 'Expert']

# ─────────────────────────────────────────────
#  TABLES DE CONVERSION Q1 → NUMÉRIQUE
#  Chaque table mappe une réponse texte normalisée à un entier ordinal.
#  La normalisation est faite via normalize_name() : minuscules, sans accent, alphanum.
# ─────────────────────────────────────────────

# Q1_Situation_couchage → Isolation (1=seul, 2=partenaire/coloc)
# Hypothèse : dormir seul = meilleur contrôle de l'environnement (moins de perturbations)
#   Seul(e)              → 1   (référence positive)
#   Avec un(e) partenaire → 2
#   Avec un colocataire  → 2
Q1_COUCHAGE_MAP = {
    'seule': 1, 'seul': 1,
    'avecunepartenaire': 2, 'avecunpartenaire': 2,
    'avecuncolocataire': 2,
}

# Q1_Activite_physique → score 0–4 (fréquence d'activité physique)
#   Jamais                  → 0
#   1 à 2 fois / semaine    → 1
#   3 à 4 fois / semaine    → 2
#   Tous les jours ou presque → 3
Q1_ACTIVITE_MAP = {
    'jamais': 0,
    '12foispar': 1, '1a2fois': 1,
    '34foispar': 2, '3a4fois': 2,
    'touslesjoursoupra': 3, 'touslesjours': 3,
}

# Q1_Temps_ecrans_soir → pénalité : plus d'écrans = score plus bas
#   Moins de 1h  → 4
#   1h – 2h      → 3
#   2h – 3h      → 2
#   3h – 4h      → 1
#   Plus de 4h / Plus de 6h → 0
Q1_ECRANS_MAP = {
    'moinsde1h': 4, 'moinsde': 4,
    '1h2h': 3, '1ha2h': 3,
    '2h3h': 2, '2ha3h': 2,
    '3h4h': 1, '3ha4h': 1,
    'plusde4h': 0, 'plusde6h': 0, 'plusde': 0,
}

# Q1_Cafeine → pénalité (nombre de prises / fréquence)
#   Jamais               → 3
#   Le matin uniquement  → 2
#   Le matin + après-midi avant 16h → 1
#   Le matin + après-midi avant 16h + après 16h → 0
# Règle : on compte le nombre de mentions horaires dans la réponse (séparées par des virgules)
# score = max(0, 3 - nb_occurrences_apres_midi)
# Implémenté via comptage des "/" dans la chaîne normalisée.
def _cafeine_score(raw: str) -> int:
    """Plus il y a de créneaux caféine mentionnés, plus le score est bas (0–3)."""
    raw_norm = normalize_name(raw)
    if 'jamais' in raw_norm:
        return 3
    # Compte les occurrences de créneaux (séparés par des virgules dans la réponse originale)
    count = raw.count(',') + 1 if raw.strip() else 0
    return max(0, 3 - (count - 1))  # 1 créneau=2, 2 créneaux=1, 3+ créneaux=0

# Q1_Alcool → pénalité fréquence (score 0–3)
#   Jamais                  → 3
#   Moins d'une fois/semaine → 2
#   1 à 3 fois/semaine      → 1
#   4 fois ou plus/semaine  → 0
Q1_ALCOOL_MAP = {
    'jamais': 3,
    'moinsdune': 2, 'moinsdunefoissemaine': 2, 'moinsdunefoisparsemaine': 2,
    '1a3fois': 1, '13foisparsemaine': 1,
    '4foisouplus': 0, '4foisouplusparsemaine': 0,
}

# Q1_Medicaments / Q1_Pathologies → binaire pénalité
#   Non → 0 (pas de pénalité)
#   Oui → 1 (pénalité)
# (inversé pour le score final : Non=1, Oui=0)

# Q1_Temperature_chambre → score 0–2
#   Plutôt froide (< 16°C)     → 0
#   Fraîche à tempérée (16–19°C) → 2  (optimal sommeil)
#   Tiède (19–22°C)             → 1
#   Chaude (> 22°C)             → 0
Q1_TEMP_MAP = {
    'plutotfroide': 0, 'froid': 0,
    'fraicheatemperee': 2, 'fraiche': 2,
    'tiede': 1,
    'chaude': 0,
}

# Q1_Luminosite_chambre → score 0–2 (plus sombre = mieux)
#   Très sombre (aucune lumière)           → 2
#   Légèrement éclairée (veilleuse, LED)   → 1
#   Assez lumineuse (volets ouverts…)      → 0
#   Très lumineuse                         → 0
Q1_LUMINOSITE_MAP = {
    'tressombre': 2,
    'legerementeclairee': 1, 'legerement': 1,
    'assezlumineuse': 0,
    'treslumineuse': 0,
}

# Q1_Bruit_chambre → score 0–2 (plus silencieux = mieux)
#   Très silencieux         → 2
#   Quelques bruits ponctuels → 1
#   Bruits réguliers modérés  → 0
#   Bruits forts/continus    → 0
Q1_BRUIT_MAP = {
    'tressilencieux': 2,
    'quelquesbruit': 1, 'quelquesponctuels': 1,
    'bruitsreguliers': 0, 'bruitsreguliersmoder': 0,
    'bruitsfortsoucont': 0,
}


def _map_score(raw: str, mapping: dict, field: str = '') -> int | None:
    """Cherche la clé du mapping dont le nom normalisé est contenu dans raw normalisé."""
    raw_norm = normalize_name(raw)
    if not raw_norm:
        return None
    for key, score in mapping.items():
        if normalize_name(key) in raw_norm:
            return score
    print(f"  [WARN] Q1 score non trouvé pour '{field}' : '{raw}'")
    return None


def compute_q1_contexte_score(entry: dict) -> float | None:
    """
    Score composite Q1_Score_Contexte_Sommeil ∈ [0, 1].

    Équation (toutes variables ramenées à [0, 1] avant moyennage) :

      score = mean([
        couchage_norm,          # Situation_couchage : Seul=1 → 1.0 ; Partenaire/coloc=2 → 0.5
        etat_psycho_norm,       # Etat_psycho_T0 : (valeur-1)/4       plage [1–5]
        stress_inv_norm,        # Stress_T0 : (5-valeur)/4             inversé
        qualite_norm,           # Qualite_sommeil_T0 : (valeur-1)/4   plage [1–5]
        activite_norm,          # Activite_physique : valeur/3         plage [0–3]
        ecrans_inv_norm,        # Temps_ecrans_soir : valeur/4         plage [0–4] (déjà pénalisé)
        cafeine_inv_norm,       # Cafeine : valeur/3                   plage [0–3]
        alcool_inv_norm,        # Alcool : valeur/3                    plage [0–3]
        medicaments_inv,        # Medicaments : Non→1, Oui→0
        pathologies_inv,        # Pathologies : Non→1, Oui→0
        temperature_norm,       # Temperature_chambre : valeur/2       plage [0–2]
        luminosite_norm,        # Luminosite_chambre : valeur/2        plage [0–2]
        bruit_norm,             # Bruit_chambre : valeur/2             plage [0–2]
      ])

    Un score proche de 1.0 = hygiène de sommeil optimale à T0.
    Un score proche de 0.0 = nombreux facteurs défavorables.
    """
    parts = []

    # Situation couchage (1 ou 2) → [0.5, 1.0]
    v = _map_score(entry.get('Q1_Situation_couchage', ''), Q1_COUCHAGE_MAP, 'couchage')
    if v is not None:
        parts.append((v - 1) / 1)  # 1→1.0, 2→0.5 — échelle max=1, min=1, pas=1

    # Etat psycho T0 (Likert 1–5)
    v = likert_to_int(entry.get('Q1_Etat_psycho_T0', ''), 'etat_psycho_T0')
    if v is not None:
        parts.append((v - 1) / 4)

    # Stress T0 (Likert 1–5, inversé : moins de stress = mieux)
    v = likert_to_int(entry.get('Q1_Stress_T0', ''), 'stress_T0')
    if v is not None:
        parts.append((5 - v) / 4)

    # Qualité sommeil T0 (Likert 1–5)
    v = likert_to_int(entry.get('Q1_Qualite_sommeil_T0', ''), 'qualite_sommeil_T0')
    if v is not None:
        parts.append((v - 1) / 4)

    # Activité physique (0–3)
    v = _map_score(entry.get('Q1_Activite_physique', ''), Q1_ACTIVITE_MAP, 'activite')
    if v is not None:
        parts.append(v / 3)

    # Écrans soir (0–4, déjà pénalisé donc plus haut = mieux)
    v = _map_score(entry.get('Q1_Temps_ecrans_soir', ''), Q1_ECRANS_MAP, 'ecrans')
    if v is not None:
        parts.append(v / 4)

    # Caféine (0–3)
    raw_caf = entry.get('Q1_Cafeine', '')
    if raw_caf:
        v = _cafeine_score(raw_caf)
        parts.append(v / 3)

    # Alcool (0–3)
    v = _map_score(entry.get('Q1_Alcool', ''), Q1_ALCOOL_MAP, 'alcool')
    if v is not None:
        parts.append(v / 3)

    # Médicaments : Non→1, Oui→0
    med = normalize_name(entry.get('Q1_Medicaments', ''))
    if med in ('non', 'oui'):
        parts.append(1.0 if med == 'non' else 0.0)

    # Pathologies : Non→1, Oui (légères ou lourdes) → 0
    path = normalize_name(entry.get('Q1_Pathologies', ''))
    if path:
        parts.append(1.0 if path == 'non' else 0.0)

    # Température chambre (0–2)
    v = _map_score(entry.get('Q1_Temperature_chambre', ''), Q1_TEMP_MAP, 'temperature')
    if v is not None:
        parts.append(v / 2)

    # Luminosité chambre (0–2)
    v = _map_score(entry.get('Q1_Luminosite_chambre', ''), Q1_LUMINOSITE_MAP, 'luminosite')
    if v is not None:
        parts.append(v / 2)

    # Bruit chambre (0–2)
    v = _map_score(entry.get('Q1_Bruit_chambre', ''), Q1_BRUIT_MAP, 'bruit')
    if v is not None:
        parts.append(v / 2)

    return round(sum(parts) / len(parts), 3) if parts else None


# ─────────────────────────────────────────────
#  TABLE DE CONVERSION Q3 → SCORE ENGAGEMENT CONCRET
#
#  Q3_Score_Engagement_Concret ∈ [0, 1]
#
#  Équation :
#    score = (delai_score / 4 + accord_score / 2) / 2
#
#  Composante 1 — Délai de changement (Q3_Delai_changements) → delai_score ∈ [0, 4]
#    "Dès ce soir"                                  → 4  (action immédiate)
#    "Dans les 2–3 prochains jours"                 → 3
#    "Dans la semaine"                              → 2
#    "Dans le mois"                                 → 1
#    "Je ne suis pas encore sûr(e) / certain(e)…"  → 0  (pas de délai précis)
#    "Pas de souci de sommeil"                      → 0  (hors-sujet)
#
#  Composante 2 — Accord journal T1 (Q3_Accord_journal_T1) → accord_score ∈ [0, 2]
#    "Oui, je m'engage à participer"                → 2  (engagement fort)
#    "Oui, si possible (au mieux de mes efforts)"   → 1  (engagement modéré)
#    "Non, je ne souhaite pas continuer"            → 0  (refus)
# ─────────────────────────────────────────────

Q3_DELAI_MAP = {
    'descement': 4, 'desdesoir': 4, 'cesoir': 4, 'desoir': 4, 'dessoir': 4,
    '23prochains': 3, '2a3': 3, '23jours': 3,
    'semaine': 2, 'dasemaine': 2,
    'mois': 1,
    'passur': 0, 'pasencore': 0, 'certain': 0, 'pasdesouci': 0, 'pasdeproblem': 0,
}

Q3_ACCORD_MAP = {
    'jengage': 2, 'mengageaparticiper': 2, 'oujemengage': 2,
    'sipo': 1, 'aumieuxdemeseffort': 1, 'sipossible': 1,
    'non': 0, 'jeneso': 0,
}


def compute_q3_engagement_score(delai_raw: str, accord_raw: str) -> float | None:
    """
    Score d'engagement concret post-interaction ∈ [0, 1].

    score = (delai_score/4 + accord_score/2) / 2

    Exemple :
      "Dès ce soir" + "Oui, je m'engage"  → (4/4 + 2/2) / 2 = (1.0 + 1.0) / 2 = 1.000
      "Dans la semaine" + "si possible"   → (2/4 + 1/2) / 2 = (0.5 + 0.5) / 2 = 0.500
      "Dans le mois" + "Non"              → (1/4 + 0/2) / 2 = (0.25 + 0.0) / 2 = 0.125
    """
    d_norm = normalize_name(delai_raw)
    a_norm = normalize_name(accord_raw)

    delai_score = None
    for key, score in Q3_DELAI_MAP.items():
        if normalize_name(key) in d_norm:
            delai_score = score
            break

    accord_score = None
    for key, score in Q3_ACCORD_MAP.items():
        if normalize_name(key) in a_norm:
            accord_score = score
            break

    if delai_score is None:
        print(f"  [WARN] Q3_Delai non reconnu : '{delai_raw}'")
    if accord_score is None:
        print(f"  [WARN] Q3_Accord non reconnu : '{accord_raw}'")

    if delai_score is None and accord_score is None:
        return None
    parts = []
    if delai_score is not None:
        parts.append(delai_score / 4)
    if accord_score is not None:
        parts.append(accord_score / 2)
    return round(sum(parts) / len(parts), 3)

# ─────────────────────────────────────────────
#  ENCODAGE GOOGLE FORM
# ─────────────────────────────────────────────

ENCODING_FIXES = [
    ('Ã©', 'é'), ('Ã¨', 'è'), ('Ãª', 'ê'), ('Ã«', 'ë'),
    ('Ã ', 'à'), ('Ã¢', 'â'), ('Ã¯', 'ï'), ('Ã®', 'î'),
    ('Ã´', 'ô'), ('Ã¹', 'ù'), ('Ã»', 'û'), ('Ã¼', 'ü'),
    ('Ã§', 'ç'), ('Ã‰', 'É'), ('Ã€', 'À'), ('Ã‡', 'Ç'),
    ('â€™', "'"), ('â€œ', '"'), ('â€', '"'),
    ('Å"', 'œ'), ('Å½', 'Ž'), ('Å¡', 'š'),
    ('Ã', 'à'),
]


def fix_encoding(text):
    if text is None:
        return ''
    if not isinstance(text, str):
        text = str(text)
    for bad, good in ENCODING_FIXES:
        text = text.replace(bad, good)
    return text


# ─────────────────────────────────────────────
#  NORMALISATION DES NOMS
# ─────────────────────────────────────────────

def normalize_name(name):
    """Minuscules + sans accents + alphanum uniquement."""
    name = fix_encoding(str(name)).strip().lower()
    name = unicodedata.normalize('NFD', name)
    name = ''.join(c for c in name if unicodedata.category(c) != 'Mn')
    return re.sub(r'[^a-z0-9]', '', name)


def extract_all_words(full_name):
    """Tous les mots normalisés d'un champ nom+prénom."""
    return [normalize_name(p) for p in fix_encoding(str(full_name)).strip().split() if p]


def normalize_full_name(full_name):
    """Clé de déduplication : tous les mots normalisés triés et joints."""
    words = sorted(extract_all_words(full_name))
    return '_'.join(words)


def match_name(participant_id, name_list):
    """
    Cherche participant_id parmi TOUS les mots de chaque entrée de name_list.
    Retourne le nom original si trouvé, None sinon.
    """
    pid_norm = normalize_name(participant_id)
    for orig in name_list:
        if pid_norm in extract_all_words(orig):
            return orig
    return None


# ─────────────────────────────────────────────
#  UTILITAIRES NUMÉRIQUES
# ─────────────────────────────────────────────

def to_float(value, default=0.0):
    try:
        return float(str(value).replace(',', '.').replace(' ', ''))
    except (ValueError, TypeError):
        return default


def parse_time_to_hours(time_str):
    """'23h00' / '23:00' / '23h' → float heures. None si non parseable."""
    raw = time_str
    time_str = fix_encoding(str(time_str)).strip().lower()
    if not time_str or time_str in ('', '-', 'n/a', 'na', 'none'):
        return None
    m = re.search(r'(\d{1,2})\s*[h:]\s*(\d{2})', time_str)
    if m:
        return round(int(m.group(1)) + int(m.group(2)) / 60, 3)
    m = re.search(r'(\d{1,2})\s*[h:]\s*$', time_str)
    if m:
        return float(int(m.group(1)))
    m = re.search(r'^\s*(\d{1,2})\s*$', time_str)
    if m:
        return float(int(m.group(1)))
    print(f"  [WARN] Horaire non reconnu : '{raw}' → None")
    return None


def parse_duration_hours(s):
    return parse_time_to_hours(s)


# Mapping texte → entier pour les colonnes à réponse verbale (Q2 stress)
VERBAL_SCALE = {
    'tres bas':   1,
    'bas':        2,
    'modere':     3,
    'eleve':      4,
    'tres eleve': 5,
    # variantes
    'faible':     2,
    'moyen':      3,
    'fort':       4,
    'nul':        1,
}


def likert_to_int(value, field_name='', default=None):
    """
    Extrait un entier depuis une réponse Likert.
    Gère :
      - '2 : Mauvaise'  → 2
      - 'Bas'           → 2  (via VERBAL_SCALE)
      - 'Très bas'      → 1
      - 'Élevé'         → 4
    Affiche un [WARN] si toujours non parseable.
    """
    raw = value
    val = fix_encoding(str(value)).strip()

    # Cas 1 : chiffre en début de chaîne
    m = re.match(r'^\s*(\d+)', val)
    if m:
        return int(m.group(1))

    # Cas 2 : chiffre seul n'importe où
    m = re.search(r'(\d+)', val)
    if m:
        return int(m.group(1))

    # Cas 3 : correspondance verbale
    val_norm = normalize_name(val)
    for key, score in VERBAL_SCALE.items():
        if normalize_name(key) == val_norm:
            return score
    # Correspondance partielle (ex: 'très bas' contient 'tres bas')
    for key, score in VERBAL_SCALE.items():
        if normalize_name(key) in val_norm or val_norm in normalize_name(key):
            return score

    if val.strip() not in ('', '-', 'n/a', 'na', 'none'):
        label = f' pour {field_name}' if field_name else ''
        print(f"  [WARN] Likert non reconnu{label} : '{raw}' → None")
    return default


def safe_mean(lst):
    lst = [x for x in lst if x is not None]
    return round(sum(lst) / len(lst), 3) if lst else None


# ─────────────────────────────────────────────
#  PARSING DATE (Horodateur Google Form)
# ─────────────────────────────────────────────

def parse_date_tuple(row):
    """Retourne (jour, mois, année) depuis Horodateur, ou None."""
    col = next((k for k in row if 'horodateur' in normalize_name(k)), None)
    if not col:
        return None
    m = re.match(r'(\d{2})/(\d{2})/(\d{4})', row.get(col, '').strip())
    return (int(m.group(1)), int(m.group(2)), int(m.group(3))) if m else None


def is_t1(row, cutoff=(4, 4, 2026)):
    """True si la date de la ligne >= cutoff (jour, mois, année)."""
    d = parse_date_tuple(row)
    if d is None:
        return False
    day, month, year = d
    cd, cm, cy = cutoff
    if year != cy:
        return year > cy
    if month != cm:
        return month > cm
    return day >= cd


# ─────────────────────────────────────────────
#  CHARGEMENT Q1
# ─────────────────────────────────────────────

def load_q1(filepath):
    if not filepath or not os.path.exists(filepath):
        print(f"  [Q1] Fichier non trouvé : {filepath}")
        return {}, {}

    data, name_map = {}, {}

    with open(filepath, encoding='utf-8-sig', newline='') as f:
        reader = csv.DictReader(f)
        for row in reader:
            row = {fix_encoding(k): fix_encoding(v) for k, v in row.items()}
            full_name_col = next(
                (k for k in row if 'nom' in k.lower() and 'pr' in k.lower()), None
            )
            if not full_name_col:
                continue
            full_name = fix_encoding(row.get(full_name_col, '').strip())
            if not full_name:
                continue

            key = normalize_full_name(full_name)
            name_map[full_name] = key

            entry = {
                # Identité / profil motivationnel (conservés comme colonnes)
                'Q1_Profil_Motivationnel': row.get('Profil motivationnel', '').strip(),
                'Q1_Genre':                row.get('A1. Quel est votre genre ?', '').strip(),
                'Q1_Age':                  row.get('A2.  Quel est votre âge ?  ', '').strip(),
                # Champs bruts pour le calcul de Q1_Score_Contexte_Sommeil (non exportés individuellement)
                'Q1_Situation_couchage':   row.get('A5. Avec qui dormez-vous habituellement ?', '').strip(),
                'Q1_Etat_psycho_T0':       row.get('E1. Comment évaluez-vous votre état psychologique général en ce moment ?', '').strip(),
                'Q1_Stress_T0':            row.get('E2. Quel est votre niveau de stress perçu ces deux dernières semaines ?', '').strip(),
                'Q1_Qualite_sommeil_T0':   row.get('E3. Comment évaluez-vous globalement la qualité de votre sommeil actuellement ?', '').strip(),
                'Q1_Activite_physique':    row.get('C1. À quelle fréquence pratiquez-vous une activité physique ?', '').strip(),
                'Q1_Temps_ecrans_soir':    row.get('C3. Combien d\'heures par jour passez-vous devant des écrans (téléphone, TV, ordinateur) le soir, après 19h ?', '').strip(),
                'Q1_Cafeine':              row.get('C5. Consommez-vous de la caféine (café, thé, boissons énergisantes) ?', '').strip(),
                'Q1_Alcool':               row.get('C7. Consommez-vous de l\'alcool ?', '').strip(),
                'Q1_Medicaments':          row.get('C8. Prenez-vous régulièrement des médicaments ou des compléments (somnifères, anxiolytiques, mélatonine…) ?', '').strip(),
                'Q1_Pathologies':          row.get('C11. Avez-vous des pathologies chroniques pouvant affecter le sommeil (apnée, douleurs chroniques, anxiété, dépression…) ?', '').strip(),
                'Q1_Temperature_chambre':  row.get('D1. Comment décririez-vous la température de votre chambre la nuit ?', '').strip(),
                'Q1_Luminosite_chambre':   row.get('D2. Comment décririez-vous la luminosité dans votre chambre pendant la nuit ?', '').strip(),
                'Q1_Bruit_chambre':        row.get('D3. Quel est le niveau sonore habituel dans votre chambre la nuit ?', '').strip(),
            }
            data[key] = entry

    print(f"  [Q1] {len(data)} participants chargés")
    for orig, k in name_map.items():
        print(f"    '{orig}' → clé: '{k}'")
    return data, name_map


def match_q1(participant_id, q1_data):
    pid = normalize_name(participant_id)
    for key in q1_data:
        if pid in key.split('_'):
            return key
    return None


# ─────────────────────────────────────────────
#  CHARGEMENT Q2
# ─────────────────────────────────────────────

def load_q2(filepath):
    if not filepath or not os.path.exists(filepath):
        print(f"  [Q2] Fichier non trouvé : {filepath}")
        return {}

    # Regroupement par clé normalisée (déduplication casse/accents)
    raw_by_key  = defaultdict(list)   # key_norm → liste de rows
    key_to_repr = {}                  # key_norm → nom original représentatif

    with open(filepath, encoding='utf-8-sig', newline='') as f:
        reader = csv.DictReader(f)
        for row in reader:
            row = {fix_encoding(k): fix_encoding(v) for k, v in row.items()}
            name_col = next(
                (k for k in row if 'nom' in k.lower() and 'pr' in k.lower()), None
            )
            if not name_col:
                continue
            full_name = fix_encoding(row.get(name_col, '').strip())
            if not full_name:
                continue
            key = normalize_full_name(full_name)
            raw_by_key[key].append(row)
            if key not in key_to_repr:
                key_to_repr[key] = full_name

    print(f"  [Q2] {len(raw_by_key)} participants distincts détectés :")
    for key, repr_name in key_to_repr.items():
        print(f"    clé '{key}' ← '{repr_name}' ({len(raw_by_key[key])} entrées)")

    data = {}

    def extract_q2_metrics(entries, label):
        """Calcule toutes les métriques Q2 pour un sous-ensemble d'entrées."""
        nb = len(entries)
        if nb == 0:
            return {
                f'Q2_{label}_Nb_nuits':                      0,
                f'Q2_{label}_Heure_coucher_moy':             None,
                f'Q2_{label}_Heure_reveil_moy':              None,
                f'Q2_{label}_Heure_lever_moy':               None,
                f'Q2_{label}_Duree_sommeil_moy':             None,
                f'Q2_{label}_Qualite_sommeil_moy':           None,
                f'Q2_{label}_Nb_reveils_moy':                None,
                f'Q2_{label}_Duree_eveil_moy_min':           None,
                f'Q2_{label}_Difficulte_rendormissement_moy': None,
                f'Q2_{label}_Humeur_reveil_moy':             None,
                f'Q2_{label}_Forme_physique_reveil_moy':     None,
                f'Q2_{label}_Difficulte_endormissement_moy': None,
                f'Q2_{label}_Alcool_soir_pct':               None,
                f'Q2_{label}_Ecrans_soir_pct':               None,
                f'Q2_{label}_Stress_soir_moy':               None,
                f'Q2_{label}_Sieste_pct':                    None,
                f'Q2_{label}_Cauchemars_pct':                None,
                f'Q2_{label}_Pct_nuits_avec_reveil':         None,
            }

        def col(r, keyword):
            """Cherche la valeur de la colonne contenant keyword (normalisé)."""
            kn = normalize_name(keyword)
            for k, v in r.items():
                if kn in normalize_name(k):
                    return v
            return ''

        def col_num(r, question_number):
            """
            Cherche la colonne par son numéro de question (préfixe).
            Ex: col_num(r, '1') → colonne '1.  Heure à laquelle...'
            Plus robuste que la recherche par mot-clé.
            """
            prefix = normalize_name(str(question_number))
            for k, v in r.items():
                kn = normalize_name(k)
                if kn.startswith(prefix) and len(kn) > len(prefix):
                    return v
            return ''

        # Q7 — réveils nocturnes (oui/non)
        reveils_raw = [col_num(r, '7') for r in entries]
        reveils_bin = [1 if 'oui' in str(v).lower() else 0 for v in reveils_raw]

        # Q8 — durée d'éveil en minutes
        durees_eveil = [likert_to_int(col_num(r, '8'), f'Q2_{label}_duree_eveil') for r in entries]

        # Q9 — difficulté à se rendormir
        rendormissement = [likert_to_int(col_num(r, '9'), f'Q2_{label}_rendormissement') for r in entries]

        # Q10 — qualité globale sommeil
        qualites = [likert_to_int(col_num(r, '10'), f'Q2_{label}_qualite') for r in entries]

        # Q11 — forme physique au réveil
        formes = [likert_to_int(col_num(r, '11'), f'Q2_{label}_forme') for r in entries]

        # Q12 — humeur au réveil
        humeurs = [likert_to_int(col_num(r, '12'), f'Q2_{label}_humeur') for r in entries]

        # Q13 — difficulté à s'endormir
        endormissement = [likert_to_int(col_num(r, '13'), f'Q2_{label}_endormissement') for r in entries]

        # Q14 — alcool le soir
        alcool_raw = [col_num(r, '14') for r in entries]
        # DEBUG : affiche la première valeur brute trouvée pour vérifier le mapping
        if entries and not any('oui' in str(v).lower() for v in alcool_raw):
            sample = alcool_raw[0] if alcool_raw else '(vide)'
            print(f"  [DEBUG Q2-{label}] Q14 alcool — valeur brute : '{sample}' "
                  f"(si inattendu, vérifier le numéro de question dans le CSV Q2)")
        alcool_bin = [1 if 'oui' in str(v).lower() else 0 for v in alcool_raw]

        # Q15 — écrans avant coucher
        ecrans_raw = [col_num(r, '15') for r in entries]
        if entries and not any('oui' in str(v).lower() for v in ecrans_raw):
            sample = ecrans_raw[0] if ecrans_raw else '(vide)'
            print(f"  [DEBUG Q2-{label}] Q15 écrans — valeur brute : '{sample}' "
                  f"(si inattendu, vérifier le numéro de question dans le CSV Q2)")
        ecrans_bin = [1 if 'oui' in str(v).lower() else 0 for v in ecrans_raw]

        # Q16 — stress/anxiété le soir
        stress = [likert_to_int(col_num(r, '16'), f'Q2_{label}_stress') for r in entries]

        # Q17 — sieste
        sieste_raw = [col_num(r, '17') for r in entries]
        sieste_bin = [1 if 'oui' in str(v).lower() else 0 for v in sieste_raw]

        # Q18 — cauchemars
        cauchemars_raw = [col_num(r, '18') for r in entries]
        cauchemars_bin = [1 if 'oui' in str(v).lower() else 0 for v in cauchemars_raw]

        # Horaires par numéro de question
        # Q1 : heure coucher / Q4 : heure réveil / Q5 : heure lever / Q6 : durée
        heures_coucher = [parse_time_to_hours(col_num(r, '1')) for r in entries]
        heures_reveil  = [parse_time_to_hours(col_num(r, '4')) for r in entries]
        heures_lever   = [parse_time_to_hours(col_num(r, '5')) for r in entries]
        durees         = [parse_duration_hours(col_num(r, '6')) for r in entries]

        return {
            f'Q2_{label}_Nb_nuits':                       nb,
            f'Q2_{label}_Heure_coucher_moy':              safe_mean(heures_coucher),
            f'Q2_{label}_Heure_reveil_moy':               safe_mean(heures_reveil),
            f'Q2_{label}_Heure_lever_moy':                safe_mean(heures_lever),
            f'Q2_{label}_Duree_sommeil_moy':              safe_mean(durees),
            f'Q2_{label}_Qualite_sommeil_moy':            safe_mean(qualites),
            f'Q2_{label}_Pct_nuits_avec_reveil':          round(sum(reveils_bin) / nb, 3),
            f'Q2_{label}_Duree_eveil_moy_min':            safe_mean(durees_eveil),
            f'Q2_{label}_Difficulte_rendormissement_moy': safe_mean(rendormissement),
            f'Q2_{label}_Humeur_reveil_moy':              safe_mean(humeurs),
            f'Q2_{label}_Forme_physique_reveil_moy':      safe_mean(formes),
            f'Q2_{label}_Difficulte_endormissement_moy':  safe_mean(endormissement),
            f'Q2_{label}_Alcool_soir_pct':                round(sum(alcool_bin) / nb, 3),
            f'Q2_{label}_Ecrans_soir_pct':                round(sum(ecrans_bin) / nb, 3),
            f'Q2_{label}_Stress_soir_moy':                safe_mean(stress),
            f'Q2_{label}_Sieste_pct':                     round(sum(sieste_bin) / nb, 3),
            f'Q2_{label}_Cauchemars_pct':                 round(sum(cauchemars_bin) / nb, 3),
        }

    for key, entries in raw_by_key.items():
        nb_total   = len(entries)
        entries_t0 = [r for r in entries if not is_t1(r)]
        entries_t1 = [r for r in entries if is_t1(r)]

        print(f"    '{key}' : {nb_total} nuits | T0:{len(entries_t0)} | T1:{len(entries_t1)}")

        m_t0 = extract_q2_metrics(entries_t0, 'T0')
        m_t1 = extract_q2_metrics(entries_t1, 'T1')

        def delta(suffix):
            v0 = m_t0.get(f'Q2_T0_{suffix}')
            v1 = m_t1.get(f'Q2_T1_{suffix}')
            return round(v1 - v0, 3) if (v0 is not None and v1 is not None) else None

        deltas = {
            'Q2_Delta_Duree_sommeil':              delta('Duree_sommeil_moy'),
            'Q2_Delta_Qualite_sommeil':            delta('Qualite_sommeil_moy'),
            'Q2_Delta_Humeur_reveil':              delta('Humeur_reveil_moy'),
            'Q2_Delta_Forme_physique':             delta('Forme_physique_reveil_moy'),
            'Q2_Delta_Difficulte_endormissement':  delta('Difficulte_endormissement_moy'),
            'Q2_Delta_Stress_soir':                delta('Stress_soir_moy'),
            'Q2_Delta_Pct_nuits_avec_reveil':      delta('Pct_nuits_avec_reveil'),
        }

        entry = {'Q2_Nb_nuits_total': nb_total}
        entry.update(m_t0)
        entry.update(m_t1)
        entry.update(deltas)
        data[key] = entry

    print(f"  [Q2] Agrégation terminée — {len(data)} participants")
    return data


def match_q2(participant_id, q2_data):
    pid = normalize_name(participant_id)
    for key in q2_data:
        if pid in key.split('_'):
            return key
    return None


# ─────────────────────────────────────────────
#  CHARGEMENT Q3
#  Scores agrégés (pas de colonne par question)
#
#  Score_Interet_Engagement (Q1–Q12) :
#    Moyenne des 12 items mesurant l'intérêt situationnel et l'engagement
#    pendant l'interaction. Correspond à VD1 déclaratif.
#    Items : conversation stimulante, envie d'en savoir plus, aspects nouveaux,
#    concerné personnellement, aurait voulu plus long, crédibilité, adaptation
#    niveau, conseils réalistes, mise à l'aise, questions spontanées,
#    difficile de décrocher, sentiment de compétence.
#
#  Score_Intention_Changement (force de l'intention) :
#    Moyenne de 2 items VD2 : certitude de mise en place + effet positif estimé.
#
#  Score_Satisfaction_Globale :
#    Moyenne de 4 items : appréciation échange, recommandation, aide au
#    changement d'habitudes, satisfaction globale.
# ─────────────────────────────────────────────

# Mots-clés pour retrouver chaque item dans les en-têtes encodés de Q3
Q3_ITEMS_INTERET = [
    'conversation',       # J'ai trouvé la conversation...
    'envie d',            # envie d'en savoir plus
    'aspects du',         # aspects du sommeil
    'concern',            # concerné(e) personnellement
    'dure plus',          # aurait voulu plus longtemps
    'cr',                 # crédible
    'adapt',              # adapté ses explications
    'alistes et appl',    # réalistes et applicables
    'l aise',             # mis(e) à l'aise
    'pos spontan',        # posé spontanément
    'difficile de d',     # difficile de décrocher
    'comp',               # compétent
]

Q3_ITEMS_INTENTION = [
    'certain',            # certain(e) de mettre en place
    'effet positif',      # effet positif sur sommeil
]

Q3_ITEMS_SATISFACTION = [
    'appr',               # apprécié l'échange
    'recommanderait',     # recommanderait
    'changer mes hab',    # changer mes habitudes
    'satisfaction',       # satisfaction globale
]

# Colonnes texte libre utilisées UNIQUEMENT pour calculer Q3_Score_Engagement_Concret.
# Elles ne sont PAS exportées comme colonnes individuelles dans le CSV final.
Q3_TEXT_COLS = {}   # export vide — le chargement se fait via Q3_INTERNAL_COLS ci-dessous

# Clés internes chargées dans q3_entry mais non exportées directement
Q3_INTERNAL_COLS = {
    'Q3_Delai_changements': 'quel d',
    'Q3_Accord_journal_T1': 'acceptez-vous de remplir',
}


def find_col_value(row, keyword):
    """Cherche la valeur de la première colonne dont le nom normalisé contient keyword."""
    kn = normalize_name(keyword)
    for k, v in row.items():
        if kn in normalize_name(k):
            return v.strip() if v else ''
    return ''


def score_items(row, keywords, label):
    """
    Calcule la moyenne des items correspondant aux keywords.
    Affiche un warning si un item est manquant ou non parseable.
    """
    values = []
    for kw in keywords:
        raw = find_col_value(row, kw)
        parsed = likert_to_int(raw, f'{label}[{kw[:15]}]')
        if parsed is not None:
            values.append(parsed)
    return safe_mean(values)


def load_q3(filepath):
    if not filepath or not os.path.exists(filepath):
        print(f"  [Q3] Fichier non trouvé : {filepath}")
        return {}

    # Déduplication par clé normalisée (même logique que Q2)
    raw_by_key  = defaultdict(list)
    key_to_repr = {}

    with open(filepath, encoding='utf-8-sig', newline='') as f:
        reader = csv.DictReader(f)
        for row in reader:
            row = {fix_encoding(k): fix_encoding(v) for k, v in row.items()}

            # Q3 a deux colonnes séparées NOM et Prénom
            nom_col    = next((k for k in row if re.search(r'^nom', normalize_name(k))), None)
            prenom_col = next((k for k in row if 'prenom' in normalize_name(k)), None)

            nom    = fix_encoding(row.get(nom_col,    '').strip()) if nom_col    else ''
            prenom = fix_encoding(row.get(prenom_col, '').strip()) if prenom_col else ''
            full_name = f"{nom} {prenom}".strip() if nom else prenom
            if not full_name:
                continue

            key = normalize_full_name(full_name)
            raw_by_key[key].append(row)
            if key not in key_to_repr:
                key_to_repr[key] = full_name

    data = {}

    for key, entries in raw_by_key.items():
        # En cas de plusieurs soumissions, on prend la dernière
        row = entries[-1]
        if len(entries) > 1:
            print(f"  [Q3] '{key}' : {len(entries)} soumissions → dernière retenue")

        entry = {
            # VD1 — Score intérêt/engagement (12 items, moyenne)
            'Q3_Score_Interet_Engagement': score_items(row, Q3_ITEMS_INTERET, 'Q3_interet'),

            # VD2 — Score force intention de changement (2 items)
            'Q3_Score_Intention_Changement': score_items(row, Q3_ITEMS_INTENTION, 'Q3_intention'),

            # Satisfaction globale (4 items)
            'Q3_Score_Satisfaction_Globale': score_items(row, Q3_ITEMS_SATISFACTION, 'Q3_satisfaction'),
        }

        # Textes libres exportés (vide actuellement)
        for col_key, keyword in Q3_TEXT_COLS.items():
            entry[col_key] = find_col_value(row, keyword)

        # Colonnes internes pour calcul de Q3_Score_Engagement_Concret (non exportées)
        for col_key, keyword in Q3_INTERNAL_COLS.items():
            entry[col_key] = find_col_value(row, keyword)

        data[key] = entry

    print(f"  [Q3] {len(data)} participants chargés")
    for orig, k in key_to_repr.items():
        print(f"    '{orig}' → clé: '{normalize_full_name(orig)}'")
    return data


def match_q3(participant_id, q3_data):
    pid = normalize_name(participant_id)
    for key in q3_data:
        if pid in key.split('_'):
            return key
    return None


# ─────────────────────────────────────────────
#  VALIDATION DES VALEURS (log Unity)
# ─────────────────────────────────────────────

def validate_value(value, known_list, field_name, filename, turn_index):
    if value and value not in known_list:
        raise ValueError(
            f"\n  VALEUR INCONNUE — {field_name}\n"
            f"  Fichier : {filename} | Tour : {turn_index}\n"
            f"  Valeur  : '{value}'\n"
            f"  → Ajoute '{value}' dans {field_name} en haut du script."
        )


def compute_frequencies(values, known_list, field_name, filename):
    total  = len(values)
    counts = Counter(values)
    for val in counts:
        if val:
            validate_value(val, known_list, field_name, filename, '(agrégé)')
    return {
        f'Pct_{k}': round(counts.get(k, 0) / total, 3) if total > 0 else 0.0
        for k in known_list
    }


# ─────────────────────────────────────────────
#  PARSING D'UN FICHIER .MD
# ─────────────────────────────────────────────

def parse_md_file(filepath):
    filename       = os.path.basename(filepath)
    m              = re.search(r'interaction_(\w+)_\d{8}_\d{6}', filename)
    participant_id = m.group(1) if m else filename.replace('.md', '')
    rows           = []
    agent_profile  = None

    with open(filepath, encoding='utf-8') as f:
        lines = f.readlines()

    header_line = header_idx = None
    for i, line in enumerate(lines):
        if line.strip().startswith('| Turn'):
            header_line, header_idx = line, i
            break

    if header_line is None:
        print(f"  [WARN] Aucun tableau dans {filename}")
        return participant_id, None, []

    headers    = [h.strip() for h in header_line.strip().split('|') if h.strip()]
    data_start = header_idx + 2

    for line in lines[data_start:]:
        line = line.strip()
        if not line.startswith('|'):
            break
        values = [v.strip() for v in line.split('|') if v.strip() != '']
        if len(values) != len(headers):
            continue
        row = dict(zip(headers, values))
        rows.append(row)
        if agent_profile is None and 'Profile' in row:
            agent_profile = row['Profile']

    return participant_id, agent_profile, rows


# ─────────────────────────────────────────────
#  AGRÉGATION D'UNE SESSION
# ─────────────────────────────────────────────

def aggregate_session(participant_id, agent_profile, rows, filename,
                      q1_data, q2_data, q3_data):

    user_rows  = [r for r in rows if r.get('Role', '') == 'User']
    agent_rows = [r for r in rows if r.get('Role', '') == 'Agent']

    def smean(lst):
        return round(sum(lst) / len(lst), 3) if lst else 0.0

    user_lengths  = [to_float(r.get('Length', 0))     for r in user_rows]
    user_emo      = [to_float(r.get('Emo. Int.', -1)) for r in user_rows
                     if r.get('Emo. Int.', '-') != '-']
    user_words    = [to_float(r.get('Last Usr W.', 0)) for r in user_rows]
    agent_lengths = [to_float(r.get('Length', 0))     for r in agent_rows]
    agent_emo     = [to_float(r.get('Emo. Int.', -1)) for r in agent_rows
                     if r.get('Emo. Int.', '-') != '-']

    nb_q_agent  = sum(1 for r in agent_rows if r.get('?', 'False') == 'True')
    pct_q_agent = round(nb_q_agent / len(agent_rows) * 100, 1) if agent_rows else 0.0

    novelty    = smean([to_float(r.get('Novelty',   0)) for r in rows])
    complexity = smean([to_float(r.get('Complex',   0)) for r in rows])
    coping     = smean([to_float(r.get('Coping',    0)) for r in rows])
    goal_rel   = smean([to_float(r.get('Goal Rel.', 0)) for r in rows])
    balance    = smean([to_float(r.get('Bal.',  0)) for r in rows])
    ratio      = smean([to_float(r.get('Ratio', 0)) for r in rows])
    times      = [to_float(r.get('Time(s)', 0)) for r in rows]
    duree      = round(max(times) - min(times), 2) if len(times) > 1 else 0.0

    # Posture
    for r in rows:
        pv = r.get('Posture', '')
        if pv:
            validate_value(pv, KNOWN_POSTURES, 'KNOWN_POSTURES', filename, r.get('Turn', '?'))

    def pfreq(subset):
        vals = [r.get('Posture', '') for r in subset if r.get('Posture', '')]
        return compute_frequencies(vals, KNOWN_POSTURES, 'KNOWN_POSTURES', filename)

    ap = pfreq(agent_rows)
    up = pfreq(user_rows)
    apc = {f'Agent_Posture_{k.replace("Pct_","")}': v for k, v in ap.items()}
    upc = {f'User_Posture_{k.replace("Pct_","")}' : v for k, v in up.items()}
    pdist = round(sum(abs(apc.get(f'Agent_Posture_{p}', 0) - upc.get(f'User_Posture_{p}', 0))
                      for p in KNOWN_POSTURES) / 2, 3)

    # Knowledge
    for r in rows:
        kv = r.get('Knowledge', '')
        if kv:
            validate_value(kv, KNOWN_KNOWLEDGE, 'KNOWN_KNOWLEDGE', filename, r.get('Turn', '?'))

    def kfreq(subset):
        vals = [r.get('Knowledge', '') for r in subset if r.get('Knowledge', '')]
        return compute_frequencies(vals, KNOWN_KNOWLEDGE, 'KNOWN_KNOWLEDGE', filename)

    ak = kfreq(agent_rows)
    uk = kfreq(user_rows)
    akc = {f'Agent_Knowledge_{k.replace("Pct_","")}': v for k, v in ak.items()}
    ukc = {f'User_Knowledge_{k.replace("Pct_","")}' : v for k, v in uk.items()}
    kdist = round(sum(abs(akc.get(f'Agent_Knowledge_{k}', 0) - ukc.get(f'User_Knowledge_{k}', 0))
                      for k in KNOWN_KNOWLEDGE) / 2, 3)

    # Fusion questionnaires
    pid_norm = normalize_name(participant_id)

    q1_key   = match_q1(participant_id, q1_data)
    q1_entry = q1_data.get(q1_key, {})
    if q1_data and not q1_key:
        print(f"  [Q1] ✗ Pas de match pour '{participant_id}' ({pid_norm})")
        print(f"       Clés disponibles : {list(q1_data.keys())}")

    q2_key   = match_q2(participant_id, q2_data)
    q2_entry = q2_data.get(q2_key, {})
    if q2_data and not q2_key:
        print(f"  [Q2] ✗ Pas de match pour '{participant_id}' ({pid_norm})")
        print(f"       Clés disponibles : {list(q2_data.keys())}")

    q3_key   = match_q3(participant_id, q3_data)
    q3_entry = q3_data.get(q3_key, {})
    if q3_data and not q3_key:
        print(f"  [Q3] ✗ Pas de match pour '{participant_id}' ({pid_norm})")
        print(f"       Clés disponibles : {list(q3_data.keys())}")

    # ── Scores composites ──────────────────────────────────────────────
    # VD1_Comportemental : engagement comportemental normalisé (z-scores approchés)
    # On normalise chaque indicateur sur [0,1] à partir des valeurs observées,
    # puis on fait la moyenne. En pratique avec N petit, on stocke la moyenne
    # brute pondérée et on laissera JASP calculer le score composite final.
    # Ici on construit juste la colonne somme / 4 pour avoir un proxy.
    def _norm(val, lo, hi):
        """Min-max sur plages attendues."""
        if val is None or hi == lo:
            return None
        return max(0.0, min(1.0, (val - lo) / (hi - lo)))

    vd1_parts = [
        _norm(smean(user_lengths), 0, 500),
        _norm(smean(user_words),   0, 100),
        _norm(duree,               0, 1800),
        _norm(nb_q_agent / max(len(agent_rows), 1), 0, 1),
    ]
    vd1_vals = [x for x in vd1_parts if x is not None]
    vd1_composite = round(sum(vd1_vals) / len(vd1_vals), 3) if vd1_vals else None

    # Construction ligne finale
    result = {
        'Participant':              participant_id,
        'Profil_Agent':             agent_profile or 'Unknown',
        'Profil_Utilisateur':       q1_entry.get('Q1_Profil_Motivationnel', ''),
        'Fit':                      '',
        'Nb_Tours_total':           len(rows),
        # VD1 — engagement comportemental
        'Longueur_User_moy':        smean(user_lengths),
        'Mots_User_moy':            smean(user_words),
        'Emo_Int_User_moy':         smean(user_emo),
        'Nb_Questions_Agent':       nb_q_agent,
        'Balance_moy':              balance,
        'Duree_totale_sec':         duree,
        'VD1_Comportemental':       vd1_composite,
        # Modèle CPM
        'Novelty_moy':              novelty,
        'Complexity_moy':           complexity,
        'Coping_moy':               coping,
        'GoalRel_moy':              goal_rel,
        # Alignement
        'Posture_Match_Distance':   pdist,
    }

    # Postures : on exclut Neutral (référence), on garde Pedagogical/Empathetic/Enthusiastic
    apc_filtered = {k: v for k, v in apc.items() if 'Neutral' not in k}
    upc_filtered = {k: v for k, v in upc.items() if 'Neutral' not in k}
    result.update(apc_filtered)
    result.update(upc_filtered)
    # Knowledge : on exclut Expert (toujours à 0)
    akc_filtered = {k: v for k, v in akc.items() if 'Expert' not in k}
    ukc_filtered = {k: v for k, v in ukc.items() if 'Expert' not in k}
    result.update(akc_filtered)
    result.update(ukc_filtered)

    # Q1 — colonnes exportées : Genre, Age, et score composite contexte sommeil
    result['Q1_Genre'] = q1_entry.get('Q1_Genre', '')
    result['Q1_Age']   = q1_entry.get('Q1_Age',   '')
    result['Q1_Score_Contexte_Sommeil'] = compute_q1_contexte_score(q1_entry)

    # Q2 — uniquement T0 (données T1 insuffisantes, supprimées)
    result['Q2_Nb_nuits_total'] = q2_entry.get('Q2_Nb_nuits_total', '')
    for suffix in ['Nb_nuits', 'Heure_coucher_moy', 'Heure_lever_moy',
                   'Duree_sommeil_moy', 'Qualite_sommeil_moy',
                   'Pct_nuits_avec_reveil', 'Duree_eveil_moy_min',
                   'Humeur_reveil_moy', 'Forme_physique_reveil_moy',
                   'Difficulte_endormissement_moy', 'Alcool_soir_pct',
                   'Ecrans_soir_pct', 'Stress_soir_moy', 'Sieste_pct',
                   'Cauchemars_pct']:
        result[f'Q2_T0_{suffix}'] = q2_entry.get(f'Q2_T0_{suffix}', '')

    # VD3 score synthétique qualité de sommeil — T0 uniquement (T1 insuffisant)
    def _q2_float(key):
        v = q2_entry.get(key)
        try:
            return float(str(v).replace(',', '.')) if v not in ('', None) else None
        except (ValueError, TypeError):
            return None

    def _q2_score_sommeil(label):
        """Score composite sommeil : qualité + durée normalisée + inverse(difficulté endormissement)."""
        qualite = _q2_float(f'Q2_{label}_Qualite_sommeil_moy')
        duree   = _q2_float(f'Q2_{label}_Duree_sommeil_moy')
        diff    = _q2_float(f'Q2_{label}_Difficulte_endormissement_moy')
        parts = []
        if qualite is not None:
            parts.append(_norm(qualite, 1, 5))
        if duree is not None:
            parts.append(_norm(duree, 4, 9))
        if diff is not None:
            parts.append(_norm(5 - diff + 1, 1, 5))
        return round(sum(parts) / len(parts), 3) if parts else None

    result['VD3_Score_Sommeil_T0'] = _q2_score_sommeil('T0')

    # Q3 — scores agrégés + score engagement concret
    for k in ['Q3_Score_Interet_Engagement', 'Q3_Score_Intention_Changement',
              'Q3_Score_Satisfaction_Globale'] + list(Q3_TEXT_COLS.keys()):
        result[k] = q3_entry.get(k, '')

    # Score composite engagement concret (délai + accord journal T1)
    delai_raw  = q3_entry.get('Q3_Delai_changements', '')
    accord_raw = q3_entry.get('Q3_Accord_journal_T1', '')
    result['Q3_Score_Engagement_Concret'] = compute_q3_engagement_score(delai_raw, accord_raw)

    return result


# ─────────────────────────────────────────────
#  TRAITEMENT DE TOUS LES FICHIERS .MD
# ─────────────────────────────────────────────

def process_all_files(input_dir, output_csv, q1_path, q2_path, q3_path):
    md_files = sorted([
        os.path.join(input_dir, f)
        for f in os.listdir(input_dir)
        if f.endswith('.md')
    ])

    if not md_files:
        print(f"Aucun fichier .md trouvé dans : {input_dir}")
        return

    print(f"\nChargement des questionnaires...")
    q1_data, _ = load_q1(q1_path)
    q2_data    = load_q2(q2_path)
    q3_data    = load_q3(q3_path)

    print(f"\nTraitement des logs Unity...")
    results, errors = [], []

    for filepath in md_files:
        fname = os.path.basename(filepath)
        print(f"\n  Traitement : {fname}")
        participant_id, agent_profile, rows = parse_md_file(filepath)

        if not rows:
            print(f"    → Ignoré (aucune donnée)")
            continue

        try:
            row = aggregate_session(participant_id, agent_profile, rows, fname,
                                    q1_data, q2_data, q3_data)
            results.append(row)
            q1_ok = '✓' if row.get('Q1_Genre') else '✗'
            q2_ok = '✓' if row.get('Q2_Nb_nuits_total') else '✗'
            q3_ok = '✓' if row.get('Q3_Score_Interet_Engagement') != '' else '✗'
            print(f"    → OK | {len(rows)} tours | Q1:{q1_ok} Q2:{q2_ok} Q3:{q3_ok}")
        except ValueError as e:
            print(str(e))
            errors.append(fname)

    if errors:
        print(f"\n{'='*60}")
        for e in errors:
            print(f"    - {e}")
        print(f"{'='*60}")
        return

    if not results:
        print("Aucun résultat à exporter.")
        return

    fieldnames = list(results[0].keys())
    with open(output_csv, 'w', newline='', encoding='utf-8-sig') as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames, delimiter=';')
        writer.writeheader()
        writer.writerows(results)

    print(f"\n✓ {output_csv} — {len(results)} participants | {len(fieldnames)} colonnes")
    print("\nScores composites générés :")
    print("  VD1_Comportemental            → score d'engagement comportemental [0-1]")
    print("  VD3_Score_Sommeil_T0/T1       → score synthétique qualité sommeil [0-1]")
    print("  VD4_Score_Bien_Etre_Delta      → delta bien-être global (5 composantes)")
    print("  Q3_Score_Interet_Engagement   → moy. 12 items VD1 déclaratif (1-5)")
    print("  Q3_Score_Intention_Changement → moy. 2 items VD2 (certitude + effet)")
    print("  Q3_Score_Satisfaction_Globale → moy. 4 items satisfaction")
    print("\nÉTAPES SUIVANTES :")
    print("  1. Vérifie Profil_Utilisateur (auto-rempli depuis Q1)")
    print("  2. python md_to_jasp.py --compute-fit resultats_jasp.csv")


# ─────────────────────────────────────────────
#  CALCUL DU FIT
# ─────────────────────────────────────────────

def compute_fit(csv_path):
    rows = []
    with open(csv_path, encoding='utf-8-sig') as f:
        reader     = csv.DictReader(f, delimiter=';')
        fieldnames = reader.fieldnames
        for row in reader:
            user_p  = normalize_name(row.get('Profil_Utilisateur', ''))
            agent_p = normalize_name(row.get('Profil_Agent', ''))
            if not user_p:
                row['Fit'] = 'MANQUANT'
            elif user_p == agent_p:
                row['Fit'] = 'Fit'
            else:
                row['Fit'] = 'Non-Fit'
            rows.append(row)

    with open(csv_path, 'w', newline='', encoding='utf-8-sig') as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames, delimiter=';')
        writer.writeheader()
        writer.writerows(rows)

    fit     = sum(1 for r in rows if r['Fit'] == 'Fit')
    nonfit  = sum(1 for r in rows if r['Fit'] == 'Non-Fit')
    missing = sum(1 for r in rows if r['Fit'] == 'MANQUANT')
    print(f"✓ Fit calculé | Fit:{fit} | Non-Fit:{nonfit} | Manquant:{missing}")


# ─────────────────────────────────────────────
#  POINT D'ENTRÉE
# ─────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--input',  '-i', default='./logs')
    parser.add_argument('--output', '-o', default='resultats_jasp.csv')
    parser.add_argument('--q1', default='Q1_questionnaire_initial.csv')
    parser.add_argument('--q2', default='Q2_journal_quotidien_de_sommeil.csv')
    parser.add_argument('--q3', default='Q3_questionnaire_post_interaction.csv')
    parser.add_argument('--compute-fit', metavar='CSV_PATH')
    args = parser.parse_args()

    if args.compute_fit:
        compute_fit(args.compute_fit)
    else:
        process_all_files(args.input, args.output, args.q1, args.q2, args.q3)


if __name__ == '__main__':
    main()