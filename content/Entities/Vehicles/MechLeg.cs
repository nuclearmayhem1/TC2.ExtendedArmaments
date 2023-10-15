using Internal;
using Keg.Engine.Game;
using System.Diagnostics;
using TC2.Base.Components;

namespace TC2.ExtendedArmaments;

public static partial class MechLeg
{

    [IComponent.Data(Net.SendType.Reliable)]
    public partial struct Data : IComponent
    {
        public float upper_leg_length = 1f;
        public float lower_leg_length = 1f;

        public Vector2 leg_offset = Vector2.Zero;

        public float step_distance = 0.5f;
        public float max_vertical_distance = 2f;

        public float move_speed = 0.5f;

        public Vector2 upper_leg_offset = Vector2.Zero;
        public Vector2 lower_leg_offset = Vector2.Zero;

        public Data()
        {

        }
    }

    [IComponent.Data(Net.SendType.Unreliable)]
    public partial struct State : IComponent
    {
        public float angle_a;
        public float angle_b;

        public Vector2 target_pos;
    }

    [ISystem.Update(ISystem.Mode.Single, ISystem.Scope.Region)]
    public static void Update(ISystem.Info info, Entity entity, ref XorRandom random, ref Region.Data region, [Source.Owned] ref MechLeg.Data leg, [Source.Owned] in Control.Data control,
        [Source.Owned] in Transform.Data transform, [Source.Owned] ref Body.Data body, [Source.Owned] ref MechLeg.State state)
    {
        //Update Target Position

        float horizontal_distance = transform.position.X - state.target_pos.X;

        if (horizontal_distance > leg.step_distance)
        {
            Vector2 castStart = transform.position + new Vector2(leg.step_distance, 0);
            Vector2 castEnd = castStart + new Vector2(0, leg.max_vertical_distance);

            if (region.TryLinecast(castStart, castEnd, 0.1f, out var result))
            {
                state.target_pos = result.world_position;
            }
        }
        else if (horizontal_distance < leg.step_distance)
        {
            Vector2 castStart = transform.position - new Vector2(leg.step_distance, 0);
            Vector2 castEnd = castStart + new Vector2(0, leg.max_vertical_distance);

            if (region.TryLinecast(castStart, castEnd, 0.1f, out var result))
            {
                state.target_pos = result.world_position;
            }
        }



        //Resolve angles
        IK.Resolve2x(new(leg.upper_leg_length, leg.lower_leg_length), transform.LocalToWorld(leg.leg_offset), state.target_pos, new(state.angle_a, state.angle_b), out var angles, false);
        state.angle_a = angles.X;
        state.angle_b = angles.Y;
    }


#if CLIENT
    [ISystem.Render(ISystem.Mode.Single, ISystem.Scope.Region)]
    public static void RenderLegs(ISystem.Info info, Entity entity, [Source.Owned] in Transform.Data transform, [Source.Owned] in Control.Data control, [Source.Owned] ref MechLeg.Data leg, [Source.Owned] ref MechLeg.State state)
    {

        //Upper leg

        Vector2 upperOffset = leg.leg_offset + leg.upper_leg_offset;

        Transform.Data upperTransform = new Transform.Data(upperOffset, state.angle_a, Vector2.One);

        Animated.Renderer.Draw(upperTransform, new()
        {
            sprite = new Sprite("mech_leg_lower"),
            z = -110,
        });




        //Lower leg

        Vector2 lowerOffset = (transform.position + leg.leg_offset) + new Vector2(leg.upper_leg_length, 0).RotateByRad(state.angle_a) + leg.lower_leg_offset;

        Animated.Renderer.Draw(transform, new()
        {
            sprite = new Sprite("mech_leg_upper"),
            z = -120,
            scale = Vector2.One,
            rotation = state.angle_b,
            offset = lowerOffset,
        });

    }
#endif
}