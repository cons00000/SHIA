using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class ChicCafePro : MonoBehaviour
{
    [Header("Design du Café")]
    [SerializeField] private Color wallColor = new Color(0.96f, 0.93f, 0.88f); // Crème
    [SerializeField] private Color frameColor = new Color(0.1f, 0.1f, 0.1f);   // Noir métal
    [SerializeField] private float windowWidth = 6.0f;
    [SerializeField] private float windowHeight = 4.0f;

    [Header("Végétation Extérieure")]
    [SerializeField] private Color leafColor = new Color(0.13f, 0.35f, 0.12f);
    [SerializeField, Range(10, 40)] private int plantDensity = 25;

    [Header("Éclairage")]
    [SerializeField] private float sunIntensity = 3.2f;
    [SerializeField] private Vector3 sunRotation = new Vector3(50, 35, 0);

    [Header("Références (Auto-assignées)")]
    [SerializeField] private Camera mainCam;
    [SerializeField] private Light sunLight;

    [Header("Options")]
    [SerializeField] private bool autoRebuildInEditor = false;

    private const string RootName = "Generated_Cafe";

    [ContextMenu("Construire le Café")]
    public void BuildScene()
    {
        Transform root = GetOrCreateRoot();
        Clear(root);

        // 1. Structure (Sol et Murs)
        CreateBox(root, "Floor", new Vector3(0, -0.05f, 5), new Vector3(15, 0.1f, 20), wallColor * 0.9f, 0.6f);
        CreateBox(root, "Wall_Left", new Vector3(-5, 2.5f, 8), new Vector3(4, 5, 0.5f), wallColor, 0.1f);
        CreateBox(root, "Wall_Right", new Vector3(5, 2.5f, 8), new Vector3(4, 5, 0.5f), wallColor, 0.1f);
        CreateBox(root, "Wall_Top", new Vector3(0, 4.5f, 8), new Vector3(10, 1, 0.5f), wallColor, 0.1f);

        // 2. La Baie Vitrée
        CreateWindowStructure(root);

        // 3. Jardin Extérieur
        CreateExteriorGarden(root);

        // 4. Caméra & Lumière
        SetupTechnicals();

        Debug.Log("[ChicCafePro] Café chic construit avec succès !");
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && autoRebuildInEditor)
        {
            EditorApplication.delayCall += () =>
            {
                if (this != null)
                    BuildScene();
            };
        }
#endif
    }

    private Transform GetOrCreateRoot()
    {
        Transform root = transform.Find(RootName);

        if (root == null)
        {
            GameObject rootObj = new GameObject(RootName);
            rootObj.transform.SetParent(transform, false);
            root = rootObj.transform;
        }

        return root;
    }

    private void CreateCharacterFillLight()
    {
        Transform existing = transform.Find("Character_Fill_Light");
        if (existing != null)
        {
    #if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(existing.gameObject);
            else Destroy(existing.gameObject);
    #else
            Destroy(existing.gameObject);
    #endif
        }

        GameObject fillObj = new GameObject("Character_Fill_Light");
        fillObj.transform.SetParent(transform, false);
        fillObj.transform.position = new Vector3(0f, 2.3f, -1.3f); // un peu plus loin du visage

        Light fill = fillObj.AddComponent<Light>();
        fill.type = LightType.Point;
        fill.intensity = 0.75f;   // 🔥 réduit fortement
        fill.range = 10f;         // un peu moins large
        fill.shadows = LightShadows.None;
        fill.color = new Color(1f, 0.96f, 0.92f); // légèrement chaud
    }

    private void CreateWindowStructure(Transform parent)
    {
        Vector3 windowPos = new Vector3(0, 2.25f, 8.1f);

        // Cadres
        CreateBox(parent, "Frame_Bottom", new Vector3(0, 0.15f, 8), new Vector3(windowWidth + 0.2f, 0.3f, 0.4f), frameColor, 0.5f);
        CreateBox(parent, "Frame_Top", new Vector3(0, 4.35f, 8), new Vector3(windowWidth + 0.2f, 0.3f, 0.4f), frameColor, 0.5f);
        CreateBox(parent, "Frame_Left", new Vector3(-(windowWidth * 0.5f), 2.25f, 8), new Vector3(0.2f, windowHeight, 0.4f), frameColor, 0.5f);
        CreateBox(parent, "Frame_Right", new Vector3(windowWidth * 0.5f, 2.25f, 8), new Vector3(0.2f, windowHeight, 0.4f), frameColor, 0.5f);
        CreateBox(parent, "Frame_Mid", new Vector3(0, 2.25f, 8), new Vector3(0.2f, windowHeight, 0.4f), frameColor, 0.5f);

        // Vitre
        GameObject glass = GameObject.CreatePrimitive(PrimitiveType.Quad);
        glass.name = "Main_Window_Glass";
        glass.transform.SetParent(parent, false);
        glass.transform.position = windowPos;
        glass.transform.rotation = Quaternion.identity;
        glass.transform.localScale = new Vector3(windowWidth, windowHeight, 1f);

        Renderer renderer = glass.GetComponent<Renderer>();
        renderer.sharedMaterial = CreateGlassMaterial();

        // Désactiver le collider inutile sur la vitre
        Collider col = glass.GetComponent<Collider>();
        if (col != null)
            DestroyImmediate(col);
    }

    private void CreateExteriorGarden(Transform parent)
    {
        // Seed fixe pour éviter un rendu qui change à chaque rebuild en éditeur
        Random.InitState(12345);

        for (int i = 0; i < plantDensity; i++)
        {
            float x = Random.Range(-8f, 8f);
            float z = Random.Range(9.5f, 15f); // Derrière la vitre
            float scale = Random.Range(1.5f, 4f);

            GameObject bush = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bush.name = $"Plant_{i:D2}";
            bush.transform.SetParent(parent, false);
            bush.transform.position = new Vector3(x, (scale * 0.5f) - 0.5f, z);
            bush.transform.localScale = new Vector3(scale * 0.8f, scale, scale * 0.8f);

            Color c = leafColor * Random.Range(0.7f, 1.2f);

            Renderer renderer = bush.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateSimpleMaterial(c, 0.05f);

            // Optionnel : pas de collider pour déco
            Collider col = bush.GetComponent<Collider>();
            if (col != null)
                DestroyImmediate(col);
        }
    }

    private void SetupTechnicals()
    {
        // Lumière
        if (sunLight == null)
        {
            GameObject lightObj = GameObject.Find("Directional Light");
            if (lightObj != null)
                sunLight = lightObj.GetComponent<Light>();
        }

        if (sunLight != null)
        {
            sunLight.transform.rotation = Quaternion.Euler(sunRotation);
            sunLight.intensity = sunIntensity;
            sunLight.shadows = LightShadows.Soft;
            sunLight.type = LightType.Directional;
        }
        CreateCharacterFillLight();

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.75f, 0.78f, 0.8f);
        
        // Caméra
        if (mainCam == null)
            mainCam = Camera.main;

        if (mainCam != null)
        {
            mainCam.transform.position = new Vector3(0, 1.5f, -3.8f);
            mainCam.transform.rotation = Quaternion.Euler(6f, 0f, 0f);
            mainCam.fieldOfView = 40f;
            mainCam.backgroundColor = new Color(0.82f, 0.9f, 1f); // ciel clair
            mainCam.clearFlags = CameraClearFlags.SolidColor;
        }
    }

    // --- Helpers ---

    private void CreateBox(Transform parent, string objectName, Vector3 position, Vector3 scale, Color color, float smoothness)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = objectName;
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = scale;

        Renderer renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = CreateSimpleMaterial(color, smoothness);
    }

    private Material CreateSimpleMaterial(Color color, float smoothness)
    {
        Shader shader = Shader.Find("Standard");
        Material material = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Lit"));

        material.color = color;

        if (material.HasProperty("_Glossiness"))
            material.SetFloat("_Glossiness", smoothness);

        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", smoothness);

        return material;
    }

    private Material CreateGlassMaterial()
    {
        Shader shader = Shader.Find("Standard");
        Material material = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Lit"));

        Color glassColor = new Color(0.9f, 0.95f, 1f, 0.25f);
        material.color = glassColor;

        // Compatibilité Standard Shader
        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3); // Transparent
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
        }

        // Compatibilité URP Lit
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1); // Transparent
            material.SetFloat("_Blend", 0);   // Alpha
            material.SetFloat("_ZWrite", 0);
            material.renderQueue = 3000;
        }

        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.95f);

        return material;
    }

    private void Clear(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);

#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(child.gameObject);
            else
                Destroy(child.gameObject);
#else
            Destroy(child.gameObject);
#endif
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(ChicCafePro))]
public class ChicCafeProEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);

        if (GUILayout.Button("CONSTRUIRE LE CAFÉ"))
        {
            ChicCafePro cafe = (ChicCafePro)target;
            cafe.BuildScene();

            EditorUtility.SetDirty(cafe);
        }

        if (GUILayout.Button("SUPPRIMER LE CAFÉ"))
        {
            ChicCafePro cafe = (ChicCafePro)target;
            Transform root = cafe.transform.Find("Generated_Cafe");

            if (root != null)
            {
                for (int i = root.childCount - 1; i >= 0; i--)
                    DestroyImmediate(root.GetChild(i).gameObject);

                DestroyImmediate(root.gameObject);
            }

            EditorUtility.SetDirty(cafe);
        }
    }
}
#endif