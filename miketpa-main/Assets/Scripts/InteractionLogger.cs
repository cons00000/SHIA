using System;
using System.IO;
using Assets.Scripts;
using UnityEngine;

/// <summary>
/// Enregistre les métriques d'engagement (VD1) dans un fichier CSV horodaté.
/// Attachez ce script au même GameObject que LMStudioDialogManager.
/// Appelez LogTurn() à chaque tour, et SaveToCSV() à la fin de la session.
/// </summary>
public class InteractionLogger : MonoBehaviour
{
    [Header("Export")]
    [Tooltip("Identifiant participant (à renseigner avant chaque passation)")]
    public string ParticipantID = "P00";

    [Tooltip("Dossier de sortie relatif au projet Unity (Assets/Logs par défaut)")]
    public string OutputFolder = "Assets/Logs";

    private struct TurnRecord
    {
        public int    TurnIndex;
        public string Role;         // "User" ou "Agent"
        public int    MessageLength;
        public bool   ContainsQuestion;
        public string KnowledgeLevel;
        public string Posture;
        public string MotivationalProfile;
        public string Condition;    // Adaptive / Control
        public float  TimestampSec;
    }

    private System.Collections.Generic.List<TurnRecord> _records
        = new System.Collections.Generic.List<TurnRecord>();

    private int _turnIndex = 0;

    /// <summary>
    /// Enregistre un tour de parole.
    /// </summary>
    public void LogTurn(string role, string message,
                        ComputationalModel model)
    {
        _records.Add(new TurnRecord
        {
            TurnIndex          = _turnIndex++,
            Role               = role,
            MessageLength      = message?.Length ?? 0,
            ContainsQuestion   = message != null && message.Contains("?"),
            KnowledgeLevel     = model.UserKnowledge.ToString(),
            Posture            = model.CurrentPosture.ToString(),
            MotivationalProfile = model.ActiveProfile.ToString(),
            TimestampSec       = Time.time
        });
    }

    /// <summary>
    /// Exporte les données dans un CSV horodaté.
    /// Appelé automatiquement à la destruction du GameObject (fin de session).
    /// </summary>
    void OnDestroy() => SaveToCSV();

    public void SaveToCSV()
    {
        if (_records.Count == 0) return;

        if (!Directory.Exists(OutputFolder))
            Directory.CreateDirectory(OutputFolder);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(OutputFolder, $"interaction_{ParticipantID}_{timestamp}.csv");

        using (var sw = new StreamWriter(path, false, System.Text.Encoding.UTF8))
        {
            sw.WriteLine("TurnIndex,Role,MessageLength,ContainsQuestion," +
                         "KnowledgeLevel,Posture,MotivationalProfile,Condition,TimestampSec");

            foreach (var r in _records)
            {
                sw.WriteLine($"{r.TurnIndex},{r.Role},{r.MessageLength}," +
                             $"{r.ContainsQuestion},{r.KnowledgeLevel},{r.Posture}," +
                             $"{r.MotivationalProfile},{r.Condition},{r.TimestampSec:F2}");
            }
        }

        Debug.Log($"[InteractionLogger] Session exportée → {path}");
    }
}
