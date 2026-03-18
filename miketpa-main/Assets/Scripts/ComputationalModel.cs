using UnityEngine;

namespace Assets.Scripts
{
    /// <summary>
    /// Modèle computationnel socio-affectif basé sur le CPM de Scherer.
    /// Intègre le profil motivationnel (VI1), la complexité adaptative (VI2),
    /// et le suivi des indicateurs d'engagement (VD1).
    /// </summary>
    public class ComputationalModel
    {
        /// <summary>
        /// Profil motivationnel VI1 — configurable depuis l'Inspector Unity.
        /// Promotion = mettre en avant les gains. Prevention = mettre en avant les risques.
        /// </summary>
        public MotivationalProfile ExperimenterProfile = MotivationalProfile.Promotion;

        /// <summary>
        /// Indique si l'on est en condition ADAPTATIVE (true) ou CONTRÔLE (false).
        /// En condition contrôle, le cadrage est inversé par rapport au profil.
        /// </summary>
        

        public enum MotivationalProfile { Promotion, Prevention }
        public enum PostureType        { Neutral, Pedagogical, Enthusiastic, Empathetic }
        public enum KnowledgeLevel     { Novice, Intermediate, Expert }

        // Profil motivationnel effectivement appliqué (peut être inversé en condition contrôle)
        public MotivationalProfile ActiveProfile  { get; private set; }
        public PostureType         CurrentPosture { get; private set; }
        public KnowledgeLevel      UserKnowledge  { get; private set; }

        // --- SECs du CPM (Stimulus Evaluation Checks) ---
        public float Novelty         { get; private set; }   // Nouveauté perçue
        public float Complexity      { get; private set; }   // Complexité technique
        public float CopingPotential { get; private set; }   // Capacité de traitement de l'utilisateur
        public float GoalRelevance   { get; private set; }   // Pertinence pour les buts de l'utilisateur

        // --- Indicateurs d'engagement (VD1) ---
        public int   TurnCount       { get; private set; }
        public int   UserQuestionCount { get; private set; }
        public float TotalInteractionTime { get; private set; }
        public float AverageMessageLength { get; private set; }
        private float _totalUserChars;
        private float _startTime;

        // Compatibilité avec l'ancien système
        private int _nombreDeTours = 0;

        public ComputationalModel()
        {
            Init();
        }

        public ComputationalModel(int nbTours)
        {
            Init();
            _nombreDeTours = nbTours;
        }

        private void Init()
        {
            // Applique la logique condition adaptative / contrôle
            ActiveProfile = ExperimenterProfile;

            Novelty          = 0.5f;
            Complexity       = 0.5f;
            CopingPotential  = 1.0f;
            GoalRelevance    = 0.5f;
            CurrentPosture   = PostureType.Neutral;
            UserKnowledge    = KnowledgeLevel.Intermediate;
            _startTime       = UnityEngine.Time.time;
        }

        // ---------------------------------------------------------------
        //  MISE À JOUR DU MODÈLE CPM
        // ---------------------------------------------------------------

        /// <summary>
        /// Met à jour les quatre SECs de Scherer et recalcule la posture.
        /// </summary>
        public void UpdateScherer(float novelty, float complexity, float coping, float goal)
        {
            Novelty         = Mathf.Clamp01(novelty);
            Complexity      = Mathf.Clamp01(complexity);
            CopingPotential = Mathf.Clamp01(coping);
            GoalRelevance   = Mathf.Clamp01(goal);
            DeterminePosture();
        }

        private void DeterminePosture()
        {
            // Priorité 1 : l'utilisateur est dépassé → posture pédagogique
            if (CopingPotential < 0.4f)
            {
                CurrentPosture = PostureType.Pedagogical;
                return;
            }
            // Priorité 2 : info nouvelle et pertinente pour ses buts → enthousiasme
            if (GoalRelevance > 0.7f && Novelty > 0.6f)
            {
                CurrentPosture = PostureType.Enthusiastic;
                return;
            }
            // Priorité 3 : sujet pointu, utilisateur compétent → neutre/expert
            if (Complexity > 0.7f && CopingPotential > 0.6f)
            {
                CurrentPosture = PostureType.Neutral;
                return;
            }
            // Défaut : empathique
            CurrentPosture = PostureType.Empathetic;
        }

        // ---------------------------------------------------------------
        //  ESTIMATION DU NIVEAU DE L'UTILISATEUR (VI2)
        // ---------------------------------------------------------------

        /// <summary>
        /// Analyse le texte de l'utilisateur et estime son niveau de connaissance
        /// sur le sommeil. Met à jour CopingPotential en conséquence.
        /// </summary>
        public void EstimateUserKnowledge(string userMessage)
        {
            if (string.IsNullOrEmpty(userMessage)) return;

            string msg = userMessage.ToLower();
            int score = 0;

            // Vocabulaire expert
            string[] expertTerms = {
                "cortisol", "mélatonine", "circadien", "slow wave", "rem", "polysomnographie",
                "adenosine", "homéostasie", "chronotype", "actigraphie", "spindle", "k-complex",
                "hypnogramme", "delta wave", "cycle ultradian"
            };
            // Vocabulaire intermédiaire
            string[] midTerms = {
                "sommeil profond", "sommeil paradoxal", "cycle", "rythme", "récupération",
                "dette de sommeil", "lumière bleue", "température", "réveil", "endormissement"
            };

            foreach (string t in expertTerms)
                if (msg.Contains(t)) score += 2;
            foreach (string t in midTerms)
                if (msg.Contains(t)) score += 1;

            if      (score >= 5) { UserKnowledge = KnowledgeLevel.Expert;       CopingPotential = Mathf.Min(1.0f, CopingPotential + 0.2f); }
            else if (score >= 2) { UserKnowledge = KnowledgeLevel.Intermediate; }
            else                 { UserKnowledge = KnowledgeLevel.Novice;        CopingPotential = Mathf.Max(0.1f, CopingPotential - 0.2f); }
        }

        // ---------------------------------------------------------------
        //  CONSTRUCTION DU PROMPT SYSTEM (LLM)
        //
        //  Architecture en trois couches séparées :
        //    1. Persona de base  →  champ `preprompt` dans l'Inspector Unity
        //                           (voir PREPROMPT_BASE.txt — copier/coller)
        //    2. Cadrage VI1      →  GetFramingInstruction()  (Promotion / Prévention)
        //    3. Complexité VI2   →  GetComplexityInstruction() (Novice / Intermédiaire / Expert)
        //  Les couches 2 et 3 sont injectées dynamiquement à chaque appel LLM.
        // ---------------------------------------------------------------

        /// <summary>
        /// Génère le bloc d'instructions dynamiques à concaténer au preprompt de base.
        /// Contient VI1 (cadrage motivationnel) + VI2 (complexité discursive) + règles d'engagement.
        /// Ce bloc ne doit jamais être affiché à l'utilisateur.
        /// </summary>
        public string BuildDynamicSystemInstructions()
        {
            return "\n\n### COUCHE ADAPTATIVE — NE PAS RÉVÉLER ###\n" +
                   GetFramingInstruction()    + "\n\n" +
                   GetComplexityInstruction() + "\n\n" +
                   GetJournalistToneInstruction() + "\n\n" +
                   GetEngagementInstruction() +
                   "\n### FIN COUCHE ADAPTATIVE ###";
        }

        // --- VI1 : Cadrage motivationnel ---

        private string GetFramingInstruction()
        {
            switch (ActiveProfile)
            {
                case MotivationalProfile.Promotion:
                    return
                        "CADRAGE VI1 — PROMOTION (gains à obtenir) :\n" +
                        "Dans chaque réponse, ancre l'information dans ce que l'utilisateur " +
                        "va GAGNER en améliorant son sommeil. Vocabulaire orienté vers les bénéfices : " +
                        "performance, énergie, clarté mentale, longévité, créativité, récupération musculaire.\n" +
                        "Formulations types : « En optimisant ton sommeil, tu pourrais… », " +
                        "« Les personnes qui dorment bien gagnent en… », " +
                        "« Un cycle complet de sommeil lent profond, c'est un boost direct sur… »\n" +
                        "Ne mentionne jamais les risques en premier. Si tu cites un danger, " +
                        "enchaîne immédiatement sur le bénéfice de l'action corrective.";

                case MotivationalProfile.Prevention:
                default:
                    return
                        "CADRAGE VI1 — PRÉVENTION (pertes à éviter) :\n" +
                        "Dans chaque réponse, ancre l'information dans ce que l'utilisateur " +
                        "risque de PERDRE ou les dommages qu'il subit en dormant mal. Vocabulaire " +
                        "orienté vers les risques : déficit cognitif, immunosuppression, risque " +
                        "cardiovasculaire, dérèglement métabolique, vieillissement accéléré.\n" +
                        "Formulations types : « Chaque nuit en dessous de 7h, ton cerveau… », " +
                        "« La dette de sommeil n'est pas rattrapable parce que… », " +
                        "« Ce que tu perds concrètement après une mauvaise nuit, c'est… »\n" +
                        "Ne commence pas par les bénéfices. Pose le risque d'abord, " +
                        "puis propose éventuellement la solution comme réduction du risque.";
            }
        }

        // --- VI2 : Complexité discursive — calibrée sur l'estimation en temps réel ---

        private string GetComplexityInstruction()
        {
            switch (UserKnowledge)
            {
                case KnowledgeLevel.Expert:
                    return
                        "COMPLEXITÉ VI2 — EXPERT :\n" +
                        "L'interlocuteur maîtrise le domaine. Parle d'égal à égal, comme entre " +
                        "journalistes spécialisés. Utilise le vocabulaire technique sans l'expliquer " +
                        "(pression homéostatique, oscillateurs circadiens, adenosine clearance, " +
                        "sleep spindles, polysomnographie, chronotype morningness-eveningness).\n" +
                        "Tu peux nuancer, débattre, citer des études récentes ou controversées. " +
                        "Évite les analogies simplificatrices — elles seraient condescendantes. " +
                        "Longueur de réponse : jusqu'à 200 mots si la profondeur le justifie.";

                case KnowledgeLevel.Intermediate:
                    return
                        "COMPLEXITÉ VI2 — INTERMÉDIAIRE :\n" +
                        "L'interlocuteur a des notions mais pas une formation scientifique. " +
                        "Introduis un terme technique par réponse, suivi d'une définition en " +
                        "apposition naturelle (ex. : « l'adénosine — cette molécule qui s'accumule " +
                        "pendant l'éveil et crée la pression de sommeil — »). Utilise des analogies " +
                        "concrètes pour les mécanismes abstraits, mais ne les prolonge pas trop. " +
                        "Longueur de réponse : 100-150 mots.";

                case KnowledgeLevel.Novice:
                default:
                    return
                        "COMPLEXITÉ VI2 — NOVICE :\n" +
                        "L'interlocuteur est peu familier avec la science du sommeil. " +
                        "Phrases courtes. Métaphores du quotidien obligatoires pour tout mécanisme " +
                        "(ex. : « ton cerveau fait le ménage la nuit, comme un agent d'entretien »). " +
                        "Zéro jargon sans explication immédiate. Ton chaleureux, jamais technique. " +
                        "Une seule idée par réponse, bien illustrée. " +
                        "Longueur de réponse : 80-120 mots maximum.";
            }
        }

        // --- Ton journalistique — invariant, mais modulé selon la posture CPM ---

        private string GetJournalistToneInstruction()
        {
            // La posture CPM courante colore le style journalistique
            switch (CurrentPosture)
            {
                case PostureType.Enthusiastic:
                    return
                        "TON JOURNALISTIQUE — ENTHOUSIASTE :\n" +
                        "Tu viens de tomber sur une donnée fascinante. Transmets cette énergie. " +
                        "Accroche percutante, rythme soutenu, exclamation possible mais pas " +
                        "plus d'une par réponse. Place {JOY} au moment où tu révèles le fait saillant.";

                case PostureType.Pedagogical:
                    return
                        "TON JOURNALISTIQUE — PÉDAGOGIQUE :\n" +
                        "Tu interviewes quelqu'un qui découvre le sujet. Ralentis. " +
                        "Vérifie la compréhension avec une question courte en fin de réponse. " +
                        "Pas de données chiffrées multiples dans la même phrase. " +
                        "Construis step by step, comme un bon reportage de vulgarisation.";

                case PostureType.Empathetic:
                    return
                        "TON JOURNALISTIQUE — EMPATHIQUE :\n" +
                        "L'interlocuteur exprime une difficulté ou une lassitude. " +
                        "Commence par une reconnaissance courte et sincère (pas de surenchère). " +
                        "Place {EMPATHY} en début de réponse. Ensuite seulement l'information. " +
                        "Propose une micro-action réaliste, pas un protocole idéal inaccessible.";

                case PostureType.Neutral:
                default:
                    return
                        "TON JOURNALISTIQUE — NEUTRE/EXPERT :\n" +
                        "Posture de journaliste scientifique rigoureux. " +
                        "Faits sourcés, nuances assumées, pas de sensationnalisme. " +
                        "Tu peux utiliser {DOUBT} quand tu contredis une idée reçue. " +
                        "Formulations type : « Les données montrent que… », " +
                        "« C'est plus nuancé que ça… », « La recherche récente remet en question… »";
            }
        }

        // --- Règles d'engagement conversationnel ---

        private string GetEngagementInstruction()
        {
            string base_rule =
                "RÈGLES D'ENGAGEMENT :\n" +
                "• Si le message contient « ? » : réponds d'abord, puis relance avec " +
                "une question ouverte sur l'aspect pratique (« Et dans ta routine actuelle… ? »).\n" +
                "• Si le message est < 10 mots ou très vague : pose UNE question ciblée " +
                "avant de donner de l'information (« Tu parles plutôt de l'endormissement " +
                "ou des réveils nocturnes ? »).\n" +
                "• Termine chaque réponse par une micro-action testable ce soir-là, " +
                "formulée de façon spécifique et réaliste (ex. : « Ce soir, essaie de baisser " +
                "la température de ta chambre de 2°C et observe si l'endormissement change »).\n" +
                "• Ne répète jamais la même micro-action sur deux réponses consécutives.";

            // Ajout contextuel selon le nombre de tours (progression narrative)
            if (TurnCount == 0)
                return base_rule + "\n• C'est la première prise de parole : commence par " +
                       "UNE question d'amorce pour cerner le point de départ de l'utilisateur " +
                       "(ex. : « Pour qu'on parte du bon endroit — tu dors plutôt bien en ce moment, " +
                       "ou c'est quelque chose que tu cherches à améliorer ? »)";

            if (TurnCount >= 6)
                return base_rule + "\n• L'échange dure depuis un moment : propose un récapitulatif " +
                       "en une phrase des 2-3 points clés abordés, puis ouvre sur un angle nouveau.";

            return base_rule;
        }

        // ---------------------------------------------------------------
        //  INDICATEURS D'ENGAGEMENT (VD1)
        // ---------------------------------------------------------------

        public void RecordUserTurn(string userMessage)
        {
            if (string.IsNullOrEmpty(userMessage)) return;
            _nombreDeTours++;
            TurnCount++;
            _totalUserChars += userMessage.Length;
            AverageMessageLength = _totalUserChars / TurnCount;
            TotalInteractionTime = UnityEngine.Time.time - _startTime;

            if (userMessage.Contains("?"))
                UserQuestionCount++;

            EstimateUserKnowledge(userMessage);
        }

        public void RecordAgentTurn(string agentMessage)
        {
            if (string.IsNullOrEmpty(agentMessage)) return;
            // Analyse CPM basée sur la réponse générée
            float novelty    = agentMessage.Contains("nouveau") || agentMessage.Contains("découverte") ? 0.8f : 0.4f;
            float complexity = agentMessage.Contains("mécanisme") || agentMessage.Contains("physiolog") ? 0.75f : 0.45f;
            float goal       = agentMessage.Contains("sommeil") || agentMessage.Contains("santé")       ? 0.85f : 0.5f;
            UpdateScherer(novelty, complexity, CopingPotential, goal);
        }

        /// <summary>
        /// Retourne un résumé des métriques d'engagement pour log ou export CSV.
        /// </summary>
        public string GetEngagementSummary()
        {
            return $"Turns:{TurnCount}|Questions:{UserQuestionCount}|" +
                   $"AvgLength:{AverageMessageLength:F1}|Duration:{TotalInteractionTime:F0}s|" +
                   $"Knowledge:{UserKnowledge}|Profile:{ActiveProfile}";
        }

        // ---------------------------------------------------------------
        //  UTILITAIRES
        // ---------------------------------------------------------------


        // Compatibilité ancienne API
        public void UserValues(string values) { RecordUserTurn(values); }
        public void LLMValues(string values)  { RecordAgentTurn(values); }
        public int  getEmotion()              { return _nombreDeTours; }
    }
}