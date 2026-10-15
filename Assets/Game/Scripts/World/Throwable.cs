using UnityEngine;

namespace Gamex
{
    /// <summary>
    /// A rock or branch the player can pick up and throw. Where it lands it makes a noise loud
    /// enough to pull a dinosaur away.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Throwable : MonoBehaviour, IInteractable
    {
        [SerializeField] float impactNoiseRadius = 24f;
        [SerializeField] float minImpactSpeed = 3f;

        Rigidbody body;
        Collider shape;
        bool thrown;

        public string Prompt => "Pegar";

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            shape = GetComponent<Collider>();
        }

        public void Interact(PlayerInteractor interactor) => interactor.Hold(this);

        public void OnHeld(Transform holdPoint)
        {
            thrown = false;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            shape.enabled = false;
            transform.SetParent(holdPoint, false);
            transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        public void Throw(Vector3 velocity, Collider thrower)
        {
            transform.SetParent(null, true);
            shape.enabled = true;
            // Otherwise it can clip the thrower on the way out and count as having landed.
            Physics.IgnoreCollision(shape, thrower);
            body.isKinematic = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = velocity;
            thrown = true;
        }

        void OnCollisionEnter(Collision collision)
        {
            // Only the first hard landing after a throw is heard; rolling to a stop is silent.
            if (!thrown || collision.relativeVelocity.magnitude < minImpactSpeed)
                return;

            thrown = false;
            NoiseSystem.Emit(transform.position, impactNoiseRadius, NoiseKind.Impact);
        }
    }
}
