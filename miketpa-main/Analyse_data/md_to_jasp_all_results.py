"""
md_to_jasp.py
=============
Transforme les fichiers de log .md (InteractionLogger Unity) en CSV JASP.
Fusionne les données des questionnaires Google Form :
  - Q1_questionnaire_initial.csv
  - Q2_journal_quotidien_de_sommeil.csv  (T0 avant 04/04 / T1 à partir du 04/04)
  - Q3_post_interaction.csv

UTILISATION :
    python md_to_jasp_all_results.py --input ./logs --output resultats_jasp.csv
                         --q1 Q1_questionnaire_initial.csv
                         --q2 Q2_journal_quotidien_de_sommeil.csv
                         --q3 Q3_post_interaction.csv

    python md_to_jasp_all_results.py --compute-fit results_before_cleaning.csv
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
                'Q1_Profil_Motivationnel': row.get('Profil motivationnel', '').strip(),
                'Q1_Score_Promotion':      row.get('Score promotion', '').strip(),
                'Q1_Score_Prevention':     row.get('Score prévention', '').strip(),
                'Q1_Genre':                row.get('A1. Quel est votre genre ?', '').strip(),
                'Q1_Age':                  row.get('A2.  Quel est votre âge ?  ', '').strip(),
                'Q1_Niveau_etudes':        row.get('A3. Quel est votre niveau d\'études le plus élevé ?', '').strip(),
                'Q1_CSP':                  row.get('A4. Quelle est votre catégorie socio-professionnelle ?', '').strip(),
                'Q1_Situation_couchage':   row.get('A5. Avec qui dormez-vous habituellement ?', '').strip(),
                'Q1_Heure_coucher_hab':    row.get('A6.  Heure habituelle de coucher :   (ex : 23h00)', '').strip(),
                'Q1_Heure_lever_hab':      row.get('A7.  Heure habituelle de réveil :   (ex : 07h00)', '').strip(),
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
        alcool_bin = [1 if 'oui' in str(v).lower() else 0 for v in alcool_raw]

        # Q15 — écrans avant coucher
        ecrans_raw = [col_num(r, '15') for r in entries]
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

# Colonnes texte libre conservées telles quelles
Q3_TEXT_COLS = {
    'Q3_Changements_envisages':  'changements concrets',
    'Q3_Delai_changements':      'quel d',
    'Q3_Changement_specifique':  'changement sp',
    'Q3_Plus_interesse':         'plus int',
    'Q3_Moins_convaincu':        'moins convaincu',
    'Q3_Accord_journal_T1':      'acceptez-vous de remplir',
    'Q3_Motivation_continuer':   'motivation',
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

        # Textes libres
        for col_key, keyword in Q3_TEXT_COLS.items():
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

    # Construction ligne finale
    result = {
        'Participant':              participant_id,
        'Profil_Agent':             agent_profile or 'Unknown',
        'Profil_Utilisateur':       q1_entry.get('Q1_Profil_Motivationnel', ''),
        'Fit':                      '',
        'Nb_Tours_total':           len(rows),
        'Nb_Tours_User':            len(user_rows),
        'Nb_Tours_Agent':           len(agent_rows),
        'Longueur_User_moy':        smean(user_lengths),
        'Mots_User_moy':            smean(user_words),
        'Emo_Int_User_moy':         smean(user_emo),
        'Longueur_Agent_moy':       smean(agent_lengths),
        'Emo_Int_Agent_moy':        smean(agent_emo),
        'Nb_Questions_Agent':       nb_q_agent,
        'Pct_Questions_Agent':      pct_q_agent,
        'Novelty_moy':              novelty,
        'Complexity_moy':           complexity,
        'Coping_moy':               coping,
        'GoalRel_moy':              goal_rel,
        'Balance_moy':              balance,
        'Ratio_AgentUser_moy':      ratio,
        'Duree_totale_sec':         duree,
        'Posture_Match_Distance':   pdist,
        'Knowledge_Match_Distance': kdist,
    }

    result.update(apc)
    result.update(upc)
    result.update(akc)
    result.update(ukc)

    # Q1
    for k in ['Q1_Score_Promotion', 'Q1_Score_Prevention', 'Q1_Genre', 'Q1_Age',
              'Q1_Niveau_etudes', 'Q1_CSP', 'Q1_Situation_couchage',
              'Q1_Heure_coucher_hab', 'Q1_Heure_lever_hab', 'Q1_Etat_psycho_T0',
              'Q1_Stress_T0', 'Q1_Qualite_sommeil_T0', 'Q1_Activite_physique',
              'Q1_Temps_ecrans_soir', 'Q1_Cafeine', 'Q1_Alcool', 'Q1_Medicaments',
              'Q1_Pathologies', 'Q1_Temperature_chambre', 'Q1_Luminosite_chambre',
              'Q1_Bruit_chambre']:
        result[k] = q1_entry.get(k, '')

    # Q2 — toutes les colonnes générées dynamiquement
    result['Q2_Nb_nuits_total'] = q2_entry.get('Q2_Nb_nuits_total', '')
    for label in ('T0', 'T1'):
        for suffix in ['Nb_nuits', 'Heure_coucher_moy', 'Heure_reveil_moy', 'Heure_lever_moy',
                       'Duree_sommeil_moy', 'Qualite_sommeil_moy',
                       'Pct_nuits_avec_reveil', 'Duree_eveil_moy_min',
                       'Difficulte_rendormissement_moy', 'Humeur_reveil_moy',
                       'Forme_physique_reveil_moy', 'Difficulte_endormissement_moy',
                       'Alcool_soir_pct', 'Ecrans_soir_pct', 'Stress_soir_moy',
                       'Sieste_pct', 'Cauchemars_pct']:
            col_name = f'Q2_{label}_{suffix}'
            result[col_name] = q2_entry.get(col_name, '')
    for suffix in ['Duree_sommeil', 'Qualite_sommeil', 'Humeur_reveil',
                   'Forme_physique', 'Difficulte_endormissement',
                   'Stress_soir', 'Pct_nuits_avec_reveil']:
        col_name = f'Q2_Delta_{suffix}'
        result[col_name] = q2_entry.get(col_name, '')

    # Q3 — scores agrégés + textes libres
    for k in ['Q3_Score_Interet_Engagement', 'Q3_Score_Intention_Changement',
              'Q3_Score_Satisfaction_Globale'] + list(Q3_TEXT_COLS.keys()):
        result[k] = q3_entry.get(k, '')

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
    print("\nScores Q3 générés :")
    print("  Q3_Score_Interet_Engagement   → moy. 12 items VD1 (1-5)")
    print("  Q3_Score_Intention_Changement → moy. 2 items VD2 (certitude + effet)")
    print("  Q3_Score_Satisfaction_Globale → moy. 4 items satisfaction")
    print("\nÉTAPES SUIVANTES :")
    print("  1. Vérifie Profil_Utilisateur (auto-rempli depuis Q1)")
    print("  2. python md_to_jasp_all_results.py --compute-fit results_before_cleaning.csv")


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