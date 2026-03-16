
using ACTA;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
/*
 * Ce script gère les expressions faciales et l'animation des lèvres de l'agent.
 * Il est un peu basique, mais couvre les besoins minimums pour le cours IA & SHS
 */
public class FacialExpressionAvaturn : MonoBehaviour
{

    public class AnimationParameter
    {
        private int AUId;
        public List<string> Names { get; set; }
        public int Value { get; set; }

        public AnimationParameter(int AUId, List<string> Names)
        {
            this.AUId = AUId;
            this.Names = Names;
            this.Value = 0;
        }

    }

    public AudioSource audioSource;
    public List<SkinnedMeshRenderer> skinnedMeshRenderers;

    private Dictionary<int, AnimationParameter> faceAnimationParameters;
    private Dictionary<string, int> visemeAnimationParameters;
    private Dictionary<string, int> visemeAnimationParameters_Back;


    private float referenceLipTime;
    private float referenceFaceTime;
    private float referenceBlinkTime;
    private float deltaBlink;
    private int choice;
    private float timeBetweenViseme = 0.175f;
    private float timeBetweenBlink = 5.000f;
    private float facialExpressionDuration = 2.0f;
    private int[] aus = { 0 };

    public AnimationCurve animationCurve = AnimationCurve.EaseInOut(0.0f, 0.0f, 1.0f, 1.0f);

    /*Pour chaque paramètre du visage, on va conserver la valeur cible, vers laquelle on souhaite que le muscle du visage aille,
    * et la valeur précédente (dans le paramètre Back) afin de pouvoir interpoler ensuite entre ces deux valeurs pour animer en douceur le visage
    */


    void Start()
    {
        referenceLipTime = Time.time;
        referenceFaceTime = Time.time;
        referenceBlinkTime = Time.time;
        audioSource = GetComponent<AudioSource>();
        /*if (SkinnedMeshRendererTarget == null)
            SkinnedMeshRendererTarget = gameObject.GetComponent<SkinnedMeshRenderer>();
        */
        faceAnimationParameters = new Dictionary<int, AnimationParameter>();
        //AU1
        faceAnimationParameters.Add(1, new AnimationParameter(1, new List<string> { "browInnerUp" }));
        //AU2
        faceAnimationParameters.Add(2, new AnimationParameter(2, new List<string> { "browOuterUpLeft", "browOuterUpRight" }));
        //AU4
        faceAnimationParameters.Add(4, new AnimationParameter(4, new List<string> { "browDownLeft", "browDownRight" }));
        //AU5
        faceAnimationParameters.Add(5, new AnimationParameter(5, new List<string> { "eyeWideLeft", "eyeWideRight" }));
        //AU6
        faceAnimationParameters.Add(6, new AnimationParameter(6, new List<string> { "cheekSquintLeft", "cheekSquintRight" }));
        //AU7
        faceAnimationParameters.Add(7, new AnimationParameter(7, new List<string> { "eyeSquintLeft", "eyeSquintRight" }));
        //AU9
        faceAnimationParameters.Add(9, new AnimationParameter(9, new List<string> { "noseSneerLeft", "noseSneerRight" }));
        //AU10
        faceAnimationParameters.Add(10, new AnimationParameter(10, new List<string> { "mouthUpperUpLeft", "mouthUpperUpRight" }));
        //AU12
        faceAnimationParameters.Add(12, new AnimationParameter(12, new List<string> { "mouthSmileLeft", "mouthSmileRight" }));
        //AU14
        faceAnimationParameters.Add(14, new AnimationParameter(14, new List<string> { "mouthDimpleLeft", "mouthDimpleRight" }));
        //AU15
        faceAnimationParameters.Add(15, new AnimationParameter(15, new List<string> { "mouthFrownLeft", "mouthFrownRight" }));
        //AU16
        faceAnimationParameters.Add(16, new AnimationParameter(16, new List<string> { "mouthLowerDownLeft", "mouthLowerDownRight" }));
        //AU17
        faceAnimationParameters.Add(17, new AnimationParameter(17, new List<string> { "mouthShrugLower" }));
        //AU18
        faceAnimationParameters.Add(18, new AnimationParameter(18, new List<string> { "mouthPucker" }));
        //AU20
        faceAnimationParameters.Add(20, new AnimationParameter(20, new List<string> { "mouthStretchLeft", "mouthStretchRight" }));
        //AU22
        faceAnimationParameters.Add(22, new AnimationParameter(22, new List<string> { "mouthFunnel" }));
        //AU24
        faceAnimationParameters.Add(24, new AnimationParameter(24, new List<string> { "mouthPressLeft", "mouthPressRight" }));
        //AU26
        faceAnimationParameters.Add(26, new AnimationParameter(26, new List<string> { "jawOpen" }));
        //AU27
        faceAnimationParameters.Add(27, new AnimationParameter(27, new List<string> { "jawOpen" }));
        //AU28
        faceAnimationParameters.Add(28, new AnimationParameter(28, new List<string> { "mouthRollLower", "mouthRollUpper" }));
        //AD29
        faceAnimationParameters.Add(29, new AnimationParameter(29, new List<string> { "jawForward" }));
        //AD30 but right should be here as well
        faceAnimationParameters.Add(30, new AnimationParameter(30, new List<string> { "jawLeft" }));
        //AD34
        faceAnimationParameters.Add(34, new AnimationParameter(34, new List<string> { "cheekPuff" }));
        //AU45
        faceAnimationParameters.Add(45, new AnimationParameter(45, new List<string> { "eyeBlink" }));
        //M63
        faceAnimationParameters.Add(63, new AnimationParameter(63, new List<string> { "eyeLookUpLeft", "eyeLookUpRight" }));
        //M64
        faceAnimationParameters.Add(64, new AnimationParameter(64, new List<string> { "eyeLookDownLeft", "eyeLookDownRight" }));
        //AU65
        faceAnimationParameters.Add(65, new AnimationParameter(65, new List<string> { "eyeLookOutLeft", "eyeLookOutRight" }));
        //AU66
        faceAnimationParameters.Add(66, new AnimationParameter(66, new List<string> { "eyeLookInLeft", "eyeLookInRight" }));

        //VISEME
        visemeAnimationParameters = new Dictionary<string, int>();
        visemeAnimationParameters_Back = new Dictionary<string, int>();

        visemeAnimationParameters.Add("viseme_sil", 0);
        visemeAnimationParameters_Back.Add("viseme_sil", 0);
        visemeAnimationParameters.Add("viseme_PP", 0);
        visemeAnimationParameters_Back.Add("viseme_PP", 0);
        visemeAnimationParameters.Add("viseme_FF", 0);
        visemeAnimationParameters_Back.Add("viseme_FF", 0);
        visemeAnimationParameters.Add("viseme_TH", 0);
        visemeAnimationParameters_Back.Add("viseme_TH", 0);
        visemeAnimationParameters.Add("viseme_DD", 0);
        visemeAnimationParameters_Back.Add("viseme_DD", 0);
        visemeAnimationParameters.Add("viseme_kk", 0);
        visemeAnimationParameters_Back.Add("viseme_kk", 0);
        visemeAnimationParameters.Add("viseme_CH", 0);
        visemeAnimationParameters_Back.Add("viseme_CH", 0);
        visemeAnimationParameters.Add("viseme_SS", 0);
        visemeAnimationParameters_Back.Add("viseme_SS", 0);
        visemeAnimationParameters.Add("viseme_nn", 0);
        visemeAnimationParameters_Back.Add("viseme_nn", 0);
        visemeAnimationParameters.Add("viseme_RR", 0);
        visemeAnimationParameters_Back.Add("viseme_RR", 0);
        visemeAnimationParameters.Add("viseme_aa", 0);
        visemeAnimationParameters_Back.Add("viseme_aa", 0);
        visemeAnimationParameters.Add("viseme_E", 0);
        visemeAnimationParameters_Back.Add("viseme_E", 0);
        visemeAnimationParameters.Add("viseme_I", 0);
        visemeAnimationParameters_Back.Add("viseme_I", 0);
        visemeAnimationParameters.Add("viseme_O", 0);
        visemeAnimationParameters_Back.Add("viseme_O", 0);
        visemeAnimationParameters.Add("viseme_U", 0);
        visemeAnimationParameters_Back.Add("viseme_U", 0);

    }

    void Update()
    {
        float now = Time.time;


        //FACE LERP
        float faceLerp = (now - referenceFaceTime) / facialExpressionDuration;
        //LIP LERP (CONSTANT)
        float lipLerp = (now - referenceLipTime) / timeBetweenViseme;
        if (skinnedMeshRenderers != null && skinnedMeshRenderers.Count > 0)
        {
#if UNITY_STANDALONE_WIN
            if (audioSource.isPlaying || Narrator.isSpeaking())
#else
            if (audioSource.isPlaying)
#endif
            {
                if (now - referenceLipTime > timeBetweenViseme)
                {
                    UpdateLipBackWeight();
                    choice = UnityEngine.Random.Range(0, 11);
                    referenceLipTime = Time.time;
                    setRandomViseme(choice);
                }

            }

            else
            {
                setVisemeNeutral();
                if (now - referenceFaceTime > facialExpressionDuration)
                {
                    //UpdateFaceBackWeight();

                    setFaceNeutral();
                    //referenceFaceTime = Time.time;

                }
            }
            //Interpolation des animations
            lerpViseme(lipLerp);
            lerpFace(faceLerp);

        }
        //Blink
        if (now > referenceBlinkTime + timeBetweenBlink + deltaBlink)
        {
            blink();

        }
        if (now > referenceBlinkTime + timeBetweenBlink + deltaBlink + 0.2f)
        {
            unblink();
            referenceBlinkTime = now;
            deltaBlink = UnityEngine.Random.Range(-1.0f, 1.0f);
        }

    }

    public void setFacialAUs(int[] aus, int[] intensities, float duration)
    {
        this.aus = aus;
        facialExpressionDuration = duration;
        referenceFaceTime = Time.time;
        for (int i = 0; i < aus.Length; i++)
        {
            faceAnimationParameters[aus[i]].Value = intensities[i];
        }

    }

    public void UpdateLipBackWeight()
    {
        List<string> values = Enumerable.ToList(visemeAnimationParameters.Keys);
        foreach (string v in values)
        {
            visemeAnimationParameters_Back[v] = visemeAnimationParameters[v];
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

    public void setRandomViseme(int choice)
    {
        setVisemeNeutral();
        List<string> values = Enumerable.ToList(visemeAnimationParameters.Keys);
        visemeAnimationParameters[values.ElementAt(choice)] = 100;
    }



    public void setVisemeNeutral()
    {
        List<string> values = Enumerable.ToList(visemeAnimationParameters.Keys);
        foreach (string v in values)
        {
            visemeAnimationParameters[v] = 0;
        }

    }

    public void setFaceNeutral()
    {
        List<int> values = Enumerable.ToList(faceAnimationParameters.Keys);
        foreach (int v in values)
        {
            faceAnimationParameters[v].Value = 0;
        }
    }
    /*
     * On vient animer les Blendshapes des lèvres à l'aide de l'interpolation entre nos deux valeurs
     */
    public void lerpViseme(float lerp)
    {
        foreach (SkinnedMeshRenderer SkinnedMeshRendererTarget in skinnedMeshRenderers)
        {
            Mesh m = SkinnedMeshRendererTarget.sharedMesh;
            foreach (KeyValuePair<string, int> t in visemeAnimationParameters)
            {
                int i = getBlendShapeIndex(SkinnedMeshRendererTarget, t.Key);
                if (i >= 0)
                    SkinnedMeshRendererTarget.SetBlendShapeWeight(i, (int)Mathf.Lerp(visemeAnimationParameters_Back[t.Key], visemeAnimationParameters[t.Key], lerp));

            }
        }

    }

    /*
     * On vient animer les BlendShapes du visage (correspondant plus ou moins aux AUs) à l'aide de l'interpolation
     */
    public void lerpFace(float lerp)
    {
        foreach (SkinnedMeshRenderer SkinnedMeshRendererTarget in skinnedMeshRenderers)
        {
            Mesh m = SkinnedMeshRendererTarget.sharedMesh;
            foreach (KeyValuePair<int, AnimationParameter> t in faceAnimationParameters)
            {
                foreach (string name in t.Value.Names)
                {
                    int i = getBlendShapeIndex(SkinnedMeshRendererTarget, name);
                    if (i >= 0)
                        SkinnedMeshRendererTarget.SetBlendShapeWeight(i, (int)Mathf.Lerp(0, faceAnimationParameters[t.Key].Value, animationCurve.Evaluate(lerp)));

                }
            }
        }
    }

    public void blink()
    {
        foreach (SkinnedMeshRenderer SkinnedMeshRendererTarget in skinnedMeshRenderers)
        {
            Mesh m = SkinnedMeshRendererTarget.sharedMesh;

            int i = getBlendShapeIndex(SkinnedMeshRendererTarget, "eyeBlinkLeft");
            int j = getBlendShapeIndex(SkinnedMeshRendererTarget, "eyeBlinkRight");
            if (i >= 0 && j >= 0)
            {
                SkinnedMeshRendererTarget.SetBlendShapeWeight(i, 100);
                SkinnedMeshRendererTarget.SetBlendShapeWeight(j, 100);
            }


        }
    }
    public void unblink()
    {
        foreach (SkinnedMeshRenderer SkinnedMeshRendererTarget in skinnedMeshRenderers)
        {
            Mesh m = SkinnedMeshRendererTarget.sharedMesh;

            int i = getBlendShapeIndex(SkinnedMeshRendererTarget, "eyeBlinkLeft");
            int j = getBlendShapeIndex(SkinnedMeshRendererTarget, "eyeBlinkRight");
            if (i >= 0 && j >= 0)
            {
                SkinnedMeshRendererTarget.SetBlendShapeWeight(i, 0);
                SkinnedMeshRendererTarget.SetBlendShapeWeight(j, 0);
            }

        }
    }


}