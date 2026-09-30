using UnityEngine;

// 아이템 자체 음파: 주기마다 약한 파동(아이템 색) + 3D 울림 소리. 몬스터는 듣지 않음
public class EchoItem : MonoBehaviour
{
    public Color color = Color.white;
    public float radius = 3, interval = 4;
    public AudioClip clip;
    float t;
    AudioSource au;

    void Start()
    {
        t = Random.value * interval;
        au = gameObject.AddComponent<AudioSource>(); au.spatialBlend = 1; au.spatialize = true; au.rolloffMode = AudioRolloffMode.Linear; au.maxDistance = 12;
        if (!clip) clip = EchoMaze.Clip(EchoMaze.Band(1800, 12, .01f, .8f, 3));
    }

    void Update()
    {
        if (EchoMaze.Over || (t -= Time.deltaTime) > 0) return;
        t = interval; EchoMaze.Pulse(transform.position, radius, false, color); au.PlayOneShot(clip, .4f);
    }
}
