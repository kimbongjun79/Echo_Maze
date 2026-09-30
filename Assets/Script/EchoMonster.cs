using UnityEngine;

// 몬스터: 청각 전용 AI. mode 0 배회, 1 소리 추적, 2 근접 추격(경로 2칸 이내, 플레이어 위치 직접 추격)
public class EchoMonster : MonoBehaviour
{
    public static EchoMonster I;
    public float[] speed = { 1, 2, 3.2f };
    public int mode;
    Vector2Int? goal, hearC;
    Vector3 tg;
    float dist, bt, hearT;
    AudioSource au;
    AudioClip stepC, breathC, roarC;

    void Awake() => I = this;

    void Start()
    {
        tg = transform.position;
        au = gameObject.AddComponent<AudioSource>(); au.spatialBlend = 1; au.spatialize = true;
        stepC = EchoMaze.Clip(EchoMaze.Band(70, 1, .01f, .3f, 4));
        breathC = EchoMaze.Clip(EchoMaze.Band(220, 2, .5f, .8f, 3));
        roarC = EchoMaze.Clip(EchoMaze.Band(300, .7f, .1f, 1.5f, 3, 95));
    }

    // 청각: 소리의 BFS 거리장(Pulse 결과)으로 경로 거리 판정, 파동 도달 시간(거리/6)만큼 지연 반응
    public static void Hear(Vector3 p, float r, int[,] D)
    {
        var m = I; var e = EchoMaze.Cell(m.transform.position);
        if (m.mode == 2 || D[e.x, e.y] == int.MaxValue || D[e.x, e.y] * EchoMaze.C > r) return;
        m.hearT = Time.time + D[e.x, e.y] * EchoMaze.C / 20; m.hearC = EchoMaze.Cell(p);
    }
    public void Roar() => au.PlayOneShot(roarC);

    void Update()
    {
        if (EchoMaze.Over) return;
        var p = transform.position; var e = EchoMaze.Cell(p);
        var pl = EchoPlayer.I.transform.position; var pc = EchoMaze.Cell(pl);

        // 근접: 숨지 않았고 경로 2칸 이내면 플레이어 위치 직접 추격, 벗어나면 마지막 칸 추적으로 복귀
        bool near = !EchoPlayer.I.Hidden && Mathf.Abs(pc.x - e.x) + Mathf.Abs(pc.y - e.y) <= 2 && EchoMaze.Bfs(pc, 2)[e.x, e.y] <= 2;
        if (near) { if (mode != 2) Roar(); mode = 2; tg = new Vector3(pl.x, p.y, pl.z); }
        else if (mode == 2) { mode = 1; goal = pc; tg = new Vector3(e.x * EchoMaze.C, p.y, e.y * EchoMaze.C); }

        // 지연된 청각 반응 (표호는 배회 중 처음 들었을 때 1회)
        if (hearC != null && Time.time >= hearT) { if (mode == 0) Roar(); if (mode < 2) { mode = 1; goal = hearC; } hearC = null; }

        if (!near && Vector3.Distance(p, tg) < .05f) Step();
        else
        {
            var v = Vector3.MoveTowards(p, tg, speed[mode] * Time.deltaTime) - p;
            if (near)
            {
                if (EchoMaze.Wall(p + new Vector3(v.x + Mathf.Sign(v.x) * .3f, 0, 0))) v.x = 0;
                if (EchoMaze.Wall(p + new Vector3(0, 0, v.z + Mathf.Sign(v.z) * .3f))) v.z = 0;
            }
            transform.position = p + v;
            if ((dist += v.magnitude) > 1.5f) { dist = 0; EchoMaze.Pulse(p + v, 7, true); au.PlayOneShot(stepC); }
        }
        if ((bt -= Time.deltaTime) <= 0) { bt = mode > 0 ? 1 : 2.2f; au.PlayOneShot(breathC); }
    }

    // 목표 칸 기준 BFS 거리장 → 인접 칸 중 최소 거리로 한 칸 이동
    void Step()
    {
        var e = EchoMaze.Cell(transform.position);
        if (goal == null || goal == e) { mode = 0; goal = EchoMaze.Free[Random.Range(0, EchoMaze.Free.Count)]; }
        var D = EchoMaze.Bfs(goal.Value, int.MaxValue); var b = e;
        foreach (var (x, y) in EchoMaze.D) { var n = new Vector2Int(e.x + x, e.y + y); if (D[n.x, n.y] < D[b.x, b.y]) b = n; }
        tg = new Vector3(b.x * EchoMaze.C, transform.position.y, b.y * EchoMaze.C);
    }
}