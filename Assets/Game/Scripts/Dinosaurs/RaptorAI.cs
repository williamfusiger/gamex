using System;
using UnityEngine;
using UnityEngine.AI;

namespace Gamex
{
    public enum RaptorState
    {
        Patrol,
        Investigate,
        Chase,
        Search,
    }

    /// <summary>
    /// The stalker. It patrols until it hears or glimpses something, walks over to check, and
    /// once it is sure it has seen the player it runs them down. It is faster than the player,
    /// so being chased in the open is fatal; the player survives by not being noticed.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class RaptorAI : MonoBehaviour, INoiseListener
    {
        [SerializeField] GameSession session;
        [Tooltip("Origin of sight. Keep it inside the raptor's own collider, so sight rays never hit the raptor itself.")]
        [SerializeField] Transform eyes;
        [Tooltip("Visited in order, looping. Left empty, the raptor wanders around where it started.")]
        [SerializeField] Transform[] patrolPoints = Array.Empty<Transform>();

        [Header("Sight")]
        [SerializeField] float viewDistance = 28f;
        [SerializeField, Range(0f, 360f)] float viewAngle = 110f;
        [Tooltip("A crouching player is only seen within this fraction of the view distance.")]
        [SerializeField, Range(0f, 1f)] float crouchViewDistanceScale = 0.6f;
        [Tooltip("Within this distance the player is noticed whatever way the raptor is facing.")]
        [SerializeField] float proximityRadius = 2.5f;
        [SerializeField] LayerMask sightBlockers = ~0;

        [Header("Awareness")]
        [Tooltip("Seconds of continuous sight needed to spot a player right next to the raptor.")]
        [SerializeField] float detectTimeNear = 0.25f;
        [Tooltip("Seconds of continuous sight needed to spot a player at the edge of the view distance.")]
        [SerializeField] float detectTimeFar = 2.2f;
        [SerializeField] float crouchDetectTimeScale = 1.5f;
        [Tooltip("Awareness lost per second while the player is out of sight.")]
        [SerializeField] float awarenessDecay = 0.4f;
        [Tooltip("Awareness at which the raptor walks over to look, without chasing yet.")]
        [SerializeField, Range(0f, 1f)] float suspicionThreshold = 0.5f;

        [Header("Hearing")]
        [Tooltip("Multiplies the radius of every noise. Above 1 the raptor hears from further away.")]
        [SerializeField] float hearingScale = 1f;

        [Header("Movement")]
        [SerializeField] float patrolSpeed = 2.2f;
        [SerializeField] float investigateSpeed = 4.5f;
        [SerializeField] float chaseSpeed = 9f;

        [Header("Behaviour")]
        [SerializeField] float attackRange = 1.8f;
        [SerializeField] float patrolPause = 2f;
        [SerializeField] float investigateDuration = 5f;
        [Tooltip("For this long after losing sight in a chase, the raptor still knows where the player went.")]
        [SerializeField] float chaseMemory = 1.5f;
        [Tooltip("Seconds without sight before a chase turns into a search.")]
        [SerializeField] float loseSightTime = 3f;
        [SerializeField] float searchDuration = 14f;
        [SerializeField] float searchRadius = 10f;

        const float RepathInterval = 0.15f;
        const float WitnessWindow = 0.5f;

        NavMeshAgent agent;
        Vector3 home;
        Vector3 lastKnownPosition;
        Vector3 investigatePoint;
        float lastSeenTime = float.NegativeInfinity;
        float stateTime;
        float idleTime;
        float repathTime;
        int patrolIndex;
        bool wasHidden;
        bool witnessedHiding;

        public RaptorState State { get; private set; } = RaptorState.Patrol;

        /// <summary>0 when unaware of the player, 1 once it has spotted them.</summary>
        public float Awareness { get; private set; }

        public bool CanSeePlayer { get; private set; }

        public event Action<RaptorState> StateChanged;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            home = transform.position;
        }

        void OnEnable() => NoiseSystem.Register(this);

        void OnDisable() => NoiseSystem.Unregister(this);

        void Start() => Enter(RaptorState.Patrol);

        void Update()
        {
            if (session.State != SessionState.Playing)
            {
                agent.isStopped = true;
                return;
            }

            UpdateSenses();
            stateTime += Time.deltaTime;

            switch (State)
            {
                case RaptorState.Patrol: TickPatrol(); break;
                case RaptorState.Investigate: TickInvestigate(); break;
                case RaptorState.Chase: TickChase(); break;
                case RaptorState.Search: TickSearch(); break;
            }
        }

        public void OnNoise(in NoiseEvent noise)
        {
            // In a chase it is already locked on to the player and ignores distractions.
            if (State == RaptorState.Chase || session.State != SessionState.Playing)
                return;

            if (Vector3.Distance(transform.position, noise.Position) <= noise.Radius * hearingScale)
                Investigate(noise.Position);
        }

        void UpdateSenses()
        {
            PlayerController player = session.Player;

            // Hiding does not help if the raptor watched the player get in during a chase.
            if (player.IsHidden && !wasHidden)
                witnessedHiding = State == RaptorState.Chase && Time.time - lastSeenTime <= WitnessWindow;
            else if (!player.IsHidden)
                witnessedHiding = false;
            wasHidden = player.IsHidden;

            CanSeePlayer = CheckSight(player, out float distance);

            if (CanSeePlayer)
            {
                lastSeenTime = Time.time;
                lastKnownPosition = TargetPosition(player);

                float detectTime = Mathf.Lerp(detectTimeNear, detectTimeFar,
                    Mathf.InverseLerp(proximityRadius, viewDistance, distance));
                if (player.IsCrouching)
                    detectTime *= crouchDetectTimeScale;
                Awareness = Mathf.Min(1f, Awareness + Time.deltaTime / detectTime);
            }
            else if (State != RaptorState.Chase)
            {
                Awareness = Mathf.Max(0f, Awareness - awarenessDecay * Time.deltaTime);
            }

            if (State == RaptorState.Chase)
                return;

            if (Awareness >= 1f)
                Enter(RaptorState.Chase);
            else if (CanSeePlayer && Awareness >= suspicionThreshold)
                Investigate(lastKnownPosition);
        }

        bool CheckSight(PlayerController player, out float distance)
        {
            Vector3 origin = eyes.position;
            Vector3 toPlayer = player.EyePosition - origin;
            distance = toPlayer.magnitude;

            if (player.IsHidden)
                return witnessedHiding;

            float maxDistance = player.IsCrouching ? viewDistance * crouchViewDistanceScale : viewDistance;
            bool inViewCone = distance <= maxDistance && Vector3.Angle(eyes.forward, toPlayer) <= viewAngle * 0.5f;
            if (!inViewCone && distance > proximityRadius)
                return false;

            // The ray ends inside the player's collider, so reaching the player means hitting them.
            if (!Physics.Linecast(origin, player.EyePosition, out RaycastHit hit, sightBlockers, QueryTriggerInteraction.Ignore))
                return true;
            return hit.collider.transform.IsChildOf(player.transform);
        }

        /// <summary>Where the raptor has to go to reach the player.</summary>
        static Vector3 TargetPosition(PlayerController player)
        {
            // A hidden player is off the navmesh; the way in is the spot's exit point.
            return player.IsHidden ? player.CurrentHidingSpot.ExitPoint.position : player.transform.position;
        }

        void Investigate(Vector3 point)
        {
            investigatePoint = point;
            if (State != RaptorState.Investigate)
                Enter(RaptorState.Investigate);

            idleTime = 0f;
            agent.SetDestination(investigatePoint);
        }

        void Enter(RaptorState next)
        {
            State = next;
            stateTime = 0f;
            idleTime = 0f;
            repathTime = 0f;
            agent.isStopped = false;

            switch (next)
            {
                case RaptorState.Patrol:
                    agent.speed = patrolSpeed;
                    agent.SetDestination(PatrolDestination());
                    break;
                case RaptorState.Investigate:
                    agent.speed = investigateSpeed;
                    break;
                case RaptorState.Chase:
                    agent.speed = chaseSpeed;
                    Awareness = 1f;
                    break;
                case RaptorState.Search:
                    agent.speed = investigateSpeed;
                    // Stays alert: it will spot the player again much faster than from cold.
                    Awareness = suspicionThreshold;
                    agent.SetDestination(lastKnownPosition);
                    break;
            }

            StateChanged?.Invoke(next);
        }

        void TickPatrol()
        {
            if (!HasArrived())
                return;

            idleTime += Time.deltaTime;
            if (idleTime < patrolPause)
                return;

            idleTime = 0f;
            if (patrolPoints.Length > 0)
                patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
            agent.SetDestination(PatrolDestination());
        }

        void TickInvestigate()
        {
            if (!HasArrived())
                return;

            idleTime += Time.deltaTime;
            LookAround();
            if (idleTime >= investigateDuration)
                Enter(RaptorState.Patrol);
        }

        void TickChase()
        {
            PlayerController player = session.Player;
            float sinceSeen = Time.time - lastSeenTime;

            // Losing sight for a moment does not shake it off: it keeps tracking by sound and momentum.
            if (!CanSeePlayer && !player.IsHidden && sinceSeen <= chaseMemory)
                lastKnownPosition = player.transform.position;

            repathTime -= Time.deltaTime;
            if (repathTime <= 0f)
            {
                repathTime = RepathInterval;
                agent.SetDestination(lastKnownPosition);
            }

            if (CanSeePlayer && HorizontalDistance(transform.position, lastKnownPosition) <= attackRange)
            {
                agent.isStopped = true;
                session.KillPlayer();
                return;
            }

            if (sinceSeen > loseSightTime)
                Enter(RaptorState.Search);
        }

        void TickSearch()
        {
            if (stateTime >= searchDuration)
            {
                Enter(RaptorState.Patrol);
                return;
            }

            if (!HasArrived())
                return;

            idleTime += Time.deltaTime;
            LookAround();
            if (idleTime < 1.5f)
                return;

            idleTime = 0f;
            if (TryRandomPoint(lastKnownPosition, searchRadius, out Vector3 point))
                agent.SetDestination(point);
        }

        Vector3 PatrolDestination()
        {
            if (patrolPoints.Length > 0)
                return patrolPoints[patrolIndex].position;

            return TryRandomPoint(home, searchRadius * 2f, out Vector3 point) ? point : home;
        }

        static bool TryRandomPoint(Vector3 centre, float radius, out Vector3 point)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * radius;
            Vector3 candidate = centre + new Vector3(offset.x, 0f, offset.y);
            bool found = NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas);
            point = hit.position;
            return found;
        }

        bool HasArrived() => !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f;

        void LookAround() => transform.Rotate(0f, Mathf.Sin(idleTime * 1.5f) * 120f * Time.deltaTime, 0f);

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y;
            return Vector3.Distance(a, b);
        }

        void OnDrawGizmosSelected()
        {
            if (eyes == null)
                return;

            Gizmos.color = Color.yellow;
            Vector3 origin = eyes.position;
            Gizmos.DrawRay(origin, Quaternion.Euler(0f, -viewAngle * 0.5f, 0f) * eyes.forward * viewDistance);
            Gizmos.DrawRay(origin, Quaternion.Euler(0f, viewAngle * 0.5f, 0f) * eyes.forward * viewDistance);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, proximityRadius);
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }
    }
}
