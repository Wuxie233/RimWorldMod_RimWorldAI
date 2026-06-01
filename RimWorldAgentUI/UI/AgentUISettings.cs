using Verse;

namespace RimWorldAgent
{
    public class AgentUISettings : ModSettings
    {
        public string BridgeWsUrl = "ws://127.0.0.1:19999";
        public int WebUIPort = 19997;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref BridgeWsUrl, "bridgeWsUrl", "ws://127.0.0.1:19999");
            Scribe_Values.Look(ref WebUIPort, "webUIPort", 19997);
        }
    }
}
