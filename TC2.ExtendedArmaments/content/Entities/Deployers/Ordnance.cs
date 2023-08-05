using Internal;
using Keg.Engine.Game;
using TC2.Base.Components;

namespace TC2.ExtendedArmaments;

public static partial class Ordnance
{
    public static readonly Sound.Handle sound_arm_default = "item.adjust.03";

    [IComponent.Data(Net.SendType.Reliable)]
    public partial struct Data : IComponent
    {

        public Ordnance.Tags tags = Tags.Bomb | Tags.Medium;
        public Ordnance.Type type = Type.Explosive;
        public Ordnance.Fuse fuse = Fuse.Impact;
        public Ordnance.Flags flags = Ordnance.Flags.None;
        public Physics.Layer hit_layers = Physics.Layer.World | Physics.Layer.Vehicle;

        public int ordnance_points = 4;
        public Sound.Handle sound_arm = Ordnance.sound_arm_default;

        [Statistics.Info("Minimum Trigger Velocity", description: "Minimum velocity required for detonation.", format: "{0:0.##} m/s", comparison: Statistics.Comparison.None, priority: Statistics.Priority.Medium)]
        public float min_trigger_velocity = 5f;

        [Statistics.Info("Time", description: "Time until detonation after arming.", format: "{0:0.00} s", comparison: Statistics.Comparison.None, priority: Statistics.Priority.Medium)]
        public float trigger_time = 5f;
        public float time = 5f;

        public float detonate_time = -1;
        public float impact_arming_time = -1;



        public Data()
        {

        }
    }
                       
    [Flags]
    public enum Tags : ulong
    {
        None = 0,
        Tiny = 1,
        Small = 2,
        Medium = 4,
        Large = 8,
        Huge = 16,
        Bomb = 32,
        Rocket = 64,
        Missile = 512,
        Other = 16384,
    }

    [Flags]
    public enum Flags : uint
    {
        None = 0,

        Armed = 1 << 0,
    }

    public enum Type : byte
    {
        Undefined = 0,

        Explosive,
        Cluster,
        Kinetic,
        Other,
    }

    public enum Fuse : byte
    {
        Undefined = 0,

        Impact,
        Timed,
        Penetration,
    }



    [ISystem.Update(ISystem.Mode.Single)]
    public static void Update(ISystem.Info info, Entity entity, ref Region.Data region,
    [Source.Owned] ref Ordnance.Data ordnance, [Source.Owned] ref Body.Data body, [Source.Owned, Optional] in Faction.Data faction,
        [Source.Owned, Optional] ref Explosive.Data explosive, [Source.Owned, Pair.Of<Body.Data>] ref Shape.Line shape, [Source.Owned] ref Transform.Data transform)
    {

        if (!entity.TryGetParent(Relation.Type.Child, out Entity parent))
        {
            float angle = 0;

            if (transform.scale.X < 0)
            {
                angle = 180 * Maths.deg2rad;
            }

            if (body.GetVelocity().Length() > 2.5f)
            {
                float time = Maths.Clamp(body.GetVelocity().Length() / 25, 0f, 1f);
                body.SetRotation(Maths.LerpEaseIn(body.GetRotation(), body.GetVelocity().GetAngleRadians() + angle, time, Maths.Easing.Cubic));
            }
            else
            {
                body.SetRotation(Maths.LerpEaseIn(body.GetRotation(), 0, 0.1f, Maths.Easing.Linear));
            }
        }

        if (ordnance.detonate_time != -1 && ordnance.detonate_time < region.GetFixedTime())
        {
#if SERVER
            if (ordnance.type == Ordnance.Type.Explosive)
            {
                explosive.flags.SetFlag(Explosive.Flags.Primed, true);
            }
            entity.Delete();
#endif
        }


        if (ordnance.flags.HasAny(Ordnance.Flags.Armed))
        {
            switch (ordnance.fuse)
            {
                case Fuse.Undefined:
                    break;
                case Fuse.Impact:

                    if (body.GetVelocity().Length() > ordnance.min_trigger_velocity &&
                        ordnance.impact_arming_time == -1)
                    {
                        ordnance.impact_arming_time = region.GetFixedTime() + 0.25f;
                    }
                    if (ordnance.impact_arming_time < region.GetFixedTime() &&
                        ordnance.impact_arming_time != -1 &&
                        ordnance.impact_arming_time != -2)
                    {
                        if (ordnance.impact_arming_time != -2)
                        {
                            Sound.Play(ref region, ordnance.sound_arm, body.GetPosition(), volume: 0.60f, pitch: 1.00f, priority: 0.60f, size: 0.90f);
                            ordnance.impact_arming_time = -2;
                        }
                    }
                    else if (ordnance.impact_arming_time == -2)
                    {
                        if (body.HasArbiters())
                        {
                            foreach (var arbiter in body.GetArbiters())
                            {
                                var layer = arbiter.GetLayer();

                                if (layer.HasAny(ordnance.hit_layers))
                                {
#if SERVER
                                    if (ordnance.type == Ordnance.Type.Explosive)
                                    {
                                        explosive.flags.SetFlag(Explosive.Flags.Primed, true);
                                    }
                                    entity.Delete();
#endif
                                }
                            }
                        }
                    }

                    break;
                case Fuse.Timed:
                    ordnance.time -= App.fixed_update_interval_s;
                    if (ordnance.time <= 0)
                    {
#if SERVER
                        if (ordnance.type == Ordnance.Type.Explosive)
                        {
                            explosive.flags.SetFlag(Explosive.Flags.Primed, true);
                        }
                        entity.Delete();
#endif
                    }
                    break;
                case Fuse.Penetration:



                    break;
                default:
                    break;
            }
        }
    }

#if SERVER
    [ISystem.Event<EssenceNode.FailureEvent>(ISystem.Mode.Single)]
    public static void OnFailure(ref Region.Data region, ISystem.Info info, Entity entity, ref XorRandom random, ref EssenceNode.FailureEvent data, [Source.Owned] ref Transform.Data transform, [Source.Owned] ref Ordnance.Data ordnance)
    {
        if (ordnance.flags.HasAny(Ordnance.Flags.Armed))
        {
            WorldNotification.Push(ref region, "Fuse failure", Color32BGRA.Red, transform.position, lifetime: 1.00f, send_type: Net.SendType.Unreliable);
            ordnance.flags.SetFlag(Ordnance.Flags.Armed, false);
            ordnance.Sync(entity);
        }
    }
#endif

#if SERVER
    [ISystem.Update(ISystem.Mode.Single)]
    public static void OnInteract(ISystem.Info info, Entity entity, ref Region.Data region, [Source.Owned] in Control.Data control,
        [Source.Owned] in Transform.Data transform, [Source.Owned] ref Body.Data body, [Source.Owned] ref Ordnance.Data ordnance)
    {
        if (control.keyboard.GetKeyDown(Keyboard.Key.Spacebar))
        {
            Sound.Play(ref region, ordnance.sound_arm, transform.position, volume: 0.60f, pitch: 1.00f, priority: 0.60f, size: 0.90f);

            if (ordnance.flags.HasAny(Ordnance.Flags.Armed))
            {
                ordnance.flags.SetFlag(Ordnance.Flags.Armed, false);
                WorldNotification.Push(ref region, "Ordinance disarmed", Color32BGRA.Green, body.GetPosition(), lifetime: 1.00f, send_type: Net.SendType.Unreliable);
            }
            else
            {
                ordnance.flags.SetFlag(Ordnance.Flags.Armed, true);
                ordnance.time = ordnance.trigger_time;
                ordnance.detonate_time = -1;
                ordnance.impact_arming_time = -1;
                WorldNotification.Push(ref region, "Ordinance armed", Color32BGRA.Red, body.GetPosition(), lifetime: 1.00f, send_type: Net.SendType.Unreliable);
            }
        }

        ordnance.Sync(entity);
    }
#endif
}