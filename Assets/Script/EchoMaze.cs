using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class EchoMaze : MonoBehaviour
{
    static readonly float[] Hy = new float[256];
    public Material face, line;
    [Tooltip("비우면 기본 도형 사용. 캐비넷 피벗 = 바닥 중앙, 정면(+Z) = 통로 방향")]
    public GameObject monsterPrefab, cabinetPrefab;
    [Range(0, 5)] public float echoIntensity = 1;
    [Range(0, 1)] public float echoContrast = .5f;
    public float echoContrastRange = 8;
    public int N = 10;
    public bool randomEachRun = true;
    [Tooltip("# 벽, . 일반, C 캐비넷, I 아이템 (행 = z, 열 = x)")]
    public string[] map;
    public const float C = 2, H = 3;
    public static readonly (int x, int y)[] D = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    public static int G;
    public static bool[,] W;
    public static readonly List<Vector2Int> Free = new();
    public static readonly List<Transform> Items = new();
    public static readonly List<(Transform t, Vector3 o, float yaw)> Cabs = new();
    public static bool Over;
    public static int Total;
    public static string Msg;
    static float[,] OP;
    static readonly Vector4[] P = new Vector4[16], Col = new Vector4[16], Hs = new Vector4[256];
    static readonly float[] R = new float[16];
    static int idx, hi, pi;
    static byte[] Dt;
    static Texture2D Tex;
    struct Part { public Vector3 p, v; public float e, d; public bool red, on; }
    static readonly Part[] Pt = new Part[800];
    readonly List<Vector3> v = new();
    readonly List<int> id = new();

    // 소리 1회: 경로 거리장 기록 + 파동 등록 + 음파 입자 방사
    public static int[,] Pulse(Vector3 p, float r, bool red = false, Color? col = null)
    {
        var d = Bfs(Cell(p), (int)(r / C) + 1); int o = idx * G * G;
        for (int z = 0; z < G; z++) for (int x = 0; x < G; x++) Dt[o + z * G + x] = (byte)Mathf.Min(255f, d[x, z] * C * 4);
        Tex.SetPixelData(Dt, 0); Tex.Apply(false);
        P[idx] = new Vector4(p.x, p.y, p.z, Time.time); R[idx] = r; Col[idx] = col ?? (red ? new Color(1, .1f, .1f) : Color.white); idx = (idx + 1) % 16;
        for (int i = 0, n = 6 + (int)(r * 1.5f); i < n; i++)
        {
            float a = (i + Random.value) / n * Mathf.PI * 2;
            Pt[pi++ % Pt.Length] = new Part { p = new(p.x, .3f + Random.value * (H - .6f), p.z), v = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 20, e = 1, d = r, red = red, on = true };
        }
        return d;
    }

    public static Vector2Int Cell(Vector3 p) => new(Mathf.RoundToInt(p.x / C), Mathf.RoundToInt(p.z / C));
    public static bool Wall(Vector3 p) { var c = Cell(p); return W[c.x, c.y]; }

    // 격자 BFS 거리장 (칸 단위, 미도달 = int.MaxValue)
    public static int[,] Bfs(Vector2Int s, int max)
    {
        var d = new int[G, G]; for (int x = 0; x < G; x++) for (int z = 0; z < G; z++) d[x, z] = int.MaxValue;
        var q = new Queue<Vector2Int>(); d[s.x, s.y] = 0; q.Enqueue(s);
        while (q.Count > 0)
        {
            var c = q.Dequeue(); if (d[c.x, c.y] >= max) continue;
            foreach (var (a, b) in D) { var n = new Vector2Int(c.x + a, c.y + b); if (!W[n.x, n.y] && d[n.x, n.y] == int.MaxValue) { d[n.x, n.y] = d[c.x, c.y] + 1; q.Enqueue(n); } }
        }
        return d;
    }

    // 사운드 합성: 밴드패스 노이즈(+선택: 하강 톱니파) + 선형 엔벨로프
    public static float[] Band(float f, float q, float a, float r, float g = 1, float saw = 0)
    {
        int sr = 44100, n = (int)((a + r) * sr); var d = new float[n];
        float w = 2 * Mathf.PI * f / sr, al = Mathf.Sin(w) / (2 * q), c = -2 * Mathf.Cos(w), x1 = 0, x2 = 0, y1 = 0, y2 = 0, ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / sr, x = Random.value * 2 - 1;
            if (saw > 0) { ph = (ph + saw * Mathf.Lerp(1, .42f, t / (a + r)) / sr) % 1; x = x * .5f + ph * 2 - 1; }
            float y = (al * x - al * x2 - c * y1 - (1 - al) * y2) / (1 + al); x2 = x1; x1 = x; y2 = y1; y1 = y;
            d[i] = y * g * (t < a ? t / a : 1 - (t - a) / r);
        }
        return d;
    }
    public static AudioClip Clip(params float[][] s) { var d = s.SelectMany(x => x).ToArray(); var c = AudioClip.Create("sfx", d.Length, 1, 44100, false); c.SetData(d, 0); return c; }

    // 랜덤 맵 문자열 생성: Prim 미로 + 아이템 5 + 막다른 길 캐비넷 최대 6
    public static string[] Generate(int n)
    {
        int g = n * 2 + 1; var w = new bool[g, g];
        for (int x = 0; x < g; x++) for (int z = 0; z < g; z++) w[x, z] = true;
        var fr = new List<(int x, int z, int px, int pz)> { (1, 1, 1, 1) };
        while (fr.Count > 0)
        {
            int i = Random.Range(0, fr.Count); var (x, z, px, pz) = fr[i]; fr[i] = fr[^1]; fr.RemoveAt(fr.Count - 1);
            if (!w[x, z]) continue; w[x, z] = w[(x + px) / 2, (z + pz) / 2] = false;
            foreach (var (a, b) in D) { int X = x + a * 2, Z = z + b * 2; if (X > 0 && Z > 0 && X < g && Z < g && w[X, Z]) fr.Add((X, Z, x, z)); }
        }
        var m = new char[g][];
        for (int z = 0; z < g; z++) { m[z] = new char[g]; for (int x = 0; x < g; x++) m[z][x] = w[x, z] ? '#' : '.'; }
        var fs = new List<Vector2Int>();
        for (int x = 1; x < g; x += 2) for (int z = 1; z < g; z += 2) if (x + z > 2 && !(x == g - 2 && z == g - 2)) fs.Add(new(x, z));
        fs = fs.OrderBy(_ => Random.value).ToList();
        for (int i = 0; i < 5; i++) m[fs[i].y][fs[i].x] = 'I';
        int c = 0;
        foreach (var f in fs.Skip(5)) if (c < 6 && D.Count(q => !w[f.x + q.x, f.y + q.y]) == 1) { m[f.y][f.x] = 'C'; c++; }
        return m.Select(r => new string(r)).ToArray();
    }
    public static string[] Empty(int g) => Enumerable.Range(0, g).Select(z => new string(Enumerable.Range(0, g).Select(x => x == 0 || z == 0 || x == g - 1 || z == g - 1 ? '#' : '.').ToArray())).ToArray();

    public static void End(string m) { Over = true; Msg = m; Cursor.lockState = CursorLockMode.None; }

    void L(Vector3 a, Vector3 b) { id.Add(v.Count); v.Add(a); id.Add(v.Count); v.Add(b); }
    void Box(Vector3 c, Vector3 h)
    {
        Vector3 K(int i) => c + Vector3.Scale(h, new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1));
        for (int i = 0; i < 8; i++) for (int b = 1; b < 8; b <<= 1) if ((i & b) == 0) L(K(i), K(i | b));
    }
    Transform Prim(PrimitiveType t, Vector3 p, Vector3 s, Material m)
    {
        var g = GameObject.CreatePrimitive(t).transform; g.position = p; g.localScale = s; g.GetComponent<Renderer>().sharedMaterial = m; return g;
    }
    Material Tint(Color c) { var m = new Material(line); m.SetColor("_Color", c); return m; }

    static bool TryMountDirection(Vector2Int cell, out Vector3 direction)
    {
        direction = Vector3.zero;
        foreach (var (x, z) in D)
        {
            if (!W[cell.x + x, cell.y + z]) continue;
            direction = new Vector3(-x, 0, -z);
            // Prefer a wall with open space directly in front, but any real adjacent wall is valid.
            if (!W[cell.x - x, cell.y - z]) return true;
        }
        return direction != Vector3.zero;
    }

    static Vector2Int DisplayCell(Vector2Int source, bool[,] occupied)
    {
        if (TryMountDirection(source, out _)) return source;
        var distances = Bfs(source, G * G);
        int nearest = int.MaxValue;
        var chosen = new Vector2Int(-1, -1);
        foreach (var cell in Free)
        {
            if (occupied[cell.x, cell.y] || (cell.x == 1 && cell.y == 1)
                || (cell.x == G - 2 && cell.y == G - 2)
                || distances[cell.x, cell.y] >= nearest || !TryMountDirection(cell, out _)) continue;
            chosen = cell;
            nearest = distances[cell.x, cell.y];
        }
        if (chosen.x < 0)
            throw new System.InvalidOperationException($"No unoccupied reachable wall-adjacent cell for item at {source}.");
        return chosen;
    }

    void Awake()
    {
        Over = false; Free.Clear(); Items.Clear(); Cabs.Clear();
        for (int i = 0; i < 16; i++) P[i].w = -99;
        for (int i = 0; i < 256; i++) Hs[i].z = -99;
        for (int i = 0; i < Pt.Length; i++) Pt[i].on = false;

        // 맵 읽기: 외곽·시작(1,1)·몬스터(G-2,G-2) 칸은 강제
        var mp = randomEachRun || map == null || map.Length < 5 ? Generate(N) : map;
        G = mp.Length; W = new bool[G, G];
        for (int z = 0; z < G; z++) for (int x = 0; x < G; x++) W[x, z] = x == 0 || z == 0 || x == G - 1 || z == G - 1 || x >= mp[z].Length || mp[z][x] == '#';
        W[1, 1] = W[G - 2, G - 2] = false;
        // 잔향 시간: 3x3 개방도가 낮을수록(좁을수록) 김
        OP = new float[G, G];
        for (int x = 1; x < G - 1; x++) for (int z = 1; z < G - 1; z++) { int n = 0; for (int a = -1; a < 2; a++) for (int b = -1; b < 2; b++) n += W[x + a, z + b] ? 0 : 1; OP[x, z] = 2.6f - n * .3f; }

        var h = new Vector3(C, H, C) / 2;
        for (int z = 0; z < G; z++)
            for (int x = 0; x < G; x++)
            {
                if (!W[x, z]) continue;
                var c = new Vector3(x * C, H / 2, z * C);
                Prim(PrimitiveType.Cube, c, h * 2, face);
                Box(c, h);
            }
        float S = G * C, o = -C / 2;
        foreach (var y in new[] { 0, H })
        {
            // 바닥: 윗면 y=-0.01 (y=0 격자선과 겹치지 않음), 플레이어 충돌용 콜라이더 포함
            Prim(PrimitiveType.Cube, new(o + S / 2, -.06f, o + S / 2), new(S, .1f, S), face);
            for (int k = 0; k <= S; k++) { L(new(o + k, y, o), new(o + k, y, o + S)); L(new(o, y, o + k), new(o + S, y, o + k)); }
        }


        // Reserve every authored display/cabinet before relocation, so iteration order cannot overlap them.
        // Relocation changes furniture positions only: W/Free and all navigation paths remain intact.
        var occupied = new bool[G, G];
        for (int z = 1; z < G - 1; z++)
            for (int x = 1; x < G - 1; x++)
            {
                if (W[x, z]) continue;
                Free.Add(new(x, z));
                occupied[x, z] = x < mp[z].Length && (mp[z][x] == 'I' || mp[z][x] == 'C');
            }

        // I: wall-mounted displays; C: cabinets against the wall opposite their open direction.
        Color[] ic = { new(1, .87f, .2f), new(.2f, .87f, 1), new(.4f, 1, .4f), new(1, .4f, 1), new(1, .6f, .2f) };
        // Tools/Echo Maze/Build Item Display generates a Resources prefab with serialized FBX references.
        // Resources keeps the real stool/frame meshes and echo materials available in player builds.
        var itemDisplay = Resources.Load<GameObject>("EchoItemDisplay");
        for (int z = 0; z < G; z++)
            for (int x = 0; x < G; x++)
            {
                if (W[x, z] || x >= mp[z].Length) continue;
                var cc = new Vector3(x * C, 1, z * C);
                if (mp[z][x] == 'I')
                {
                    if (!itemDisplay) throw new System.InvalidOperationException("Run Tools/Echo Maze/Build Item Display before playing or building.");
                    var cell = DisplayCell(new(x, z), occupied);
                    occupied[cell.x, cell.y] = true;
                    TryMountDirection(cell, out var dir);
                    // Root is the actual wall surface. Prefab mesh rear faces are local Z=0,
                    // frame center Y=1.65m, stool floor Y=0, and local +Z faces the corridor.
                    var wallSurface = new Vector3(cell.x * C, 0, cell.y * C) - dir * (C * .5f);
                    var stand = Instantiate(itemDisplay, wallSurface, Quaternion.LookRotation(dir));
                    var item = stand.GetComponentInChildren<EchoItem>();
                    var icol = ic[Items.Count % ic.Length];
                    item.InitializeGem(icol);
                    Items.Add(item.transform);
                }
                else if (mp[z][x] == 'C')
                {
                    // 캐비넷: 벽 쪽에 등을 붙임(맞은편이 열린 벽 우선), 정면은 타일 중앙을 바라봄
                    var wl = D.Where(q => W[x + q.x, z + q.y]).OrderBy(q => W[x - q.x, z - q.y] ? 1 : 0).ToArray(); if (wl.Length == 0) continue;
                    var dir = new Vector3(-wl[0].x, 0, -wl[0].y); var cp = cc - dir * .5f; Transform cab;
                    if (cabinetPrefab) cab = Instantiate(cabinetPrefab, new(cp.x, 0, cp.z), Quaternion.identity).transform;
                    else { cab = Prim(PrimitiveType.Cube, cp, new(.9f, 2, .9f), face); Box(cp, new(.45f, 1, .45f)); }
                    var look = new Vector3(cc.x - cab.position.x, 0, cc.z - cab.position.z); cab.rotation = Quaternion.LookRotation(look);
                    Cabs.Add((cab, cc + dir * .6f, cab.eulerAngles.y));
                }
            }
        Total = Items.Count;
        // 모델 프리팹은 바닥 피벗, 기본 구체는 중심 높이 1. 접촉은 수평 거리 판정.
        var mp0 = new Vector3((G - 2) * C, monsterPrefab ? 0 : 1, (G - 2) * C);
        var mo = monsterPrefab ? Instantiate(monsterPrefab, mp0, Quaternion.identity).transform : Prim(PrimitiveType.Sphere, mp0, Vector3.one * .6f, Tint(new Color(1, .15f, .15f)));
        foreach (var col in mo.GetComponentsInChildren<Collider>()) Destroy(col);
        if (!mo.GetComponent<EchoMonster>()) mo.gameObject.AddComponent<EchoMonster>();

        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; m.SetVertices(v); m.SetIndices(id, MeshTopology.Lines, 0);
        var g = new GameObject("Lines");
        g.AddComponent<MeshFilter>().sharedMesh = m;
        g.AddComponent<MeshRenderer>().sharedMaterial = line;

        // 경로 거리장 텍스처: 파동 16개 x G행, 1/4 유닛 단위
        Dt = new byte[G * G * 16];
        Tex = new Texture2D(G, G * 16, TextureFormat.R8, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        Shader.SetGlobalTexture("_EchoD", Tex); Shader.SetGlobalFloat("_EchoG", G); Shader.SetGlobalFloat("_EchoCell", C);
    }

    void Update()
    {
        // 음파 입자(비표시): 벽 충돌 시 반사(에너지 x0.6), 세기 .5 초과 충돌만 발광 기록
        for (int k = 0; k < Pt.Length; k++)
        {
            ref var q = ref Pt[k]; if (!q.on) continue;
            var np = q.p + q.v * Time.deltaTime;
            if (Wall(np))
            {
                if (q.e > .5f) { var c = Cell(q.p); Hy[hi % 256] = q.p.y; Hs[hi++ % 256] = new Vector4(q.p.x, q.p.z, Time.time, (q.red ? -1 : 1) * (Mathf.Floor(OP[c.x, c.y] * 10) + q.e * .9f)); }
                bool bx = Wall(new(np.x, 0, q.p.z)), bz = Wall(new(q.p.x, 0, np.z));
                if (bx || !bz) q.v.x *= -1; if (bz || !bx) q.v.z *= -1; q.e *= .6f;
            }
            else q.p = np;
            if ((q.d -= 20 * Time.deltaTime) < 0 || q.e < .2f) q.on = false;
        }

        Shader.SetGlobalFloat("_EchoTime", Time.time);
        Shader.SetGlobalFloat("_EchoI", echoIntensity);
        Shader.SetGlobalFloat("_EchoCt", echoContrast);
        Shader.SetGlobalFloat("_EchoCR", Mathf.Max(.01f, echoContrastRange));
        Shader.SetGlobalVectorArray("_EchoP", P);
        Shader.SetGlobalFloatArray("_EchoR", R);
        Shader.SetGlobalVectorArray("_EchoC", Col);
        Shader.SetGlobalVectorArray("_EchoH", Hs);
        Shader.SetGlobalFloatArray("_EchoHY", Hy);
    }
}
