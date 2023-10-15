using Internal;
using TC2.Base.Components;
namespace TC2.ExtendedArmaments;

public static partial class EssenceExplosive
{

    [IComponent.Data(Net.SendType.Unreliable)]
    public partial struct Data : IComponent
    {
        [Statistics.Info("Type", description: "Essence type.", comparison: Statistics.Comparison.None, priority: Statistics.Priority.High)]
        public IEssence.Handle h_essence;
        [Statistics.Info("Amount", format: "{0:0.##}", comparison: Statistics.Comparison.Higher, priority: Statistics.Priority.High)]
        public float available;
        public Data()
        {

        }
    }

#if SERVER
    [ISystem.RemoveLast(ISystem.Mode.Single, ISystem.Scope.Region)]
    public static void OnRemove(ISystem.Info info, Entity entity, [Source.Owned] in EssenceExplosive.Data essence_explosive, [Source.Owned] in Transform.Data transform, ref Region.Data region)
    {
        Essence.SpawnCollapsingNode(ref region, essence_explosive.h_essence, essence_explosive.available, transform.position, true);
    }
#endif

}