using Keg.Engine.Game;
using TC2.Base.Components;
using static Keg.Engine.Chat;

namespace TC2.ExtendedArmaments;

public static partial class Trap
{
    [Flags]
    public enum Flags : uint
    {
        None = 0,

        Armed = 1 << 0,
        Triggered = 1 << 1,
        Triggered_Creature = 1 << 2,
        Faction = 1 << 3
    }

    public static readonly Sound.Handle sound_arm_default = "item.adjust.03";
    public static readonly Sound.Handle sound_trigger_default = "item.adjust.02";

    [IComponent.Data(Net.SendType.Reliable)]
    public partial struct Data : IComponent
    {
        public Sound.Handle sound_arm = Trap.sound_arm_default;
        public Sound.Handle sound_trigger = Trap.sound_trigger_default;

        public Entity trapped_entity = Entity.None;

        public uint frame_default = 0;
        public uint frame_armed = 1;
        public uint frame_triggered = 2;
        public uint frame_triggered_creature = 3;
        public Trap.Flags flags = default;

		public Damage.Type damage_type = Damage.Type.Slash;

        [Statistics.Info("Damage", description: "Damage dealt by the trap.", format: "{0:0.##}", comparison: Statistics.Comparison.Higher, priority: Statistics.Priority.High)]
        public float damage = 100;
        [Statistics.Info("Stun", description: "Stun dealt by the trap.", format: "{0:0.##}", comparison: Statistics.Comparison.Higher, priority: Statistics.Priority.High)]
        public float stun = 100;
		
        public Data()
        {
            
        }
    }

#if CLIENT
    [ISystem.Update(ISystem.Mode.Single, ISystem.Scope.Region, interval: 0.10f)]
    public static void UpdateSprite(ISystem.Info info, Entity entity,
    [Source.Owned] ref Trap.Data trap, [Source.Owned] ref Animated.Renderer.Data renderer,
    [Source.Owned] ref Body.Data body, [Source.Owned] in Transform.Data transform)
    {
        if (trap.flags.HasAny(Trap.Flags.Triggered_Creature))
        {
            renderer.sprite.frame.X = trap.frame_triggered_creature;
        }
        else if (trap.flags.HasAny(Trap.Flags.Triggered))
        {
            renderer.sprite.frame.X = trap.frame_triggered;
        }
        else if (trap.flags.HasAny(Trap.Flags.Armed))
        {
            renderer.sprite.frame.X = trap.frame_armed;
        }
        else
        {
            renderer.sprite.frame.X = trap.frame_default;
        }
    }
#endif

#if SERVER
	[ISystem.Event<Interactable.InteractEvent>(ISystem.Mode.Single, ISystem.Scope.Region)]
	public static void OnInteract(ISystem.Info info, Entity entity, ref Region.Data region, ref XorRandom random, [Source.Owned] ref Interactable.InteractEvent data,
	[Source.Owned] in Transform.Data transform, [Source.Owned] ref Interactable.Data interactable, [Source.Owned] ref Body.Data body, [Source.Owned] ref Trap.Data trap)
	{
		Sound.Play(ref region, trap.sound_arm, transform.position, volume: 0.60f, pitch: 1.00f, priority: 0.60f, size: 0.90f);

        if (trap.flags.HasAny(Trap.Flags.Triggered | Trap.Flags.Triggered_Creature))
        {
            trap.flags.SetFlag(Trap.Flags.Triggered, false);
            trap.flags.SetFlag(Trap.Flags.Triggered_Creature, false);
            trap.trapped_entity.RemoveRelation(entity, Relation.Type.Rope);
            ref var holdable = ref entity.GetOrAddComponent<Holdable.Data>(sync: true);
            holdable.flags.SetFlag(Holdable.Flags.Storable, true);
            WorldNotification.Push(ref region, "Trap released", Color32BGRA.Yellow, body.GetPosition(), lifetime: 1.00f, send_type: Net.SendType.Unreliable);
        }
        else if (!trap.flags.HasAny(Trap.Flags.Armed))
        {
            entity.RemoveComponent<Holdable.Data>();
            trap.flags.SetFlag(Trap.Flags.Armed, true);
            WorldNotification.Push(ref region, "Trap armed", Color32BGRA.Red, body.GetPosition(), lifetime: 1.00f, send_type: Net.SendType.Unreliable);
        }
        else if (trap.flags.HasAny(Trap.Flags.Armed))
        {
            trap.flags.SetFlag(Trap.Flags.Armed, false);
            ref var holdable = ref entity.GetOrAddComponent<Holdable.Data>(sync: true);
            holdable.flags.SetFlag(Holdable.Flags.Storable, true);
            WorldNotification.Push(ref region, "Trap disarmed", Color32BGRA.Green, body.GetPosition(), lifetime: 1.00f, send_type: Net.SendType.Unreliable);
        }

		trap.Sync(entity);
	}

	[ISystem.Update(ISystem.Mode.Single, ISystem.Scope.Region)]
	public static void Update(ISystem.Info info, Entity entity, ref Region.Data region,
	[Source.Owned] ref Trap.Data trap, [Source.Owned] ref Body.Data body, [Source.Owned] in Transform.Data transform,
        [Source.Owned, Optional] in Faction.Data faction)
	{
		if (body.HasArbiters() && trap.flags.HasAny(Trap.Flags.Armed))
		{
			foreach (var arbiter in body.GetArbiters())
			{
				if (arbiter.GetState() == Body.Arbiter.State.Begin)
				{
					var layer = arbiter.GetLayer();
					if (layer.HasAny(Physics.Layer.Dynamic) && layer.HasAny(Physics.Layer.Creature))
					{
						if (trap.flags.HasAny(Trap.Flags.Faction) && faction.id != 0 && arbiter.GetFaction() == faction.id) continue;

						Sound.Play(ref region, trap.sound_trigger, transform.position, volume: 0.70f, pitch: 0.80f, priority: 0.60f);

						Damage.Hit(entity, entity, arbiter.GetEntity(), arbiter.GetBodyPosition(), Vector2.Zero, Vector2.Zero,
							trap.damage, trap.damage, 0, arbiter.GetMaterial(), trap.damage_type, 0.5f, stun: trap.stun);

						trap.flags.SetFlag(Trap.Flags.Armed, false);
                        trap.flags.SetFlag(Trap.Flags.Triggered_Creature, true);

                        trap.trapped_entity = arbiter.GetEntity().GetRoot(Relation.Type.Child);
                        trap.trapped_entity.AddRelation(entity, Relation.Type.Rope);

                        trap.Sync(entity);
						break;
					}
                    else if (layer.HasAny(Physics.Layer.Dynamic) && layer.HasAny(Physics.Layer.Solid | Physics.Layer.Item))
                    {
                        Sound.Play(ref region, trap.sound_trigger, transform.position, volume: 0.70f, pitch: 0.80f, priority: 0.60f);

                        Damage.Hit(entity, entity, arbiter.GetEntity(), arbiter.GetBodyPosition(), Vector2.Zero, Vector2.Zero,
                            trap.damage, trap.damage, 0, arbiter.GetMaterial(), trap.damage_type, 0.5f, stun: trap.stun);

                        trap.flags.SetFlag(Trap.Flags.Armed, false);
                        trap.flags.SetFlag(Trap.Flags.Triggered, true);

                        trap.trapped_entity = arbiter.GetEntity().GetRoot(Relation.Type.Child);
                        trap.trapped_entity.AddRelation(entity, Relation.Type.Rope);

                        trap.Sync(entity);
                        break;
                    }
				}
			}
		}
	}
#endif
}