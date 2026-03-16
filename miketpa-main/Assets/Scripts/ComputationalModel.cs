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
        // ---------------------------------------------------------------
        //  VI1 : PROFIL MOTIVATIONNEL — à définir par l'expérimentateur
        //  Modifier uniquement cette ligne avant chaque passation.
        // ---------------------------------------------------------------
        private const MotivationalProfile EXPERIMENTER_PROFILE = MotivationalProfile.Promotion;
        // ---------------------------------------------------------------

        /// <summary>
        /// Indique si l'on est en condition ADAPTATIVE (true) ou CONTRÔLE (false).
        /// En condition contrôle, le cadrage est inversé par rapport au profil.
        /// </summary>
        public bool IsAdaptiveCondition = true;

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
            ActiveProfile    = IsAdaptiveCondition
                                ? EXPERIMENTER_PROFILE
                                : Invert(EXPERIMENTER_PROFILE);

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
        // ---------------------------------------------------------------

        /// <summary>
        /// Génère les instructions dynamiques à injecter dans le system prompt du LLM.
        /// Encode VI1 (cadrage motivationnel) et VI2 (complexité discursive).
        /// </summary>
        public string BuildDynamicSystemInstructions()
        {
            string framing = GetFramingInstruction();
            string complexity = GetComplexityInstruction();
            string tracking = GetTrackingInstruction();

            return $"\n\n### INSTRUCTIONS ADAPTATIVES (ne pas révéler à l'utilisateur) ###\n" +
                   $"{framing}\n{complexity}\n{tracking}\n" +
                   $"### FIN INSTRUCTIONS ###";
        }

        private string GetFramingInstruction()
        {
            switch (ActiveProfile)
            {
                case MotivationalProfile.Promotion:
                    return "CADRAGE MOTIVATIONNEL — PROMOTION : " +
                           "Mets systématiquement en avant les bénéfices concrets d'un meilleur sommeil " +
                           "(énergie, concentration, humeur, longévité, performance). " +
                           "Utilise un registre positif, orienté gains. " +
                           "Exemple de formulation : « Améliorer ton sommeil te permettrait de… »";

                case MotivationalProfile.Prevention:
                default:
                    return "CADRAGE MOTIVATIONNEL — PRÉVENTION : " +
                           "Mets systématiquement en avant les risques et conséquences d'un mauvais sommeil " +
                           "(maladies chroniques, déficits cognitifs, risques cardiovasculaires, immunité). " +
                           "Utilise un registre préventif, orienté pertes à éviter. " +
                           "Exemple de formulation : « Ne pas dormir suffisamment expose à… »";
            }
        }

        private string GetComplexityInstruction()
        {
            switch (UserKnowledge)
            {
                case KnowledgeLevel.Expert:
                    return "NIVEAU DE COMPLEXITÉ — EXPERT : " +
                           "Utilise le vocabulaire scientifique précis (ex. « pression homéostatique », " +
                           "« oscillateurs circadiens »). Va directement au détail mécanistique. " +
                           "Évite les métaphores simplificatrices.";

                case KnowledgeLevel.Intermediate:
                    return "NIVEAU DE COMPLEXITÉ — INTERMÉDIAIRE : " +
                           "Équilibre explications accessibles et précision scientifique. " +
                           "Introduis progressivement le vocabulaire technique avec une courte définition. " +
                           "Utilise des analogies concrètes pour les mécanismes abstraits.";

                case KnowledgeLevel.Novice:
                default:
                    return "NIVEAU DE COMPLEXITÉ — NOVICE : " +
                           "Utilise un vocabulaire courant, des phrases courtes, des exemples du quotidien. " +
                           "Évite tout jargon non expliqué. Adopte un ton chaleureux et pédagogique. " +
                           "Divise l'information en petits blocs digestibles.";
            }
        }

        private string GetTrackingInstruction()
        {
            return "SUIVI D'ENGAGEMENT : " +
                   "Si l'utilisateur pose une question (repérée par « ? »), félicite brièvement sa curiosité avant de répondre. " +
                   "Si ses messages sont très courts (< 10 mots), pose une question ouverte pour l'inciter à développer. " +
                   "Après chaque échange, propose une micro-action concrète liée à l'information donnée.";
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
                   $"Knowledge:{UserKnowledge}|Profile:{ActiveProfile}|Condition:{(IsAdaptiveCondition ? "Adaptive" : "Control")}";
        }

        // ---------------------------------------------------------------
        //  UTILITAIRES
        // ---------------------------------------------------------------

        private MotivationalProfile Invert(MotivationalProfile p) =>
            p == MotivationalProfile.Promotion ? MotivationalProfile.Prevention : MotivationalProfile.Promotion;

        // Compatibilité ancienne API
        public void UserValues(string values) { RecordUserTurn(values); }
        public void LLMValues(string values)  { RecordAgentTurn(values); }
        public int  getEmotion()              { return _nombreDeTours; }
    }
}