using TC2.Base.Components;


namespace TC2.ExtendedArmaments
{
	public sealed partial class ModInstance: Mod
	{
		protected override void OnRegister(ModContext context)
		{
			
		}

		protected override void OnInitialize(ModContext context)
		{
            foreach (var essence_asset in IEssence.Database.GetAssets())
            {
                if (essence_asset.id == 0) continue;

                ref var essence_data = ref essence_asset.GetData();
                if (essence_data.IsNotNull())
                {
                    ref var material_data = ref essence_data.h_material_pellet.GetData();
                    if (material_data.IsNotNull())
                    {
                        ref var ammo = ref material_data.ammo.GetRefOrDefault();

                        ammo.prefab = "projectile." + essence_data.name;
                        ammo.heat = 100;
                        ammo.count = 1;
                        ammo.mass = 0.01f;
                        ammo.speed_mult = 1;
                        ammo.velocity_min = 0;
                        ammo.failure_base = 0;
                        ammo.recoil_mult = 1;
                        ammo.stability_base = 500;
                        ammo.stability_mult = 1;

                    }
                }
            }
        }
	}
}
