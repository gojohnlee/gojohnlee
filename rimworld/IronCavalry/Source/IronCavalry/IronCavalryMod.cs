using HarmonyLib;
using Verse;

namespace IronCavalry
{
    public class IronCavalryMod : Mod
    {
        public IronCavalryMod(ModContentPack content) : base(content)
        {
            new Harmony("gojohnlee.ironcavalry").PatchAll();
        }
    }
}
