using System;
using System.Collections.Generic;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;
/*
* Ce script permet de recevoir sur une WebSocket des blendshapes au format de Google Mediapipe.
*/
public class BlendShapeReceiver : MonoBehaviour
{
    public static BlendShapeReceiver mainBSR;
    public SkinnedMeshRenderer[] renderers;
    public string url = "ws://localhost";
    public int port = 8080;
    private Dictionary<string, int> blenshapesValues = new Dictionary<string, int>();
    private WebSocketServer wss;


    void Start()
    {

        mainBSR = this;
        // Crée un serveur WebSocket sur le port 8080
        wss = new WebSocketServer(url + ":" + port);

        // Ajoute un comportement de réception
        wss.AddWebSocketService<Handler>("/");

        // Démarre le serveur
        wss.Start();
        Debug.Log("Serveur WebSocket démarré");


    }

    void OnDestroy()
    {
        // Arrête le serveur lors de la destruction
        if (wss != null)
        {
            wss.Stop();
            Debug.Log("Serveur WebSocket arrêté");
        }
    }

    /*Une classe servant de classe racine pour le Parsing du JSON reçu sur la WebSocket
    * Cette classe contient la collection des blendshapes reçues, une blendshape étant appellée une catégorie
    */
    [System.Serializable]
    public class BlendShapeInfoCollection
    {
        public List<Category> categories;
        public int headIndex;
        public string headName;

        public static BlendShapeInfoCollection CreateFromJSON(string jsonString)
        {
            return JsonUtility.FromJson<BlendShapeInfoCollection>(jsonString);
        }

    }
    /*
     * Une classe permettant de construire une Blendshape lors du parsing du JSON reçu
     * Le nom de la Blendshape se trouve dans la propriété categoryName et la valeur dans la propriété score
     */
    [System.Serializable]
    public class Category
    {
        public int index;
        public double score;
        public string categoryName;
        public string displayName;

        public static Category CreateFromJSON(string jsonString)
        {
            return JsonUtility.FromJson<Category>(jsonString);
        }

    }

    public class Handler : WebSocketBehavior
    {
        protected override void OnMessage(MessageEventArgs e)
        {
            try
            {
                // Reçoit le message JSON

                BlendShapeInfoCollection bsic = BlendShapeInfoCollection.CreateFromJSON(e.Data);
                foreach (Category bsi in bsic.categories)
                {
                    lock (BlendShapeReceiver.mainBSR.blenshapesValues)
                    {
                        if (BlendShapeReceiver.mainBSR.blenshapesValues.ContainsKey(bsi.categoryName))
                        {
                            BlendShapeReceiver.mainBSR.blenshapesValues[bsi.categoryName] = (int)(bsi.score * 100);
                        }
                        else
                        {
                            BlendShapeReceiver.mainBSR.blenshapesValues.Add(bsi.categoryName, (int)(bsi.score * 100));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log(ex.ToString());
            }
        }
    }
    /*!
   * @brief A function for getting blendshape index by name.
   * @return int
   */
    public int getBlendShapeIndex(SkinnedMeshRenderer smr, string bsName)
    {
        Mesh m = smr.sharedMesh;

        for (int i = 0; i < m.blendShapeCount; i++)
        {
            string name = m.GetBlendShapeName(i);
            if (bsName.Equals(m.GetBlendShapeName(i)) == true)
                return i;
        }

        return -1;
    }

    void Update()
    {

        animFace();
    }

    /*
     * On vient animer les BlendShapes du visage pour chaque SkinnedMeshRenderer
     */
    public void animFace()
    {
        foreach (SkinnedMeshRenderer smr in renderers)
        {
            try
            {
                lock (BlendShapeReceiver.mainBSR.blenshapesValues)
                {
                    foreach (KeyValuePair<string, int> kvp in blenshapesValues)
                    {
                        if (getBlendShapeIndex(smr, kvp.Key) != -1)
                            smr.SetBlendShapeWeight(getBlendShapeIndex(smr, kvp.Key), blenshapesValues[kvp.Key]);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log(ex.ToString());
            }

        }
    }
}
