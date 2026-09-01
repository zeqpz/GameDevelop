// DamageableDummy — a shooting-range target: X Bot visual on the idle
// controller, trigger hitboxes on the humanoid bones (spheres + boxes, the
// explicit-hitbox policy), a non-trigger capsule so it blocks movement and
// camera, Health 100. Death tips it over; it stands back up 3 s later.
using UnityEngine;
using Game.Core;
using Game.Ragdoll;

namespace Game.Combat
{
    public class DamageableDummy : MonoBehaviour
    {
        Health _health;
        Animator _anim;
        GameObject _visual;
        RagdollController _ragdoll;
        Vector3 _lastHitPoint;
        Vector3 _lastHitDir = Vector3.forward;
        Vector3 _spawnPos;
        Quaternion _spawnRot;
        bool _dead;
        float _respawnT;

        public static DamageableDummy Spawn(Transform parent, Vector3 pos, float yawDeg)
        {
            var go = new GameObject("Dummy");
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yawDeg, 0f));
            return go.AddComponent<DamageableDummy>();
        }

        void Start()
        {
            _health = gameObject.AddComponent<Health>();
            _health.Died += _ => OnDied();
            _ragdoll = gameObject.AddComponent<RagdollController>();
            EventBus.Subscribe<EntityDamaged>(OnDamaged);
            _spawnPos = transform.position;
            _spawnRot = transform.rotation;

            var blocker = gameObject.AddComponent<CapsuleCollider>();   // world blocker
            blocker.center = new Vector3(0f, 0.9f, 0f);
            blocker.height = 1.8f;
            blocker.radius = 0.3f;

            var model = Resources.Load<GameObject>("Locomotion/X Bot");
            var controller = Resources.Load<RuntimeAnimatorController>("Locomotion/PlayerLocomotion");
            if (model == null)
            {
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(capsule.GetComponent<Collider>());
                capsule.transform.SetParent(transform, false);
                capsule.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                _visual = capsule;
                AddBox(transform, BodyRegion.Torso, new Vector3(0f, 1.1f, 0f),
                    new Vector3(0.4f, 1f, 0.3f));
                AddSphere(transform, BodyRegion.Head, new Vector3(0f, 1.65f, 0f), 0.14f);
                return;
            }

            _visual = Instantiate(model, transform);
            _visual.name = "Visual";
            _visual.transform.localPosition = Vector3.zero;
            _visual.transform.localRotation = Quaternion.identity;
            _anim = _visual.GetComponent<Animator>();
            if (_anim != null && controller != null)
            {
                _anim.runtimeAnimatorController = controller;
                _anim.applyRootMotion = false;
            }
            AttachHitboxes();
        }

        void AttachHitboxes()
        {
            if (_anim == null || !_anim.isHuman) return;
            Bone(HumanBodyBones.Head, BodyRegion.Head, 0.13f, new Vector3(0f, 0.06f, 0.02f));
            BoneBox(HumanBodyBones.Chest, BodyRegion.Torso,
                new Vector3(0f, 0.12f, 0f), new Vector3(0.32f, 0.42f, 0.24f));
            BoneBox(HumanBodyBones.Hips, BodyRegion.Torso,
                Vector3.zero, new Vector3(0.3f, 0.22f, 0.22f));
            Bone(HumanBodyBones.RightUpperArm, BodyRegion.Arm, 0.08f, Vector3.zero);
            Bone(HumanBodyBones.RightLowerArm, BodyRegion.Arm, 0.07f, Vector3.zero);
            Bone(HumanBodyBones.LeftUpperArm, BodyRegion.Arm, 0.08f, Vector3.zero);
            Bone(HumanBodyBones.LeftLowerArm, BodyRegion.Arm, 0.07f, Vector3.zero);
            Bone(HumanBodyBones.RightUpperLeg, BodyRegion.Leg, 0.1f, Vector3.zero);
            Bone(HumanBodyBones.RightLowerLeg, BodyRegion.Leg, 0.09f, Vector3.zero);
            Bone(HumanBodyBones.LeftUpperLeg, BodyRegion.Leg, 0.1f, Vector3.zero);
            Bone(HumanBodyBones.LeftLowerLeg, BodyRegion.Leg, 0.09f, Vector3.zero);
        }

        void Bone(HumanBodyBones boneId, BodyRegion region, float radius, Vector3 center)
        {
            var bone = _anim.GetBoneTransform(boneId);
            if (bone == null) return;
            float scale = Mathf.Max(0.0001f, bone.lossyScale.x);
            var col = bone.gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = radius / scale;
            col.center = center / scale;
            Tag(col, region);
        }

        void AddSphere(Transform parent, BodyRegion region, Vector3 center, float radius)
        {
            var col = parent.gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.center = center;
            col.radius = radius;
            Tag(col, region);
        }

        void BoneBox(HumanBodyBones boneId, BodyRegion region, Vector3 center, Vector3 size)
        {
            var bone = _anim.GetBoneTransform(boneId);
            if (bone == null) return;
            float scale = Mathf.Max(0.0001f, bone.lossyScale.x);
            var col = bone.gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = center / scale;
            col.size = size / scale;
            Tag(col, region);
        }

        void AddBox(Transform parent, BodyRegion region, Vector3 center, Vector3 size)
        {
            var col = parent.gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = center;
            col.size = size;
            Tag(col, region);
        }

        void Tag(Collider col, BodyRegion region)
        {
            var hb = col.gameObject.AddComponent<BodyHitbox>();
            hb.region = region;
            hb.health = _health;
        }

        void OnDestroy() => EventBus.Unsubscribe<EntityDamaged>(OnDamaged);

        // Remember the killing shot so the death ragdoll launches with it.
        void OnDamaged(EntityDamaged e)
        {
            if (e.Target != gameObject) return;
            _lastHitPoint = e.Point;
            Vector3 to = transform.position + Vector3.up * 1.2f - e.Point;
            if (to.sqrMagnitude > 0.01f) _lastHitDir = to.normalized;
        }

        void OnDied()
        {
            _dead = true;
            _respawnT = 4f;
            // Real death flop: the Verlet ragdoll takes over, launched along
            // the killing shot; StayDown until respawn stands it back up.
            _ragdoll.Knockdown(_lastHitPoint, _lastHitDir, 8f, 999f, stayDown: true);
        }

        void Update()
        {
            if (!_dead) return;
            _respawnT -= Time.deltaTime;
            if (_respawnT <= 0f)
            {
                _dead = false;
                _ragdoll.RestoreInstant();
                transform.SetPositionAndRotation(_spawnPos, _spawnRot);
                _health.ResetHealth();
            }
        }
    }
}
