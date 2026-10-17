using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Endless waves. A short "get ready", then each wave sends in a set number of enemies, only a few alive at
/// once (another comes in as one dies), at spawn points round the arena: away from the player, out of their
/// view where possible, announced by a smoke puff a moment before. Clearing a wave heals the player and gives
/// a break before the next, harder one (more enemies, more at once, sooner, tougher). Dead bodies sink into
/// the ground after a while. If the player dies the run is over: everyone stops, "YOU DIED", and after a
/// pause it starts again from wave 1.
/// </summary>
public class WaveDirector : MonoBehaviour
{
    public enum Phase { Starting, Fighting, Break, Dead }

    [Header("Enemies")]
    public Shooter enemyPrefab;
    [Tooltip("Where enemies come in. Empty = a ring round the arena (the NavMesh's edge)")]
    public Transform[] spawnPoints;
    [Tooltip("Plays where an enemy is about to appear")]
    public GameObject spawnFx;
    public float spawnWarning = 0.7f;       // s between the puff and the enemy
    public float minSpawnDistance = 8f;     // m from the player
    [Tooltip("Spawn points at least this far (deg) off where the player is looking count as out of view, and are preferred")]
    [Range(0f, 180f)] public float hiddenAngle = 70f;
    [Tooltip("Enemies already placed in the scene are taken out when the waves start")]
    public bool removePlacedEnemies = true;

    [Header("Waves")]
    public float firstWaveDelay = 3f;       // "get ready" before wave 1
    public int firstWaveCount = 3;          // enemies in wave 1...
    public int countPerWave = 2;            // ...and this many more each wave
    public int maxWaveCount = 30;
    public int firstMaxAlive = 2;           // alive at once in wave 1...
    public float maxAlivePerWave = 0.5f;    // ...more each wave (rounded down)...
    public int maxAliveCap = 6;             // ...up to this
    public float firstSpawnInterval = 2.5f; // s between arrivals in wave 1...
    public float spawnIntervalPerWave = 0.15f;  // ...this much sooner each wave...
    public float minSpawnInterval = 1f;     // ...down to this
    public float breakTime = 8f;            // between waves
    public float clearHeal = 30f;           // health back for clearing a wave

    [Header("Getting harder (each wave after the first)")]
    public float healthPerWave = 0.08f;     // +8 % health
    public float damagePerWave = 0.05f;     // +5 % stake and kick damage
    public float fireRatePerWave = 0.04f;   // throws come 4 % sooner...
    [Range(0.1f, 1f)] public float minFireIntervalScale = 0.6f;   // ...to at most this much sooner

    [Header("Corpses")]
    public float corpseTime = 10f;          // dead bodies lie this long (all of them go when a wave is cleared)...
    public float sinkTime = 2.5f;           // ...then sink into the ground over this long
    public float sinkDepth = 1.8f;

    [Header("Death")]
    public float deathPause = 4f;           // "YOU DIED" before the run starts again

    public Phase State { get; private set; }
    public int Wave { get; private set; }
    /// <summary>Best wave reached since play started.</summary>
    public int Best { get; private set; }
    /// <summary>Enemies of this wave not dead yet (still to come, arriving, or fighting).</summary>
    public int Remaining => toSpawn + pending + alive.Count;

    public event System.Action<int> WaveStarted, WaveCleared, RunOver;

    Player player;
    WaveHUD hud;
    readonly List<Shooter> alive = new List<Shooter>();
    readonly List<(Shooter body, float sinkAt)> corpses = new List<(Shooter, float)>();
    readonly List<(Transform body, Vector3 from, float start)> sinking = new List<(Transform, Vector3, float)>();
    readonly List<Vector3> arriving = new List<Vector3>();   // spots with a puff going, an enemy on the way
    List<Vector3> ring;
    int toSpawn, pending, maxAlive, spawnedThisWave;
    float spawnInterval, nextSpawn, phaseEnd;

    void OnEnable() => Player.Died += OnPlayerDied;
    void OnDisable() => Player.Died -= OnPlayerDied;

    void Start()
    {
        // no scene reload in this project: start from nothing every run
        player = FindAnyObjectByType<Player>();
        hud = FindAnyObjectByType<WaveHUD>(FindObjectsInactive.Include);
        StopAllCoroutines();
        alive.Clear(); corpses.Clear(); sinking.Clear(); arriving.Clear();
        ring = null;
        Best = 0;
        if (removePlacedEnemies)
            foreach (var s in FindObjectsByType<Shooter>(FindObjectsInactive.Include)) Destroy(s.gameObject);
        BeginRun();
    }

    // ── difficulty per wave ──
    public int WaveCount(int wave) => Mathf.Clamp(firstWaveCount + countPerWave * (wave - 1), 1, Mathf.Max(1, maxWaveCount));
    public int MaxAlive(int wave) => Mathf.Clamp(Mathf.FloorToInt(firstMaxAlive + maxAlivePerWave * (wave - 1)), 1, Mathf.Max(1, maxAliveCap));
    public float SpawnInterval(int wave) => Mathf.Max(minSpawnInterval, firstSpawnInterval - spawnIntervalPerWave * (wave - 1));
    public float HealthScale(int wave) => 1f + healthPerWave * (wave - 1);
    public float DamageScale(int wave) => 1f + damagePerWave * (wave - 1);
    public float FireIntervalScale(int wave) => Mathf.Max(minFireIntervalScale, 1f - fireRatePerWave * (wave - 1));

    void BeginRun()
    {
        Wave = 0;
        toSpawn = pending = 0;
        State = Phase.Starting;
        phaseEnd = Time.time + firstWaveDelay;
        if (hud != null) hud.Announce("GET READY", "", Mathf.Max(0.5f, firstWaveDelay - 0.8f));
    }

    void StartWave(int wave)
    {
        Wave = wave;
        Best = Mathf.Max(Best, wave);
        toSpawn = WaveCount(wave);
        pending = 0;
        spawnedThisWave = 0;
        maxAlive = MaxAlive(wave);
        spawnInterval = SpawnInterval(wave);
        nextSpawn = Time.time + 0.6f;
        arriving.Clear();
        State = Phase.Fighting;
        if (hud != null) hud.Announce($"WAVE {wave}", toSpawn == 1 ? "1 ENEMY" : $"{toSpawn} ENEMIES", 1.6f);
        WaveStarted?.Invoke(wave);
    }

    void Update()
    {
        float now = Time.time;
        UpdateCorpses(now);

        switch (State)
        {
            case Phase.Starting:
                if (now >= phaseEnd) StartWave(1);
                break;

            case Phase.Fighting:
                PruneAlive(now);
                if (toSpawn > 0 && alive.Count + pending < maxAlive && now >= nextSpawn)
                {
                    if (TrySpawn()) { toSpawn--; pending++; nextSpawn = now + spawnInterval; }
                    else nextSpawn = now + 0.5f;   // nowhere to put one right now: try again shortly
                }
                if (toSpawn == 0 && pending == 0 && alive.Count == 0) ClearWave(now);
                break;

            case Phase.Break:
                if (now >= phaseEnd) StartWave(Wave + 1);
                break;

            case Phase.Dead:
                if (now >= phaseEnd) { ClearArena(); if (player != null) player.Heal(player.maxHealth); BeginRun(); }
                break;
        }

        if (hud != null)
        {
            string top = Wave > 0 ? $"WAVE {Wave}" : "";
            string under = State == Phase.Fighting ? $"{Remaining} LEFT"
                         : State == Phase.Break ? $"NEXT WAVE IN {Mathf.Max(1, Mathf.CeilToInt(phaseEnd - now))}"
                         : "";
            hud.SetStatus(top, under);
        }
    }

    void ClearWave(float now)
    {
        State = Phase.Break;
        phaseEnd = now + breakTime;
        if (player != null && clearHeal > 0f) player.Heal(clearHeal);
        if (hud != null) hud.Announce("<color=#FFC93C>WAVE CLEARED</color>", clearHeal > 0f ? $"+{clearHeal:0} HEALTH" : "", 2f);
        // the arena is cleaned up for the next wave
        for (int i = 0; i < corpses.Count; i++) corpses[i] = (corpses[i].body, Mathf.Min(corpses[i].sinkAt, now + 1.5f));
        WaveCleared?.Invoke(Wave);
    }

    void OnPlayerDied()
    {
        if (!isActiveAndEnabled || State == Phase.Dead) return;
        State = Phase.Dead;
        phaseEnd = Time.time + deathPause;
        StopAllCoroutines();   // nobody else arrives
        pending = 0;
        toSpawn = 0;
        arriving.Clear();
        foreach (var s in alive)
        {
            if (s == null || s.IsDead) continue;
            s.target = null;   // they stop fighting
            if (s.agent != null && s.agent.enabled && s.agent.isOnNavMesh) s.agent.isStopped = true;
        }
        if (hud != null) hud.Announce("<color=#E0362C>YOU DIED</color>", Wave > 0 ? $"REACHED WAVE {Wave}" : "", Mathf.Max(0.5f, deathPause - 0.8f));
        RunOver?.Invoke(Wave);
    }

    /// <summary>Everyone gone (alive, dead or sinking), for a fresh run.</summary>
    void ClearArena()
    {
        foreach (var s in alive) if (s != null) Destroy(s.gameObject);
        foreach (var (body, _) in corpses) if (body != null) Destroy(body.gameObject);
        foreach (var (body, _, _) in sinking) if (body != null) Destroy(body.gameObject);
        alive.Clear(); corpses.Clear(); sinking.Clear(); arriving.Clear();
    }

    /// <summary>The dead become corpses; anyone who fell out of the world is gone.</summary>
    void PruneAlive(float now)
    {
        for (int i = alive.Count - 1; i >= 0; i--)
        {
            Shooter s = alive[i];
            if (s == null) { alive.RemoveAt(i); continue; }
            if (s.IsDead) { alive.RemoveAt(i); corpses.Add((s, now + corpseTime)); continue; }
            if (s.transform.position.y < -10f) { alive.RemoveAt(i); Destroy(s.gameObject); }
        }
    }

    void UpdateCorpses(float now)
    {
        for (int i = corpses.Count - 1; i >= 0; i--)
        {
            var (body, at) = corpses[i];
            if (body == null) { corpses.RemoveAt(i); continue; }
            if (now < at) continue;
            corpses.RemoveAt(i);
            // stop the ragdoll so it goes down with the body, then sink it
            foreach (var rb in body.GetComponentsInChildren<Rigidbody>()) { rb.isKinematic = true; rb.detectCollisions = false; }
            foreach (var c in body.GetComponentsInChildren<Collider>()) c.enabled = false;
            sinking.Add((body.transform, body.transform.position, now));
        }
        for (int i = sinking.Count - 1; i >= 0; i--)
        {
            var (body, from, start) = sinking[i];
            if (body == null) { sinking.RemoveAt(i); continue; }
            float t = Mathf.Clamp01((now - start) / Mathf.Max(0.01f, sinkTime));
            body.position = from + Vector3.down * (sinkDepth * t * t);   // slow at first, then down it goes
            if (t >= 1f) { Destroy(body.gameObject); sinking.RemoveAt(i); }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  SPAWNING
    // ══════════════════════════════════════════════════════════════════

    bool TrySpawn()
    {
        if (enemyPrefab == null || player == null) return false;
        if (!PickSpawn(out Vector3 at)) return false;
        arriving.Add(at);
        StartCoroutine(Arrive(at, Wave));
        return true;
    }

    IEnumerator Arrive(Vector3 at, int wave)
    {
        if (spawnFx != null) Destroy(Instantiate(spawnFx, at, Quaternion.identity), 5f);
        yield return new WaitForSeconds(spawnWarning);
        arriving.Remove(at);
        pending = Mathf.Max(0, pending - 1);
        if (State != Phase.Fighting || wave != Wave) yield break;

        Vector3 look = player.transform.position - at;
        look.y = 0f;
        var agentPrefab = enemyPrefab.GetComponent<NavMeshAgent>();
        float lift = agentPrefab != null ? agentPrefab.baseOffset : 1f;
        Shooter e = Instantiate(enemyPrefab, at + Vector3.up * lift, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity, transform);
        e.name = $"Wave {wave} Enemy {++spawnedThisWave}";
        e.target = player.transform;
        // tougher each wave (before its Start, which fills its health)
        e.maxHealth *= HealthScale(wave);
        e.health = e.maxHealth;
        float dmg = DamageScale(wave);
        e.damage *= dmg;
        e.kickDamage *= dmg;
        float fire = FireIntervalScale(wave);
        e.minFireInterval *= fire;
        e.maxFireInterval *= fire;
        alive.Add(e);
    }

    /// <summary>
    /// The spawn point to use now: on the NavMesh, far enough from the player, not on top of another enemy,
    /// out of the player's view if possible; a random one of the best (so they don't always come from one place).
    /// </summary>
    bool PickSpawn(out Vector3 at)
    {
        at = Vector3.zero;
        Vector3 me = player.transform.position;
        Transform eye = player.cam != null ? player.cam : player.transform;
        Vector3 view = eye.forward; view.y = 0f;

        var options = new List<(Vector3 p, float score)>();
        foreach (Vector3 c in Candidates())
        {
            if (!NavMesh.SamplePosition(c, out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
            Vector3 p = hit.position;
            Vector3 to = p - me; to.y = 0f;
            float distance = to.magnitude;
            bool far = distance >= minSpawnDistance;
            bool hidden = view.sqrMagnitude < 0.0001f || Vector3.Angle(view, to) >= hiddenAngle;
            bool crowded = false;
            foreach (var s in alive) if (s != null && !s.IsDead && (s.transform.position - p).sqrMagnitude < 2.5f * 2.5f) { crowded = true; break; }
            foreach (var a in arriving) if ((a - p).sqrMagnitude < 2.5f * 2.5f) { crowded = true; break; }
            float score = (far ? 4f : 0f) + (hidden ? 2f : 0f) - (crowded ? 8f : 0f) + Mathf.Min(distance, 20f) * 0.05f;
            options.Add((p, score));
        }
        if (options.Count == 0) return false;
        options.Sort((x, y) => y.score.CompareTo(x.score));
        // a random one of those as good as the best (within one point)
        float top = options[0].score;
        int good = 1;
        while (good < options.Count && options[good].score >= top - 1f) good++;
        at = options[Random.Range(0, good)].p;
        return true;
    }

    IEnumerable<Vector3> Candidates()
    {
        bool any = false;
        if (spawnPoints != null)
            foreach (var t in spawnPoints) if (t != null) { any = true; yield return t.position; }
        if (any) yield break;

        // no spawn points: a ring just inside the NavMesh's edge
        if (ring == null)
        {
            ring = new List<Vector3>();
            var tri = NavMesh.CalculateTriangulation();
            if (tri.vertices.Length > 0)
            {
                var b = new Bounds(tri.vertices[0], Vector3.zero);
                foreach (var v in tri.vertices) b.Encapsulate(v);
                float r = Mathf.Min(b.extents.x, b.extents.z) * 0.85f;
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI * 2f / 12f;
                    ring.Add(new Vector3(b.center.x + Mathf.Cos(a) * r, b.min.y, b.center.z + Mathf.Sin(a) * r));
                }
            }
        }
        foreach (var p in ring) yield return p;
    }

    void OnDrawGizmos()
    {
        if (spawnPoints == null) return;
        Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.9f);
        foreach (var t in spawnPoints)
            if (t != null) { Gizmos.DrawWireSphere(t.position + Vector3.up * 0.1f, 0.6f); Gizmos.DrawLine(t.position, t.position + Vector3.up * 2f); }
    }
}
