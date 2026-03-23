using System;
using System.Collections.Generic;
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
        private static readonly string[] ExpertTerms = {
            "cortisol", "melatonine", "mélatonine", "circadien", "slow wave", "rem", "polysomnographie",
            "adenosine", "homeostasie", "homéostasie", "chronotype", "actigraphie", "spindle", "k-complex",
            "hypnogramme", "delta wave", "cycle ultradian"
        };

        private static readonly string[] MidTerms = {
            "sommeil profond", "sommeil paradoxal", "cycle", "rythme", "recuperation", "récupération",
            "dette de sommeil", "lumiere bleue", "lumière bleue", "temperature", "température",
            "reveil", "réveil", "endormissement"
        };

        private static readonly string[] GoalTerms = {
            "sommeil", "dorm", "insom", "fatigue", "reveil", "réveil", "endorm", "nuit",
            "routine", "energie", "énergie", "concentration", "stress", "sieste"
        };

        private static readonly string[] NoveltyTerms = {
            "nouveau", "nouvelle", "decouverte", "découverte", "surprenant", "surprenante",
            "contre-intuitif", "contre-intuitive", "recemment", "récemment", "en realite", "en réalité"
        };

        private static readonly string[] TechnicalTerms = {
            "mecanisme", "mécanisme", "physiolog", "homeost", "homéost", "circad", "adenos",
            "melaton", "mélaton", "cortisol", "neuro", "ultradian", "polysomnograph"
        };

        private static readonly string[] ActionTerms = {
            "essaie", "essayez", "teste", "teste ce soir", "observe", "ajuste", "baisse",
            "augmente", "evite", "évite", "garde", "note", "compare"
        };

        private static readonly string[] SelfReferenceTerms = {
            " je ", " j'", " moi ", " mon ", " ma ", " mes ", " routine ", " habitudes "
        };

        private static readonly char[] WordSeparators = {
            ' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}',
            '"', '\'', '«', '»', '/', '\\', '-', '_', '’'
        };

        private const float EstimatedSpeechRateWordsPerSecond = 2.6f;

        /// <summary>
        /// Profil motivationnel VI1 — configurable depuis l'Inspector Unity.
        /// Promotion = mettre en avant les gains. Prevention = mettre en avant les risques.
        /// </summary>
        private MotivationalProfile _experimenterProfile = MotivationalProfile.Promotion;
        public MotivationalProfile ExperimenterProfile
        {
            get => _experimenterProfile;
            set
            {
                _experimenterProfile = value;
                ActiveProfile = value;
            }
        }

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
        public int   AgentTurnCount  { get; private set; }
        public int   UserQuestionCount { get; private set; }
        public float TotalInteractionTime { get; private set; }
        public float AverageMessageLength { get; private set; }
        public float AverageAgentMessageLength { get; private set; }
        public int   LastUserMessageLength { get; private set; }
        public int   LastAgentMessageLength { get; private set; }
        public int   LastUserWordCount { get; private set; }
        public int   LastAgentWordCount { get; private set; }
        public float AgentToUserLengthRatio { get; private set; }
        public float DialogueBalance { get; private set; }
        public float EstimatedLastUserSpeechSec { get; private set; }
        public float EstimatedLastAgentSpeechSec { get; private set; }
        public float MaxRecommendedAgentSpeechSec { get; private set; }
        public float MaxAgentToUserSpeechRatio { get; private set; }
        public int   RecommendedAgentMinWords { get; private set; }
        public int   RecommendedAgentMaxWords { get; private set; }
        private float _totalUserChars;
        private float _totalAgentChars;
        private float _startTime;
        private string _lastUserMessage = string.Empty;
        private string _lastAgentMessage = string.Empty;
        private string _previousUserMessage = string.Empty;
        private string _previousAgentMessage = string.Empty;

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
            ActiveProfile = _experimenterProfile;

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
            UpdateRecommendedResponseLength();
        }

        private void DeterminePosture()
        {
            // Priorité 1 : l'utilisateur est dépassé ou l'agent monopolise l'échange → posture pédagogique
            if (CopingPotential < 0.45f ||
                (AgentTurnCount > 0 && DialogueBalance < 0.45f && AgentToUserLengthRatio > 2.4f))
            {
                CurrentPosture = PostureType.Pedagogical;
                return;
            }
            // Priorité 2 : info nouvelle et pertinente pour ses buts → enthousiasme
            if (GoalRelevance > 0.7f && Novelty > 0.6f && DialogueBalance > 0.4f)
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
        /// sur le sommeil. Le CopingPotential est ensuite recalculé à partir
        /// de l'état global du dialogue.
        /// </summary>
        public void EstimateUserKnowledge(string userMessage)
        {
            if (string.IsNullOrEmpty(userMessage)) return;

            string msg = userMessage.ToLowerInvariant();
            int score = 0;

            score += CountMatches(msg, ExpertTerms) * 2;
            score += CountMatches(msg, MidTerms);

            if      (score >= 5) UserKnowledge = KnowledgeLevel.Expert;
            else if (score >= 2) UserKnowledge = KnowledgeLevel.Intermediate;
            else                 UserKnowledge = KnowledgeLevel.Novice;
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
                   GetEngagementInstruction() + "\n\n" +
                   GetDialogueRhythmInstruction() +
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
                        "Longueur de réponse : environ 35 à 80 mots, uniquement si la profondeur le justifie.";

                case KnowledgeLevel.Intermediate:
                    return
                        "COMPLEXITÉ VI2 — INTERMÉDIAIRE :\n" +
                        "L'interlocuteur a des notions mais pas une formation scientifique. " +
                        "Introduis un terme technique par réponse, suivi d'une définition en " +
                        "apposition naturelle (ex. : « l'adénosine — cette molécule qui s'accumule " +
                        "pendant l'éveil et crée la pression de sommeil — »). Utilise des analogies " +
                        "concrètes pour les mécanismes abstraits, mais ne les prolonge pas trop. " +
                        "Longueur de réponse : environ 25 à 55 mots.";

                case KnowledgeLevel.Novice:
                default:
                    return
                        "COMPLEXITÉ VI2 — NOVICE :\n" +
                        "L'interlocuteur est peu familier avec la science du sommeil. " +
                        "Phrases courtes. Métaphores du quotidien obligatoires pour tout mécanisme " +
                        "(ex. : « ton cerveau fait le ménage la nuit, comme un agent d'entretien »). " +
                        "Zéro jargon sans explication immédiate. Ton chaleureux, jamais technique. " +
                        "Une seule idée par réponse, bien illustrée. " +
                        "Longueur de réponse : environ 15 à 40 mots maximum.";
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
            if (TurnCount <= 1)
                return base_rule + "\n• C'est la première prise de parole : commence par " +
                       "UNE question d'amorce pour cerner le point de départ de l'utilisateur " +
                       "(ex. : « Pour qu'on parte du bon endroit — tu dors plutôt bien en ce moment, " +
                       "ou c'est quelque chose que tu cherches à améliorer ? »)";

            if (TurnCount >= 6)
                return base_rule + "\n• L'échange dure depuis un moment : propose un récapitulatif " +
                       "en une phrase des 2-3 points clés abordés, puis ouvre sur un angle nouveau.";

            return base_rule;
        }

        private string GetDialogueRhythmInstruction()
        {
            string turnDescription;
            if (LastUserWordCount <= 6)
                turnDescription = "Le dernier tour utilisateur est très bref : réponds brièvement, puis clarifie avec une seule question.";
            else if (LastUserWordCount <= 18)
                turnDescription = "Le dernier tour utilisateur est court : réponds précisément sans lancer un monologue.";
            else
                turnDescription = "Le dernier tour utilisateur est développé : tu peux apporter plus de matière, mais garde une structure simple.";

            string balanceDescription =
                AgentTurnCount > 0 && AgentToUserLengthRatio > 2.0f
                ? "Ta réponse précédente était trop longue par rapport à l'utilisateur : raccourcis nettement celle-ci."
                : "Maintiens une alternance naturelle : laisse de l'espace à l'utilisateur au prochain tour.";

            string lastAgentLength =
                AgentTurnCount > 0
                ? $"• Ta réponse précédente faisait environ {LastAgentWordCount} mots.\n"
                : string.Empty;

            return "RYTHME DIALOGIQUE :\n" +
                   $"• Dernier tour utilisateur : environ {LastUserWordCount} mots.\n" +
                   lastAgentLength +
                   $"• Le dernier tour utilisateur représente environ {EstimatedLastUserSpeechSec:F1} s d'oral.\n" +
                   $"• Vise {RecommendedAgentMinWords}-{RecommendedAgentMaxWords} mots pour la prochaine réponse.\n" +
                   $"• Ne dépasse pas environ {MaxRecommendedAgentSpeechSec:F1} s d'oral, soit au plus {MaxAgentToUserSpeechRatio:F1}x la durée estimée du dernier tour utilisateur.\n" +
                   $"• {turnDescription}\n" +
                   $"• {balanceDescription}\n" +
                   "• Si l'utilisateur répond en quelques mots, privilégie une relance courte plutôt qu'une explication dense.\n" +
                   "• Évite l'effet tunnel : même avec beaucoup d'informations, garde une réponse respirable et laisse de la place à la relance.\n" +
                   "• Si l'utilisateur détaille son vécu, adapte la longueur et le niveau de détail sans dépasser le cadre d'une vraie conversation.";
        }

        // ---------------------------------------------------------------
        //  INDICATEURS D'ENGAGEMENT (VD1)
        // ---------------------------------------------------------------

        public void RecordUserTurn(string userMessage)
        {
            userMessage = SanitizeMessage(userMessage);
            if (string.IsNullOrEmpty(userMessage)) return;

            _previousUserMessage = _lastUserMessage;
            _lastUserMessage = userMessage;
            _nombreDeTours++;
            TurnCount++;
            LastUserMessageLength = userMessage.Length;
            LastUserWordCount = CountWords(userMessage);
            _totalUserChars += userMessage.Length;
            AverageMessageLength = _totalUserChars / TurnCount;
            TotalInteractionTime = UnityEngine.Time.time - _startTime;

            if (userMessage.Contains("?"))
                UserQuestionCount++;

            EstimateUserKnowledge(userMessage);
            RefreshDialogueMetrics();
            UpdateDialogueSECs();
        }

        public void RecordAgentTurn(string agentMessage)
        {
            agentMessage = SanitizeMessage(agentMessage);
            if (string.IsNullOrEmpty(agentMessage)) return;

            _previousAgentMessage = _lastAgentMessage;
            _lastAgentMessage = agentMessage;
            AgentTurnCount++;
            LastAgentMessageLength = agentMessage.Length;
            LastAgentWordCount = CountWords(agentMessage);
            _totalAgentChars += agentMessage.Length;
            AverageAgentMessageLength = _totalAgentChars / AgentTurnCount;
            TotalInteractionTime = UnityEngine.Time.time - _startTime;

            RefreshDialogueMetrics();
            UpdateDialogueSECs();
        }

        /// <summary>
        /// Retourne un résumé des métriques d'engagement pour log ou export CSV.
        /// </summary>
        public string GetEngagementSummary()
        {
            return $"Turns:{TurnCount}|Questions:{UserQuestionCount}|" +
                   $"AvgUserLength:{AverageMessageLength:F1}|AvgAgentLength:{AverageAgentMessageLength:F1}|" +
                   $"LastUserWords:{LastUserWordCount}|LastAgentWords:{LastAgentWordCount}|" +
                   $"UserSpeechSec:{EstimatedLastUserSpeechSec:F1}|AgentSpeechSec:{EstimatedLastAgentSpeechSec:F1}|" +
                   $"MaxAgentSpeechSec:{MaxRecommendedAgentSpeechSec:F1}|" +
                   $"Balance:{DialogueBalance:F2}|Ratio:{AgentToUserLengthRatio:F2}|" +
                   $"Duration:{TotalInteractionTime:F0}s|Knowledge:{UserKnowledge}|" +
                   $"Posture:{CurrentPosture}|Profile:{ActiveProfile}";
        }

        private void RefreshDialogueMetrics()
        {
            AverageAgentMessageLength = AgentTurnCount > 0 ? _totalAgentChars / AgentTurnCount : 0f;

            if (LastUserWordCount <= 0 || LastAgentWordCount <= 0)
            {
                AgentToUserLengthRatio = 1f;
                DialogueBalance = 1f;
                EstimatedLastUserSpeechSec = EstimateSpeechDurationSeconds(LastUserWordCount);
                EstimatedLastAgentSpeechSec = EstimateSpeechDurationSeconds(LastAgentWordCount);
                UpdateRecommendedResponseLength();
                return;
            }

            AgentToUserLengthRatio = (float)LastAgentWordCount / LastUserWordCount;
            float longerTurn = Mathf.Max(LastUserWordCount, LastAgentWordCount);
            float shorterTurn = Mathf.Max(1f, Mathf.Min(LastUserWordCount, LastAgentWordCount));
            DialogueBalance = shorterTurn / longerTurn;
            EstimatedLastUserSpeechSec = EstimateSpeechDurationSeconds(LastUserWordCount);
            EstimatedLastAgentSpeechSec = EstimateSpeechDurationSeconds(LastAgentWordCount);
            UpdateRecommendedResponseLength();
        }

        private void UpdateDialogueSECs()
        {
            UpdateScherer(
                ComputeNovelty(_lastUserMessage, _lastAgentMessage),
                ComputeComplexity(_lastUserMessage, _lastAgentMessage),
                ComputeCopingPotential(_lastUserMessage),
                ComputeGoalRelevance(_lastUserMessage, _lastAgentMessage));
        }

        private float ComputeNovelty(string userMessage, string agentMessage)
        {
            float score = 0.25f;
            float overlapWithHistory = ComputeTokenOverlap(
                userMessage,
                $"{_previousUserMessage} {_previousAgentMessage}");

            score += Mathf.Lerp(0.30f, 0.05f, overlapWithHistory);

            if (ContainsAny(userMessage, NoveltyTerms) || userMessage.Contains("?"))
                score += 0.10f;

            if (ContainsAny(agentMessage, NoveltyTerms))
                score += 0.15f;

            if (TurnCount >= 5 && overlapWithHistory > 0.55f)
                score -= 0.08f;

            return Mathf.Clamp01(score);
        }

        private float ComputeComplexity(string userMessage, string agentMessage)
        {
            float knowledgeBase = 0.35f;
            switch (UserKnowledge)
            {
                case KnowledgeLevel.Expert:
                    knowledgeBase = 0.80f;
                    break;
                case KnowledgeLevel.Intermediate:
                    knowledgeBase = 0.55f;
                    break;
            }

            float userLengthScore = Mathf.InverseLerp(4f, 35f, LastUserWordCount);
            float agentLengthScore = Mathf.InverseLerp(20f, 140f, LastAgentWordCount);
            float userTechnicality = Mathf.Clamp01(
                CountMatches(userMessage, ExpertTerms) * 0.12f +
                CountMatches(userMessage, MidTerms) * 0.05f);
            float agentTechnicality = Mathf.Clamp01(
                CountMatches(agentMessage, ExpertTerms) * 0.10f +
                CountMatches(agentMessage, TechnicalTerms) * 0.05f);
            float imbalanceBoost =
                AgentTurnCount > 0 && AgentToUserLengthRatio > 1.8f
                ? Mathf.Min(0.15f, (AgentToUserLengthRatio - 1.8f) * 0.08f)
                : 0f;

            return Mathf.Clamp01(
                0.10f +
                knowledgeBase * 0.25f +
                userLengthScore * 0.15f +
                agentLengthScore * 0.15f +
                userTechnicality * 0.15f +
                agentTechnicality * 0.15f +
                imbalanceBoost);
        }

        private float ComputeCopingPotential(string userMessage)
        {
            float score = 0.38f;
            switch (UserKnowledge)
            {
                case KnowledgeLevel.Expert:
                    score = 0.82f;
                    break;
                case KnowledgeLevel.Intermediate:
                    score = 0.60f;
                    break;
            }

            score += Mathf.InverseLerp(3f, 25f, LastUserWordCount) * 0.15f;

            if (userMessage.Contains("?"))
                score += 0.08f;

            if (ContainsAny(userMessage, GoalTerms))
                score += 0.06f;

            if (LastUserWordCount <= 3)
                score -= 0.12f;

            if (AgentTurnCount > 0 && AgentToUserLengthRatio > 2.2f)
                score -= Mathf.Min(0.25f, (AgentToUserLengthRatio - 2.2f) * 0.12f);

            if (AgentTurnCount > 0 && DialogueBalance < 0.45f)
                score -= 0.08f;

            return Mathf.Clamp01(score);
        }

        private float ComputeGoalRelevance(string userMessage, string agentMessage)
        {
            float score = 0.25f;

            if (ContainsAny(userMessage, GoalTerms))
                score += 0.25f;

            if (ContainsAny(agentMessage, GoalTerms))
                score += 0.15f;

            if (userMessage.Contains("?"))
                score += 0.10f;

            if (ContainsAny($" {userMessage.ToLowerInvariant()} ", SelfReferenceTerms))
                score += 0.10f;

            if (ContainsAny(agentMessage, ActionTerms))
                score += 0.10f;

            if (!string.IsNullOrEmpty(agentMessage))
                score += ComputeTokenOverlap(userMessage, agentMessage) * 0.15f;

            if (LastUserWordCount <= 3 && !userMessage.Contains("?"))
                score -= 0.10f;

            return Mathf.Clamp01(score);
        }

        private void UpdateRecommendedResponseLength()
        {
            int minWords = 35;
            int maxWords = 70;
            float durationCapSec = 18f;
            float ratioCap = 2.0f;

            if (LastUserWordCount <= 6)
            {
                minWords = 18;
                maxWords = 45;
                durationCapSec = 8f;
                ratioCap = 3.0f;
            }
            else if (LastUserWordCount <= 18)
            {
                minWords = 35;
                maxWords = 75;
                durationCapSec = 14f;
                ratioCap = 2.4f;
            }
            else if (LastUserWordCount <= 35)
            {
                minWords = 60;
                maxWords = 110;
                durationCapSec = 20f;
                ratioCap = 2.0f;
            }
            else
            {
                minWords = 90;
                maxWords = 150;
                durationCapSec = 28f;
                ratioCap = 1.6f;
            }

            switch (UserKnowledge)
            {
                case KnowledgeLevel.Expert:
                    minWords += 10;
                    maxWords += 20;
                    break;
                case KnowledgeLevel.Novice:
                    maxWords = Mathf.Min(maxWords, 110);
                    break;
            }

            if (CurrentPosture == PostureType.Pedagogical)
            {
                minWords = Mathf.Max(15, minWords - 10);
                maxWords = Mathf.Min(maxWords, 90);
            }
            else if (CurrentPosture == PostureType.Empathetic)
            {
                maxWords = Mathf.Min(maxWords, 100);
            }

            if (AgentTurnCount > 0 && AgentToUserLengthRatio > 2.0f)
            {
                minWords = Mathf.Max(15, minWords - 10);
                maxWords = Mathf.Max(minWords + 15, maxWords - 20);
                durationCapSec = Mathf.Max(6f, durationCapSec - 3f);
            }

            float userSpeechSec = EstimateSpeechDurationSeconds(LastUserWordCount);
            float ratioLimitedDurationSec = userSpeechSec > 0f
                ? Mathf.Max(6f, userSpeechSec * ratioCap)
                : durationCapSec;
            float effectiveDurationCapSec = Mathf.Min(durationCapSec, ratioLimitedDurationSec);
            int durationLimitedMaxWords = Mathf.Max(
                minWords + 5,
                Mathf.RoundToInt(effectiveDurationCapSec * EstimatedSpeechRateWordsPerSecond));

            maxWords = Mathf.Min(maxWords, durationLimitedMaxWords);
            maxWords = Mathf.Max(minWords + 5, maxWords);

            EstimatedLastUserSpeechSec = userSpeechSec;
            MaxRecommendedAgentSpeechSec = effectiveDurationCapSec;
            MaxAgentToUserSpeechRatio = ratioCap;
            RecommendedAgentMinWords = minWords;
            RecommendedAgentMaxWords = maxWords;
        }

        private static float EstimateSpeechDurationSeconds(int wordCount)
        {
            if (wordCount <= 0) return 0f;
            return wordCount / EstimatedSpeechRateWordsPerSecond;
        }

        private static string SanitizeMessage(string message)
        {
            return string.IsNullOrWhiteSpace(message)
                ? string.Empty
                : message.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static int CountMatches(string text, string[] terms)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            int matches = 0;
            string lowerText = text.ToLowerInvariant();
            foreach (string term in terms)
            {
                if (lowerText.Contains(term))
                    matches++;
            }
            return matches;
        }

        private static bool ContainsAny(string text, string[] terms)
        {
            if (string.IsNullOrEmpty(text)) return false;

            string lowerText = text.ToLowerInvariant();
            foreach (string term in terms)
            {
                if (lowerText.Contains(term))
                    return true;
            }
            return false;
        }

        private static float ComputeTokenOverlap(string left, string right)
        {
            HashSet<string> leftTokens = ExtractTokens(left);
            HashSet<string> rightTokens = ExtractTokens(right);

            if (leftTokens.Count == 0 || rightTokens.Count == 0)
                return 0f;

            int overlap = 0;
            foreach (string token in leftTokens)
            {
                if (rightTokens.Contains(token))
                    overlap++;
            }

            int union = leftTokens.Count + rightTokens.Count - overlap;
            return union == 0 ? 0f : (float)overlap / union;
        }

        private static HashSet<string> ExtractTokens(string text)
        {
            HashSet<string> tokens = new HashSet<string>();
            if (string.IsNullOrWhiteSpace(text)) return tokens;

            string[] rawTokens = text.ToLowerInvariant()
                                     .Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in rawTokens)
            {
                if (token.Length >= 3)
                    tokens.Add(token);
            }

            return tokens;
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
