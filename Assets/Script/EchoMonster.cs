using UnityEngine;

// 청각 AI: mode 0 배회, 1 소리 추적, 2 근접 추격(경로 2칸 이내).
// EchoMonster.prefab: pentimento_monster_v03 Humanoid + Speed 기반 대기/걷기/달리기.
// 위협은 .2초, 접촉 공격은 .12초 크로스페이드. 공격은 게임 종료 후에도 완료됩니다.
// 소리 반응 포효는 시작 시 장거리 붉은 음파를 1회 방출합니다(roarRadius, 기본 30).
// 플레이어 조우는 포효 없이 즉시 추격하며, 진행 중인 포효도 .15초 블렌드로 중단합니다.
// 위치는 AI만 변경하며, FBX의 XZ 이동은 추출 후 적용하지 않아 메시 이탈을 방지합니다.
// 에셋 재생성: main.unity에서 Tools > Echo Maze > Build Monster (생성 에셋 덮어쓰기).
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
    Animator animator;
    float actionUntil;
    static readonly int MoveSpeed = Animator.StringToHash("Speed");
    static readonly int LocomotionState = Animator.StringToHash("Base Layer.Locomotion");
    static readonly int AlertState = Animator.StringToHash("Base Layer.Alert");
    static readonly int AttackState = Animator.StringToHash("Base Layer.Attack");
    [Tooltip("위협 클립 길이 / Animator 재생 속도. 이동을 멈추고 위협 동작을 마친 뒤 추적합니다.")]
    public float alertDuration = 2;
    [Tooltip("포효 시작 시 방출하는 붉은 음파의 최대 경로 거리. 발걸음 음파는 7.")]
    public float roarRadius = 30;

    void Awake() => I = this;

    void Start()
    {
        tg = transform.position;
        animator = GetComponentInChildren<Animator>();
        if (animator) animator.applyRootMotion = false;
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
    public void Roar()
    {
        if (Time.time < actionUntil) return;
        au.PlayOneShot(roarC);
        EchoMaze.Pulse(transform.position + Vector3.up, roarRadius, true);
        if (!animator) return;
        animator.CrossFadeInFixedTime(AlertState, .2f);
        actionUntil = Time.time + alertDuration;
    }

    public void Attack()
    {
        au.PlayOneShot(roarC);
        if (!animator) return;
        animator.SetFloat(MoveSpeed, 0);
        animator.CrossFadeInFixedTime(AttackState, .12f);
    }

    void Animate(Vector3 movement)
    {
        if (animator) animator.SetFloat(MoveSpeed, movement.magnitude / Mathf.Max(Time.deltaTime, .0001f), .15f, Time.deltaTime);
        if (movement.sqrMagnitude > .000001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(movement), 360 * Time.deltaTime);
    }

    void Update()
    {
        // Animator는 종료 후에도 공격을 끝까지 재생하고 대기로 복귀합니다.
        if (EchoMaze.Over) { Animate(Vector3.zero); return; }
        var p = transform.position; var e = EchoMaze.Cell(p);
        var pl = EchoPlayer.I.transform.position; var pc = EchoMaze.Cell(pl);

        // 근접: 숨지 않았고 경로 2칸 이내면 플레이어 위치 직접 추격, 벗어나면 마지막 칸 추적으로 복귀
        bool near = !EchoPlayer.I.Hidden && Mathf.Abs(pc.x - e.x) + Mathf.Abs(pc.y - e.y) <= 2 && EchoMaze.Bfs(pc, 2)[e.x, e.y] <= 2;
        if (near)
        {
            // 조우 시 포효를 새로 시작하지 않고, 진행 중인 청각 반응 포효도 끊고 추격합니다.
            if (mode != 2 && Time.time < actionUntil)
            {
                actionUntil = 0;
                if (animator) animator.CrossFadeInFixedTime(LocomotionState, .15f);
            }
            mode = 2;
            tg = new Vector3(pl.x, p.y, pl.z);
        }
        else if (mode == 2) { mode = 1; goal = pc; tg = new Vector3(e.x * EchoMaze.C, p.y, e.y * EchoMaze.C); }

        // 지연된 청각 반응 (포효는 배회 중 처음 들었을 때 1회, 조우 시에는 생략)
        if (hearC != null && Time.time >= hearT) { if (mode == 0) Roar(); if (mode < 2) { mode = 1; goal = hearC; } hearC = null; }

        if (Time.time < actionUntil) { Animate(Vector3.zero); return; }

        if (!near && Vector3.Distance(p, tg) < .05f) Step();
        // 칸 경계에서도 한 프레임 멈추지 않아 보행 블렌딩이 끊기지 않습니다.
        {
            var v = Vector3.MoveTowards(p, tg, speed[mode] * Time.deltaTime) - p;
            if (near)
            {
                if (EchoMaze.Wall(p + new Vector3(v.x + Mathf.Sign(v.x) * .3f, 0, 0))) v.x = 0;
                if (EchoMaze.Wall(p + new Vector3(0, 0, v.z + Mathf.Sign(v.z) * .3f))) v.z = 0;
            }
            transform.position = p + v;
            Animate(v);
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