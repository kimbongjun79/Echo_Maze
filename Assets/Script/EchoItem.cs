using UnityEngine;

// The frame is the stationary aim target. Only GemVisual morphs out of its image plane;
// cancellation retracts it without collecting, and collection retains the frame/stool.
public class EchoItem : MonoBehaviour
{
    public Color color = Color.white;
    public float radius = 3, interval = 4;
    public AudioClip clip;
    [field: SerializeField] public Transform AimTarget { get; private set; }
    [SerializeField] Transform display, frame;
    public bool Available => !collecting && !EchoMaze.Over;

    const float ExtractionDistance = .35f, ReturnDuration = .25f, FlightDuration = .5f;
    static readonly int DepthId = Shader.PropertyToID("_Depth");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static Mesh gemMesh;
    static Shader gemShader;
    Transform visual, collector;
    Material gemMaterial;
    Vector3 imagePosition, outward, flightStart;
    float extraction, requestedExtraction, returnStart, returnTime, flightTime, t;
    bool collecting;
    System.Action onCollected;
    AudioSource au;

    // Editor setup stores the actual FBX frame and its persistent display parent in the Resources prefab.
    public void ConfigureDisplay(Transform displayRoot, Transform frameRoot)
    {
        display = displayRoot;
        frame = frameRoot;
        AimTarget = frameRoot;
    }

    // Called once by EchoMaze after the display has its final wall-mounted world pose.
    // The shared 20-triangle cut is .20m wide/.26m tall; _Depth explicitly flattens
    // its vertices, not the Transform, so normals remain usable in the 2D depiction.
    public void InitializeGem(Color gemColor)
    {
        color = gemColor;
        if (visual)
        {
            gemMaterial.SetColor(ColorId, color);
            return;
        }
        if (!gemMesh) gemMesh = BuildGemMesh();
        if (!gemShader) gemShader = Resources.Load<Shader>("EchoGem");
        gemMaterial = new Material(gemShader) { name = "Echo Gem" };
        gemMaterial.SetColor(ColorId, color);
        visual = new GameObject("GemVisual").transform;
        visual.SetParent(transform, false);
        visual.gameObject.AddComponent<MeshFilter>().sharedMesh = gemMesh;
        visual.gameObject.AddComponent<MeshRenderer>().sharedMaterial = gemMaterial;
        outward = frame.forward;
        imagePosition = frame.position;
        float front = 0;
        // Project the world bounds onto the frame normal, including its raised border,
        // instead of placing the flat gem at the frame pivot behind the image surface.
        foreach (var renderer in frame.GetComponentsInChildren<Renderer>())
        {
            var bounds = renderer.bounds;
            var extents = bounds.extents;
            float projectedExtent = Mathf.Abs(outward.x) * extents.x
                + Mathf.Abs(outward.y) * extents.y + Mathf.Abs(outward.z) * extents.z;
            front = Mathf.Max(front, Vector3.Dot(bounds.center - imagePosition, outward) + projectedExtent);
        }
        imagePosition += outward * (front + .006f);
        visual.rotation = frame.rotation;
        SetVisualExtraction(0);
    }

    // Player supplies its normalized 2.5-second hold every frame. A zero request
    // starts one .25-second suction animation; repeated cancellation cannot restart it.
    public void SetExtractionProgress(float normalized)
    {
        if (collecting || !visual) return;
        normalized = Mathf.Clamp01(normalized);
        if (normalized > 0)
        {
            requestedExtraction = normalized;
        }
        else if (requestedExtraction > 0)
        {
            requestedExtraction = 0;
            returnStart = extraction;
            returnTime = 0;
        }
    }

    void SetVisualExtraction(float progress)
    {
        extraction = progress;
        gemMaterial.SetFloat(DepthId, progress);
        visual.position = imagePosition + outward * (ExtractionDistance * progress);
    }

    public void Collect(Transform player, System.Action onCollected)
    {
        if (!Available || !player || !visual) return;
        collecting = true;
        collector = player;
        this.onCollected = onCollected;
        EchoMaze.Items.Remove(transform);
        if (au) au.Stop();
        // Preserve the exact final preview pose: no second emergence or forced morph.
        flightStart = visual.position;
        frame.GetComponent<Collider>().enabled = false;
        // The frame collider resolves GetComponentInParent<EchoItem>() only while available.
        frame.SetParent(display, true);
    }

    void Start()
    {
        t = Random.value * interval;
        au = gameObject.AddComponent<AudioSource>(); au.spatialBlend = 1; au.spatialize = true; au.rolloffMode = AudioRolloffMode.Linear; au.maxDistance = 12;
        if (!clip) clip = EchoMaze.Clip(EchoMaze.Band(1800, 12, .01f, .8f, 3));
    }

    void Update()
    {
        if (collecting)
        {
            if (EchoMaze.Over || !collector)
            {
                onCollected = null;
                Destroy(gameObject);
                return;
            }
            flightTime += Time.deltaTime;
            // Re-evaluate the destination every frame so the gem follows a moving player.
            var destination = collector.position + collector.forward * .2f;
            visual.position = Vector3.Lerp(flightStart, destination,
                Mathf.SmoothStep(0, 1, Mathf.Clamp01(flightTime / FlightDuration)));
            if (flightTime >= FlightDuration)
            {
                var completed = onCollected;
                onCollected = null;
                enabled = false;
                Destroy(gameObject);
                completed?.Invoke();
            }
            return;
        }
        // Re-entering a hold during suction follows the new target without resetting
        // the current visual pose; normal holding advances slower than this bound.
        if (requestedExtraction > 0 && extraction != requestedExtraction)
            SetVisualExtraction(Mathf.MoveTowards(extraction, requestedExtraction, 4 * Time.deltaTime));
        else if (requestedExtraction == 0 && extraction > 0)
        {
            returnTime += Time.deltaTime;
            SetVisualExtraction(Mathf.Lerp(returnStart, 0,
                Mathf.SmoothStep(0, 1, Mathf.Clamp01(returnTime / ReturnDuration))));
        }
        if (EchoMaze.Over || (t -= Time.deltaTime) > 0) return;
        t = interval; EchoMaze.Pulse(transform.position, radius, false, color); au.PlayOneShot(clip, .4f);
    }

    void OnDestroy()
    {
        if (gemMaterial) Destroy(gemMaterial);
    }

    static Mesh BuildGemMesh()
    {
        // Clockwise diamond silhouette, inset front table/bezel, and pointed pavilion.
        // Each triangle owns its vertices so normal interpolation cannot round the cut.
        var rim = new[]
        {
            new Vector3(-.055f, .13f, 0), new Vector3(.055f, .13f, 0),
            new Vector3(.10f, .04f, 0), new Vector3(0, -.13f, 0),
            new Vector3(-.10f, .04f, 0)
        };
        var vertices = new Vector3[60];
        var normals = new Vector3[60];
        var triangles = new int[60];
        var tableCenter = new Vector3(0, .015f, .055f);
        var pavilion = new Vector3(0, -.01f, -.065f);
        int index = 0;
        for (int i = 0; i < rim.Length; i++)
        {
            int next = (i + 1) % rim.Length;
            var tableA = rim[i] * .45f + tableCenter;
            var tableB = rim[next] * .45f + tableCenter;
            AddFacet(tableCenter, tableA, tableB, Vector3.forward, vertices, normals, triangles, ref index);
            AddFacet(tableA, rim[i], rim[next], Vector3.forward, vertices, normals, triangles, ref index);
            AddFacet(tableA, rim[next], tableB, Vector3.forward, vertices, normals, triangles, ref index);
            AddFacet(pavilion, rim[next], rim[i], Vector3.back, vertices, normals, triangles, ref index);
        }
        var mesh = new Mesh { name = "Shared Diamond Cut", vertices = vertices, normals = normals, triangles = triangles };
        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);
        return mesh;
    }

    static void AddFacet(Vector3 a, Vector3 b, Vector3 c, Vector3 facing,
        Vector3[] vertices, Vector3[] normals, int[] triangles, ref int index)
    {
        var normal = Vector3.Cross(b - a, c - a);
        if (Vector3.Dot(normal, facing) < 0)
        {
            var swap = b;
            b = c;
            c = swap;
            normal = -normal;
        }
        normal.Normalize();
        vertices[index] = a;
        vertices[index + 1] = b;
        vertices[index + 2] = c;
        for (int i = 0; i < 3; i++, index++)
        {
            normals[index] = normal;
            triangles[index] = index;
        }
    }
}
