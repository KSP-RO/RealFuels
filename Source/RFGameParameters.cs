namespace RealFuels
{
    /// <summary>
    /// Player-facing settings for RealFuels, exposed via KSP's Difficulty settings screen.
    /// Access at runtime with HighLogic.CurrentGame.Parameters.CustomParams&lt;RFGameParameters&gt;().
    /// </summary>
    public class RFGameParameters : GameParameters.CustomParameterNode
    {
        public override string Title => "Engine Config GUI";
        public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;
        public override string Section => "RealFuels";
        public override string DisplaySection => "RealFuels";
        public override int SectionOrder => 1;
        public override bool HasPresets => false;

        [GameParameters.CustomParameterUI("Auto-open Engine Config window",
            toolTip = "Automatically open the engine config selection window when right-clicking an engine part.")]
        public bool autoOpenEngineGUI = true;
    }
}
