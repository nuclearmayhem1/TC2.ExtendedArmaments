using Keg.Engine.Game;
using System;
using System.Reflection;

namespace TC2.ExtendedArmaments;

public static partial class OrdnanceDeployer

{
    public static readonly Sound.Handle sound_drop_default = "drop";

    [IComponent.Data(Net.SendType.Reliable)]
    public partial struct Data : IComponent
    {
        public Keyboard.Key key = Keyboard.Key.Spacebar;

        public FixedArray16<int> points_per_slot = new FixedArray16<int>();

        public FixedArray16<Shipment.Item> slots = new FixedArray16<Shipment.Item>();

        public Sound.Handle sound_drop = OrdnanceDeployer.sound_drop_default;

        public int num_slots = 4;
        public int index = 0;
        public int max_slots_horizontally = 4;

        public float weight = 0;

        public Ordnance.Tags required_ordnance_tag = Ordnance.Tags.Bomb;
        public Ordnance.Tags excluded_ordnance_tag = Ordnance.Tags.Rocket | Ordnance.Tags.Other | Ordnance.Tags.Huge | Ordnance.Tags.Missile;

        public OrdnanceDeployer.Mode mode = OrdnanceDeployer.Mode.First;

        [Statistics.Info("Maximum ordnance", description: "How much ordnance can fit inside the deployer", format: "{0:0}", comparison: Statistics.Comparison.Higher, priority: Statistics.Priority.High)]
        public float ordnance_max = 8;

        public float ordnance_current = 0;

        [Statistics.Info("Cycle Speed", description: "Rate of fire.", format: "{0:0.##}s", comparison: Statistics.Comparison.Lower, priority: Statistics.Priority.High)]
        public float cycle_interval = 1;
        [Save.Ignore, Net.Ignore] public float next_cycle = 0;

        [Statistics.Info("Deployment Velocity", description: "Velocity of the deployed ordnance.", format: "{0:0.##} m/s", comparison: Statistics.Comparison.Higher, priority: Statistics.Priority.Medium)]
        public float launch_velocity = 0;

        [Statistics.Info("Dud Rate", description: "Chance of a deployed bomb not being armed", format: "{0:P2}", comparison: Statistics.Comparison.Lower, priority: Statistics.Priority.Low)]
        public float dud_rate = 0.00f;

        [Editor.Picker.Position(true, true)]
        public Vector2 deployment_offset = Vector2.Zero;

        public float deployment_angle = 90;

        public Data()
        {

        }
    }

    public enum Mode : uint
    {
        Undefined,
        Round_robin,
        First,
    }

    public partial struct DepositRPC : Net.IRPC<OrdnanceDeployer.Data>
    {
        public int index;
        public int ordnance_points;
        public Shipment.Item item;
        public Entity held;

#if SERVER
		public void Invoke(ref NetConnection connection, Entity entity, ref OrdnanceDeployer.Data data)
		{
            if (data.slots[index].CanDepositPrefab(ref item))
            {
                data.weight += item.GetMass();

                if (entity.TryGetComponent<Body.Data>(out RefTuple<Body.Data> body))
                {
                    body.item_0.mass_extra = data.weight;
                }

                data.slots[index].Deposit(ref item);
                data.ordnance_current += ordnance_points;
                data.points_per_slot[index] = ordnance_points;
                held.Delete();
            }

            data.Sync(entity);
        }
#endif
    }

    public partial struct WithdrawRPC : Net.IRPC<OrdnanceDeployer.Data>
    {
        public int index;
        public Entity ent_pickup;

#if SERVER
        public void Invoke(ref NetConnection connection, Entity entity, ref OrdnanceDeployer.Data data)
        {
            if (data.slots[index].quantity > 0)
            {
                data.weight -= data.slots[index].GetMass();

                if (entity.TryGetComponent<Body.Data>(out RefTuple<Body.Data> body))
                {
                    body.item_0.mass_extra = data.weight;
                }

                Entity target = ent_pickup;
                ent_pickup.GetRegion().SpawnPrefab(data.slots[index].prefab, ent_pickup.GetComponent<Body.Data>().GetPosition()).ContinueWith((ent) => ent.AddRelation(target, Relation.Type.Child));
                data.ordnance_current -= data.points_per_slot[index];
                data.slots[index].quantity -= 1;
                if (data.slots[index].quantity <= 0)
                {
                    data.slots[index].prefab = default;
                    data.points_per_slot[index] = 0;
                }
            }

            data.Sync(entity);
        }
#endif
    }

    public partial struct EditRPC : Net.IRPC<OrdnanceDeployer.Data>
    {
        public Mode? mode;
        public Keyboard.Key? key;

#if SERVER
        public void Invoke(ref NetConnection connection, Entity entity, ref OrdnanceDeployer.Data data)
        {
            if (mode.HasValue)
            {
                data.mode = mode.Value;
                if (mode == Mode.First)
                {
                    data.index = 0;
                }
                else if (mode == Mode.Round_robin)
                {
                    data.index = data.num_slots - 1;
                }
            }
            if (key.HasValue)
            {
                data.key = key.Value;
            }

            data.Sync(entity);
        }
#endif
    }

#if SERVER
    [ISystem.Update(ISystem.Mode.Single)]
    public static void Update(ISystem.Info info, Entity entity, ref XorRandom random, ref Region.Data region, [Source.Owned] ref OrdnanceDeployer.Data deployer, [Source.Owned] in Control.Data control,
        [Source.Owned] in Transform.Data transform, [Source.Owned] ref Body.Data body)
    {
        if (control.keyboard.GetKeyDown(deployer.key) && deployer.next_cycle < region.GetFixedTime())
        {
            if (region.TryOverlapBB(body.GetPosition() + deployer.deployment_offset, new Vector2(4, 2), out OverlapBBResult result, mask: Physics.Layer.World, exclude: Physics.Layer.Creature))
            {
                WorldNotification.Push(ref region, "Deployer blocked", Color32BGRA.Red, body.GetPosition(), lifetime: 1.00f, send_type: Net.SendType.Unreliable);
                return;
            }

            Sound.Play(ref region, deployer.sound_drop, body.GetPosition(), volume: 0.60f, pitch: 1.00f, priority: 0.60f, size: 0.90f);

            deployer.next_cycle = region.GetFixedTime() + deployer.cycle_interval;

            int last_index = deployer.index;
            int index = last_index;

            switch (deployer.mode)
            {
                case Mode.Undefined: 
                    goto no_ordinance;
                case Mode.Round_robin:
                    for (int i = last_index + 1; i < deployer.num_slots; i++)
                    {
                        if (deployer.slots[i].quantity > 0)
                        {
                            index = i;
                            goto index_set;
                        }
                    }
                    for (int i = 0; i < last_index + 1; i++)
                    {
                        if (deployer.slots[i].quantity > 0)
                        {
                            index = i;
                            goto index_set;
                        }
                    }
                    goto no_ordinance;
                case Mode.First:
                    for (int i = 0; i < deployer.num_slots; i++)
                    {
                        if (deployer.slots[i].quantity > 0)
                        {
                            index = i;
                            goto index_set;
                        }
                    }
                    goto no_ordinance;
                default:
                    goto no_ordinance;
            }

        index_set:

            deployer.index = index;

            Vector2 velocity = new Vector2(deployer.launch_velocity, 0);
            Vector2 position = transform.LocalToWorld(deployer.deployment_offset);
            float angle = 0;

            if (transform.scale.X < 0)
            {
                angle = transform.LocalToWorldRotation(-deployer.deployment_angle * Maths.deg2rad);
            }
            else
            {
                angle = transform.LocalToWorldRotation(deployer.deployment_angle * Maths.deg2rad);
            }

            velocity = velocity.RotateByRad(angle);

            velocity += body.GetVelocity();


            bool is_dud = random.NextFloatRange(0, 1) < deployer.dud_rate;

            if (is_dud)
            {
                WorldNotification.Push(ref region, "Arming failure", Color32BGRA.Red, body.GetPosition(), lifetime: 1.00f, send_type: Net.SendType.Unreliable);
            }

            region.SpawnPrefab(deployer.slots[index].prefab, position, velocity: velocity).ContinueWith((ent) =>
            {
                if (!is_dud)
                {
                    if (ent.TryGetComponent<Ordnance.Data>(out RefTuple<Ordnance.Data> data))
                    {
                        data.item_0.flags.SetFlag(Ordnance.Flags.Armed, true);
                        data.item_0.time = data.item_0.trigger_time;
                        data.item_0.Sync(entity);
                    }
                }
            });

            deployer.slots[index].quantity -= 1;
            deployer.ordnance_current -= deployer.points_per_slot[index];
            deployer.weight -= deployer.slots[index].GetMass();

            if (deployer.slots[index].quantity <= 0)
            {
                deployer.points_per_slot[index] = 0;
                deployer.slots[index].prefab = default;
            }

            deployer.Sync(entity);
            return;
            no_ordinance:;
            deployer.index = 0;
            deployer.Sync(entity);
            return;
        }
    }
#endif

#if CLIENT
    [ISystem.GUI(ISystem.Mode.Single)]
    public static void OnGUI(ISystem.Info info, Entity entity, [Source.Owned] ref OrdnanceDeployer.Data deployer, [Source.Owned] ref Interactable.Data interactable)
    {
        if (interactable.show)
        {
            var gui = new OrdinanceDeployerGUI()
            {
                ent_deployer = entity,
                deployer = deployer
            };
            gui.Submit();
        }
    }

    public struct OrdinanceDeployerGUI : IGUICommand
    {
        public Entity ent_deployer;
        public OrdnanceDeployer.Data deployer;

        public void Draw()
        {
            using (var window = GUI.Window.Interaction("Ordnance Deployer", this.ent_deployer))
            {
                this.StoreCurrentWindowTypeID();
                if (window.show)
                {
                    using (var main = GUI.Group.New(GUI.GetRemainingSpace()))
                    {
                        var item_context = GUI.ItemContext.Begin();

                        using (var group_left = GUI.Group.New(new Vector2(MathF.Max(142 * deployer.max_slots_horizontally, GUI.GetRemainingWidth() - 50), GUI.GetRemainingHeight())))
                        {
                            using (var group_top = GUI.Group.New(new Vector2(GUI.GetRemainingWidth(), MathF.Max(64 * MathF.Ceiling(deployer.num_slots / deployer.max_slots_horizontally), GUI.GetRemainingHeight() - 80))))
                            {
                                int horizontal_count = deployer.max_slots_horizontally;

                                for (int i = 0; i < deployer.num_slots; i++)
                                {
                                    if (horizontal_count != deployer.max_slots_horizontally)
                                    {
                                        GUI.SameLine();
                                        horizontal_count++;
                                    }
                                    else
                                    {
                                        horizontal_count = 1;
                                    }

                                    using (var button = GUI.CustomButton.New("Slot " + i, new Vector2(142, 64)))
                                    {
                                        GUI.Draw9Slice(GUI.tex_slot, new Vector4(3, 3, 3, 3), button.bb);

                                        var held = item_context.ent_pickup_target;
                                        bool draw_icon = true;
                                        bool draw_number = true;

                                        if (button.hovered)
                                        {
                                            if (held != Entity.None) //Holding item
                                            {

                                                if (held.TryGetComponent<Ordnance.Data>(out RefTuple<Ordnance.Data> data) &&
                                                    data.item_0.tags.HasAny(deployer.required_ordnance_tag) &&
                                                    !data.item_0.tags.HasAny(deployer.excluded_ordnance_tag) &&
                                                    (int)deployer.slots[i].quantity <= 0 ||
                                                    (held.TryGetPrefab(out Prefab prefab) &&
                                                    prefab.TryGetHandle(out Prefab.Handle handle) &&
                                                    handle.id == deployer.slots[i].prefab.id)) //legal item
                                                {
                                                    if (deployer.ordnance_current >= deployer.ordnance_max) //bay full
                                                    {
                                                        draw_icon = false;
                                                        draw_number = false;
                                                        GUI.TitleCentered("Bay full", color: Color32BGRA.Red);
                                                    }
                                                    else if (deployer.ordnance_current + data.item_0.ordnance_points > deployer.ordnance_max) //not enough space
                                                    {
                                                        draw_icon = false;
                                                        draw_number = false;
                                                        GUI.TitleCentered("Insufficient space", color: Color32BGRA.Yellow);
                                                    }
                                                    else //Can place
                                                    {
                                                        GUI.DrawEntityIcon(held, 3f, Color32BGRA.Green);
                                                        draw_icon = false;
                                                        if (button.pressed)
                                                        {
                                                            var rpc = new OrdnanceDeployer.DepositRPC()
                                                            {
                                                                index = i,
                                                                item = item_context.item,
                                                                ordnance_points = data.item_0.ordnance_points,
                                                                held = item_context.ent_pickup_target,
                                                            };
                                                            rpc.Send(ent_deployer);
                                                        }
                                                    }
                                                }
                                                else //illegal item
                                                {
                                                    draw_icon = false;
                                                    draw_number = false;
                                                    GUI.DrawEntityIcon(held, 3f, Color32BGRA.Red);
                                                }
                                            }
                                            else //Not holding item
                                            {
                                                if (button.hovered)
                                                {
                                                    if (button.pressed)
                                                    {
                                                        if ((int)deployer.slots[i].quantity > 0)
                                                        {
                                                            var rpc = new OrdnanceDeployer.WithdrawRPC()
                                                            {
                                                                index = i,
                                                                ent_pickup = item_context.ent_pickup,
                                                            };
                                                            rpc.Send(ent_deployer);
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                        if ((int)deployer.slots[i].quantity <= 0)
                                        {
                                            draw_icon = false;
                                            draw_number = false;
                                        }

                                        if (draw_icon)
                                        {
                                            GUI.DrawSpriteCentered(deployer.slots[i].prefab.GetPrefab().GetIcon(), button.bb, GUI.Layer.Foreground, scale: 3);
                                        }
                                        if (draw_number)
                                        {
                                            GUI.DrawText(deployer.slots[i].quantity.ToString(), new Vector2(button.bb.a.X + 8, button.bb.b.Y - 22), size: 16);
                                        }
                                    }
                                }
                            }//Slots end

                            using (var group_bottom_left = GUI.Group.New(new Vector2(GUI.GetRemainingWidth() - 133, 80)))
                            {
                                if (GUI.EnumInput<OrdnanceDeployer.Mode>("mode", ref deployer.mode, new Vector2((GUI.GetRemainingWidth() - 10), 40), label_ratio: 1))
                                {
                                    var rpc = new OrdnanceDeployer.EditRPC()
                                    {
                                        mode = deployer.mode,
                                    };
                                    rpc.Send(ent_deployer);
                                }

                                if (GUI.EnumInput<Keyboard.Key>("key", ref deployer.key, new Vector2((GUI.GetRemainingWidth() - 10), 40), label_ratio: 1))
                                {
                                    var rpc = new OrdnanceDeployer.EditRPC()
                                    {
                                        key = deployer.key,
                                    };
                                    rpc.Send(ent_deployer);
                                }
                            }
                            GUI.SameLine();
                            using (var group_bottom_right = GUI.Group.New(new Vector2(GUI.GetRemainingWidth(), 80)))
                            {
                                GUI.Draw9Slice(GUI.tex_frame, new Vector4(3, 3, 3, 3), group_bottom_right.size);
                                using (var group = GUI.Group.New(GUI.GetRemainingSpace(), new Vector2(6, 6)))
                                {
                                    GUI.DrawSprite("ui_icon_weight", scale: 1);
                                    GUI.SameLine();
                                    GUI.Text(deployer.weight.ToString() + "kg", size: 16);
                                    GUI.NewLine();
                                    GUI.Text("Slot: " + (deployer.index + 1).ToString(), size: 16);
                                }
                            }
                        }
                        GUI.SameLine();
                        using (var group_right = GUI.Group.New(GUI.GetRemainingSpace()))
                        {
                            Color32BGRA color = Color32BGRA.FromHSV(1 - ((deployer.ordnance_current / deployer.ordnance_max) * 1f), 1f, 1f);
                            GUI.DrawVerticalGauge(deployer.ordnance_current, deployer.ordnance_max, new Vector2(50, GUI.GetRemainingHeight()), color);
                        }
                    }
                }
            }
        }
    }
#endif
}