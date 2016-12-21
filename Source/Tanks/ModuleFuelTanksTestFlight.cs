namespace RealFuels.Tanks
{
    public partial class ModuleFuelTanks
    {
        partial void UpdateTestFlight()
        {
            TestFlightWrapper.AddInteropValue(this.part, "tankType", type, "realFuels");
        }
    }
}
