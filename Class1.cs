using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ThunderRoad;
using UnityEngine;

namespace ExplosionSpell
{
    public class ExplosionSpell : SpellCastCharge
    {
        public float explosionRadius = 10;
        public float explosionForce = 25;
        public float explosionDamage = 20;
        public bool explosionDamageFallOff = true;
        public string explosionEffectId = "ExplosionSpellEffect";
        public bool explosionEffectAutoScaling = true;
        public Vector3 explosionEffectScale = new Vector3(1, 1, 1);
        public float burnDamagePerSecond = 5;
        public float burnDuration = 10;
        public string burnEffectId = "ExplosionSpellBurn";
        public float playerExplosionForce = 10;
        public float dismemberInExplosionRadius = 0.25f;
        public float burnInExplosionRadius = 5f;
        bool isFiring = false;
        ExplosionComponent component;
        public override void Load(SpellCaster spellCaster, Level level)
        {
            base.Load(spellCaster, level);
            component = spellCaster.gameObject.AddComponent<ExplosionComponent>();
        }
        public override void Unload()
        {
            base.Unload();
            if (component != null) GameObject.Destroy(component);
        }
        public override void Fire(bool active)
        {
            base.Fire(active);
            if (active)
            {
                component.StartCoroutine(component.Impact(spellCaster, null, spellCaster.magicSource.position, spellCaster.magicSource.rotation, explosionEffectId, 
                    explosionEffectAutoScaling, explosionEffectScale, explosionRadius, explosionForce, explosionDamage, explosionDamageFallOff, 
                    burnDuration, burnDamagePerSecond, burnEffectId, burnInExplosionRadius, dismemberInExplosionRadius));
                spellCaster.mana.creature.currentLocomotion.rb.AddForceAtPosition(-spellCaster.GetShootDirection() * playerExplosionForce, spellCaster.magic.position, ForceMode.VelocityChange);
                spellCaster.isFiring = false;
                Fire(false);
            }
        }
        public override void UpdateCaster()
        {
            base.UpdateCaster();
            if(spellCaster.mana.creature.isPlayer && spellCaster?.ragdollHand?.grabbedHandle is HandleRagdoll grabbedHandle && !isFiring && spellCaster.ragdollHand.playerHand.controlHand.castPressed)
            {
                isFiring = true;
                component.StartCoroutine(component.Impact(spellCaster, grabbedHandle, spellCaster.magicSource.position, spellCaster.magicSource.rotation, explosionEffectId,
                    explosionEffectAutoScaling, explosionEffectScale, explosionRadius, explosionForce, explosionDamage, explosionDamageFallOff,
                    burnDuration, burnDamagePerSecond, burnEffectId, burnInExplosionRadius, dismemberInExplosionRadius));
            }
            if (spellCaster.mana.creature.isPlayer && !spellCaster.ragdollHand.playerHand.controlHand.castPressed) isFiring = false;
        }
    }
    public class ExplosionComponent : MonoBehaviour
    {
        public IEnumerator Impact(SpellCaster spellCaster, HandleRagdoll handleRagdoll, Vector3 position, Quaternion rotation, string explosionEffectId, bool explosionEffectAutoScaling, Vector3 explosionEffectScale, 
            float explosionRadius, float explosionForce, float explosionDamage, bool explosionDamageFallOff, float burnDuration, float burnDamagePerSecond, string burnEffectId, float burnInExplosionRadius, float dismemberInExplosionRadius)
        {
            EffectInstance effectInstance = Catalog.GetData<EffectData>(explosionEffectId).Spawn(position, rotation);
            effectInstance.SetIntensity(1f);
            effectInstance.Play();
            foreach (Effect effect in effectInstance.effects)
            {
                effect.transform.localScale = explosionEffectAutoScaling ? Vector3.one * (explosionRadius / 10) : explosionEffectScale;
            }
            Collider[] sphereContacts = Physics.OverlapSphere(position, explosionRadius, 232799233);
            List<Creature> creaturesPushed = new List<Creature>();
            List<Rigidbody> rigidbodiesPushed = new List<Rigidbody>();
            rigidbodiesPushed.Add(spellCaster.mana.creature.currentLocomotion.rb);
            creaturesPushed.Add(spellCaster.mana.creature);
            foreach (Creature creature in Creature.allActive)
            {
                if (Vector3.Distance(position, creature.transform.position) is float distance && distance <= explosionRadius && !creaturesPushed.Contains(creature))
                {
                    if (!creature.isKilled && creature.loaded)
                    {
                        CollisionInstance collision = new CollisionInstance(new DamageStruct(DamageType.Energy, explosionDamageFallOff ? explosionDamage * (1 - (distance / explosionRadius)) : explosionDamage));
                        collision.damageStruct.hitRagdollPart = creature.ragdoll.rootPart;
                        creature.Damage(collision);
                        if (!creature.isPlayer)
                            creature.ragdoll.SetState(Ragdoll.State.Destabilized);
                    }
                    if (distance <= burnInExplosionRadius && creature.loaded)
                    {
                        if (creature.gameObject.GetComponent<Burning>() is Burning burning) Destroy(burning);
                        creature.gameObject.AddComponent<Burning>().Setup(burnDamagePerSecond, burnDuration, burnEffectId, spellCaster);
                    }
                    creaturesPushed.Add(creature);
                }
            }
            bool isSliced = false;
            if (handleRagdoll?.ragdollPart != null)
            {
                isSliced = handleRagdoll.ragdollPart.isSliced; 
                spellCaster.ragdollHand.UnGrab(false);
            }
            foreach (Collider collider in sphereContacts)
            {
                if (collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic && Vector3.Distance(position, collider.transform.position) is float distance && distance <= explosionRadius)
                {
                    if (collider.attachedRigidbody.gameObject.layer != GameManager.GetLayer(LayerName.NPC) && !rigidbodiesPushed.Contains(collider.attachedRigidbody))
                    {
                        if (collider.attachedRigidbody.GetComponentInParent<RagdollPart>() is RagdollPart part && part?.ragdoll?.creature != spellCaster.mana.creature && part.sliceAllowed &&
                            !part.isSliced && distance <= dismemberInExplosionRadius)
                        {
                            if (part.ragdoll.TrySlice(part))
                            {
                                if (part.data.sliceForceKill && !part.ragdoll.creature.isKilled) part.ragdoll.creature.Kill();
                                yield return null;
                            }
                        }
                        collider.attachedRigidbody.AddExplosionForce(explosionForce, position, explosionRadius, 0f, ForceMode.VelocityChange);
                        rigidbodiesPushed.Add(collider.attachedRigidbody);
                    }
                }
            }
            if (!isSliced && handleRagdoll?.ragdollPart is RagdollPart ragdollPart && ragdollPart.ragdoll.creature.isKilled) ragdollPart.ragdoll.creature.Despawn();
            yield break;
        }
    }
    public class Burning : MonoBehaviour
    {
        Creature creature;
        EffectInstance instance;
        float timer;
        float burnDamagePerSecond;
        float burnDuration;
        string burnEffectId;
        CollisionInstance collision;
        SpellCaster caster;
        public void Start()
        {
            creature = GetComponent<Creature>();
            creature.OnDespawnEvent += Creature_OnDespawnEvent;
            instance = Catalog.GetData<EffectData>(burnEffectId).Spawn(creature.ragdoll.rootPart.transform, true);
            instance.SetRenderer(creature.GetRendererForVFX(), false);
            instance.SetIntensity(1f);
            instance.Play();
            timer = Time.time; 
            collision = new CollisionInstance(new DamageStruct(DamageType.Energy, burnDamagePerSecond * Time.deltaTime));
            collision.damageStruct.hitRagdollPart = creature.ragdoll.rootPart;
            collision.casterHand = caster;
            if (!creature.loaded) Destroy(this);
        }

        private void Creature_OnDespawnEvent(EventTime eventTime)
        {
            instance.Stop();
            Destroy(this);
        }

        public void Setup(float dps, float duration, string effect, SpellCaster spellCaster)
        {
            burnDamagePerSecond = dps;
            burnDuration = duration;
            burnEffectId = effect;
            caster = spellCaster;
        }
        public void Update()
        {
            if (Time.time - timer >= burnDuration)
            {
                instance.Stop();
                Destroy(this);
            }
            else if (!creature.isKilled && creature.loaded)
            {
                collision.damageStruct.baseDamage = burnDamagePerSecond * Time.deltaTime;
                creature.Damage(collision);
            }
        }
        public void OnDestroy()
        {
            if (instance != null && instance.isPlaying)
                instance.Stop();
        }
    }
}
